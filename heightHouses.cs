
using UnityEngine;
using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using TMPro;
using System.Linq;

public class MapGenerator : MonoBehaviour
{
    [SerializeField] private GameObject player; // Assign the player object here

    public TextAsset houseCsvFile; // Assign your house CSV file here in the Inspector
    public TextAsset mstCsvFile; // Assign your MST CSV file here in the Inspector
    public List<GameObject> housePrefabs; // Assign multiple house prefabs here
    public GameObject roadPrefab; // Assign your road prefab here
    public List<Biome> biomes; // Assign biomes here
    public Terrain terrain; // Assign the Terrain object here in the Inspector
    public float elevationFactor = 0.01f; // Factor to control terrain elevation
    public Material roadMaterial; // Assign the material for the roads here
    public GameObject waterPrefab; // Assign the water prefab here
    public float waterHeight = 2f; // Height of the water
    public float checkRadius = 10f; // Radius to check for nearby houses
    //public List<GameObject> brokenParts; // Assign biomes here
    //public GameObject fire;
    public Dictionary<string, List<Quaternion>> houseRotationsByLabel = new Dictionary<string, List<Quaternion>>();
    public GameObject cubePrefab; // Assign the cube prefab here
    public GameObject houseLabelPrefab; // Assign a prefab for the house labels here
    public MiniMapController miniMapController; // Assign the MiniMapController here in the Inspector
    public CharacterSpawner characterSpawner;
    public Dictionary<string, Vector3> housePositions = new Dictionary<string, Vector3>();
    public List<Vector3> roadPositions = new List<Vector3>(); // Store road positions
    public Dictionary<int, List<Vector3>> clusters = new Dictionary<int, List<Vector3>>(); // Store clusters
    public Dictionary<int, Biome> clusterBiomes = new Dictionary<int, Biome>(); // Store assigned biomes for each cluster
    public Dictionary<int, Terrain> clusterTerrains = new Dictionary<int, Terrain>(); // Store terrains for each cluster
    public Dictionary<int, float[,]> originalHeights = new Dictionary<int, float[,]>(); // Store original terrain heights for each cluster
    public float minX = float.MaxValue, maxX = float.MinValue;
    public float minZ = float.MaxValue, maxZ = float.MinValue;
    public bool canclearmap = true;
    public Dictionary<string, List<Vector3>> housePositionsByLabel = new Dictionary<string, List<Vector3>>();
    public float spawnRadius = 3.0f;  // Adjust this radius as needed to ensure parts are close but not touching
    public Dictionary<int, Vector3> terrainStartPoints = new Dictionary<int, Vector3>();
    public Dictionary<int, Vector2> terrainSizes = new Dictionary<int, Vector2>();
    public Vector3 spawnPos;



    public Dictionary<string, bool> masteredTopics = new Dictionary<string, bool>();
    public Dictionary<string, bool> MasteredTopics => masteredTopics;
    public Dictionary<int, List<string>> clusterLabels = new Dictionary<int, List<string>>();
    public Dictionary<string, float> houseTranscriptLengths = new Dictionary<string, float>();

