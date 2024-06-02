using UnityEngine;
using System.IO;
using System.Globalization;
using System.Collections.Generic;

public class MapGenerator : MonoBehaviour
{
    public TextAsset houseCsvFile; // Assign your house CSV file here in the Inspector
    public TextAsset mstCsvFile; // Assign your MST CSV file here in the Inspector
    public GameObject housePrefab; // Assign your house prefab here
    public GameObject roadPrefab; // Assign your road prefab here
    public Material roadMaterial; // Assign your desired road material here

    private Dictionary<string, Vector3> housePositions = new Dictionary<string, Vector3>();

    void Start()
    {
        if (houseCsvFile == null || mstCsvFile == null)
        {
            Debug.LogError("CSV files not assigned. Please assign CSV files in the Inspector.");
            return;
        }

        if (housePrefab == null || roadPrefab == null)
        {
            Debug.LogError("Prefabs not assigned. Please assign prefabs in the Inspector.");
            return;
        }

        GenerateMapFromCSV();
        GenerateRoadsFromMST();
    }

    void GenerateMapFromCSV()
    {
        StringReader reader = new StringReader(houseCsvFile.text);

        // Skip the header row
        string headerLine = reader.ReadLine();
        if (headerLine == null)
        {
            Debug.LogError("House CSV file is empty.");
            return;
        }

        while (reader.Peek() != -1)
        {
            string line = reader.ReadLine();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            string[] fields = ParseCSVLine(line);

            if (fields.Length < 4)
            {
                Debug.LogWarning($"Skipping invalid row: {line}");
                continue;
            }

            string label = fields[0];
            if (float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) &&
                float.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                Vector3 position = new Vector3(x, y, z);
                Instantiate(housePrefab, position, Quaternion.identity);
                housePositions[label] = position;
            }
            else
            {
                Debug.LogWarning($"Failed to parse coordinates: {line}");
            }
        }
    }

    void GenerateRoadsFromMST()
    {
        StringReader reader = new StringReader(mstCsvFile.text);

        // Skip the header row
        string headerLine = reader.ReadLine();
        if (headerLine == null)
        {
            Debug.LogError("MST CSV file is empty.");
            return;
        }

        while (reader.Peek() != -1)
        {
            string line = reader.ReadLine();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            string[] fields = ParseCSVLine(line);

            if (fields.Length < 3)
            {
                Debug.LogWarning($"Skipping invalid row: {line}");
                continue;
            }

            string startNode = fields[1];
            string endNode = fields[2];

            if (housePositions.TryGetValue(startNode, out Vector3 startPos) && housePositions.TryGetValue(endNode, out Vector3 endPos))
            {
                Vector3 direction = (endPos - startPos).normalized;
                float roadLength = roadPrefab.transform.localScale.z;
                float distance = Vector3.Distance(startPos, endPos);

                // Adjust end positions to stop before reaching the house
                Vector3 adjustedStartPos = startPos + direction * roadLength / 2;
                Vector3 adjustedEndPos = endPos - direction * roadLength / 2;

                distance = Vector3.Distance(adjustedStartPos, adjustedEndPos);
                int numSegments = Mathf.CeilToInt(distance / roadLength);
                Quaternion rotation = Quaternion.LookRotation(direction);
                rotation *= Quaternion.Euler(270, 0, 0);

                Debug.Log($"Creating road from {startNode} to {endNode}, Distance: {distance}, Segments: {numSegments}");

                for (int i = 0; i <= numSegments; i++)
                {
                    float t = (float)i / numSegments;
                    Vector3 segmentPos = Vector3.Lerp(adjustedStartPos, adjustedEndPos, t);
                    segmentPos.y = -5; // Set the y-coordinate to -5
                    GameObject roadSegment = Instantiate(roadPrefab, segmentPos, rotation);
                    roadSegment.transform.localScale = new Vector3(0.25f, 0.25f, roadSegment.transform.localScale.z); // Scale the road segment

                    if (roadMaterial != null)
                    {
                        roadSegment.GetComponent<Renderer>().material = roadMaterial; // Change the material of the road
                    }
                }
            }
            else
            {
                Debug.LogWarning($"Failed to find positions for nodes: {startNode}, {endNode}");
            }
        }
    }

    string[] ParseCSVLine(string line)
    {
        List<string> fields = new List<string>();
        bool inQuotes = false;
        string field = "";

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(field);
                field = "";
            }
            else
            {
                field += c;
            }
        }

        if (field.Length > 0)
        {
            fields.Add(field);
        }

        return fields.ToArray();
    }
}
