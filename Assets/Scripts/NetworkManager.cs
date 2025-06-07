// HoloLensZmqClient.cs
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using NetMQ;
using NetMQ.Sockets;
using TMPro;
using System.Net.NetworkInformation;

public class NetworkManager : MonoBehaviour
{

    [SerializeField]
    private TMP_Text debugText;

    [SerializeField]
    private GazeTracker gazeTracker;
    public static event Action OnImageReady = delegate { };
    public static event Action OnNewImage = delegate { };


    // --- UDP discovery parameters (unchanged) ---
    private const int DISCOVERY_PORT = 5005;
    private const string DISCOVER_MESSAGE = "DISCOVER_PC";
    private const string DISCOVERY_REPLY = "PC_HERE";
    private const int UDP_TIMEOUT_MS = 50000;

    private string pcIpAddress = null;

    // --- ZMQ endpoints (unchanged) ---
    private const int ZMQ_IMAGE_PORT = 5006;
    private const int ZMQ_GAZE_PORT = 5007;

    private SubscriberSocket imageSubscriber;
    private PublisherSocket gazePublisher;

    private Thread imageThread;
    private bool imageThreadRunning = false;
    private Texture2D texture;
    private byte[] newImageBytes;

    // ---- NEW: flag to indicate a fresh image arrived ----
    private volatile bool newImageAvailable = false;
    private int currentStep = -1;
    private PullSocket imagePullSocket;
    private PushSocket gazePushSocket;


    public Texture2D IncomingTexture
    {
        get => texture;

    }

    void Start()
    {
        StartCoroutine(DiscoverPCCoroutine());
        OnNewImage += GazePublish;
    }

    void Update()
    {
        if (newImageAvailable)
        {
            texture.LoadImage(newImageBytes);
            OnNewImage?.Invoke();
            newImageAvailable = false;
        }
    }

    IEnumerator DiscoverPCCoroutine()
    {
        UdpClient udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;
        udpClient.Client.ReceiveTimeout = UDP_TIMEOUT_MS;

        // Get the broadcast address for the local network

        IPAddress broadcastAddress = GetWLANBroadcastAddress();
        IPEndPoint broadcastEP = new IPEndPoint(broadcastAddress, DISCOVERY_PORT);
        byte[] discoverBytes = Encoding.UTF8.GetBytes(DISCOVER_MESSAGE);
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

        Debug.Log($"[HL2][UDP] Sending DISCOVER_PC broadcast to {broadcastEP.Address}:{broadcastEP.Port}, waiting for reply ...");
        debugText.text = $"[HL2][UDP] Sending DISCOVER_PC broadcast to {broadcastEP.Address}:{broadcastEP.Port}, waiting for reply ...";

        while (true)
        {
            udpClient.SendAsync(discoverBytes, discoverBytes.Length, broadcastEP);
            yield return new WaitForSeconds(0.1f); // Small delay to avoid flooding
            try
            {
                // If no data available, skip this iteration, necessary because Receive will block if no data is available
                if (udpClient.Available == 0) continue;

                byte[] data = udpClient.Receive(ref remoteEP);
                string text = Encoding.UTF8.GetString(data);
                if (text == DISCOVERY_REPLY)
                {
                    pcIpAddress = remoteEP.Address.ToString();
                    Debug.Log($"[HL2][UDP] Received PC_HERE from {pcIpAddress}");
                    debugText.text = $"[HL2][UDP] Received PC_HERE from {pcIpAddress}";
                    break;
                }
            }
            catch (SocketException)
            {
                // Timeout; keep waiting until total timeout
            }
            yield return null;
        }
        udpClient.Close();
        StartZmqSockets();
    }


    void StartZmqSockets()
    {
        AsyncIO.ForceDotNet.Force();
        NetMQConfig.Cleanup();
        imagePullSocket = new PullSocket();
        string address = $"tcp://{pcIpAddress}:{ZMQ_IMAGE_PORT}";
        imagePullSocket.Connect(address);
        Debug.Log($"[HL2][ZMQ] Connected to image publisher at {address}");

        texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        OnImageReady?.Invoke();

        imageThreadRunning = true;
        imageThread = new Thread(ImageReceiveLoop);
        imageThread.IsBackground = true;
        imageThread.Start();


        gazePushSocket = new PushSocket();
        string gazeAddress = $"tcp://{pcIpAddress}:{ZMQ_GAZE_PORT}";
        gazePushSocket.Connect(gazeAddress);
        Debug.Log($"[HL2][ZMQ] Connected to gaze publisher at {gazeAddress}");
        debugText.text = $"[HL2][ZMQ] Connected to gaze publisher at {gazeAddress}";
    }

    void ImageReceiveLoop()
    {
        while (imageThreadRunning)
        {
            try
            {
                var msg = imagePullSocket.ReceiveMultipartMessage();
                if (msg == null || msg.FrameCount != 2)
                {
                    Debug.LogWarning("[HL2][ZMQ] Received invalid Image, waiting for next frame...");
                    continue;
                }

                currentStep = msg[0].ConvertToInt32();
                byte[] imageBytes = msg[1].Buffer;

                Debug.Log($"[HL2][ZMQ] Received image frame with step {currentStep}, size: {imageBytes.Length} bytes");

                newImageBytes = imageBytes;
                newImageAvailable = true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[HL2][ZMQ] ImageReceiveLoop exception: " + ex.Message);
                break;
            }
        }
    }

    void GazePublish()
    {
        // At this point, a new frame has arrived. Send gaze only once per frame:
        Vector2 gazeXY = gazeTracker.GetGazePointOnTexture();
        string gazeJson = $"{{\"x\": {gazeXY.x}, \"y\": {gazeXY.y}, \"step\": {currentStep}}}";

        try
        {
            gazePushSocket.SendFrame(gazeJson);
        }
        catch (Exception ex)
        {
            Debug.LogError("[HL2][ZMQ] Failed to publish gaze: " + ex.Message);
        }
    }

    private void OnDestroy()
    {
        imageThreadRunning = false;
        if (imageThread != null && imageThread.IsAlive)
        {
            imageThread.Join(500);
        }

        imageSubscriber?.Close();
        gazePublisher?.Close();
        NetMQConfig.Cleanup();
    }

    private IPAddress GetWLANBroadcastAddress()
    {
        NetworkInterface[] intf = NetworkInterface.GetAllNetworkInterfaces();
        foreach (NetworkInterface device in intf)
        {
            if (device.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && device.OperationalStatus == OperationalStatus.Up)
            {
                IPAddress ipv4Address = device.GetIPProperties().UnicastAddresses[1].Address; //This will give ipv4 address of certain adapter
                IPAddress unicastIPv4Mask = device.GetIPProperties().UnicastAddresses[1].IPv4Mask; //This will give ipv4 mask of certain adapter
                Debug.Log($"[HL2][Network] Found WLAN interface: {device.Name} with IPv4: {ipv4Address} and mask: {unicastIPv4Mask}");
                // Get the broadcast address for the IPv4 address
                return GetBroadcastAddress(ipv4Address, unicastIPv4Mask);
            }
        }
        Debug.LogError("[HL2][Network] No active WLAN interface found.");
        return null;
    }

    public static IPAddress GetBroadcastAddress(IPAddress address, IPAddress mask)
    {
        uint ipAddress = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
        uint ipMaskV4 = BitConverter.ToUInt32(mask.GetAddressBytes(), 0);
        uint broadCastIpAddress = ipAddress | ~ipMaskV4;

        return new IPAddress(BitConverter.GetBytes(broadCastIpAddress));
    }
}
