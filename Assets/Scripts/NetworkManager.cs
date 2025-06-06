// HoloLensZmqClient.cs
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using NetMQ;
using NetMQ.Sockets;
using TMPro;

public class NetworkManager : MonoBehaviour
{
    
    [SerializeField]
    private TMP_Text debugText;

    [SerializeField]
    private GazeTracker gazeTracker;
    public static event Action OnImageReady = delegate { };


    // --- UDP discovery parameters (unchanged) ---
    private const int DISCOVERY_PORT = 5005;
    private const string DISCOVER_MESSAGE  = "DISCOVER_PC";
    private const string DISCOVERY_REPLY   = "PC_HERE";
    private const int    UDP_TIMEOUT_MS    = 50000;

    private string pcIpAddress = null;

    // --- ZMQ endpoints (unchanged) ---
    private const int    ZMQ_IMAGE_PORT = 5006;  
    private const int    ZMQ_GAZE_PORT  = 5007;  

    private SubscriberSocket imageSubscriber;
    private PublisherSocket  gazePublisher;

    private Thread imageThread;
    private bool   imageThreadRunning = false;
    private Texture2D texture;
    private byte[] newImageBytes;

    // ---- NEW: flag to indicate a fresh image arrived ----
    private volatile bool newImageAvailable = false;
    private int currentStep = -1;
    private PullSocket imagePullSocket;
    private PushSocket gazePushSocket;
    private readonly object textureLock = new object();


    public Texture2D IncomingTexture
    {
        get => texture;

    }
    
    void Start()
    {

        StartCoroutine(DiscoverPCCoroutine());
    }

    void Update()
    {
        if (newImageAvailable)
        {
            texture.LoadImage(newImageBytes);
            GazePublish();
            newImageAvailable = false;
        }
    }

    IEnumerator DiscoverPCCoroutine()
    {
        UdpClient udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;
        udpClient.Client.ReceiveTimeout = UDP_TIMEOUT_MS;

        // IPEndPoint broadcastEP = new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT);
        IPEndPoint broadcastEP = new IPEndPoint(IPAddress.Parse("192.168.0.208"), DISCOVERY_PORT);
        byte[] discoverBytes = Encoding.UTF8.GetBytes(DISCOVER_MESSAGE);
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        DateTime startTime = DateTime.UtcNow;

        while ((DateTime.UtcNow - startTime).TotalMilliseconds < UDP_TIMEOUT_MS) 
        {
            udpClient.SendAsync(discoverBytes, discoverBytes.Length, broadcastEP);
            Debug.Log("[HL2][UDP] Sent DISCOVER_PC broadcast to " + broadcastEP.Address);
            debugText.text = "[HL2][UDP] Sent DISCOVER_PC broadcast to " + broadcastEP.Address;
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

        if (string.IsNullOrEmpty(pcIpAddress))
        {
            Debug.LogError("[HL2][UDP] Discovery timed out. Cannot find PC.");
            debugText.text = "[HL2][UDP] Discovery timed out. Cannot find PC.";
            yield break;
        }

        StartZmqSockets();
    }

    void StartZmqSockets()
    {
        AsyncIO.ForceDotNet.Force();
        NetMQConfig.Cleanup();

        // --- Start image SUB ---
        // imageSubscriber = new SubscriberSocket();
        // string imageConnectStr = $"tcp://{pcIpAddress}:{ZMQ_IMAGE_PORT}";
        // imageSubscriber.Options.ReceiveHighWatermark = 1;
        // imageSubscriber.Connect(imageConnectStr);
        // imageSubscriber.SubscribeToAnyTopic();


        // Debug.Log($"[HL2][ZMQ] SUBscribed to images at {address}");
        // debugText.text = $"[HL2][ZMQ] SUBscribed to images at {address}";

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
        

        // // --- Start gaze PUB ---
        // gazePublisher = new PublisherSocket();
        // string gazeConnectStr = $"tcp://{pcIpAddress}:{ZMQ_GAZE_PORT}";
        // gazePublisher.Options.SendHighWatermark = 1;
        // gazePublisher.Connect(gazeConnectStr);
        // Debug.Log($"[HL2][ZMQ] Publishing gaze to {gazeConnectStr}");
        // debugText.text = $"[HL2][ZMQ] Publishing gaze to {gazeConnectStr}";
    }

    void ImageReceiveLoop()
    {
        while (imageThreadRunning)
        {
            // try
            // {
                // Receive multi-frame message
                var msg = imagePullSocket.ReceiveMultipartMessage();
                if (msg == null || msg.FrameCount != 2)
                {
                    Debug.LogWarning("[HL2][ZMQ] Received invalid message, waiting for next frame...");
                    continue; // Invalid message, continue to next iteration
                }

                currentStep = msg[0].ConvertToInt32();
                byte[] imageBytes = msg[1].Buffer;
                Debug.Log("[HL2][ZMQ] Received image frame with " + imageBytes.Length + " bytes. Step: " + currentStep);
                newImageBytes = imageBytes;
                newImageAvailable = true;
            // }
            // catch (Exception ex)
            // {
            //     Debug.LogError("[HL2][ZMQ] ImageReceiveLoop exception: " + ex.Message);
            //     debugText.text = "[HL2][ZMQ] ImageReceiveLoop exception: " + ex.Message;
            //     break;
            // }
        }
    }

    void GazePublish()
    {
        // At this point, a new frame has arrived. Send gaze only once per frame:
        Vector2 gazeXY = gazeTracker.GetGazePointOnTexture();
        var gazeObj = new
        {
            x = (int)gazeXY.x,
            y = (int)gazeXY.y,
            step = currentStep
        };
        string gazeJson = JsonUtility.ToJson(gazeObj);
        try
        {
            gazePushSocket.SendFrame(gazeJson);
        }
        catch (Exception ex)
        {
            Debug.LogError("[HL2][ZMQ] Failed to publish gaze: " + ex.Message);
            debugText.text = "[HL2][ZMQ] Failed to publish gaze: " + ex.Message;
        }
        Debug.Log($"[HL2][ZMQ] Published gaze at ({gazeXY.x}, {gazeXY.y}) for step {currentStep}");
        debugText.text = $"[HL2][ZMQ] Published gaze at ({gazeXY.x}, {gazeXY.y}) for step {currentStep}";
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
}