    void Start()
    {
        Debug.Log("MapGenerator Start");
        characterSpawner = FindObjectOfType<CharacterSpawner>();
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

        if (biomes == null || biomes.Count == 0)
        {
            Debug.LogError("Biomes not assigned. Please assign biomes in the Inspector.");
            return;
        }

        if (terrain == null)
        {
            Debug.LogError("Terrain not assigned. Please assign Terrain in the Inspector.");
            return;
        }

        CalculateTerrainSize();
        AdjustTerrainSize();
        GenerateMapFromCSV();
        createSpawnIsland();
        AssignBiomesToClusters();
        ApplyBiomeTextures();

        // Ensure all terrain adjustments are completed
        AdjustTerrainAroundHouses();
        PlaceWater();

        // Generate main roads and small roads after terrain adjustments
        GenerateMainRoadFromMST();
        InstantiateHouses();
        //GenerateSmallRoads(); // Ensure to remove this if not needed

        SpawnTreesAndRocks();
        RemoveTreesBelowHeight(3f);

        SetupMiniMap();
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

            if (float.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
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

    void createSpawnIsland()
    {
        TerrainData terrainData = new TerrainData();
        terrainData.heightmapResolution = terrain.terrainData.heightmapResolution;
        terrainData.size = new Vector3(600, terrain.terrainData.size.y, 600);
        GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
        Terrain newTerrain = terrainObject.GetComponent<Terrain>();
        newTerrain.transform.position = new Vector3((maxX - minX) / 2 + minX - 300, 0, (maxZ - minZ) / 2 + minZ - 300);

        Vector3 islandCenter = new Vector3((maxX - minX) / 2 + minX, 0, (maxZ - minZ) / 2 + minZ);

        float islandRadius = 100; // Define the half-length of the side of the square island

        // Create a random number generator
        System.Random random = new System.Random();

        List<Vector3> houseCoordinates = new List<Vector3>();

        // Generate three random positions within the square island bounds
        for (int i = 0; i < 2; i++)
        {
            // Generate random x and z positions within the square
            float randomX = (float)(random.NextDouble() * 2 * islandRadius - islandRadius);
            float randomZ = (float)(random.NextDouble() * 2 * islandRadius - islandRadius);
            float randomY = (float)(random.NextDouble() * (15 - 10) + 10); // Random Y between 15 and 60

            // Calculate the random position within the square island
            Vector3 randomPosition = new Vector3(islandCenter.x + randomX, randomY, islandCenter.z + randomZ);

            // Elevate terrain around the random position
            ElevateTerrainAround(newTerrain, randomPosition);

            // Instantiate the house prefab at the adjusted height
            GameObject housePrefab = housePrefabs[UnityEngine.Random.Range(0, housePrefabs.Count)];
            float houseHeight = housePrefab.GetComponent<Renderer>().bounds.size.y;
            GameObject house = Instantiate(housePrefab, new Vector3(randomPosition.x, randomPosition.y + (houseHeight / 2), randomPosition.z), Quaternion.identity);

            houseCoordinates.Add(randomPosition);
            characterSpawner.spawnSpawnCharacter(randomPosition, i);

            if (i == 0)
            {
                spawnPos = new Vector3(randomPosition.x - 20, randomPosition.y + 10, randomPosition.z + 20); 
                player.transform.position = spawnPos;
                //Instantiate(fire, new Vector3(randomPosition.x - 20, randomPosition.y, randomPosition.z + 20), Quaternion.identity);
                //SpawnPartsNearPoint(player.transform.position, newTerrain);
            }
        }

        Biome islandBiome = biomes[0];

        // Create and apply the biome's terrain layer
        TerrainLayer newLayer = new TerrainLayer
        {
            diffuseTexture = islandBiome.terrainTexture,
            tileSize = islandBiome.textureTiling,
            tileOffset = islandBiome.textureOffset
        };
        terrainData.terrainLayers = new TerrainLayer[] { newLayer };

        // Apply the texture to the entire spawn island terrain
        ApplyTextureToTerrain(newTerrain, islandCenter, newLayer);

        int segments = Mathf.CeilToInt(Vector3.Distance(houseCoordinates[0], houseCoordinates[1]) / 1f); // Increase the number of segments
        Vector3 direction = -(houseCoordinates[0] - houseCoordinates[1]) / segments;

        for (int i = 0; i < segments; i++)
        {
            Vector3 start = houseCoordinates[0] + direction * i;
            Vector3 end = houseCoordinates[0] + direction * (i + 1);

            // Adjust the y-coordinate to match the terrain height
            start.y = newTerrain.SampleHeight(start) + 0.1f; // Slightly above the terrain
            end.y = newTerrain.SampleHeight(end) + 0.1f;

            CreateRoadBetween(start, end, newTerrain);
        }

        SpawnBiomeObjects(islandBiome, islandCenter);
    }

    /*
    void SpawnPartsNearPoint(Vector3 randomPos, Terrain terrain)
    {
        foreach (GameObject part in brokenParts)
        {
            GameObject spawnedPart = Instantiate(part, GetRandomPositionNearPoint(randomPos, spawnRadius, terrain), Quaternion.identity);
            AddPhysicsComponents(spawnedPart);
        }
    }

    Vector3 GetRandomPositionNearPoint(Vector3 point, float radius, Terrain terrain)
    {   
        Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * radius;
        float actualHeight = terrain.SampleHeight(new Vector3(point.x + randomCircle.x, 0, point.z + randomCircle.y));
        return new Vector3(point.x + randomCircle.x, actualHeight, point.z + randomCircle.y);
    }

    bool IsTooCloseToOtherParts(Vector3 position, List<Vector3> otherPositions, float minDistance)
    {
        foreach (Vector3 otherPosition in otherPositions)
        {
            if (Vector3.Distance(position, otherPosition) < minDistance)
            {
                return true;
            }
        }
        return false;
    }

    void AddPhysicsComponents(GameObject obj)
    {
        // Add a Rigidbody if it doesn't already have one
        if (obj.GetComponent<Rigidbody>() == null)
        {
            obj.AddComponent<Rigidbody>();
        }

        // Ensure the object has at least one collider
        if (obj.GetComponent<Collider>() == null)
        {
            // Add a default BoxCollider if no collider is present
            obj.AddComponent<BoxCollider>();
        }
    }
    */
    void AdjustTerrainAroundHouses()
    {
        float adjustmentRadius = 30f; // Adjust this radius as needed
        float minHeightAboveWater = waterHeight + 0.1f; // Ensure terrain is slightly above water

        foreach (var housePosition in housePositions.Values)
        {
            // Calculate the terrain height to ensure it's above water
            int xResolution = terrain.terrainData.heightmapResolution;
            int zResolution = terrain.terrainData.heightmapResolution;
            Vector3 terrainPos = terrain.transform.position;

            // Convert house position to terrain coordinates
            float relativeX = (housePosition.x - terrainPos.x) / terrain.terrainData.size.x * xResolution;
            float relativeZ = (housePosition.z - terrainPos.z) / terrain.terrainData.size.z * zResolution;

            int radius = Mathf.CeilToInt(adjustmentRadius * xResolution / terrain.terrainData.size.x);

            int startX = Mathf.Clamp(Mathf.RoundToInt(relativeX) - radius, 0, xResolution - 1);
            int startZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) - radius, 0, zResolution - 1);
            int endX = Mathf.Clamp(Mathf.RoundToInt(relativeX) + radius, 0, xResolution - 1);
            int endZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) + radius, 0, zResolution - 1);

