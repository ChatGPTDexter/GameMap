using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using TMPro;

public class HouseNames : MonoBehaviour
{
    public CSVData csvData1; // CSV containing transcripts
    public CSVData csvData2; // CSV containing coordinates (x, y, z)

    private const string OpenAIAPIKey = "api-key";
    private const string OpenAIEndpoint = "https://api.openai.com/v1/chat/completions";

    void Start()
    {
        StartCoroutine(ProcessCSVData());
    }

    private IEnumerator ProcessCSVData()
    {
        if (csvData1 == null || csvData1.csvFile == null || csvData2 == null || csvData2.csvFile == null)
        {
            Debug.LogError("CSV Data or CSV file not set in the inspector.");
            yield break;
        }

        // Parse CSV data from both files, skipping the first row for headers
        csvData1.parsedData = ParseCSV(csvData1.csvFile.text, true);
        csvData2.parsedData = ParseCSV(csvData2.csvFile.text, true);

        // Ensure both datasets have the same number of rows
        int numRows = Mathf.Min(csvData1.parsedData.Count, csvData2.parsedData.Count);

        // Iterate through each row
        for (int i = 0; i < numRows; i++)
        {
            // Extract transcript from CSV 1 (assuming it's in the fourth column, index 3)
            if (csvData1.parsedData[i].Length < 4)
            {
                Debug.LogWarning($"Not enough columns in row {i + 1} of CSV 1.");
                continue;
            }
            string transcript = csvData1.parsedData[i][3];

            // Generate puzzle room name based on transcript
            yield return StartCoroutine(MakeName(transcript, i));
        }
    }

    private IEnumerator MakeName(string transcript, int rowIndex)
    {
        // Initialize chat history with system message
        var systemMessage = new OpenAIMessage
        {
            role = "system",
            content = $"You answer very createively and concise."
        };

        // Make OpenAI request or any other processing with the transcript
        yield return StartCoroutine(GetResponseFromAI(transcript, rowIndex));
    }

    private IEnumerator GetResponseFromAI(string transcript, int rowIndex)
    {
        var requestData = new OpenAIRequest
        {
            model = "gpt-3.5-turbo",
            messages = new OpenAIMessage[] { new OpenAIMessage { role = "user", content = $"Create a createive name for a puzzle room with this topic: {transcript}"} },
            max_tokens = 150,
            temperature = 0.7f
        };

        string jsonData = JsonUtility.ToJson(requestData);

        using (UnityWebRequest request = new UnityWebRequest(OpenAIEndpoint, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {OpenAIAPIKey}");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"Error: {request.error}\nResponse: {request.downloadHandler.text}");
                // Handle error here
            }
            else
            {
                Debug.Log($"Response: {request.downloadHandler.text}");
                var jsonResponse = JsonUtility.FromJson<OpenAIResponse>(request.downloadHandler.text);
                if (jsonResponse.choices != null && jsonResponse.choices.Count > 0)
                {
                    var firstChoice = jsonResponse.choices[0];
                    var messageContent = firstChoice.message.content.Trim();

                    // Display puzzle room name at the corresponding coordinates from CSV 2
                    if (csvData2.parsedData[rowIndex].Length >= 4)
                    {
                        if (float.TryParse(csvData2.parsedData[rowIndex][1], out float x) &&
                            float.TryParse(csvData2.parsedData[rowIndex][2], out float z))
                        {
                            // Create a TextMeshPro object to display the name
                            GameObject nameObject = new GameObject("PuzzleRoomName");
                            Terrain terrain = Terrain.activeTerrain; // Change this to your actual terrain reference if needed

                            // Get height at (x, z) position
                            float terrainHeight = terrain.SampleHeight(new Vector3(x, 0, z)) + 20f;
                            nameObject.transform.position = new Vector3(x, terrainHeight, z);

                            TextMeshPro textMesh = nameObject.AddComponent<TextMeshPro>();
                            textMesh.text = messageContent;
                            textMesh.fontSize = 40; // Adjust size as needed
                            textMesh.alignment = TextAlignmentOptions.Center; // Center text

                            RectTransform rectTransform = nameObject.GetComponent<RectTransform>();
                            rectTransform.sizeDelta = new Vector2(100, 50); // Adjust these values as needed
                            rectTransform.pivot = new Vector2(0.5f, 0.5f); // Center the pivot

                            // Optionally, you can parent it to another GameObject for organization
                            nameObject.transform.SetParent(transform);
                        }
                        else
                        {
                            Debug.LogWarning($"Invalid coordinate format in row {rowIndex + 1} of CSV 2.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Not enough columns in row {rowIndex + 1} of CSV 2 to display name.");
                    }
                }
                else
                {
                    // Handle no response from AI
                }
            }
        }
    }

    List<string[]> ParseCSV(string csvText, bool skipFirstRow = false)
    {
        List<string[]> parsedData = new List<string[]>();
        string[] lines = csvText.Split(new char[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);

        for (int i = skipFirstRow ? 1 : 0; i < lines.Length; i++)
        {
            parsedData.Add(ParseCSVLine(lines[i]));
        }

        return parsedData;
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

[System.Serializable]
public class CSVData
{
    public TextAsset csvFile;
    public List<string[]> parsedData;
}
