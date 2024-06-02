using UnityEngine;
using System.IO;
using System.Globalization;
using System.Collections.Generic;

public class MapGenerator : MonoBehaviour
{
    public TextAsset houseCsvFile; // Assign your house CSV file here in the Inspector
    public TextAsset mstCsvFile; // Assign your MST CSV file here in the Inspector
    public GameObject housePrefab; // Assign your house prefab here
    public Terrain terrain; // Assign the Terrain object here in the Inspector
    public float elevationFactor = 0.01f; // Factor to control terrain elevation

    private Dictionary<string, Vector3> housePositions = new Dictionary<string, Vector3>();

    void Start()
    {
        Debug.Log("MapGenerator Start");

        if (houseCsvFile == null || mstCsvFile == null)
        {
            Debug.LogError("CSV files not assigned. Please assign CSV files in the Inspector.");
            return;
        }

        if (housePrefab == null)
        {
            Debug.LogError("Prefabs not assigned. Please assign prefabs in the Inspector.");
            return;
        }

        if (terrain == null)
        {
            Debug.LogError("Terrain not assigned. Please assign Terrain in the Inspector.");
            return;
        }

        GenerateMapFromCSV();
    }

    void GenerateMapFromCSV()
    {
        Debug.Log("GenerateMapFromCSV");

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
                int.TryParse(fields[3], out int viewCount))
            {
                Vector3 position = new Vector3(x, 0, z);
                housePositions[label] = position;
                ElevateTerrainAround(position, viewCount);
                position.y = terrain.SampleHeight(position);
                Instantiate(housePrefab, position, Quaternion.identity);
            }
            else
            {
                Debug.LogWarning($"Failed to parse coordinates or view count: {line}");
            }
        }
    }

    void ElevateTerrainAround(Vector3 position, int viewCount)
    {
        Debug.Log($"Elevating terrain around position: {position} with view count: {viewCount}");

        TerrainData terrainData = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;

        int xResolution = terrainData.heightmapResolution;
        int zResolution = terrainData.heightmapResolution;

        // Convert position to terrain coordinates
        float relativeX = (position.x - terrainPos.x) / terrainData.size.x * xResolution;
        float relativeZ = (position.z - terrainPos.z) / terrainData.size.z * zResolution;

        // Define the area around the position to elevate
        int radius = 10; // Adjust the radius as needed
        int startX = Mathf.Clamp(Mathf.RoundToInt(relativeX) - radius, 0, xResolution - 1);
        int startZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) - radius, 0, zResolution - 1);
        int endX = Mathf.Clamp(Mathf.RoundToInt(relativeX) + radius, 0, xResolution - 1);
        int endZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) + radius, 0, zResolution - 1);

        Debug.Log($"Elevating terrain at range: ({startX},{startZ}) to ({endX},{endZ})");

        // Get the current heights
        float[,] heights = terrainData.GetHeights(startX, startZ, endX - startX, endZ - startZ);

        // Calculate height increment based on view count and maximum elevation
        float maxElevation = Mathf.Min(50f, viewCount * elevationFactor) / terrainData.size.y;

        // Iterate over the terrain area and elevate based on distance from position
        for (int x = 0; x < heights.GetLength(0); x++)
        {
            for (int z = 0; z < heights.GetLength(1); z++)
            {
                // Calculate distance from position
                float distance = Vector2.Distance(new Vector2(x, z), new Vector2(relativeX - startX, relativeZ - startZ));

                // Calculate height increment based on distance and maximum elevation
                float heightIncrement = Mathf.Lerp(0, maxElevation, 1 - (distance / radius));

                // Apply height increment to terrain height
                heights[x, z] += heightIncrement;
            }
        }

        // Set the modified heights back to the terrain
        terrainData.SetHeights(startX, startZ, heights);

        Debug.Log("Terrain elevation applied");
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
