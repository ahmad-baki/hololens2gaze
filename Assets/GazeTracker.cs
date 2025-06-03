using UnityEngine;
using MixedReality.Toolkit.Input;

public class GazeTracker : MonoBehaviour
{
    [SerializeField]
    private GameObject go;
    [SerializeField]
    private float rayMaxDistance = 10f;
    [SerializeField]
    private FuzzyGazeInteractor gazeInteractor;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        // check if gaze hit image
        RaycastHit hit;
        if (Physics.Raycast(gazeInteractor.rayOriginTransform.position, gazeInteractor.rayOriginTransform.forward, out hit, rayMaxDistance))
        {
            // If we hit something, check if it's an image

            if (hit.collider.gameObject.layer == LayerMask.NameToLayer("UI"))
            {
                Vector2 hitTextureCoord = hit.textureCoord;
                Debug.Log($"Gaze hit image at texture coordinates: {hitTextureCoord}");
                go.transform.position = hit.point; // Move the GameObject to the hit point
            }
        }

    }
}
