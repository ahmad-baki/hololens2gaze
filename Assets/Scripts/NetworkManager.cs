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

public class NetworkManager : MonoBehaviour
{
    public static event Action OnImageReady;


    // --- UDP discovery parameters (unchanged) ---
    private const int DISCOVERY_PORT = 5005;
    private const string DISCOVER_MESSAGE  = "DISCOVER_PC";
    private const string DISCOVERY_REPLY   = "PC_HERE";
    private const int    UDP_TIMEOUT_MS    = 5000;

    private string pcIpAddress = null;

    // --- ZMQ endpoints (unchanged) ---
    private const int    ZMQ_IMAGE_PORT = 5556;  
    private const int    ZMQ_GAZE_PORT  = 5557;  

    private SubscriberSocket imageSubscriber;
    private PublisherSocket  gazePublisher;

    private Thread imageThread;
    private bool   imageThreadRunning = false;

    private Texture2D incomingTexture ;

    // ---- NEW: flag to indicate a fresh image arrived ----
    private volatile bool newImageAvailable = false;
    private int currentStep;
    private readonly object textureLock = new object();

    void Start()
    {
        StartCoroutine(DiscoverPCCoroutine());
    }

    public Texture2D IncomingTexture
    {
        get => incomingTexture;

    }

    IEnumerator DiscoverPCCoroutine()
    {
        UdpClient udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;
        udpClient.Client.ReceiveTimeout = UDP_TIMEOUT_MS;

        IPEndPoint broadcastEP = new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT);
        byte[] discoverBytes = Encoding.UTF8.GetBytes(DISCOVER_MESSAGE);

        udpClient.Send(discoverBytes, discoverBytes.Length, broadcastEP);
        Debug.Log("[HL2][UDP] Sent DISCOVER_PC broadcast.");

        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        DateTime startTime = DateTime.UtcNow;

        while ((DateTime.UtcNow - startTime).TotalMilliseconds < UDP_TIMEOUT_MS)
        {
            try
            {
                byte[] data = udpClient.Receive(ref remoteEP);
                string text = Encoding.UTF8.GetString(data);
                if (text == DISCOVERY_REPLY)
                {
                    pcIpAddress = remoteEP.Address.ToString();
                    Debug.Log($"[HL2][UDP] Received PC_HERE from {pcIpAddress}");
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
            yield break;
        }

        StartZmqSockets();
    }

    void StartZmqSockets()
    {
        AsyncIO.ForceDotNet.Force();
        NetMQConfig.Cleanup();

        // --- Start image SUB ---
        imageSubscriber = new SubscriberSocket();
        string imageConnectStr = $"tcp://{pcIpAddress}:{ZMQ_IMAGE_PORT}";
        imageSubscriber.Options.ReceiveHighWatermark = 1;
        imageSubscriber.Connect(imageConnectStr);
        imageSubscriber.SubscribeToAnyTopic();
        Debug.Log($"[HL2][ZMQ] SUBscribed to images at {imageConnectStr}");

        incomingTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);

        imageThreadRunning = true;
        imageThread = new Thread(ImageReceiveLoop);
        imageThread.IsBackground = true;
        imageThread.Start();

        // --- Start gaze PUB ---
        gazePublisher = new PublisherSocket();
        string gazeConnectStr = $"tcp://{pcIpAddress}:{ZMQ_GAZE_PORT}";
        gazePublisher.Options.SendHighWatermark = 1;
        gazePublisher.Connect(gazeConnectStr);
        Debug.Log($"[HL2][ZMQ] PUBlishing gaze to {gazeConnectStr}");

        // 2c) Begin the coroutine that waits for newImageAvailable
        StartCoroutine(GazePublishCoroutine());
    }

    void ImageReceiveLoop()
    {
        while (imageThreadRunning)
        {
            try
            {
                var msg = imageSubscriber.ReceiveMultipartMessage();

                lock (textureLock)
                {
                    var headerFrame = msg[0].Buffer;
                    if (headerFrame.Length < 1) continue;
                    currentStep = headerFrame[0];

                    // Frame 1 is the JPEG payload
                    var jpgBytes = msg[1].ToByteArray();

                    bool updated = IncomingTexture.LoadImage(jpgBytes);
                    if (updated)
                    {
                        // Mark that a fresh image has arrived:
                        newImageAvailable = true;
                    }
                    else
                    {
                        Debug.LogWarning("[HL2][ZMQ] Failed to decode JPEG.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[HL2][ZMQ] ImageReceiveLoop exception: " + ex.Message);
                break;
            }
        }
    }

    IEnumerator GazePublishCoroutine()
    {
        while (true)
        {
            // Wait until ImageReceiveLoop sets newImageAvailable = true
            yield return new WaitUntil(() => newImageAvailable);

            // At this point, a new frame has arrived. Send gaze only once per frame:
            Vector2 gazeXY = GetGazePointOnTexture();
            var gazeObj = new
            {
                x         = (int)gazeXY.x,
                y         = (int)gazeXY.y,
                step = currentStep
            };
            string gazeJson = JsonUtility.ToJson(gazeObj);

            try
            {
                gazePublisher.SendFrame(gazeJson);
            }
            catch (Exception ex)
            {
                Debug.LogError("[HL2][ZMQ] Failed to publish gaze: " + ex.Message);
            }

            // Reset the flag so we wait for the next image:
            newImageAvailable = false;

            // Immediately loop back and wait for the next image.
            // (You can insert a tiny delay here if needed, e.g. yield return null;)
        }
    }

    // Example placeholder for eye-tracking → texture‐space coords:
    private Vector2 GetGazePointOnTexture()
    {
        if (IncomingTexture != null)
        {
            return new Vector2(IncomingTexture.width / 2f, IncomingTexture.height / 2f);
        }
        return Vector2.zero;
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
