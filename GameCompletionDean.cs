using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameCompletion : MonoBehaviour
{
    private MapGenerator mapGenerator;
    private Dictionary<int, Dictionary<string, bool>> clusterMasteryStatus;
    public Dictionary<int, bool> clusterMastery;
    private HashSet<int> displayedClusters;
    private ProgressBar progressBar;


    private HouseNames houseNames;
    public Texture2D[] clusterTextures;
    public RawImage[] clusterRawImages;
    public GameObject rocket;
    public GameObject spawnPlatform;

    private Dictionary<int, Coroutine> hideImageCoroutines;
    private Dictionary<int, float> imageDisplayStartTime;
    private const float DisplayDuration = 5f;



    void Start()
    {
        mapGenerator = FindObjectOfType<MapGenerator>();
        houseNames = FindObjectOfType<HouseNames>();
        progressBar = FindObjectOfType<ProgressBar>();

        if (mapGenerator == null)
        {
            Debug.LogError("MapGenerator component not found. Please check scene setup.");
            return;
        }
        if (houseNames == null)
        {
            Debug.LogError("HouseNames component not found. Please check scene setup.");
            return;
        }
        if (progressBar == null)
        {
            Debug.LogError("ProgressBar component not found. Please check scene setup.");
            return;
        }

        clusterMastery = new Dictionary<int, bool>();
        displayedClusters = new HashSet<int>();
        hideImageCoroutines = new Dictionary<int, Coroutine>();
        imageDisplayStartTime = new Dictionary<int, float>();

        if (clusterTextures.Length != clusterRawImages.Length)
        {
            Debug.LogError("Mismatch between clusterTextures and clusterRawImages array lengths.");
        }

        // Ensure all RawImages are initially hidden
        foreach (var rawImage in clusterRawImages)
        {
            if (rawImage != null)
            {
                rawImage.gameObject.SetActive(false);
            }
        }
    }

    public void OnAskQuestion()
    {
        Debug.Log("GameCompletion OnAskQuestion called");

        if (mapGenerator == null)
        {
            Debug.LogError("mapGenerator is null in OnAskQuestion");
            return;
        }

        Dictionary<string, bool> masteredTopics = mapGenerator.MasteredTopics;
        Dictionary<int, List<string>> clusterLabels = mapGenerator.clusterLabels;

        clusterMasteryStatus = new Dictionary<int, Dictionary<string, bool>>();
        int amountMastered = 0;

        foreach (var cluster in clusterLabels)
        {
            int clusterId = cluster.Key;
            List<string> labels = cluster.Value;
            Dictionary<string, bool> labelMasteryStatus = new Dictionary<string, bool>();

            bool allTopicsMastered = true;
            foreach (var label in labels)
            {
                bool isMastered = masteredTopics.ContainsKey(label) && masteredTopics[label];
                labelMasteryStatus[label] = isMastered;
                allTopicsMastered &= isMastered;
            }

            clusterMastery[clusterId] = allTopicsMastered;
            clusterMasteryStatus[clusterId] = labelMasteryStatus;

            Debug.Log($"Cluster {clusterId} mastery: {allTopicsMastered}");
            if (allTopicsMastered)
            {
                amountMastered++;
            }
        }

        amountMastered++;
        if (progressBar != null)
        {
            progressBar.current = amountMastered;
        }
        else
        {
            Debug.LogError("progressBar is null in OnAskQuestion");
        }

        UpdateClusterImages();
        UpdateHouseNames();
    }

    private void UpdateClusterImages()
    {
        for (int i = 0; i < Mathf.Min(clusterTextures.Length, clusterRawImages.Length); i++)
        {
            int clusterId = i;
            if (clusterMastery.TryGetValue(clusterId, out bool isMastered) && isMastered)
            {
                if (!displayedClusters.Contains(clusterId))
                {
                    DisplayClusterImage(i);
                }
                else if (Time.time - imageDisplayStartTime[clusterId] >= DisplayDuration)
                {
                    HideClusterImage(i);
                }
            }
        }
    }

    private void DisplayClusterImage(int index)
    {
        if (index < 0 || index >= clusterRawImages.Length) return;

        RawImage rawImage = clusterRawImages[index];
        if (rawImage == null || clusterTextures[index] == null)
        {
            Debug.LogError($"RawImage or Texture is null for cluster {index}");
            return;
        }

        rawImage.texture = clusterTextures[index];
        rawImage.gameObject.SetActive(true);

        // Increase the size of the image
        RectTransform rectTransform = rawImage.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.sizeDelta = new Vector2(200, 200); // Set desired width and height
        }

        displayedClusters.Add(index);
        imageDisplayStartTime[index] = Time.time;

        // Cancel any existing hide coroutine for this image
        if (hideImageCoroutines.TryGetValue(index, out Coroutine existingCoroutine))
        {
            if (existingCoroutine != null)
            {
                StopCoroutine(existingCoroutine);
            }
        }

        // Start a new hide coroutine and store its reference
        Coroutine newCoroutine = StartCoroutine(HideImageAfterDelay(rawImage.gameObject, index));
        hideImageCoroutines[index] = newCoroutine;

        Debug.Log($"Image for cluster {index} displayed");
    }


    private void HideClusterImage(int index)
    {
        if (index < 0 || index >= clusterRawImages.Length) return;

        RawImage rawImage = clusterRawImages[index];
        if (rawImage != null)
        {
            rawImage.gameObject.SetActive(false);
            displayedClusters.Remove(index);
            imageDisplayStartTime.Remove(index);
        }
        Debug.Log($"Image for cluster {index} hidden");
    }

    private void UpdateHouseNames()
    {
        if (houseNames != null)
        {
            houseNames.ChangeCompletedColors(clusterMasteryStatus);
        }
        else
        {
            Debug.LogWarning("HouseNames component not found.");
        }
    }

    private IEnumerator HideImageAfterDelay(GameObject imageObject, int index)
    {
        yield return new WaitForSeconds(DisplayDuration);
        if (imageObject != null)
        {
            imageObject.SetActive(false);
            displayedClusters.Remove(index);
            imageDisplayStartTime.Remove(index);
            Debug.Log($"Image for cluster {index} hidden after {DisplayDuration} seconds");
        }
        hideImageCoroutines.Remove(index);
    }

    public void SpawnRocket(Vector3 blockPos)
    {
        Vector3 adjustedPos = AdjustPos(blockPos);
        GameObject rocketObj = Instantiate(rocket, adjustedPos, Quaternion.identity);
    }

    private Vector3 AdjustPos(Vector3 blockPos)
    {
        float blockHeight = spawnPlatform.GetComponent<Renderer>().bounds.size.y;
        float rocketHeight = rocket.GetComponent<Renderer>().bounds.size.y;

        return new Vector3(blockPos.x, blockPos.y + blockHeight / 2 + rocketHeight / 2, blockPos.z);
    }
}
