using UnityEngine;
using MixedReality.Toolkit.Input;
using TMPro;

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
        // check if gaze hit image
        Vector3 hitPoint;
        if (gazeInteractor.TryGetHitInfo(out hitPoint, out _, out _, out _))
        {
            go.transform.position = hitPoint;
            debugText.text = $"Hit Point: {hitPoint}";
        }
    }
}
