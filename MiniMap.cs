using UnityEngine;
using System.Collections.Generic;
using System.IO;
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
    private const float maxZoomLevel = 10f; // Increase max zoom level for more detail
    private float initialOrthographicSize;
    private float zoomSpeed = 0.5f;
    private Vector3 lastMousePosition;

    public TextAsset housePositionsCsvFile; // CSV file with house positions
    public GameObject houseLabelPrefab;
    public float minX, maxX, minZ, maxZ;

    public GameObject player; // Assign the player object here
    public GameObject playerIndicatorPrefab; // Assign the player indicator prefab here
    public MonoBehaviour firstPersonController; // Assign the first-person controller script here

    public Dictionary<string, Vector3> housePositions = new Dictionary<string, Vector3>();
    private Dictionary<string, TMP_Text> houseLabels = new Dictionary<string, TMP_Text>();
    private RectTransform playerIndicator;
    private RectTransform miniMapRectTransform;

    void Start()
    {
        LoadHousePositions();
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
            HandleZoom();
            HandlePanning();
        }
    }

    void LoadHousePositions()
    {
        if (housePositionsCsvFile == null)
        {
            Debug.LogError("CSV file is not assigned.");
            return;
        }

        string[] lines = housePositionsCsvFile.text.Split('\n');

        foreach (string line in lines)
        {
            string[] values = line.Split(',');
            if (values.Length >= 4)
            {
                string label = values[0];
                if (float.TryParse(values[1], out float x) && float.TryParse(values[2], out float z) && float.TryParse(values[3], out float y))
                {
                    housePositions[label] = new Vector3(x, y, z);
                }
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
        miniMapRectTransform = rawImage.GetComponent<RectTransform>();
        miniMapRectTransform.anchorMin = new Vector2(0.75f, 0.75f);
        miniMapRectTransform.anchorMax = new Vector2(1f, 1f);
        miniMapRectTransform.sizeDelta = new Vector2(256, 256);
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

        if (isMiniMapVisible)
        {
            if (firstPersonController != null)
            {
                firstPersonController.enabled = false;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            if (firstPersonController != null)
            {
                firstPersonController.enabled = true;
            }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
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
        if (labelRectTransform == null)
        {
            Debug.LogError("House label prefab does not have a RectTransform component.");
            return;
        }

        Vector2 miniMapPosition = WorldToMiniMapPosition(position);
        labelRectTransform.anchoredPosition = miniMapPosition;
        houseLabel.SetActive(false); // Initially disable the label
    }

    void CreatePlayerIndicator()
    {
        if (playerIndicatorPrefab == null)
        {
            Debug.LogError("Player indicator prefab not assigned.");
            return;
        }

        GameObject playerIndicatorObject = Instantiate(playerIndicatorPrefab, miniMapUI.transform);
        playerIndicator = playerIndicatorObject.GetComponent<RectTransform>();

        if (playerIndicator == null)
        {
            Debug.LogError("Player indicator prefab does not have a RectTransform component.");
            return;
        }

        playerIndicator.sizeDelta = new Vector2(10, 10); // Adjust the size of the player indicator
    }

    void UpdateLabels()
    {
        Vector2 localMousePosition;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(miniMapRectTransform, Input.mousePosition, null, out localMousePosition);
        Vector3 cursorWorldPosition = MiniMapToWorldPosition(localMousePosition);
        Vector3 closestHousePosition = Vector3.zero;
        string closestHouseLabel = null;
        float closestDistance = float.MaxValue;

        foreach (var houseEntry in housePositions)
        {
            float distance = Vector3.Distance(cursorWorldPosition, houseEntry.Value);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestHousePosition = houseEntry.Value;
                closestHouseLabel = houseEntry.Key;
            }
        }

        foreach (var houseEntry in housePositions)
        {
            if (houseLabels.TryGetValue(houseEntry.Key, out TMP_Text label) && label != null)
            {
                label.gameObject.SetActive(houseEntry.Key == closestHouseLabel);

                RectTransform labelRectTransform = label.GetComponent<RectTransform>();
                if (labelRectTransform != null)
                {
                    Vector2 miniMapPosition = WorldToMiniMapPosition(houseEntry.Value);
                    labelRectTransform.anchoredPosition = miniMapPosition;
                }
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

        return new Vector2(x * miniMapRectTransform.sizeDelta.x, z * miniMapRectTransform.sizeDelta.y); // Adjust to the mini-map UI size
    }

    Vector3 MiniMapToWorldPosition(Vector2 miniMapPosition)
    {
        float mapWidth = maxX - minX;
        float mapHeight = maxZ - minZ;

        float x = (miniMapPosition.x / miniMapRectTransform.sizeDelta.x) * mapWidth + minX;
        float z = (miniMapPosition.y / miniMapRectTransform.sizeDelta.y) * mapHeight + minZ;

        return new Vector3(x, 0, z);
    }

    bool IsMouseOverMiniMap()
    {
        Vector2 localMousePosition = miniMapRectTransform.InverseTransformPoint(Input.mousePosition);
        return miniMapRectTransform.rect.Contains(localMousePosition);
    }

    void HandleZoom()
    {
        if (IsMouseOverMiniMap())
        {
            if (Input.GetKey(KeyCode.Alpha1))
            {
                ZoomAtMousePosition(1);
            }
            if (Input.GetKey(KeyCode.Alpha2))
            {
                ZoomAtMousePosition(-1);
            }
        }
    }

    void ZoomAtMousePosition(float direction)
    {
        Vector2 localMousePosition;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(miniMapRectTransform, Input.mousePosition, null, out localMousePosition);

        Vector3 miniMapWorldPosition = MiniMapToWorldPosition(localMousePosition);

        float newZoomLevel = Mathf.Clamp(zoomLevel + direction * zoomSpeed * Time.deltaTime, minZoomLevel, maxZoomLevel);
        if (newZoomLevel != zoomLevel)
        {
            Vector3 directionToMouse = miniMapCamera.transform.position - miniMapWorldPosition;
            float zoomFactor = zoomLevel / newZoomLevel;
            miniMapCamera.transform.position = miniMapWorldPosition + directionToMouse * zoomFactor;
            miniMapCamera.orthographicSize = initialOrthographicSize / newZoomLevel;
            zoomLevel = newZoomLevel;
        }
    }

    void HandlePanning()
    {
        if (Input.GetMouseButtonDown(0))
        {
            lastMousePosition = Input.mousePosition;
        }

        if (Input.GetMouseButton(0))
        {
            Vector3 delta = Input.mousePosition - lastMousePosition;
            Vector3 translation = new Vector3(-delta.x, 0, -delta.y) * (miniMapCamera.orthographicSize / initialOrthographicSize);
            miniMapCamera.transform.Translate(translation, Space.World);
            lastMousePosition = Input.mousePosition;
        }
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
