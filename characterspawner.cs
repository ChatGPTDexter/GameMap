using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class CharacterSpawner : MonoBehaviour
{
    [SerializeField] private GameObject characterPrefab;
    [SerializeField] private GameObject uiCanvasPrefab;
    [SerializeField] private TextAsset coordinatesCsvFile;
    [SerializeField] private TextAsset transcriptsCsvFile;
    [SerializeField] private Terrain terrain; // Reference to the Terrain component
    [SerializeField] private MapGenerator mapGenerator; // Reference to MapGenerator script

    private class TopicInfo
    {
        public Vector3 Position { get; set; }
        public string Label { get; set; }
        public string Transcript { get; set; }
    }

    void Start()
    {
        if (coordinatesCsvFile == null || transcriptsCsvFile == null)
        {
            Debug.LogError("CSV files are not assigned.");
            return;
        }

        if (mapGenerator == null)
        {
            Debug.LogError("MapGenerator script reference not assigned.");
            return;
        }

        List<TopicInfo> topics = MergeDataFromCSVFiles(coordinatesCsvFile, transcriptsCsvFile);
        Debug.Log("Number of topics combined from CSVs: " + topics.Count);
        if (topics.Count > 0)
        {
            SpawnCharacters(topics);
        }
        else
        {
            Debug.LogWarning("No topics found to spawn characters.");
        }
    }

    private List<TopicInfo> MergeDataFromCSVFiles(TextAsset coordinatesCsv, TextAsset transcriptsCsv)
    {
        var coordinates = ReadCoordinatesFromCSV(coordinatesCsv);
        var transcripts = ReadTranscriptsFromCSV(transcriptsCsv);

        List<TopicInfo> topics = new List<TopicInfo>();

        foreach (var coordinate in coordinates)
        {
            if (transcripts.TryGetValue(coordinate.Label, out string transcript))
            {
                topics.Add(new TopicInfo
                {
                    Position = new Vector3(coordinate.Position.x, coordinate.Position.z, coordinate.Position.y),
                    Label = coordinate.Label,
                    Transcript = transcript
                });
            }
            else
            {
                Debug.LogWarning($"No transcript found for label: {coordinate.Label}");
            }
        }

        return topics;
    }

    private List<(string Label, Vector3 Position)> ReadCoordinatesFromCSV(TextAsset csvFile)
    {
        List<(string Label, Vector3 Position)> coordinates = new List<(string Label, Vector3 Position)>();

        try
        {
            var lines = csvFile.text.Split('\n');
            Debug.Log("Number of lines read from coordinates CSV: " + lines.Length);

            if (lines.Length < 2)
            {
                Debug.LogWarning("The coordinates CSV file is empty or only contains a header.");
                return coordinates;
            }

            foreach (var line in lines.Skip(1))
            {
                var values = SplitCsvLine(line);

                if (values.Length >= 4)
                {
                    if (float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                        float.TryParse(values[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    {
                        Vector3 position = new Vector3(x, y, z);
                        string label = values[0].Trim('"');
                        coordinates.Add((label, position));
                        Debug.Log($"Parsed coordinate: {label} at position: {position}");
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to parse coordinates for line: {line}");
                    }
                }
                else
                {
                    Debug.LogWarning($"Insufficient data in line: {line}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Error reading coordinates CSV file: " + ex.Message);
        }

        return coordinates;
    }

    private Dictionary<string, string> ReadTranscriptsFromCSV(TextAsset csvFile)
    {
        Dictionary<string, string> transcripts = new Dictionary<string, string>();

        try
        {
            var lines = csvFile.text.Split('\n');
            Debug.Log("Number of lines read from transcripts CSV: " + lines.Length);

            foreach (var line in lines.Skip(1))
            {
                var values = SplitCsvLine(line);

                if (values.Length >= 2)
                {
                    string label = values[0].Trim('"');
                    string transcript = values[1].Trim('"');
                    transcripts[label] = transcript;
                    Debug.Log($"Parsed transcript for label: {label}");
                }
                else
                {
                    Debug.LogWarning($"Insufficient data in line: {line}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Error reading transcripts CSV file: " + ex.Message);
        }

        return transcripts;
    }

    private void SpawnCharacters(List<TopicInfo> topics)
    {
        foreach (var topic in topics)
        {
            Debug.Log($"Spawning character for topic: {topic.Label} at position: {topic.Position}");

            Vector3 adjustedPosition = AdjustPositionToTerrain(topic.Position);
            GameObject character = Instantiate(characterPrefab, adjustedPosition, Quaternion.identity);

            var characterAI = character.GetComponent<CharacterAI>();
            if (characterAI != null)
            {
                Debug.Log($"Character {topic.Label} initialized with CharacterAI component.");
                characterAI.Initialize(topic.Label, topic.Transcript);
            }
            else
            {
                Debug.LogError($"Character {topic.Label} does not have a CharacterAI component attached.");
            }

            character.name = topic.Label;

            Vector3 uiPosition = character.transform.position + new Vector3(2, 0, 0);
            GameObject uiCanvas = Instantiate(uiCanvasPrefab, uiPosition, Quaternion.Euler(0, 180, 0), character.transform);

            // Set the canvas to world space and parent it to the character
            Canvas canvasComponent = uiCanvas.GetComponent<Canvas>();
            canvasComponent.renderMode = RenderMode.WorldSpace;

            uiCanvas.transform.SetParent(character.transform);
            uiCanvas.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            AssignUIElements(characterAI, uiCanvas);
        }
    }

    private Vector3 AdjustPositionToTerrain(Vector3 position)
    {
        if (terrain == null)
        {
            Debug.LogError("Terrain component is not assigned.");
            return position;
        }

        float terrainHeight = terrain.SampleHeight(position) + terrain.transform.position.y;
        Vector3 houseDimensions = GetHouseDimensions();

        // Calculate the offset to move the character outside the house in the z direction
        float zOffset = houseDimensions.z / 2 + 1f;

        return new Vector3(
            position.x,
            terrainHeight,
            position.z + zOffset
        );
    }

    private Vector3 GetHouseDimensions()
    {
        if (mapGenerator.housePrefabs.Count == 0)
        {
            Debug.LogError("No house prefabs assigned in MapGenerator.");
            return Vector3.zero;
        }

        // Assuming we use the first house prefab for dimensions
        GameObject housePrefab = mapGenerator.housePrefabs[0];
        MeshFilter meshFilter = housePrefab.GetComponentInChildren<MeshFilter>();
        if (meshFilter == null)
        {
            Debug.LogError($"House prefab '{housePrefab.name}' does not have a MeshFilter component.");
            return Vector3.zero;
        }

        Vector3 houseSize = meshFilter.sharedMesh.bounds.size;
        Vector3 houseScale = housePrefab.transform.localScale;
        houseSize = Vector3.Scale(houseSize, houseScale);

        return houseSize;
    }

    private void AssignUIElements(CharacterAI characterAI, GameObject uiCanvas)
    {
        if (characterAI == null || uiCanvas == null)
        {
            Debug.LogError("CharacterAI or uiCanvas is null.");
            return;
        }

        TMP_InputField inputField = uiCanvas.GetComponentInChildren<TMP_InputField>();

        TMP_Text responseText = uiCanvas.GetComponentsInChildren<TMP_Text>()
                                         .FirstOrDefault(t => t.gameObject.name != "Placeholder" && t.gameObject.name != inputField.textComponent.gameObject.name);

        Button submitButton = uiCanvas.GetComponentInChildren<Button>();

        if (inputField != null)
        {
            characterAI.userInputField = inputField;
            Debug.Log("Input field assigned.");
        }
        else
        {
            Debug.LogWarning("No TMP_InputField found in the UI Canvas.");
        }

        if (responseText != null)
        {
            characterAI.responseText = responseText;
            responseText.fontSize = 8.0f;
            responseText.enableAutoSizing = false;
            responseText.alignment = TextAlignmentOptions.TopLeft;
            Debug.Log("Response text assigned and scaled down.");
        }
        else
        {
            Debug.LogWarning("No TMP_Text found in the UI Canvas for response.");
        }

        if (submitButton != null)
        {
            characterAI.submitButton = submitButton;
            submitButton.onClick.AddListener(characterAI.OnAskQuestion);
            Debug.Log("Submit button assigned and listener added.");
        }
        else
        {
            Debug.LogWarning("No Button found in the UI Canvas.");
        }
    }

    private string[] SplitCsvLine(string line)
    {
        List<string> values = new List<string>();
        bool inQuotes = false;
        string value = "";

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(value);
                value = "";
            }
            else
            {
                value += c;
            }
        }
        values.Add(value);

        return values.ToArray();
    }
}
