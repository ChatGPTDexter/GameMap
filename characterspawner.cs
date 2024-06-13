using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using System.Linq; // Needed for LINQ operations
using TMPro; // For TextMeshPro components
using UnityEngine.UI; // For Button and other UI components

public class CharacterSpawner : MonoBehaviour
{
    [SerializeField] private GameObject characterPrefab; // Assign your character prefab here
    [SerializeField] private GameObject uiCanvasPrefab; // Assign your UI Canvas prefab here
    [SerializeField] private string coordinatesCsvFileName = "coordinates.csv"; // Coordinates CSV file
    [SerializeField] private string transcriptsCsvFileName = "transcripts.csv"; // Transcripts CSV file

    private class TopicInfo
    {
        public Vector3 Position { get; set; }
        public string Label { get; set; }
        public string Transcript { get; set; }
    }

    void Start()
    {
        string coordinatesFilePath = Path.Combine(Application.dataPath, coordinatesCsvFileName);
        string transcriptsFilePath = Path.Combine(Application.dataPath, transcriptsCsvFileName);

        List<TopicInfo> topics = MergeDataFromCSVFiles(coordinatesFilePath, transcriptsFilePath);
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

    private List<TopicInfo> MergeDataFromCSVFiles(string coordinatesFilePath, string transcriptsFilePath)
    {
        // Read coordinates and labels
        var coordinates = ReadCoordinatesFromCSV(coordinatesFilePath);

        // Read transcripts
        var transcripts = ReadTranscriptsFromCSV(transcriptsFilePath);

        // Combine data
        List<TopicInfo> topics = new List<TopicInfo>();

        foreach (var coordinate in coordinates)
        {
            if (transcripts.TryGetValue(coordinate.Label, out string transcript))
            {
                topics.Add(new TopicInfo
                {
                    Position = new Vector3(coordinate.Position.x, coordinate.Position.z, coordinate.Position.y), // Swapping y and z
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

    private List<(string Label, Vector3 Position)> ReadCoordinatesFromCSV(string filePath)
    {
        List<(string Label, Vector3 Position)> coordinates = new List<(string Label, Vector3 Position)>();

        try
        {
            // Read all lines from the CSV file
            var lines = File.ReadAllLines(filePath);
            Debug.Log("Number of lines read from coordinates CSV: " + lines.Length);

            if (lines.Length < 2)
            {
                Debug.LogWarning("The coordinates CSV file is empty or only contains a header.");
                return coordinates;
            }

            // Skip the header line and parse the rest
            foreach (var line in lines.Skip(1))
            {
                // Split the line by commas, but taking care of quoted strings
                var values = SplitCsvLine(line);

                if (values.Length >= 4)
                {
                    // Try to parse the x, y, z coordinates
                    if (float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                        float.TryParse(values[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                    {
                        // Create a new Vector3 and add to the list
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

    private Dictionary<string, string> ReadTranscriptsFromCSV(string filePath)
    {
        Dictionary<string, string> transcripts = new Dictionary<string, string>();

        try
        {
            // Read all lines from the CSV file
            var lines = File.ReadAllLines(filePath);
            Debug.Log("Number of lines read from transcripts CSV: " + lines.Length);

            // Skip the header line and parse the rest
            foreach (var line in lines.Skip(1))
            {
                // Split the line by commas, but taking care of quoted strings
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
            GameObject character = Instantiate(characterPrefab, topic.Position, Quaternion.identity);

            // Verify if the character prefab has the CharacterAI component
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

            // Set the name of the instantiated character to the topic label
            character.name = topic.Label;

            // Instantiate the UI Canvas next to the character
            Vector3 uiPosition = character.transform.position + new Vector3(2, 0, 0); // Adjust UI position relative to character
            GameObject uiCanvas = Instantiate(uiCanvasPrefab, uiPosition, Quaternion.Euler(0, 180, 0), character.transform);

            // Scale down the UI Canvas to fit nicely
            uiCanvas.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            // Assign the UI elements to the CharacterAI script
            AssignUIElements(characterAI, uiCanvas);
        }
    }

private void AssignUIElements(CharacterAI characterAI, GameObject uiCanvas)
{
    if (characterAI == null || uiCanvas == null)
    {
        Debug.LogError("CharacterAI or uiCanvas is null.");
        return;
    }

    // Assign the input field for user input
    TMP_InputField inputField = uiCanvas.GetComponentInChildren<TMP_InputField>();
    
    // Find the response text which is a TMP_Text that is not named "Placeholder"
    TMP_Text responseText = uiCanvas.GetComponentsInChildren<TMP_Text>()
                                     .FirstOrDefault(t => t.gameObject.name != "Placeholder" && t.gameObject.name != inputField.textComponent.gameObject.name);

    // Assign the button used to submit the question
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

        // Adjust the properties of the response text to scale it down
        responseText.fontSize = 8.0f; // Set a smaller font size
        responseText.enableAutoSizing = false; // Disable auto-sizing if specific size control is needed
        responseText.alignment = TextAlignmentOptions.TopLeft; // Align text to the top left
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



    // Utility method to correctly split a CSV line taking care of quoted strings
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
                inQuotes = !inQuotes; // Toggle state
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
