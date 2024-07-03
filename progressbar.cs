using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ProgressBar : MonoBehaviour
{
    public int max;
    public int current;
    public Image Mask;
    public GameObject ProgressCanvas;

    // Start is called before the first frame update
    void Start()
    {
        // Initially hide the progress bar
        HideProgressBar();

        // Optionally, position the ProgressCanvas in the top right corner
        PositionInTopRightCorner();
    }

    // Update is called once per frame
    void Update()
    {
        GetCurrentFill();
    }

    void GetCurrentFill()
    {
        float fillAmount = (float)current / (float)max;
        Mask.fillAmount = fillAmount;
    }

    public void DisplayProgressBar()
    {
        ProgressCanvas.SetActive(true);
    }

    public void HideProgressBar()
    {
        ProgressCanvas.SetActive(false);
    }

    private void PositionInTopRightCorner()
    {
        RectTransform canvasRect = ProgressCanvas.GetComponent<RectTransform>();
        if (canvasRect != null)
        {
            // Set the anchor to top right
            canvasRect.anchorMin = new Vector2(1, 1);
            canvasRect.anchorMax = new Vector2(1, 1);
            canvasRect.pivot = new Vector2(1, 1);

            // Set the position
            canvasRect.anchoredPosition = new Vector2(-10, -10); // Adjust the offset as needed

            Vector2 originalSize = canvasRect.sizeDelta;
            canvasRect.sizeDelta = new Vector2(originalSize.x * 1.5f, originalSize.y);
        }
        else
        {
            Debug.LogError("RectTransform not found on ProgressCanvas.");
        }
    }
}
