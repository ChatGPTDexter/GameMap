using UnityEngine;
using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using TMPro;

public class MapGenerator : MonoBehaviour
{
    public TextAsset houseCsvFile; // Assign your house CSV file here in the Inspector
    public TextAsset mstCsvFile; // Assign your MST CSV file here in the Inspector
    public List<GameObject> housePrefabs; // Assign multiple house prefabs here
    public GameObject roadPrefab; // Assign your road prefab here
    public List<GameObject> treePrefabs; // Assign tree prefabs here
    public List<GameObject> rockPrefabs; // Assign rock prefabs here
    public Terrain terrain; // Assign the Terrain object here in the Inspector
    public float elevationFactor = 0.01f; // Factor to control terrain elevation
    public Material roadMaterial; // Assign the material for the roads here
    public int numTrees = 100; // Number of trees to spawn
    public int numRocks = 50; // Number of rocks to spawn
    public Texture2D textureToAdd; // The new texture layer to add
    public Vector2 textureOffset = Vector2.zero; // Offset of the texture layer
    public Vector2 textureTiling = Vector2.one; // Tiling of the texture layer
    public GameObject waterPrefab; // Assign the water prefab here
    public float waterHeight = 2f; // Height of the water
    public float checkRadius = 10f; // Radius to check for nearby houses

    public GameObject cubePrefab; // Assign the cube prefab here
    public GameObject houseLabelPrefab; // Assign a prefab for the house labels here
    public MiniMapController miniMapController; // Assign the MiniMapController here in the Inspector

    private Dictionary<string, Vector3> housePositions = new Dictionary<string, Vector3>();
    private List<Vector3> roadPositions = new List<Vector3>(); // Store road positions
    private float[,] originalHeights; // Store original terrain heights
    private float minX = float.MaxValue, maxX = float.MinValue;
    private float minZ = float.MaxValue, maxZ = float.MinValue;
    public bool canclearmap = true;

    void Start()
    {
        Debug.Log("MapGenerator Start");

        if (houseCsvFile == null || mstCsvFile == null)
        {
            Debug.LogError("CSV files not assigned. Please assign CSV files in the Inspector.");
            return;
        }

        if (housePrefabs == null || housePrefabs.Count == 0 || roadPrefab == null)
        {
            Debug.LogError("Prefabs not assigned. Please assign prefabs in the Inspector.");
            return;
        }

        if (terrain == null)
        {
            Debug.LogError("Terrain not assigned. Please assign Terrain in the Inspector.");
            return;
        }

        CalculateTerrainSize();
        AdjustTerrainSize();
        originalHeights = terrain.terrainData.GetHeights(0, 0, terrain.terrainData.heightmapResolution, terrain.terrainData.heightmapResolution);
        GenerateMapFromCSV();
        AddTerrainLayer();
        SpawnTreesAndRocks();
        RemoveTreesBelowHeight(3f);
        PlaceWater(); // Add water layer
        InstantiateHouses(); // Instantiate and position the houses after terrain generation
        SetupMiniMap(); // Setup the mini-map
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
        Vector3 terrainSize = new Vector3(maxX - minX + 200, terrainData.size.y, maxZ - minZ + 200);
        terrainData.size = terrainSize;
        terrain.transform.position = new Vector3(minX - 100, 0, minZ - 100);
        Debug.Log($"Terrain size adjusted to {terrainData.size} and position to {terrain.transform.position}");
    }

    void RemoveTreesBelowHeight(float heightThreshold)
    {
        TerrainData terrainData = terrain.terrainData;
        Vector3 terrainSize = terrainData.size;
        Vector3 terrainPosition = terrain.GetPosition();

        List<TreeInstance> newTreeInstances = new List<TreeInstance>();

        foreach (TreeInstance tree in terrainData.treeInstances)
        {
            // Convert tree position from terrain coordinates to world coordinates
            Vector3 treeWorldPosition = new Vector3(
                terrainPosition.x + tree.position.x * terrainSize.x,
                0f,
                terrainPosition.z + tree.position.z * terrainSize.z
            );

            // Sample height at tree position
            float height = terrain.SampleHeight(treeWorldPosition);

            // Check if height is greater than the specified threshold
            if (height > heightThreshold)
            {
                // Add tree instance to the new list if height is above the threshold
                newTreeInstances.Add(tree);
            }
        }

        // Update terrain tree instances with the new list
        terrainData.treeInstances = newTreeInstances.ToArray();
    }

