using UnityEngine;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;

public class MiniMapController : MonoBehaviour
{
    private Camera miniMapCamera;
    private GameObject miniMapUI;
    private bool isMiniMapVisible = false;
    private bool isInputEnabled = true;
    private float zoomLevel = 1f;
    private const float minZoomLevel = 1f;
    private const float maxZoomLevel = 5f;
    private float initialOrthographicSize;

    public Dictionary<string, Vector3> housePositions;
    public GameObject houseLabelPrefab;
    public float minX, maxX, minZ, maxZ;

    public GameObject player; // Assign the player object here
    public GameObject playerIndicatorPrefab; // Assign the player indicator prefab here

    private Dictionary<string, TMP_Text> houseLabels = new Dictionary<string, TMP_Text>();
    private RectTransform playerIndicator;

    void Start()
    {
        SetupMiniMap();
    }

    void Update()
    {
        if (isInputEnabled && Input.GetKeyDown(KeyCode.M))
        {
            ToggleMiniMap();
        }

        if (isMiniMapVisible)
        {
            UpdateLabels();
            UpdatePlayerIndicator();

            if (Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.Plus))
            {
                ZoomIn();
            }

            if (Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.Underscore))
            {
                ZoomOut();
            }
        }
    }

    public void SetupMiniMap()
    {
        GameObject miniMapCameraObject = new GameObject("MiniMapCamera");
        miniMapCamera = miniMapCameraObject.AddComponent<Camera>();

        miniMapCamera.orthographic = true;
        initialOrthographicSize = Mathf.Max(maxX - minX, maxZ - minZ) / 2f;
        miniMapCamera.orthographicSize = initialOrthographicSize;
        miniMapCamera.transform.position = new Vector3((minX + maxX) / 2, 100, (minZ + maxZ) / 2);
        miniMapCamera.transform.rotation = Quaternion.Euler(90, 0, 0);
        miniMapCamera.cullingMask = LayerMask.GetMask("Default");

        miniMapCamera.targetTexture = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32);
        miniMapCamera.gameObject.SetActive(false);

        miniMapUI = new GameObject("MiniMapUI");
        Canvas canvas = miniMapUI.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        GameObject rawImageObject = new GameObject("MiniMapImage");
        rawImageObject.transform.parent = miniMapUI.transform;
        UnityEngine.UI.RawImage rawImage = rawImageObject.AddComponent<UnityEngine.UI.RawImage>();
        rawImage.texture = miniMapCamera.targetTexture;
        RectTransform rectTransform = rawImage.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.75f, 0.75f);
        rectTransform.anchorMax = new Vector2(1f, 1f);
        rectTransform.sizeDelta = new Vector2(256, 256);
        miniMapUI.SetActive(false);

        foreach (var houseEntry in housePositions)
        {
            CreateHouseLabel(houseEntry.Key, houseEntry.Value);
        }

        CreatePlayerIndicator();
    }

    void ToggleMiniMap()
    {
        isMiniMapVisible = !isMiniMapVisible;
        miniMapCamera.gameObject.SetActive(isMiniMapVisible);
        miniMapUI.SetActive(isMiniMapVisible);
    }

    void CreateHouseLabel(string label, Vector3 position)
    {
        if (houseLabelPrefab == null)
        {
            Debug.LogError("House label prefab not assigned.");
            return;
        }

        GameObject houseLabel = Instantiate(houseLabelPrefab, miniMapUI.transform);
        TMP_Text labelText = houseLabel.GetComponent<TMP_Text>();
        if (labelText == null)
        {
            Debug.LogError("House label prefab does not have a TMP_Text component.");
            return;
        }

        labelText.text = label;
        houseLabels[label] = labelText;

        RectTransform labelRectTransform = houseLabel.GetComponent<RectTransform>();
        Vector2 miniMapPosition = WorldToMiniMapPosition(position);
        labelRectTransform.anchoredPosition = miniMapPosition;
    }

    void CreatePlayerIndicator()
    {
        if (playerIndicatorPrefab == null)
        {
            Debug.LogError("Player indicator prefab not assigned.");
            return;
        }

        GameObject playerIndicator = Instantiate(playerIndicatorPrefab, miniMapUI.transform);
        RectTransform rectTransform = playerIndicator.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(10, 10); // Adjust the size of the player indicator
        this.playerIndicator = rectTransform;
    }

    void UpdateLabels()
    {
        foreach (var houseEntry in housePositions)
        {
            if (houseLabels.TryGetValue(houseEntry.Key, out TMP_Text label))
            {
                RectTransform labelRectTransform = label.GetComponent<RectTransform>();
                Vector2 miniMapPosition = WorldToMiniMapPosition(houseEntry.Value);
                labelRectTransform.anchoredPosition = miniMapPosition;
            }
        }
    }

    void UpdatePlayerIndicator()
    {
        if (player == null || playerIndicator == null)
        {
            return;
        }

        Vector2 miniMapPosition = WorldToMiniMapPosition(player.transform.position);
        playerIndicator.anchoredPosition = miniMapPosition;
    }

    Vector2 WorldToMiniMapPosition(Vector3 worldPosition)
    {
        float mapWidth = maxX - minX;
        float mapHeight = maxZ - minZ;
        float x = (worldPosition.x - minX) / mapWidth;
        float z = (worldPosition.z - minZ) / mapHeight;

        return new Vector2(x * 256, z * 256); // Adjust to the mini-map UI size
    }

    void ZoomIn()
    {
        zoomLevel = Mathf.Clamp(zoomLevel - 0.1f, minZoomLevel, maxZoomLevel);
        miniMapCamera.orthographicSize = initialOrthographicSize / zoomLevel;
    }

    void ZoomOut()
    {
        zoomLevel = Mathf.Clamp(zoomLevel + 0.1f, minZoomLevel, maxZoomLevel);
        miniMapCamera.orthographicSize = initialOrthographicSize / zoomLevel;
    }

    public void DisableInput()
    {
        isInputEnabled = false;
    }

    public void EnableInput()
    {
        isInputEnabled = true;
    }
}
