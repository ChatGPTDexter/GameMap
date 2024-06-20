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

    private List<CharacterAI> spawnedCharacters = new List<CharacterAI>();

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

            Debug.Log($"This is adjusted position {adjustedPosition}!");
            GameObject character = Instantiate(characterPrefab, adjustedPosition, Quaternion.identity);

            var characterAI = character.GetComponent<CharacterAI>();
            if (characterAI != null)
            {
                Debug.Log($"Character {topic.Label} initialized with CharacterAI component.");
                characterAI.Initialize(topic.Label, topic.Transcript);
                spawnedCharacters.Add(characterAI);
            }
            else
            {
                Debug.LogError($"Character {topic.Label} does not have a CharacterAI component attached.");
            }

            character.name = topic.Label;

        

            // Position the UI Canvas slightly in front of the character
            Vector3 uiPosition = character.transform.position + character.transform.forward * 0.5f + new Vector3(0, 2, 0); // Move 0.5 units in front and 2 units above the character
            GameObject uiCanvas = Instantiate(uiCanvasPrefab, uiPosition, Quaternion.identity);

            

            Canvas canvasComponent = uiCanvas.GetComponent<Canvas>();
            canvasComponent.renderMode = RenderMode.WorldSpace;

            // Set the canvas as a child of the character to follow its movements
            uiCanvas.transform.SetParent(character.transform);

            // Apply a 180-degree rotation to the canvas around the Y-axis
            uiCanvas.transform.localRotation = Quaternion.Euler(0, 180, 0);

            uiCanvas.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            // Set the canvas sorting order (Remove the duplicate declaration)
            canvasComponent.sortingOrder = 100; // Higher value to ensure it renders on top

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

        Vector3 houseDimensions = GetHouseDimensions();

        float zOffset = houseDimensions.z / 2 + 1f;

        // Get the terrain height at the original position
        float terrainHeight = terrain.SampleHeight(new Vector3(position.x, 0, position.z));

        // Adjust the position by adding the zOffset to the z-coordinate
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

        uiCanvas.transform.rotation = Quaternion.Euler(0, 180, 0);

        TMP_InputField inputField = uiCanvas.GetComponentInChildren<TMP_InputField>();

        TMP_Text responseText = uiCanvas.GetComponentsInChildren<TMP_Text>()
                                         .FirstOrDefault(t => t.gameObject.name != "Placeholder" && t.gameObject.name != inputField.textComponent.gameObject.name);

        Button submitButton = uiCanvas.GetComponentInChildren<Button>();

        if (inputField != null)
        {
            characterAI.userInputField = inputField;
            // Manually set the position and size of the input field
            RectTransform inputFieldRect = inputField.GetComponent<RectTransform>();
            inputFieldRect.anchoredPosition = new Vector2(0, -40); // Adjust position
            inputFieldRect.sizeDelta = new Vector2(400, 60); // Adjust size
            inputField.textComponent.alignment = TextAlignmentOptions.TopLeft; // Align text to the top left
            inputField.textComponent.fontSize = 20; // Set font size
            inputField.textComponent.color = Color.black; // Set text color to black

            // Ensure the Return key submits the question
            inputField.onSubmit.AddListener(delegate { characterAI.OnAskQuestion(); });
        }
        else
        {
            Debug.LogWarning("No TMP_InputField found in the UI Canvas.");
        }

        if (responseText != null)
        {
            characterAI.responseText = responseText;
            responseText.fontSize = 24.0f;
            responseText.color = Color.black;
            responseText.enableAutoSizing = false;
            responseText.alignment = TextAlignmentOptions.TopLeft;
            responseText.fontStyle = FontStyles.Bold | FontStyles.Italic;
            // Adjust the position and size of the response text box
            RectTransform responseTextRect = responseText.GetComponent<RectTransform>();
            responseTextRect.anchoredPosition = new Vector2(0, 60); // Adjust position
            responseTextRect.sizeDelta = new Vector2(400, 100); // Adjust size
            // Add a white background
        }
        else
        {
            Debug.LogWarning("No TMP_Text found in the UI Canvas.");
        }

        if (submitButton != null)
        {
            characterAI.submitButton = submitButton;
            // Manually set the position and size of the button
            RectTransform buttonRect = submitButton.GetComponent<RectTransform>();
            buttonRect.anchoredPosition = new Vector2(0, -100); // Adjust position
            buttonRect.sizeDelta = new Vector2(200, 50); // Adjust size
            submitButton.onClick.AddListener(characterAI.OnAskQuestion);
            Debug.Log("Submit button assigned.");
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

    public CharacterAI GetClosestCharacter(Vector3 position, float radius)
    {
        CharacterAI closestCharacter = null;
        float closestDistance = radius;

        foreach (var character in spawnedCharacters)
        {
            float distance = Vector3.Distance(position, character.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestCharacter = character;
            }
        }

        return closestCharacter;
    }
}