    void PlaceWater()
    {
        if (waterPrefab == null)
        {
            Debug.LogError("Water prefab not assigned.");
            return;
        }

        // Calculate terrain size and position
        Vector3 terrainSize = terrain.terrainData.size;
        Vector3 terrainPosition = terrain.transform.position;

        // Instantiate the water prefab
        GameObject water = Instantiate(waterPrefab);

        if (water == null)
        {
            Debug.LogError("Failed to instantiate water prefab.");
            return;
        }

        // Scale the water to match the terrain size
        water.transform.localScale = new Vector3(terrainSize.x, 1, terrainSize.z);

        // Position the water at the center of the terrain with a height of 2
        water.transform.position = new Vector3(
            terrainPosition.x + terrainSize.x / 2,
            waterHeight,
            terrainPosition.z + terrainSize.z / 2
        );

        Debug.Log("Water placed at height 2, size of terrain, and shape of square.");
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

            string label = fields[0].Trim('"'); // Trim quotes from the label
            if (float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) &&
                float.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                Vector3 position = new Vector3(x, y, z);
                housePositions[label] = position;

                try
                {
                    // Adjust terrain and get the corrected height
                    ElevateTerrainAround(position);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error adjusting terrain at position {position}: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"Failed to parse coordinates or view count: {line}");
            }
        }

        GenerateRoadsFromMST();
    }

    float GetHighestNearbyHouseY(Vector3 position)
    {
        float highestY = position.y;
        foreach (Vector3 housePosition in housePositions.Values)
        {
            if (Vector3.Distance(position, housePosition) < checkRadius && housePosition.y > highestY)
            {
                highestY = housePosition.y;
            }
        }
        return highestY;
    }

    void AdjustTerrainAroundHouse(Vector3 position, float houseHeight)
    {
        TerrainData terrainData = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;

        int xResolution = terrainData.heightmapResolution;
        int zResolution = terrainData.heightmapResolution;

        // Convert position to terrain coordinates
        float relativeX = (position.x - terrainPos.x) / terrainData.size.x * xResolution;
        float relativeZ = (position.z - terrainPos.z) / terrainData.size.z * zResolution;

        // Define the area around the position to check and adjust
        int radius = 7; // Smaller radius for smoother transition
        int startX = Mathf.Clamp(Mathf.RoundToInt(relativeX) - radius, 0, xResolution - 1);
        int startZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) - radius, 0, zResolution - 1);
        int endX = Mathf.Clamp(Mathf.RoundToInt(relativeX) + radius, 0, xResolution - 1);
        int endZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) + radius, 0, zResolution - 1);

        // Get the current heights in the area
        float[,] heights = terrainData.GetHeights(startX, startZ, endX - startX, endZ - startZ);

        // Adjust terrain heights with smooth transition
        for (int x = 0; x < heights.GetLength(0); x++)
        {
            for (int z = 0; x < heights.GetLength(1); z++)
            {
                float terrainHeight = heights[x, z] * terrainData.size.y + terrainPos.y;
                float distance = Vector2.Distance(new Vector2(x, z), new Vector2(relativeX - startX, relativeZ - startZ));
                float falloff = Mathf.Clamp01(1 - (distance / radius));

                if (terrainHeight > houseHeight - 5)
                {
                    heights[x, z] = Mathf.Lerp(heights[x, z], (houseHeight - terrainPos.y) / terrainData.size.y, falloff);
                }
                else if (terrainHeight < houseHeight - 4)
                {
                    heights[x, z] = Mathf.Lerp(heights[x, z], (houseHeight - terrainPos.y) / terrainData.size.y, falloff);
                }
            }
        }

        // Set the modified heights back to the terrain
        terrainData.SetHeights(startX, startZ, heights);
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
        int radius = CalculateAdjustedDistance(position); // Adjust the radius as needed
        int startX = Mathf.Clamp(Mathf.RoundToInt(relativeX) - radius, 0, xResolution - 1);
        int startZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) - radius, 0, xResolution - 1);
        int endX = Mathf.Clamp(Mathf.RoundToInt(relativeX) + radius, 0, xResolution - 1);
        int endZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) + radius, 0, xResolution - 1);

        Debug.Log($"Elevating terrain at range: ({startX},{startZ}) to ({endX},{endZ})");

        // Get the current heights
        float[,] heights = terrainData.GetHeights(startX, startZ, endX - startX, endZ - startZ);

        // Calculate maximum elevation based on elevation factor
        float maxElevation = Mathf.Min(45f, position.y * elevationFactor) / terrainData.size.y;
        float maxIncr = 0;
        float mindist = 75;
        // Calculate falloff based on distance from the center
        for (int x = 0; x < heights.GetLength(0); x++)
        {
            for (int z = 0; z < heights.GetLength(1); z++)
            {
                // Calculate distance from center of the area
                float distance = Vector2.Distance(new Vector2(x, z), new Vector2(relativeX - startX, relativeZ - startZ));

                // Calculate height increment based on maximum elevation and falloff
                float heightIncrement = CalculateHeightIncrement(distance, maxElevation, radius);

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

        Debug.Log($"This is the maxIncr: {maxIncr} with this y: {position.y}");
        // Set the modified heights back to the terrain
        terrainData.SetHeights(startX, startZ, heights);
        Debug.Log("Terrain elevation applied");
    }

    void AddTerrainLayer()
    {
        if (terrain == null || textureToAdd == null)
        {
            Debug.LogError("Terrain or texture to add not assigned.");
            return;
        }

        TerrainLayer[] terrainLayers = terrain.terrainData.terrainLayers;

        // Check if the texture layer already exists
        foreach (TerrainLayer layer in terrainLayers)
        {
            if (layer.diffuseTexture == textureToAdd)
            {
                Debug.LogWarning("Texture layer already exists on the terrain.");
                return;
            }
        }

        // Create a new terrain layer with the provided texture
        TerrainLayer newLayer = new TerrainLayer
        {
            diffuseTexture = textureToAdd,
            tileSize = textureTiling,
            tileOffset = textureOffset
        };

        // Add the new layer to the list of terrain layers
        List<TerrainLayer> newLayersList = new List<TerrainLayer>(terrainLayers);
        newLayersList.Add(newLayer);

        // Update the terrain's terrain layers
        terrain.terrainData.terrainLayers = newLayersList.ToArray();

        Debug.Log("Terrain layer added successfully.");
    }

    float CalculateHeightIncrement(float distance, float maxElevation, int radius)
    {
        // Adjust the parameters as needed for your desired falloff curve
        float maxDistance = 25f; // Radius
        float plateauThreshold = 1f; // Threshold for plateau effect
        float plateauStrength = 0.5f; // Strength of plateau effect
        float maxIncrement = maxElevation * plateauThreshold; // Maximum increment at the center
        float falloff = Mathf.Clamp01(1 - distance / maxDistance);
        float plateauFactor = Mathf.Pow(Mathf.Clamp01((distance / maxDistance) * (1 / plateauThreshold)), plateauStrength);

        // Calculate height increment with maximum limit at the center and plateau effect near the top
        float heightIncrement = Mathf.Lerp(0, maxIncrement, falloff) * plateauFactor;
        if (radius < 25)
        {
            falloff = Mathf.Clamp01(1 - distance / radius);
            heightIncrement = falloff * maxElevation;
        }

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

            if (fields.Length < 3)
            {
                Debug.LogWarning($"Skipping invalid row: {line}");
                continue;
            }

            string house1 = fields[1].Trim('"'); // Trim quotes from the house labels
            string house2 = fields[2].Trim('"');

            if (housePositions.ContainsKey(house1) && housePositions.ContainsKey(house2))
            {
                Vector3 position1 = housePositions[house1];
                Vector3 position2 = housePositions[house2];
                GenerateRoadSegment(position1, position2);
            }
            else
            {
                Debug.LogWarning($"House label not found for: {line}");
                if (!housePositions.ContainsKey(house1))
                {
                    Debug.LogWarning($"House label not found: {house1}");
                }
                if (!housePositions.ContainsKey(house2))
                {
                    Debug.LogWarning($"House label not found: {house2}");
                }
            }
        }
    }

    void GenerateRoadSegment(Vector3 position1, Vector3 position2)
    {
        Debug.Log($"Generating road segment between {position1} and {position2}");

        // Calculate the number of segments based on the distance
        int segments = Mathf.CeilToInt(Vector3.Distance(position1, position2) / 1f); // Increase the number of segments
        Vector3 direction = (position2 - position1) / segments;

        for (int i = 0; i < segments; i++)
        {
            Vector3 start = position1 + direction * i;
            Vector3 end = position1 + direction * (i + 1);

            // Adjust the y-coordinate to match the terrain height
            start.y = terrain.SampleHeight(start) + 0.1f; // Slightly above the terrain
            end.y = terrain.SampleHeight(end) + 0.1f;

            CreateRoadBetween(start, end);
        }
    }

    void CreateRoadBetween(Vector3 position1, Vector3 position2)
    {
        Debug.Log($"Creating road between {position1} and {position2}");

        Vector3 midPoint = (position1 + position2) / 2;
        float roadLength = Vector3.Distance(position1, position2);
        float roadWidth = 0.1f; // Adjust road width to smaller size
        float minHeightAboveWater = waterHeight + 0.1f; // Minimum height of the road above the water

        GameObject road = Instantiate(roadPrefab, midPoint, Quaternion.identity);

        road.transform.localScale = new Vector3(roadWidth, 0.1f, roadLength);
        road.transform.LookAt(position2);
        road.transform.Rotate(270, 0, 0); // Rotate the road to be horizontal and properly aligned

        // Adjust the y-coordinates of the start, end, and midpoint to ensure they stay above the water level
        position1.y = Mathf.Max(terrain.SampleHeight(position1) + 0.1f, minHeightAboveWater);
        position2.y = Mathf.Max(terrain.SampleHeight(position2) + 0.1f, minHeightAboveWater);
        midPoint.y = Mathf.Max(terrain.SampleHeight(midPoint) + 0.1f, minHeightAboveWater);

        // Set the positions of the road segments
        road.transform.position = midPoint;

        // Add road positions to avoid spawning trees and rocks on them
        roadPositions.Add(position1);
        roadPositions.Add(position2);

        if (roadMaterial != null)
        {
            road.GetComponent<Renderer>().material = roadMaterial;
        }
    }

    void SpawnTreesAndRocks()
    {
        for (int i = 0; i < numTrees; i++)
        {
            SpawnObject(treePrefabs);
        }

        for (int i = 0; i < numRocks; i++)
        {
            SpawnObject(rockPrefabs);
        }
    }

    void SpawnObject(List<GameObject> prefabs)
    {
        if (prefabs.Count == 0) return;

        Vector3 position;
        int attempts = 0;
        bool validPosition = false;

        do
        {
            float x = UnityEngine.Random.Range(terrain.transform.position.x, terrain.transform.position.x + terrain.terrainData.size.x);
            float z = UnityEngine.Random.Range(terrain.transform.position.z, terrain.transform.position.z + terrain.terrainData.size.z);
            position = new Vector3(x, terrain.SampleHeight(new Vector3(x, 0, z)) + 0.1f, z);

            validPosition = IsValidSpawnPosition(position);
            attempts++;
        } while (!validPosition && attempts < 10);

        if (validPosition)
        {
            GameObject prefab = prefabs[UnityEngine.Random.Range(0, prefabs.Count)];
            Instantiate(prefab, position, Quaternion.identity);
        }
    }

    bool IsValidSpawnPosition(Vector3 position)
    {
        // Avoid water
        if (position.y < waterHeight + 0.1f) // Allow a slight margin above the water
        {
            return false;
        }

        foreach (Vector3 housePosition in housePositions.Values)
        {
            if (Vector3.Distance(position, housePosition) < 5f) // Adjust the minimum distance as needed
            {
                return false;
            }
        }

        foreach (Vector3 roadPosition in roadPositions)
        {
            if (Vector3.Distance(position, roadPosition) < 2f) // Adjust the minimum distance as needed
            {
                return false;
            }
        }

        return true;
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

    public int CalculateAdjustedDistance(Vector3 position)
    {
        float nearestDistance = float.MaxValue;

        foreach (var houseEntry in housePositions)
        {
            Vector3 housePosition = houseEntry.Value;
            // Calculate horizontal distance using only x and z coordinates
            float distance = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(housePosition.x, housePosition.z));

            // Skip if the distance is 0
            if (distance == 0)
            {
                continue;
            }

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
            }
        }

        if (nearestDistance == float.MaxValue)
        {
            return 0; // No valid distance found
        }
        else if (nearestDistance < 75)
        {
            return Mathf.RoundToInt(nearestDistance / 5);
        }
        else
        {
            return 25;
        }
    }

    void SetupMiniMap()
    {
        miniMapController.housePositions = housePositions; // Pass the house positions to the MiniMapController
        miniMapController.minX = minX;
        miniMapController.maxX = maxX;
        miniMapController.minZ = minZ;
        miniMapController.maxZ = maxZ;
        miniMapController.SetupMiniMap();
    }

    void InstantiateHouses()
    {
        foreach (var houseEntry in housePositions)
        {
            Vector3 position = houseEntry.Value;

            try
            {
                // Get the corrected height for the house position
                float terrainHeight = terrain.SampleHeight(position);

                // Instantiate the house prefab at the adjusted height
                GameObject housePrefab = housePrefabs[UnityEngine.Random.Range(0, housePrefabs.Count)];
                float houseHeight = housePrefab.GetComponent<Renderer>().bounds.size.y;
                GameObject house = Instantiate(housePrefab, new Vector3(position.x, terrainHeight + (houseHeight / 2), position.z), Quaternion.identity);

                // Get house dimensions using MeshFilter bounds for more accuracy
                MeshFilter meshFilter = house.GetComponent<MeshFilter>();
                if (meshFilter != null)
                {
                    Vector3 houseSize = meshFilter.sharedMesh.bounds.size;
                    Vector3 houseScale = house.transform.localScale;
                    houseSize = Vector3.Scale(houseSize, houseScale);

                    // Instantiate the cube underneath the house
                    Vector3 cubePosition = new Vector3(position.x, terrainHeight - (houseSize.y / 1.5f), position.z);
                    GameObject cube = Instantiate(cubePrefab, cubePosition, Quaternion.identity);
                    cube.transform.localScale = new Vector3(houseSize.x / 3.5f, houseSize.y / 3, houseSize.z / 3.5f);

                    Debug.Log($"Spawning house at y={terrainHeight} and cube at y={terrainHeight - (houseSize.y / 2)} with scale {houseSize}");
                }
                else
                {
                    Debug.LogError("House prefab does not have a MeshFilter component.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error positioning house at {position}: {ex.Message}");
            }
        }
    }
    void Update()
    {
        if (canclearmap && Input.GetKeyDown(KeyCode.P))
        {
            RestoreOriginalTerrain();
        }
    }

    void RestoreOriginalTerrain()
    {
        if (terrain == null || originalHeights == null)
        {
            Debug.LogError("Terrain or original heights not assigned.");
            return;
        }

        // Restore the terrain to its original state
        terrain.terrainData.SetHeights(0, 0, originalHeights);

        // Remove the added texture layer
        TerrainLayer[] terrainLayers = terrain.terrainData.terrainLayers;
        List<TerrainLayer> newLayersList = new List<TerrainLayer>(terrainLayers);
        newLayersList.RemoveAt(newLayersList.Count - 1); // Remove the last added layer
        terrain.terrainData.terrainLayers = newLayersList.ToArray();
        terrain.transform.position = new Vector3(0, 0, 0);
    }
}
