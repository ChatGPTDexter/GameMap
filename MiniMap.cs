using UnityEngine;
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

public float minX, maxX, minZ, maxZ;
public bool canMiniMap = true;

public GameObject player; // Assign the player object here
public GameObject playerIndicatorPrefab; // Assign the player indicator prefab here
public MonoBehaviour firstPersonController; // Assign the first-person controller script here

private RectTransform playerIndicator;
private RectTransform miniMapRectTransform;
private GameObject playerIndicatorWorld;

void Start()
{
    SetupMiniMap();
}

void Update()
{
    if (isInputEnabled && Input.GetKeyDown(KeyCode.M) && canMiniMap)
    {
        ToggleMiniMap();
    }

    if (isMiniMapVisible)
    {
        UpdatePlayerIndicator();
        HandleZoom();
        HandlePanning();
    }
}

public void SetupMiniMap()
{
    GameObject miniMapCameraObject = new GameObject("MiniMapCamera");
    miniMapCamera = miniMapCameraObject.AddComponent<Camera>();

    miniMapCamera.orthographic = true;
    initialOrthographicSize = Mathf.Max(maxX - minX, maxZ - minZ) / 2f;
    miniMapCamera.orthographicSize = initialOrthographicSize;
    miniMapCamera.transform.position = new Vector3((minX + maxX) / 2, 200, (minZ + maxZ) / 2); // Increase height to 200
    miniMapCamera.transform.rotation = Quaternion.Euler(90, 0, 0);

    // Include all layers in the culling mask
    miniMapCamera.cullingMask = ~0; // ~0 sets the culling mask to include all layers

    miniMapCamera.clearFlags = CameraClearFlags.SolidColor;
    miniMapCamera.backgroundColor = Color.gray;

    miniMapCamera.targetTexture = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32);
    miniMapCamera.gameObject.SetActive(false);

    miniMapUI = new GameObject("MiniMapUI");
    Canvas canvas = miniMapUI.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;

    GameObject rawImageObject = new GameObject("MiniMapImage");
    rawImageObject.transform.parent = miniMapUI.transform;
    RawImage rawImage = rawImageObject.AddComponent<RawImage>();
    rawImage.texture = miniMapCamera.targetTexture;
    miniMapRectTransform = rawImage.GetComponent<RectTransform>();
    miniMapRectTransform.anchorMin = new Vector2(0.75f, 0.75f);
    miniMapRectTransform.anchorMax = new Vector2(1f, 1f);
    miniMapRectTransform.sizeDelta = new Vector2(256, 256);
    miniMapUI.SetActive(false);

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

void CreatePlayerIndicator()
{
    if (playerIndicatorPrefab == null)
    {
        Debug.LogError("Player indicator prefab not assigned.");
        return;
    }

    // Create world indicator
    playerIndicatorWorld = Instantiate(playerIndicatorPrefab, player.transform);
    playerIndicatorWorld.transform.localPosition = new Vector3(0, 10f, 0); // 10 units above the player
    playerIndicatorWorld.transform.localRotation = Quaternion.Euler(90, 0, 0); // Rotate to face up
    playerIndicatorWorld.GetComponent<RectTransform>().sizeDelta = new Vector2(1f, 1f); // Small in world space

    // Create mini-map indicator
    GameObject playerIndicatorObject = Instantiate(playerIndicatorPrefab, miniMapUI.transform);
    playerIndicator = playerIndicatorObject.GetComponent<RectTransform>();

    if (playerIndicator == null)
    {
        Debug.LogError("Player indicator prefab does not have a RectTransform component.");
        return;
    }

    playerIndicator.sizeDelta = new Vector2(1000, 1000); // Bigger size for visibility on mini-map
}

void UpdatePlayerIndicator()
{
    if (player == null || playerIndicator == null)
    {
        return;
    }

    // Update the world indicator position and rotation
    playerIndicatorWorld.transform.position = player.transform.position + new Vector3(0, 10f, 0);
    playerIndicatorWorld.transform.localRotation = Quaternion.Euler(90, 0, 0); // Ensure it faces up

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
