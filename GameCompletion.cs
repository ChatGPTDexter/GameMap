using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class GameCompletion : MonoBehaviour
{
    private MapGenerator mapGenerator;
    private Dictionary<int, Dictionary<string, bool>> clusterMasteryStatus;

    public HouseNames houseNames;

    void Start()
    {
        mapGenerator = FindObjectOfType<MapGenerator>();
        houseNames = FindObjectOfType<HouseNames>();
    }

    public void OnAskQuestion()
    {
        Debug.Log("GameCompletion OnAskQuestion called");
        // Assuming clusterLabels and masteredTopics are obtained from mapGenerator

        Dictionary<string, bool> masteredTopics = mapGenerator.MasteredTopics;
        Dictionary<int, List<string>> clusterLabels = mapGenerator.clusterLabels;

        // Initialize the cluster mastery status dictionary
        clusterMasteryStatus = new Dictionary<int, Dictionary<string, bool>>();

        foreach (var cluster in clusterLabels)
        {
            int clusterId = cluster.Key;
            List<string> labels = cluster.Value;
            Dictionary<string, bool> labelMasteryStatus = new Dictionary<string, bool>();

            foreach (var label in labels)
            {
                // Check if the label is in the masteredTopics dictionary and get its value
                bool isMastered = masteredTopics.ContainsKey(label) && masteredTopics[label];
                labelMasteryStatus[label] = isMastered;
            }

            clusterMasteryStatus[clusterId] = labelMasteryStatus;
        }

        // For demonstration, printing the cluster mastery status
        foreach (var cluster in clusterMasteryStatus)
        {
            Debug.Log($"Cluster ID: {cluster.Key}");
            foreach (var labelStatus in cluster.Value)
            {
                Debug.Log($"Label: {labelStatus.Key}, Mastered: {labelStatus.Value}");
            }
        }

        Debug.Log("Triggered");

        houseNames.ChangeCompletedColors(clusterMasteryStatus);
    }
}
