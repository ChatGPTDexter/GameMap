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
    public Terrain terrain; // Assign the Terrain object here in the Inspector
    public float elevationFactor = 0.01f; // Factor to control terrain elevation
    public Material roadMaterial; // Assign the material for the roads here

    private Dictionary<string, Vector3> housePositions = new Dictionary<string, Vector3>();
    private float[,] originalHeights; // Store original terrain heights
    private float minX = float.MaxValue, maxX = float.MinValue;
    private float minZ = float.MaxValue, maxZ = float.MinValue;

    void Start()
    {
        Debug.Log("MapGenerator Start");

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

        if (terrain == null)
        {
            Debug.LogError("Terrain not assigned. Please assign Terrain in the Inspector.");
            return;
        }

        originalHeights = terrain.terrainData.GetHeights(0, 0, terrain.terrainData.heightmapResolution, terrain.terrainData.heightmapResolution);
        CalculateTerrainSize();
        AdjustTerrainSize();
        GenerateMapFromCSV();
    }

    void CalculateTerrainSize()
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

            if (float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (z < minZ) minZ = z;
                if (z > maxZ) maxZ = z;
            }
        }
    }

    void AdjustTerrainSize()
    {
        TerrainData terrainData = terrain.terrainData;
        terrainData.size = new Vector3(maxX - minX + 100, terrainData.size.y, maxZ - minZ + 100);
        terrain.transform.position = new Vector3(minX - 50, 0, minZ - 50);
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
                float.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                Vector3 position = new Vector3(x, y, z);
                housePositions[label] = position;
                ElevateTerrainAround(position);
                position.y = terrain.SampleHeight(position) + y;
                Instantiate(housePrefab, position, Quaternion.identity);
            }
            else
            {
                Debug.LogWarning($"Failed to parse coordinates or view count: {line}");
            }
        }

        GenerateRoadsFromMST();
    }

    void ElevateTerrainAround(Vector3 position)
    {
        Debug.Log($"Elevating terrain around position: {position}");

        TerrainData terrainData = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;

        int xResolution = terrainData.heightmapResolution;
        int zResolution = terrainData.heightmapResolution;

        // Convert position to terrain coordinates
        float relativeX = (position.x - terrainPos.x) / terrainData.size.x * xResolution;
        float relativeZ = (position.z - terrainPos.z) / terrainData.size.z * zResolution;
        // Define the area around the position to elevate
        int radius = 30; // Adjust the radius as needed
        int startX = Mathf.Clamp(Mathf.RoundToInt(relativeX) - radius, 0, xResolution - 1);
        int startZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) - radius, 0, zResolution - 1);
        int endX = Mathf.Clamp(Mathf.RoundToInt(relativeX) + radius, 0, xResolution - 1);
        int endZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) + radius, 0, zResolution - 1);

        Debug.Log($"Elevating terrain at range: ({startX},{startZ}) to ({endX},{endZ})");

        // Get the current heights
        float[,] heights = terrainData.GetHeights(startX, startZ, endX - startX, endZ - startZ);

        // Calculate maximum elevation based on elevation factor
        float maxElevation = Mathf.Min(50f, position.y * elevationFactor) / terrainData.size.y;
        float maxIncr = 0;
        float mindist = 300;
        // Calculate falloff based on distance from the center
        for (int x = 0; x < heights.GetLength(0); x++)
        {
            for (int z = 0; z < heights.GetLength(1); z++)
            {
                // Calculate distance from center of the area
                float distance = Vector2.Distance(new Vector2(x, z), new Vector2(relativeX - startX, relativeZ - startZ));

                // Calculate height increment based on maximum elevation and falloff
                float heightIncrement = CalculateHeightIncrement(distance, maxElevation);

                if (heightIncrement > maxIncr && distance < mindist)
                {
                    maxIncr = heightIncrement;
                    mindist = distance;
                }
                else if (heightIncrement < maxIncr && distance < mindist)
                {
                    heightIncrement = maxIncr;
                }

                heights[x, z] += heightIncrement;

            }
        }

        // Set the modified heights back to the terrain
        terrainData.SetHeights(startX, startZ, heights);

        Debug.Log("Terrain elevation applied");
    }

    float CalculateHeightIncrement(float distance, float maxElevation)
    {
        // Adjust the parameters as needed for your desired falloff curve
        float maxDistance = 30f; // Radius
        float plateauThreshold = 0.8f; // Threshold for plateau effect
        float plateauStrength = 0.5f; // Strength of plateau effect
        float maxIncrement = maxElevation * plateauThreshold; // Maximum increment at the center
        float falloff = Mathf.Clamp01(1 - distance / maxDistance);
        float plateauFactor = Mathf.Pow(Mathf.Clamp01((distance / maxDistance) * (1 / plateauThreshold)), plateauStrength);

        // Calculate height increment with maximum limit at the center and plateau effect near the top
        float heightIncrement = Mathf.Lerp(0, maxIncrement, falloff) * plateauFactor;

        return heightIncrement;
    }

    void GenerateRoadsFromMST()
    {
        Debug.Log("GenerateRoadsFromMST");

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

            if (fields.Length < 2)
            {
                Debug.LogWarning($"Skipping invalid row: {line}");
                continue;
            }

            string house1 = fields[0];
            string house2 = fields[1];

            if (housePositions.ContainsKey(house1) && housePositions.ContainsKey(house2))
            {
                Vector3 position1 = housePositions[house1];
                Vector3 position2 = housePositions[house2];
                CreateRoadBetween(position1, position2);
            }
            else
            {
                Debug.LogWarning($"House label not found for: {line}");
            }
        }
    }

    void CreateRoadBetween(Vector3 position1, Vector3 position2)
    {
        Debug.Log($"Creating road between {position1} and {position2}");

        Vector3 midPoint = (position1 + position2) / 2;
        float roadLength = Vector3.Distance(position1, position2);
        float roadWidth = 2f; // Adjust road width as needed

        GameObject road = Instantiate(roadPrefab, midPoint, Quaternion.identity);

        road.transform.localScale = new Vector3(roadWidth, 0.1f, roadLength);
        road.transform.LookAt(position2);

        if (roadMaterial != null)
        {
            road.GetComponent<Renderer>().material = roadMaterial;
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
