using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class HouseManager : MonoBehaviour
{
    public Dictionary<int, Dictionary<string, bool>> clusterMasteryStatus = new Dictionary<int, Dictionary<string, bool>>();
    public Dictionary<string, string> nameMappings = new Dictionary<string, string>();
    public List<GameObject> nameObjects = new List<GameObject>();

    public void UpdateClusterMasteryStatus()
    {
        // Assume this method updates clusterMasteryStatus based on the current game state
        ChangeCompletedColors(clusterMasteryStatus);
    }

    public void ChangeCompletedColors(Dictionary<int, Dictionary<string, bool>> clusterMasteryStatus)
    {
        foreach (var cluster in clusterMasteryStatus)
        {
            foreach (var labelStatus in cluster.Value)
            {
                string label = labelStatus.Key.Trim(); // Trim whitespace from label
                bool isMastered = labelStatus.Value;
                // Check if the label exists in nameMappings
                if (nameMappings.ContainsKey(label))
                {
                    string generatedName = nameMappings[label];
                    // Find all TextMeshPro objects that match the generated name
                    foreach (var nameObject in nameObjects)
                    {
                        TextMeshPro textMesh = nameObject.GetComponent<TextMeshPro>();
                        if (textMesh != null && textMesh.text.Trim() == generatedName)
                        {
                            // Change color based on mastery status
                            textMesh.color = isMastered ? Color.green : Color.white;
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"Label '{label}' not found in nameMappings.");
                }
            }
        }
    }
}