            float[,] heights = terrain.terrainData.GetHeights(startX, startZ, endX - startX, endZ - startZ);

            for (int x = 0; x < heights.GetLength(0); x++)
            {
                for (int z = 0; z < heights.GetLength(1); z++)
                {
                    float distance = Vector2.Distance(new Vector2(x, z), new Vector2(relativeX - startX, relativeZ - startZ));
                    if (distance < radius)
                    {
                        // Raise the terrain to be above the water height
                        heights[x, z] = Mathf.Max(minHeightAboveWater / terrain.terrainData.size.y, heights[x, z]);
                    }
                }
            }

            terrain.terrainData.SetHeights(startX, startZ, heights);
        }

        Debug.Log("Terrain adjusted around house positions to be above water level.");
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

        // Position the water at the center of the terrain with the desired height
        water.transform.position = new Vector3(
            terrainPosition.x + terrainSize.x / 2,
            waterHeight,
            terrainPosition.z + terrainSize.z / 2
        );

        Debug.Log("Water placed at height 2, size of terrain, and shape of square.");
    }
    void AdjustTerrainAboveWater(Vector3 position, Terrain terrain, float radius)
    {
        TerrainData terrainData = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;

        int xResolution = terrainData.heightmapResolution;
        int zResolution = terrainData.heightmapResolution;

        float relativeX = (position.x - terrainPos.x) / terrainData.size.x * xResolution;
        float relativeZ = (position.z - terrainPos.z) / terrainData.size.z * zResolution;

        int radiusInHeightmap = Mathf.RoundToInt(radius / terrainData.size.x * xResolution);

        int startX = Mathf.Clamp(Mathf.RoundToInt(relativeX) - radiusInHeightmap, 0, xResolution - 1);
        int startZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) - radiusInHeightmap, 0, zResolution - 1);
        int endX = Mathf.Clamp(Mathf.RoundToInt(relativeX) + radiusInHeightmap, 0, xResolution - 1);
        int endZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) + radiusInHeightmap, 0, zResolution - 1);

        float[,] heights = terrainData.GetHeights(startX, startZ, endX - startX + 1, endZ - startZ + 1);

        bool terrainAdjusted = false;
        float minHeightAboveWater = (waterHeight + 0.5f) / terrainData.size.y; // Ensure it's at least 0.5 units above water

        for (int x = 0; x < heights.GetLength(0); x++)
        {
            for (int z = 0; z < heights.GetLength(1); z++)
            {
                if (heights[x, z] < minHeightAboveWater)
                {
                    heights[x, z] = minHeightAboveWater;
                    terrainAdjusted = true;
                }
            }
        }

        if (terrainAdjusted)
        {
            terrainData.SetHeights(startX, startZ, heights);
            Debug.Log($"Terrain adjusted above water level at position {position}");
        }
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

        // Clear existing house positions and transcript lengths
        housePositions.Clear();
        houseTranscriptLengths.Clear();
        clusters.Clear();
        clusterLabels.Clear();

        while (reader.Peek() != -1)
        {
            string line = reader.ReadLine();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            string[] fields = ParseCSVLine(line);

            if (fields.Length < 6)
            {
                Debug.LogWarning($"Skipping invalid row: {line}");
                continue;
            }

            string label = fields[4].Trim().Trim('"'); // Trim quotes from the label
            if (float.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) &&
                float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                int.TryParse(fields[3], out int clusterId) && // Read cluster ID
                float.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float normalizedLength)) // Read NormalizedTranscriptLength
            {
                Vector3 position = new Vector3(x, y, z);
                housePositions[label] = position;
                houseTranscriptLengths[label] = normalizedLength;

                if (!clusters.ContainsKey(clusterId))
                {
                    clusters[clusterId] = new List<Vector3>();
                }
                clusters[clusterId].Add(position);

                if (!clusterLabels.ContainsKey(clusterId))
                {
                    clusterLabels[clusterId] = new List<string>();
                }
                clusterLabels[clusterId].Add(label);
            }
            else
            {
                Debug.LogWarning($"Failed to parse coordinates or other fields: {line}");
            }
        }

        GenerateClusterTerrains(); // Generate terrains for each cluster

        foreach (var cluster in clusters)
        {
            foreach (var position in cluster.Value)
            {
                ElevateTerrainAround(clusterTerrains[cluster.Key], position);
            }
        }

        // Generate main roads from MST after terrain modification
        GenerateMainRoadFromMST();
    }

    void GenerateClusterTerrains()
    {
        foreach (var cluster in clusters)
        {
            Vector3 clusterMin = new Vector3(float.MaxValue, 0, float.MaxValue);
            Vector3 clusterMax = new Vector3(float.MinValue, 0, float.MinValue);

            foreach (var position in cluster.Value)
            {
                if (position.x < clusterMin.x) clusterMin.x = position.x;
                if (position.x > clusterMax.x) clusterMax.x = position.x;
                if (position.z < clusterMin.z) clusterMin.z = position.z;
                if (position.z > clusterMax.z) clusterMax.z = position.z;
            }

            TerrainData terrainData = new TerrainData();
            terrainData.heightmapResolution = terrain.terrainData.heightmapResolution;
            terrainData.size = new Vector3(clusterMax.x - clusterMin.x + 100, terrain.terrainData.size.y, clusterMax.z - clusterMin.z + 100);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            Terrain newTerrain = terrainObject.GetComponent<Terrain>();
            newTerrain.transform.position = new Vector3(clusterMin.x - 50, 0, clusterMin.z - 50);
            clusterTerrains[cluster.Key] = newTerrain;

            float[,] heights = new float[terrainData.heightmapResolution, terrainData.heightmapResolution];
            originalHeights[cluster.Key] = heights;
            terrainData.SetHeights(0, 0, heights);

            // Blend the edges of the terrain to smoothly transition to the water level
            BlendTerrainEdges(newTerrain);
        }
    }

    void BlendTerrainEdges(Terrain terrain)
    {
        TerrainData terrainData = terrain.terrainData;
        int resolution = terrainData.heightmapResolution;
        float[,] heights = terrainData.GetHeights(0, 0, resolution, resolution);

        for (int i = 0; i < resolution; i++)
        {
            for (int j = 0; j < resolution; j++)
            {
                float distanceToEdge = Mathf.Min(i, j, resolution - i - 1, resolution - j - 1);
                float blendFactor = Mathf.Clamp01(distanceToEdge / (resolution * 0.1f)); // Blend within 10% of the terrain size
                heights[i, j] = Mathf.Lerp(waterHeight / terrainData.size.y, heights[i, j], blendFactor);
            }
        }

        terrainData.SetHeights(0, 0, heights);
    }

    float CalculateSmootherParabolicHeightIncrement(float distance, float maxElevation, int radius)
    {
        float maxDistance = radius;

        // Smoother parabolic falloff effect
        if (distance > maxDistance) return 0;

        float normalizedDistance = distance / maxDistance;
        float heightIncrement = maxElevation * (1 - Mathf.Pow(normalizedDistance, 3)); // Smoother parabolic curve

        return heightIncrement;
    }

    void ElevateTerrainAround(Terrain terrain, Vector3 position)
    {
        Debug.Log($"Elevating terrain around position: {position}");

        TerrainData terrainData = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;

        int xResolution = terrainData.heightmapResolution;
        int zResolution = terrainData.heightmapResolution;

        // Convert position to terrain coordinates
        float relativeX = (position.x - terrainPos.x) / terrainData.size.x * xResolution;
        float relativeZ = (position.z - terrainPos.z) / terrainData.size.z * zResolution;

        int radius = 100; // Increase the radius for larger mounds
        int startX = Mathf.Clamp(Mathf.RoundToInt(relativeX) - radius, 0, xResolution - 1);
        int startZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) - radius, 0, zResolution - 1);
        int endX = Mathf.Clamp(Mathf.RoundToInt(relativeX) + radius, 0, xResolution - 1);
        int endZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) + radius, 0, zResolution - 1);

        Debug.Log($"Elevating terrain at range: ({startX},{startZ}) to ({endX},{endZ})");

        // Get the current heights
        float[,] heights = terrainData.GetHeights(startX, startZ, endX - startX, endZ - startZ);

        // Calculate maximum elevation based on z-coordinate from CSV
        float maxElevation = position.y / terrainData.size.y;

        float minHeightAboveWater = waterHeight / terrainData.size.y + 0.01f; // Ensure it's slightly above water

        // Calculate height increment using smoother parabolic function and blending with existing heights
        for (int x = 0; x < heights.GetLength(0); x++)
        {
            for (int z = 0; z < heights.GetLength(1); z++)
            {
                float distance = Vector2.Distance(new Vector2(x, z), new Vector2(relativeX - startX, relativeZ - startZ));

                // Smoother parabolic height increment
                float heightIncrement = CalculateSmootherParabolicHeightIncrement(distance, maxElevation, radius);

                // Blend the new height increment with the existing height, ensuring it's above water
                heights[x, z] = Mathf.Max(heights[x, z], heightIncrement, minHeightAboveWater);
            }
        }

        // Set the modified heights back to the terrain
        terrainData.SetHeights(startX, startZ, heights);
        Debug.Log("Terrain elevation applied");
    }

    void AssignBiomesToClusters()
    {
        System.Random random = new System.Random();
        List<Biome> availableBiomes = new List<Biome>(biomes);

        foreach (var cluster in clusters)
        {
            int biomeIndex = random.Next(availableBiomes.Count);
            Biome biome = availableBiomes[biomeIndex];
            clusterBiomes[cluster.Key] = biome;

            // Remove biome from the list if you want unique biomes per cluster
            availableBiomes.RemoveAt(biomeIndex);
        }
    }

    void ApplyBiomeTextures()
    {
        foreach (var cluster in clusters)
        {
            Biome biome = clusterBiomes[cluster.Key];
            Terrain terrain = clusterTerrains[cluster.Key];
            TerrainData terrainData = terrain.terrainData;
            List<TerrainLayer> terrainLayers = new List<TerrainLayer>(terrainData.terrainLayers);

            TerrainLayer newLayer = new TerrainLayer
            {
                diffuseTexture = biome.terrainTexture,
                tileSize = biome.textureTiling,
                tileOffset = biome.textureOffset
            };
            terrainLayers.Add(newLayer);
            terrainData.terrainLayers = terrainLayers.ToArray();

            foreach (var position in cluster.Value)
            {
                ApplyTextureToTerrain(terrain, position, newLayer);
            }

            Debug.Log("Biome textures applied successfully.");
        }
    }

    void ApplyTextureToTerrain(Terrain terrain, Vector3 position, TerrainLayer layer)
    {
        TerrainData terrainData = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;

        int xResolution = terrainData.alphamapWidth;
        int zResolution = terrainData.alphamapHeight;

        float relativeX = (position.x - terrainPos.x) / terrainData.size.x * xResolution;
        float relativeZ = (position.z - terrainPos.z) / terrainData.size.z * zResolution;

        int radius = 20; // Define a radius for the biome area
        int startX = Mathf.Clamp(Mathf.RoundToInt(relativeX) - radius, 0, xResolution - 1);
        int startZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) - radius, 0, zResolution - 1);
        int endX = Mathf.Clamp(Mathf.RoundToInt(relativeX) + radius, 0, xResolution - 1);
        int endZ = Mathf.Clamp(Mathf.RoundToInt(relativeZ) + radius, 0, zResolution - 1);

        float[,,] alphamaps = terrainData.GetAlphamaps(startX, startZ, endX - startX, endZ - startZ);
        int layerIndex = Array.IndexOf(terrainData.terrainLayers, layer);

        for (int x = 0; x < alphamaps.GetLength(0); x++)
        {
            for (int z = 0; z < alphamaps.GetLength(1); z++)
            {
                alphamaps[x, z, layerIndex] = 1;
            }
        }

        terrainData.SetAlphamaps(startX, startZ, alphamaps);
    }


    void GenerateMainRoadFromMST()
    {
        Debug.Log("GenerateMainRoadFromMST");

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

            string house1 = fields[1].Trim('"');
            string house2 = fields[2].Trim('"');

            if (housePositions.ContainsKey(house1) && housePositions.ContainsKey(house2))
            {
                Vector3 position1 = housePositions[house1];
                Vector3 position2 = housePositions[house2];
                GenerateMainRoad(position1, position2);
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
    bool IsWithinHouseRadius(Vector3 position, float radius)
    {
        foreach (var housePosition in housePositions.Values)
        {
            if (Vector3.Distance(position, housePosition) < radius)
            {
                return true;
            }
        }
        return false;
    }

    void InstantiateHouses()
    {
        foreach (var houseEntry in housePositions)
        {
            Vector3 mainPosition = houseEntry.Value;
            string label = houseEntry.Key;
            int clusterId = clusters.First(c => c.Value.Contains(mainPosition)).Key;
            Terrain terrain = clusterTerrains[clusterId];

            if (!houseTranscriptLengths.TryGetValue(label, out float normalizedLength))
            {
                Debug.LogWarning($"NormalizedTranscriptLength not found for label: {label}");
                normalizedLength = 1.0f; // Default value in case parsing fails
            }

            int numberOfHouses = Mathf.RoundToInt(normalizedLength);
            float width = 100f; // Width of the rectangular neighborhood
            float length = 50f; // Length of the rectangular neighborhood
            int housesPerRow = Mathf.CeilToInt(Mathf.Sqrt(numberOfHouses));
            float houseSpacingX = width / housesPerRow;
            float houseSpacingZ = length / (numberOfHouses / housesPerRow);

            housePositionsByLabel[label] = new List<Vector3>();
            houseRotationsByLabel[label] = new List<Quaternion>();

            // Adjust the layout for specific cases
            if (numberOfHouses == 3)
            {
                float adjustedHouseSpacingX = width / 2;
                float adjustedHouseSpacingZ = length / 1.5f;

                for (int i = 0; i < numberOfHouses; i++)
                {
                    int row = i == 2 ? 1 : 0;
                    int col = i == 2 ? 1 : i;

                    Vector3 offset = new Vector3(
                        col * adjustedHouseSpacingX - width / 4,
                        0,
                        row * adjustedHouseSpacingZ - length / 2
                    );

                    Vector3 housePosition = mainPosition + offset;
                    
                    // Adjust terrain above water level before instantiating the house
                    AdjustTerrainAboveWater(housePosition, terrain, 50f);
                    
                    // Re-sample terrain height after adjustment
                    float terrainHeight = terrain.SampleHeight(housePosition);
                    housePosition.y = terrainHeight;

                    // Ensure house does not fall on the main road
                    if (IsOnMainRoad(housePosition))
                    {
                        housePosition.x += adjustedHouseSpacingX / 2;
                    }

                    GameObject housePrefab = housePrefabs[UnityEngine.Random.Range(0, housePrefabs.Count)];
                    float houseHeight = housePrefab.GetComponent<Renderer>().bounds.size.y;
                    GameObject house = Instantiate(housePrefab, new Vector3(housePosition.x, housePosition.y + (houseHeight / 2), housePosition.z), Quaternion.identity);

                    // Rotate house to face the main road
                    RotateHouseToFaceMainRoad(house, housePosition);

                    housePositionsByLabel[label].Add(housePosition);
                    houseRotationsByLabel[label].Add(house.transform.rotation);

                    Vector3 nearestPointOnMainRoad = GetNearestPointOnMainRoad(housePosition);
                    GenerateSmallRoad(nearestPointOnMainRoad, housePosition, terrain);
                }
            }
            else if (numberOfHouses == 5)
            {
                // Similar structure as the case for 3 houses, with adjustments for 5 houses
                // ... (code for 5 houses layout)
            }
            else
            {
                for (int i = 0; i < numberOfHouses; i++)
                {
                    int row = i / housesPerRow;
                    int col = i % housesPerRow;
                    Vector3 offset = new Vector3(
                        col * houseSpacingX - width / 2,
                        0,
                        row * houseSpacingZ - length / 2
                    );

                    Vector3 housePosition = mainPosition + offset;
                    
                    // Adjust terrain above water level before instantiating the house
                    AdjustTerrainAboveWater(housePosition, terrain, 50f);
                    
                    // Re-sample terrain height after adjustment
                    float terrainHeight = terrain.SampleHeight(housePosition);
                    housePosition.y = terrainHeight;

                    // Ensure house does not fall on the main road
                    if (IsOnMainRoad(housePosition))
                    {
                        housePosition.x += houseSpacingX / 2;
                    }

                    GameObject housePrefab = housePrefabs[UnityEngine.Random.Range(0, housePrefabs.Count)];
                    float houseHeight = housePrefab.GetComponent<Renderer>().bounds.size.y;
                    GameObject house = Instantiate(housePrefab, new Vector3(housePosition.x, housePosition.y + (houseHeight / 2), housePosition.z), Quaternion.identity);

                    // Rotate house to face the main road
                    RotateHouseToFaceMainRoad(house, housePosition);

                    housePositionsByLabel[label].Add(housePosition);
                    houseRotationsByLabel[label].Add(house.transform.rotation);

                    Vector3 nearestPointOnMainRoad = GetNearestPointOnMainRoad(housePosition);
                    GenerateSmallRoad(nearestPointOnMainRoad, housePosition, terrain);
                }
            }
        }
    }

    bool IsOnMainRoad(Vector3 position)
    {
        foreach (Vector3 roadPosition in roadPositions)
        {
            if (Vector3.Distance(position, roadPosition) < 5f) // Adjust this distance as needed
            {
                return true;
            }
        }
        return false;
    }
    void RotateHouseToFaceMainRoad(GameObject house, Vector3 housePosition)
    {
        Vector3 nearestPointOnMainRoad = GetNearestPointOnMainRoad(housePosition);
        Vector3 directionToFace = nearestPointOnMainRoad - housePosition;
        directionToFace.y = 0; // Keep the rotation on the horizontal plane
        house.transform.rotation = Quaternion.LookRotation(directionToFace);
    }

    public Vector3 GetNearestPointOnMainRoad(Vector3 housePosition)
    {
        // Find the nearest point on the main road
        Vector3 nearestPoint = roadPositions[0];
        float minDistance = Vector3.Distance(housePosition, roadPositions[0]);

        foreach (Vector3 roadPosition in roadPositions)
        {
            float distance = Vector3.Distance(housePosition, roadPosition);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearestPoint = roadPosition;
            }
        }

        return nearestPoint;
    }
    void GenerateSmallRoad(Vector3 mainRoadPosition, Vector3 housePosition, Terrain terrain)
    {
        Debug.Log($"Generating small road between {mainRoadPosition} and {housePosition}");

        // Calculate the number of segments based on the distance
        int segments = Mathf.CeilToInt(Vector3.Distance(mainRoadPosition, housePosition) / 1f); // Increase the number of segments
        Vector3 direction = (housePosition - mainRoadPosition) / segments;

        for (int i = 0; i < segments; i++)
        {
            Vector3 segmentStart = mainRoadPosition + direction * i;
            Vector3 segmentEnd = mainRoadPosition + direction * (i + 1);

            // Adjust the y-coordinate to match the terrain height
            segmentStart.y = terrain.SampleHeight(segmentStart) + 0.1f; // Slightly above the terrain
            segmentEnd.y = terrain.SampleHeight(segmentEnd) + 0.1f;

            CreateRoadBetween(segmentStart, segmentEnd, terrain);
        }
    }


    void CreateRoadBetween(Vector3 position1, Vector3 position2, Terrain terrain)
    {
        Debug.Log($"Creating road between {position1} and {position2}");

        Vector3 midPoint = (position1 + position2) / 2;
        float roadLength = Vector3.Distance(position1, position2);
        float roadWidth = 0.5f; // Adjust road width to smaller size
        float minHeightAboveWater = waterHeight + 0.1f; // Minimum height of the road above the water

        GameObject road = Instantiate(roadPrefab, midPoint, Quaternion.identity);

        road.transform.localScale = new Vector3(roadWidth, 1.0f, roadLength);
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
    void GenerateMainRoad(Vector3 position1, Vector3 position2)
    {
        Debug.Log($"Generating main road segment between {position1} and {position2}");

        // Find the cluster ID for the start and end positions
        int clusterId1 = clusters.First(c => c.Value.Contains(position1)).Key;
        int clusterId2 = clusters.First(c => c.Value.Contains(position2)).Key;

        if (clusterId1 != clusterId2)
        {
            Debug.LogWarning($"Cannot generate main road segment between different clusters: {clusterId1} and {clusterId2}");
            return;
        }

        Terrain terrain = clusterTerrains[clusterId1];

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

            CreateRoadBetween(start, end, terrain);
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
    void SpawnTreesAndRocks()
    {
        foreach (var cluster in clusters)
        {
            Biome biome = clusterBiomes[cluster.Key];
            foreach (var position in cluster.Value)
            {
                SpawnBiomeObjects(biome, position);
            }
        }
    }
    void SpawnBiomeObjects(Biome biome, Vector3 clusterCenter)
    {
        for (int i = 0; i < 50; i++) // Example number of objects to spawn per cluster
        {
            SpawnObject(biome.treePrefabs, clusterCenter);
            SpawnObject(biome.rockPrefabs, clusterCenter);
        }
    }
    void SpawnObject(List<GameObject> prefabs, Vector3 clusterCenter)
    {
        if (prefabs.Count == 0) return;

        Vector3 position;
        int attempts = 0;
        bool validPosition = false;

        do
        {
            float x = UnityEngine.Random.Range(clusterCenter.x - 50, clusterCenter.x + 50);
            float z = UnityEngine.Random.Range(clusterCenter.z - 50, clusterCenter.z + 50);
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
    void SetupMiniMap()
    {
        miniMapController.minX = minX;
        miniMapController.maxX = maxX;
        miniMapController.minZ = minZ;
        miniMapController.maxZ = maxZ;
        miniMapController.SetupMiniMap();
    }

    public void TeleportTo(int clusterID)
    {
        // Check if the clusterID exists in your dictionary
        if (clusterTerrains.ContainsKey(clusterID))
        {
            Terrain terrain = clusterTerrains[clusterID];

            // Get terrain size
            Vector3 terrainSize = terrain.terrainData.size;

            // Calculate random position within the terrain bounds
            float randomX = UnityEngine.Random.Range(terrain.transform.position.x, terrain.transform.position.x + terrainSize.x);
            float randomZ = UnityEngine.Random.Range(terrain.transform.position.z, terrain.transform.position.z + terrainSize.z);
            float yPosition = terrain.SampleHeight(new Vector3(randomX, 0, randomZ)) + terrain.transform.position.y; // Adjust y to terrain height

            // Set the teleport position
            Vector3 teleportPosition = new Vector3(randomX, yPosition + 30, randomZ);

            // Teleport the GameObject to the calculated position
            player.transform.position = teleportPosition;

            Debug.Log($"Teleported to {teleportPosition}");
        }
        else
        {
            Debug.LogError($"ClusterID {clusterID} not found in clusterTerrains dictionary.");
        }
    }


}

[System.Serializable]
public class Biome
{
    public string name;
    public Texture2D terrainTexture;
    public Vector2 textureOffset;
    public Vector2 textureTiling;
    public List<GameObject> treePrefabs;
    public List<GameObject> rockPrefabs;
}
