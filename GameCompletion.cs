using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class GameCompletion : MonoBehaviour
{
    private MapGenerator mapGenerator;
    private Dictionary<int, Dictionary<string, int>> clusterPoints;  // Track points instead of mastery status

    public HouseNames houseNames;

    void Start()
    {
        mapGenerator = FindObjectOfType<MapGenerator>();
        houseNames = FindObjectOfType<HouseNames>();
    }

    public void OnAskQuestion()
    {
        Debug.Log("GameCompletion OnAskQuestion called");

        Dictionary<string, int> topicPoints = mapGenerator.TopicPoints;  // This should be a new dictionary in MapGenerator to track points
        Dictionary<int, List<string>> clusterLabels = mapGenerator.clusterLabels;

        // Initialize the cluster points dictionary
        clusterPoints = new Dictionary<int, Dictionary<string, int>>();

        foreach (var cluster in clusterLabels)
        {
            int clusterId = cluster.Key;
            List<string> labels = cluster.Value;
            Dictionary<string, int> labelPoints = new Dictionary<string, int>();

            foreach (var label in labels)
            {
                // Check if the label is in the topicPoints dictionary and get its value
                int points = topicPoints.ContainsKey(label) ? topicPoints[label] : 0;
                labelPoints[label] = points;
            }

            clusterPoints[clusterId] = labelPoints;
        }

        // For demonstration, printing the cluster points
        foreach (var cluster in clusterPoints)
        {
            Debug.Log($"Cluster ID: {cluster.Key}");
            foreach (var labelPoints in cluster.Value)
            {
                Debug.Log($"Label: {labelPoints.Key}, Points: {labelPoints.Value}");
            }
        }

        Debug.Log("Triggered");

        houseNames.ChangeCompletedColors(clusterPoints);
    }
}
