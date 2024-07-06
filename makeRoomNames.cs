using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using TMPro;

public class HouseNames : MonoBehaviour
{
    public CSVData transcripts; // CSV containing transcripts
    public CSVData vidCords; // CSV containing coordinates (x, y, z)
    public string duplicateLayerName = "MiniMapOnly"; // Name of the layer for duplicates

    private const string OpenAIAPIKey = "api-key";
    private const string OpenAIEndpoint = "https://api.openai.com/v1/chat/completions";

    private List<GameObject> nameObjects = new List<GameObject>(); // Keep track of created name objects
    private Camera mainCamera; // Reference to the main camera
    private GameCompletion gameCompletion;

    private Dictionary<string, string> nameMappings = new Dictionary<string, string>();
    private MapGenerator mapGenerator;
    private CharacterSpawner characterSpawner;

    public List<string> clusterNames = new List<string>();

    void Start()
    {
        gameCompletion = FindObjectOfType<GameCompletion>();
        mapGenerator = FindObjectOfType<MapGenerator>();
        characterSpawner = FindObjectOfType<CharacterSpawner>();

        StartCoroutine(GenerateClusterNames());
        StartCoroutine(ProcessCSVData());

        // Get reference to the main camera
        mainCamera = Camera.main;
    }

    void Update()
    {
        // Ensure mainCamera reference is valid
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
                return;
        }

