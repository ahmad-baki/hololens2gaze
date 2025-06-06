using UnityEngine;
using MixedReality.Toolkit.Input;
using UnityEngine.Assertions;
using TMPro;
using System;

public class GazeTracker : MonoBehaviour
{
    [SerializeField]
    private GameObject go;
    [SerializeField]
    private float rayMaxDistance = 100f;
    [SerializeField]
    private TMP_Text debugText;
    private FuzzyGazeInteractor gazeInteractor;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // Get the FuzzyGazeInteractor component from the GameObject
        gazeInteractor = FindAnyObjectByType<FuzzyGazeInteractor>();
        if (gazeInteractor == null)
        {
            Debug.LogError("FuzzyGazeInteractor component not found on this GameObject.");
            debugText.text = "FuzzyGazeInteractor not found.";
            return;
        }

        // Ensure the GameObject is set
        if (go == null)
        {
            Debug.LogError("GameObject to move is not assigned.");
            debugText.text = "GameObject to move is not assigned.";
            return;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (gazeInteractor.TryGetCurrentRaycast(out RaycastHit? hitInfo, out _, out _, out _, out _) && hitInfo.HasValue)
        {
            GameObject hitGO = hitInfo.Value.collider.gameObject;
            Vector3 hitPoint = hitInfo.Value.point;
            RectTransform hitRect = hitGO.GetComponent<RectTransform>();

            Assert.IsNotNull(hitRect, "Hit GameObject does not have a RectTransform component.");
            // assumes that pivot is at the center of the RectTransform
            // (0, 0) is the bottom-left corner, (1, 1) is the top-right corner

            Vector3 canvasPos = hitGO.transform.parent.position;
            Vector2 uvPoint = new Vector2(
                (hitPoint.x - canvasPos.x - hitRect.offsetMin.x) / hitRect.rect.width,
                (hitPoint.y - canvasPos.y - hitRect.offsetMin.y) / hitRect.rect.height
            );
            debugText.text = $"Object: {hitGO.name}, 3D Position: {hitPoint}, UV Coordinates: {uvPoint}";
            Debug.Log($"UV Coordinates: {uvPoint}, offset: {hitRect.offsetMin}, {hitRect.offsetMax}");
            go.transform.position = hitPoint;
        }
        else
        {
            debugText.text = "No hit detected.";
        }
    }
    
    public Vector2 GetGazePointOnTexture()
    {
        if (gazeInteractor.TryGetCurrentRaycast(out RaycastHit? hitInfo, out _, out _, out _, out _) && hitInfo.HasValue)
        {
            GameObject hitGO = hitInfo.Value.collider.gameObject;
            RectTransform hitRect = hitGO.GetComponent<RectTransform>();

            Assert.IsNotNull(hitRect, "Hit GameObject does not have a RectTransform component.");
            Vector3 canvasPos = hitGO.transform.parent.position;

            return new Vector2(
                (hitInfo.Value.point.x - canvasPos.x - hitRect.offsetMin.x) / hitRect.rect.width,
                (hitInfo.Value.point.y - canvasPos.y - hitRect.offsetMin.y) / hitRect.rect.height
            );
        }
        return Vector2.zero;
    }
}
