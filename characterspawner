using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using System.Linq;

public class CharacterSpawner : MonoBehaviour
{
    [SerializeField] private GameObject characterPrefab; // Assign your character prefab here
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
        SpawnCharacters(topics);
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
                    Position = coordinate.Position,
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

            // Set the name of the instantiated character to the topic label
            character.name = topic.Label;

            var characterAI = character.GetComponent<CharacterAI>();
            if (characterAI != null)
            {
                characterAI.Initialize(topic.Label, topic.Transcript);
            }
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
