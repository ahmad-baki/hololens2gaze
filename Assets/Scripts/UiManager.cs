using UnityEngine;
using UnityEngine.UI;

public class UiManager : MonoBehaviour
{
    [SerializeField]
    private RawImage imageDisplay;
    private NetworkManager networkManager;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        networkManager = FindAnyObjectByType<NetworkManager>();
        if (networkManager == null)
        {
            Debug.LogError("NetworkManager component not found in the scene.");
            return;
        }
        NetworkManager.OnImageReady += InitImageDisplay;
    }

    // Update is called once per frame
    void Update()
    {

    }

    private void InitImageDisplay()
    {
        var texture = networkManager.IncomingTexture;
        if (texture != null)
        {
            imageDisplay.texture = texture;
        }
    }
}