        // Update rotation of each name object to face the camera
        foreach (var nameObject in nameObjects)
        {
            if (nameObject.CompareTag("FaceCamera"))
            {
                Vector3 directionToCamera = mainCamera.transform.position - nameObject.transform.position;
                Quaternion rotation = Quaternion.LookRotation(-directionToCamera);
                nameObject.transform.rotation = rotation;
            }
        }
    }
    private IEnumerator GenerateClusterNames()
    {
        if (mapGenerator != null && mapGenerator.clusterLabels != null)
        {
            foreach (var cluster in mapGenerator.clusterLabels)
            {
                int clusterId = cluster.Key;
                List<string> labels = cluster.Value;

                // Concatenate all labels for the cluster to form the name
                string concatenatedLabels = string.Join(", ", labels);

                // Generate cluster name using ChatGPT
                yield return StartCoroutine(MakeClusterName(clusterId, concatenatedLabels));
            }
            Debug.Log("Clusters created successfullly!");

            characterSpawner.spawnSecondCharacter(clusterNames);
        }
        else
        {
            Debug.LogWarning("MapGenerator or clusterLabels is null.");
        }
    }

    private IEnumerator MakeClusterName(int clusterId, string concatenatedLabels)
    {
        bool nameGenerated = false;
        string generatedName = "";

        // Retry loop to generate a name
        while (!nameGenerated)
        {
            yield return StartCoroutine(GetClusterResponseFromAI(concatenatedLabels, (name) =>
            {
                if (!string.IsNullOrEmpty(name))
                {
                    nameGenerated = true;
                    generatedName = name;
                }
            }));
        }

        // Once name is generated, log the cluster name
        if (!string.IsNullOrEmpty(generatedName))
        {
            Debug.Log($"Cluster {clusterId} Name: {generatedName}");
            clusterNames.Add(generatedName);
        }
        else
        {
            Debug.LogWarning($"Failed to generate name for cluster with labels: {concatenatedLabels}");
        }
    }

    private IEnumerator GetClusterResponseFromAI(string concatenatedLabels, System.Action<string> callback)
    {
        var requestData = new OpenAIRequest
        {
            model = "gpt-3.5-turbo",
            messages = new OpenAIMessage[] { new OpenAIMessage { role = "user", content = $"Create a creative, concise, and fun name for a cluster with these node names: {concatenatedLabels}, that is to be in a video game." } },
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
                callback?.Invoke("");
            }
            else
            {
                Debug.Log($"Response: {request.downloadHandler.text}");
                var jsonResponse = JsonUtility.FromJson<OpenAIResponse>(request.downloadHandler.text);
                if (jsonResponse.choices != null && jsonResponse.choices.Count > 0)
                {
                    var firstChoice = jsonResponse.choices[0];
                    var messageContent = firstChoice.message.content.Trim();
                    callback?.Invoke(messageContent);
                }
                else
                {
                    // Handle no response from AI
                    callback?.Invoke("");
                }
            }
        }
    }

    private IEnumerator ProcessCSVData()
    {
        if (transcripts == null || transcripts.csvFile == null || vidCords == null || vidCords.csvFile == null)
        {
            Debug.LogError("CSV Data or CSV file not set in the inspector.");
            yield break;
        }

        // Parse CSV data from both files, skipping the first row for headers
        transcripts.parsedData = ParseCSV(transcripts.csvFile.text, true);
        vidCords.parsedData = ParseCSV(vidCords.csvFile.text, true);

        // Ensure both datasets have the same number of rows
        int numRows = Mathf.Min(transcripts.parsedData.Count, vidCords.parsedData.Count);

        // Iterate through each row
        for (int i = 0; i < numRows; i++)
        {
            // Extract transcript from CSV 1 (assuming it's in the fourth column, index 3)
            if (transcripts.parsedData[i].Length < 4)
            {
                Debug.LogWarning($"Not enough columns in row {i + 1} of CSV 1.");
                continue;
            }
            string transcript = transcripts.parsedData[i][3];

            // Generate puzzle room name based on transcript
            yield return StartCoroutine(MakeName(transcript, i));

            yield return new WaitForSeconds(1f);
        }
    }

    private IEnumerator MakeName(string transcript, int rowIndex)
    {
        bool nameGenerated = false;
        string generatedName = "";

        // Retry loop to generate a name
        while (!nameGenerated)
        {
            yield return StartCoroutine(GetResponseFromAI(transcript, rowIndex, (name) =>
            {
                if (!string.IsNullOrEmpty(name))
                {
                    nameGenerated = true;
                    generatedName = name;
                }
            }));
        }

        // Once name is generated, create the TextMeshPro object
        if (!string.IsNullOrEmpty(generatedName))
        {
            string originalName = transcripts.parsedData[rowIndex][0].Trim(); // Assuming transcript is in the fourth column
            nameMappings[originalName] = generatedName;
            CreateNameObject(generatedName, rowIndex);
        }
        else
        {
            Debug.LogWarning($"Failed to generate name for transcript: {transcript}");
        }
    }


    private IEnumerator GetResponseFromAI(string transcript, int rowIndex, System.Action<string> callback)
    {
        var requestData = new OpenAIRequest
        {
            model = "gpt-3.5-turbo",
            messages = new OpenAIMessage[] { new OpenAIMessage { role = "user", content = $"Create a creative name for a puzzle room with this topic: {transcript}" } },
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
                callback?.Invoke("");
            }
            else
            {
                Debug.Log($"Response: {request.downloadHandler.text}");
                var jsonResponse = JsonUtility.FromJson<OpenAIResponse>(request.downloadHandler.text);
                if (jsonResponse.choices != null && jsonResponse.choices.Count > 0)
                {
                    var firstChoice = jsonResponse.choices[0];
                    var messageContent = firstChoice.message.content.Trim();
                    callback?.Invoke(messageContent);
                }
                else
                {
                    // Handle no response from AI
                    callback?.Invoke("");
                }
            }
        }
    }

    private void CreateNameObject(string nameContent, int rowIndex)
    {
        // Display puzzle room name at the corresponding coordinates from CSV 2
        if (vidCords.parsedData[rowIndex].Length >= 4)
        {
            if (float.TryParse(vidCords.parsedData[rowIndex][0], out float x) &&
                float.TryParse(vidCords.parsedData[rowIndex][1], out float z))
            {
                // Create a TextMeshPro object to display the name
                GameObject nameObject = new GameObject("PuzzleRoomName");

                // Set the position based on (x, 0, z)
                float terrainHeight = Terrain.activeTerrain.SampleHeight(new Vector3(x, 0, z)) + 20f; // Adjust height if needed
                Vector3 namePosition = new Vector3(x, terrainHeight, z);
                nameObject.transform.position = namePosition;

                TextMeshPro textMesh = nameObject.AddComponent<TextMeshPro>();
                textMesh.text = nameContent;
                textMesh.fontSize = 40; // Adjust size as needed
                textMesh.alignment = TextAlignmentOptions.Center; // Center text

                RectTransform rectTransform = nameObject.GetComponent<RectTransform>();
                rectTransform.sizeDelta = new Vector2(200, 50); // Adjust these values as needed to make it wider
                rectTransform.pivot = new Vector2(0.5f, 0.5f); // Center the pivot

                // Optionally, you can parent it to another GameObject for organization
                nameObject.transform.SetParent(transform);

                // Tag the original name object to face the camera
                nameObject.tag = "FaceCamera";

                // Add the created nameObject to the list
                nameObjects.Add(nameObject);

                // Create a duplicate 10 units above and rotated to face the sky
                GameObject duplicateNameObject = Instantiate(nameObject, namePosition + Vector3.up * 10f, Quaternion.Euler(90f, 0f, 0f));
                duplicateNameObject.tag = "Untagged"; // Ensure the duplicate does not have the tag
                duplicateNameObject.transform.SetParent(transform);

                // Adjust font size of the duplicate
                TextMeshPro duplicateTextMesh = duplicateNameObject.GetComponent<TextMeshPro>();
                duplicateTextMesh.fontSize = 300; // Increase font size as needed

                // Adjust the size of the duplicate's text box to make it wider
                RectTransform duplicateRectTransform = duplicateNameObject.GetComponent<RectTransform>();
                duplicateRectTransform.sizeDelta = new Vector2(500, 100); // Adjust these values as needed

                // Set the duplicate to the specified layer
                int duplicateLayer = LayerMask.NameToLayer(duplicateLayerName);
                if (duplicateLayer != -1)
                {
                    duplicateNameObject.layer = duplicateLayer;
                }
                else
                {
                    Debug.LogWarning($"Layer '{duplicateLayerName}' not found. Please ensure the layer exists.");
                }

                nameObjects.Add(duplicateNameObject);
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

    public void ChangeCompletedColors(Dictionary<int, Dictionary<string, bool>> clusterMasteryStatus)
    {
        foreach (var cluster in clusterMasteryStatus)
        {
            foreach (var labelStatus in cluster.Value)
            {
                string label = labelStatus.Key.Trim(); // Trim whitespace from label
                bool isMastered = labelStatus.Value;
                // Check if the label exists in nameMappings
                if (nameMappings.ContainsKey(label))
                {
                    string generatedName = nameMappings[label];
                    // Find all TextMeshPro objects that match the generated name
                    foreach (var nameObject in nameObjects)
                    {
                        TextMeshPro textMesh = nameObject.GetComponent<TextMeshPro>();
                        if (textMesh != null && textMesh.text.Trim() == generatedName)
                        {
                            // Change color based on mastery status
                            textMesh.color = isMastered ? Color.green : Color.white;
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"Label '{label}' not found in nameMappings.");
                }
            }
        }
    }
}

[System.Serializable]
public class CSVData
{
    public TextAsset csvFile;
    public List<string[]> parsedData;
}
