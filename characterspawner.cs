using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using TMPro;
using System.Collections;
using UnityEngine.UI;

public class CharacterSpawner : MonoBehaviour
{
    private ApiRequest apiRequest;
    [SerializeField] private GameObject characterPrefab;
    [SerializeField] private GameObject spawnCharacterPrefab; // Assign the spawnCharacterAI here
    [SerializeField] private GameObject uiCanvasPrefab;
    [SerializeField] public TextAsset coordinatesCsvFile;
    [SerializeField] public TextAsset transcriptsCsvFile;
    [SerializeField] private MapGenerator mapGenerator; // Reference to MapGenerator script
    [SerializeField] private Camera mainCamera; 

    private class TopicInfo
    {
        public string Label { get; set; }
        public string Transcript { get; set; }
        public float NormalizedTranscriptLength { get; set; }
        public string URL { get; set; }
    }

    private List<CharacterAI> spawnedCharacters = new List<CharacterAI>();
    private List<SpawnCharacterAI> startSpawnedCharacters = new List<SpawnCharacterAI>();
    private Dictionary<string, bool> masteredTopics = new Dictionary<string, bool>();

    public Dictionary<string, bool> MasteredTopics => masteredTopics;
    public Vector3 secondCord;
    void Start()
    {
        apiRequest = FindObjectOfType<ApiRequest>();
        if (apiRequest == null)
        {
            Debug.LogError("ApiRequest component not found in the scene.");
            return;
        }

        StartCoroutine(WaitForFilesAndInitialize());
    }

    IEnumerator WaitForFilesAndInitialize()
    {
        // Wait until files are ready
        yield return new WaitUntil(() => apiRequest.filesAssigned);

        // Now proceed with the original initialization
        if (coordinatesCsvFile == null || transcriptsCsvFile == null)
        {
            Debug.LogError("CSV files are not assigned.");
            yield break; // Ensure all code paths return a value
        }

        if (mapGenerator == null)
        {
            Debug.LogError("MapGenerator script reference not assigned.");
            yield break; // Ensure all code paths return a value
        }

        List<TopicInfo> topics = MergeDataFromCSVFiles(coordinatesCsvFile, transcriptsCsvFile);
        Debug.Log("Number of topics combined from CSVs: " + topics.Count);
        if (topics.Count > 0)
        {
            SpawnCharacters(topics);
        }

        yield break; // Ensure all code paths return a value
    }

    private List<TopicInfo> MergeDataFromCSVFiles(TextAsset coordinatesCsv, TextAsset transcriptsCsv)
    {
        var coordinates = ReadCoordinatesFromCSV(coordinatesCsv);
        var transcripts = ReadTranscriptsFromCSV(transcriptsCsv);

        List<TopicInfo> topics = new List<TopicInfo>();

        foreach (var coordinate in coordinates)
        {
            string trimmedLabel = coordinate.Label.Trim().Trim('"');

            if (transcripts.TryGetValue(trimmedLabel, out var transcriptInfo))
            {
                topics.Add(new TopicInfo
                {
                    Label = trimmedLabel,
                    Transcript = transcriptInfo.Transcript,
                    NormalizedTranscriptLength = coordinate.NormalizedTranscriptLength,
                    URL = transcriptInfo.URL
                });
            }

        }

        return topics;
    }

    private List<(string Label, float NormalizedTranscriptLength)> ReadCoordinatesFromCSV(TextAsset csvFile)
    {
        List<(string Label, float NormalizedTranscriptLength)> coordinates = new List<(string Label, float NormalizedTranscriptLength)>();

        try
        {
            var lines = csvFile.text.Split('\n');

            if (lines.Length < 2)
            {
                Debug.LogWarning("The coordinates CSV file is empty or only contains a header.");
                return coordinates;
            }

            foreach (var line in lines.Skip(1))
            {
                var values = SplitCsvLine(line);

                if (values.Length >= 6)
                {
                    if (float.TryParse(values[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float normalizedTranscriptLength))
                    {
                        string label = values[4].Trim().Trim('"');
                        coordinates.Add((label, normalizedTranscriptLength));
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to parse normalized transcript length for line: {line}");
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

    private Dictionary<string, (string Transcript, string URL)> ReadTranscriptsFromCSV(TextAsset csvFile)
    {
        Dictionary<string, (string Transcript, string URL)> transcripts = new Dictionary<string, (string Transcript, string URL)>();

        try
        {
            var lines = csvFile.text.Split('\n');

            foreach (var line in lines.Skip(1))
            {
                var values = SplitCsvLine(line);

                if (values.Length >= 4)
                {
                    string label = values[0].Trim().Trim('"');
                    string transcript = values[3].Trim().Trim('"');
                    string url = values[2].Trim().Trim('"');
                    transcripts[label] = (transcript, url);
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
            if (!mapGenerator.housePositionsByLabel.TryGetValue(topic.Label, out List<Vector3> housePositions))
            {
                Debug.LogWarning($"No house positions found for label: {topic.Label}");
                continue;
            }

            if (!mapGenerator.houseRotationsByLabel.TryGetValue(topic.Label, out List<Quaternion> houseRotations))
            {
                Debug.LogWarning($"No house rotations found for label: {topic.Label}");
                continue;
            }

            int numSegments = Mathf.Max(1, Mathf.FloorToInt(topic.NormalizedTranscriptLength));

            for (int i = 0; i < housePositions.Count; i++)
            {
                Vector3 housePosition = housePositions[i];
                Quaternion houseRotation = houseRotations[i];

                // Add 1f to the y-coordinate of the character's position
                Vector3 characterPosition = new Vector3(housePosition.x, housePosition.y + 0.5f, housePosition.z);

                GameObject character = Instantiate(characterPrefab, characterPosition, houseRotation);

                // Start the coroutine to move the character
                StartCoroutine(MoveCharacter(character, 5f));

                var characterAI = character.GetComponent<CharacterAI>();
                if (characterAI != null)
                {
                    string characterTranscript = GetTranscriptSegment(topic.Transcript, numSegments, i % numSegments);
                    characterAI.Initialize($"{topic.Label} - Part {i + 1}", characterTranscript, topic.URL);  // Pass the URL
                    spawnedCharacters.Add(characterAI);
                }
                else
                {
                    Debug.LogError($"Character {topic.Label} does not have a CharacterAI component attached.");
                }

                character.name = $"{topic.Label} - Part {i + 1}";

                // Position the UI Canvas slightly in front of the character
                Vector3 uiPosition = character.transform.position + character.transform.forward * 0.5f + new Vector3(0, 2, 0);
                GameObject uiCanvas = Instantiate(uiCanvasPrefab, uiPosition, houseRotation);

                if (uiCanvas == null)
                {
                    Debug.LogError("Failed to instantiate uiCanvasPrefab.");
                }

                Canvas canvasComponent = uiCanvas.GetComponent<Canvas>();
                canvasComponent.renderMode = RenderMode.WorldSpace;

                // Set the canvas as a child of the character to follow its movements
                uiCanvas.transform.SetParent(character.transform);

                // Apply a 180-degree rotation to the canvas around the Y-axis
                uiCanvas.transform.localRotation = Quaternion.Euler(0, 180, 0);

                uiCanvas.transform.localScale = new Vector3(0.005f, 0.005f, 0.005f);

                // Set the canvas sorting order
                canvasComponent.sortingOrder = 100; // Higher value to ensure it renders on top

                AssignUIElements(characterAI, uiCanvas, i);
            }
        }
    }

    private string GetTranscriptSegment(string transcript, int totalSegments, int segmentIndex)
    {
        if (totalSegments <= 1)
        {
            return transcript;
        }

        var words = transcript.Split(' ');
        int segmentSize = words.Length / totalSegments;
        int start = segmentIndex * segmentSize;
        int end = (segmentIndex == totalSegments - 1) ? words.Length : start + segmentSize;

        return string.Join(" ", words.Skip(start).Take(end - start));
    }

    private void AdjustRectTransform(RectTransform rectTransform, float height, float posYShift)
    {
        // Set the size delta for the height while keeping the current width
        Vector2 sizeDelta = rectTransform.sizeDelta;
        sizeDelta.y = height;
        rectTransform.sizeDelta = sizeDelta;

        // Adjust the anchored position to shift it up
        Vector2 anchoredPosition = rectTransform.anchoredPosition;
        anchoredPosition.y += posYShift;
        rectTransform.anchoredPosition = anchoredPosition;
    }

    private void AssignUIElements(CharacterAI characterAI, GameObject uiCanvas, int index)
    {
        if (characterAI == null || uiCanvas == null)
        {
            Debug.LogError("CharacterAI or uiCanvas is null.");
            return;
        }
        RectTransform uiCanvasRect = uiCanvas.GetComponent<RectTransform>();
        AdjustRectTransform(uiCanvasRect, 307.16f, 1.5f); // 50.0f is an example value for the Y-axis shift

        TMP_InputField inputField = uiCanvas.GetComponentInChildren<TMP_InputField>();

        TMP_Text responseText = uiCanvas.GetComponentsInChildren<TMP_Text>()
                                         .FirstOrDefault(t => t.gameObject.name != "Placeholder" && t.gameObject.name != inputField.textComponent.gameObject.name);

        Button submitButton = uiCanvas.GetComponentInChildren<Button>();

        if (inputField != null)
        {
            characterAI.userInputField = inputField;

            // Manually set the position and size of the input field
            RectTransform inputFieldRect = inputField.GetComponent<RectTransform>();
            inputFieldRect.anchoredPosition = new Vector2(0, -90); // Adjust position
            inputFieldRect.sizeDelta = new Vector2(200, 60); // Adjust size
            inputField.textComponent.alignment = TextAlignmentOptions.TopLeft; // Align text to the top left
            inputField.textComponent.fontSize = 20; // Set font size
            inputField.textComponent.color = Color.white; // Set text color to black

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
            characterAI.labelIndex = index;
            responseText.fontSize = 24.0f;
            responseText.color = Color.black;
            responseText.enableAutoSizing = false;
            responseText.alignment = TextAlignmentOptions.TopLeft;
            responseText.fontStyle = FontStyles.Bold | FontStyles.Italic;

            // Adjust the position and size of the response text box to match the content box
            RectTransform responseTextRect = responseText.GetComponent<RectTransform>();

            // Find the Content RectTransform
            RectTransform contentRectTransform = uiCanvas.transform.Find("Scroll View/Viewport/Content").GetComponent<RectTransform>();

            if (contentRectTransform != null)
            {
                // Match anchors, pivot, and width
                responseTextRect.anchorMin = contentRectTransform.anchorMin;
                responseTextRect.anchorMax = contentRectTransform.anchorMax;
                responseTextRect.pivot = contentRectTransform.pivot;
                responseTextRect.anchoredPosition = contentRectTransform.anchoredPosition;
                responseTextRect.sizeDelta = new Vector2(contentRectTransform.rect.width, responseTextRect.sizeDelta.y);

                // Ensure the vertical alignment is set to the top
                responseText.verticalAlignment = VerticalAlignmentOptions.Top;
            }
            else
            {
                Debug.LogWarning("No Content RectTransform found in the UI Canvas.");
            }
        }
        else
        {
            Debug.LogWarning("No TMP_Text found in the UI Canvas.");
        }
        StartCoroutine(UpdateCanvasRotation(uiCanvas.transform));
    }
    private IEnumerator UpdateCanvasRotation(Transform canvasTransform)
    {
        while (true)
        {
            if (mainCamera != null)
            {
                Vector3 directionToCamera = mainCamera.transform.position - canvasTransform.position;
                canvasTransform.rotation = Quaternion.LookRotation(-directionToCamera);
            }
            yield return null;
        }
    }


    private void AssignUIElementsStart(SpawnCharacterAI sCharacterAI, GameObject uiCanvas)
    {
        if (sCharacterAI == null || uiCanvas == null)
        {
            Debug.LogError("SpawnCharacterAI or uiCanvas is null.");
            return;
        }
        RectTransform uiCanvasRect = uiCanvas.GetComponent<RectTransform>();
        AdjustRectTransform(uiCanvasRect, 307.16f, 1.5f); // 50.0f is an example value for the Y-axis shift

        uiCanvas.transform.rotation = Quaternion.Euler(0, 180, 0);

        TMP_InputField inputField = uiCanvas.GetComponentInChildren<TMP_InputField>();

        TMP_Text responseText = uiCanvas.GetComponentsInChildren<TMP_Text>()
                                         .FirstOrDefault(t => t.gameObject.name != "Placeholder" && t.gameObject.name != inputField.textComponent.gameObject.name);

        Button submitButton = uiCanvas.GetComponentInChildren<Button>();

        if (inputField != null)
        {
            sCharacterAI.userInputField = inputField;
            Debug.Log("Inputfield assigned");
            // Manually set the position and size of the input field
            RectTransform inputFieldRect = inputField.GetComponent<RectTransform>();
            inputFieldRect.anchoredPosition = new Vector2(0, -90); // Adjust position
            inputFieldRect.sizeDelta = new Vector2(200, 60); // Adjust size
            inputField.textComponent.alignment = TextAlignmentOptions.TopLeft; // Align text to the top left
            inputField.textComponent.fontSize = 20; // Set font size
            inputField.textComponent.color = Color.white; // Set text color to black

            // Ensure the Return key submits the question
            inputField.onSubmit.AddListener(delegate { sCharacterAI.OnAskQuestion(); });
        }
        else
        {
            Debug.LogWarning("No TMP_InputField found in the UI Canvas.");
        }

        if (responseText != null)
        {
            sCharacterAI.responseText = responseText;
            responseText.fontSize = 24.0f;
            responseText.color = Color.black;
            responseText.enableAutoSizing = false;
            responseText.alignment = TextAlignmentOptions.TopLeft;
            responseText.fontStyle = FontStyles.Bold | FontStyles.Italic;

            // Adjust the position and size of the response text box to match the content box
            RectTransform responseTextRect = responseText.GetComponent<RectTransform>();

            // Find the Content RectTransform
            RectTransform contentRectTransform = uiCanvas.transform.Find("Scroll View/Viewport/Content").GetComponent<RectTransform>();

            if (contentRectTransform != null)
            {
                // Match anchors, pivot, and width
                responseTextRect.anchorMin = contentRectTransform.anchorMin;
                responseTextRect.anchorMax = contentRectTransform.anchorMax;
                responseTextRect.pivot = contentRectTransform.pivot;
                responseTextRect.anchoredPosition = contentRectTransform.anchoredPosition;
                responseTextRect.sizeDelta = new Vector2(contentRectTransform.rect.width, responseTextRect.sizeDelta.y);

                // Ensure the vertical alignment is set to the top
                responseText.verticalAlignment = VerticalAlignmentOptions.Top;
            }
            else
            {
                Debug.LogWarning("No Content RectTransform found in the UI Canvas.");
            }
        }
        else
        {
            Debug.LogWarning("No TMP_Text found in the UI Canvas.");
        }
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

    public IInteractiveCharacter GetClosestCharacter(Vector3 position, float radius)
    {
        IInteractiveCharacter closestCharacter = null;
        float closestDistance = radius;

        // Check regular characters
        foreach (var character in spawnedCharacters)
        {
            float distance = Vector3.Distance(position, character.GetPosition());
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestCharacter = character;
            }
        }

        // Check start characters
        foreach (var startCharacter in startSpawnedCharacters)
        {
            float distance = Vector3.Distance(position, startCharacter.GetPosition());
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestCharacter = startCharacter;
            }
        }

        return closestCharacter;
    }

    public void spawnSpawnCharacter(Vector3 position, int index)
    {
        Vector3 houseDimensions = GetHouseDimensions();

        float zOffset = houseDimensions.z + 1f;
        Vector3 adjustedPos = new Vector3(position.x, position.y, position.z + zOffset);
        Debug.Log($"Spawn character spawned at: {adjustedPos}");

        GameObject character = Instantiate(spawnCharacterPrefab, adjustedPos, Quaternion.identity);
        var sCharacterAI = character.GetComponent<SpawnCharacterAI>();

        if (sCharacterAI != null)
        {
            if (index == 0)
            {
                sCharacterAI.InitializeFirstOne();
                startSpawnedCharacters.Add(sCharacterAI);
            }
            if (index == 1)
            {
                Destroy(character.gameObject); // Destroy the GameObject associated with the character AI
                secondCord = adjustedPos;
                return;
            }
        }
        else
        {
            Debug.LogError($"Character does not have a CharacterAI component attached.");
        }

        Vector3 uiPosition = character.transform.position + character.transform.forward * 0.5f + new Vector3(0, 2, 0); // Move 0.5 units in front and 2 units above the character
        GameObject uiCanvas = Instantiate(uiCanvasPrefab, uiPosition, Quaternion.identity);

        Canvas canvasComponent = uiCanvas.GetComponent<Canvas>();
        canvasComponent.renderMode = RenderMode.WorldSpace;

        // Set the canvas as a child of the character to follow its movements
        uiCanvas.transform.SetParent(character.transform);

        // Apply a 180-degree rotation to the canvas around the Y-axis
        uiCanvas.transform.localRotation = Quaternion.Euler(0, 180, 0);

        uiCanvas.transform.localScale = new Vector3(0.005f, 0.005f, 0.005f);

        // Set the canvas sorting order (Remove the duplicate declaration)
        canvasComponent.sortingOrder = 100; // Higher value to ensure it renders on top

        AssignUIElementsStart(sCharacterAI, uiCanvas);
    }

    private IEnumerator MoveCharacter(GameObject character, float distance)
    {
        Vector3 startPosition = character.transform.position;
        Vector3 endPosition = startPosition + character.transform.forward * distance;
        float elapsedTime = 0f;
        float moveTime = 2f; // Time to move in seconds

        while (elapsedTime < moveTime)
        {
            character.transform.position = Vector3.Lerp(startPosition, endPosition, elapsedTime / moveTime);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        character.transform.position = endPosition;
    }

    public void spawnSecondCharacter(List<string> clusterNames)
    {
        Debug.Log("skdlfhasf");
        GameObject character = Instantiate(spawnCharacterPrefab, secondCord, Quaternion.identity);
        Debug.Log("skdlfhasf");
        var sCharacterAI = character.GetComponent<SpawnCharacterAI>();
        sCharacterAI.InitializeSecondOne(clusterNames);
        Debug.Log("skdlfhasf");

        startSpawnedCharacters.Add(sCharacterAI);

        Vector3 uiPosition = character.transform.position + character.transform.forward * 0.5f + new Vector3(0, 2, 0); // Move 0.5 units in front and 2 units above the character

        GameObject uiCanvas = Instantiate(uiCanvasPrefab, uiPosition, Quaternion.identity);
        Debug.Log("skdlfhasf");

        Canvas canvasComponent = uiCanvas.GetComponent<Canvas>();
        canvasComponent.renderMode = RenderMode.WorldSpace;

        // Set the canvas as a child of the character to follow its movements
        uiCanvas.transform.SetParent(character.transform);

        // Apply a 180-degree rotation to the canvas around the Y-axis
        uiCanvas.transform.localRotation = Quaternion.Euler(0, 180, 0);

        uiCanvas.transform.localScale = new Vector3(0.005f, 0.005f, 0.005f);

        // Set the canvas sorting order (Remove the duplicate declaration)
        canvasComponent.sortingOrder = 100; // Higher value to ensure it renders on top

        Debug.Log("skdlfhasf");
        AssignUIElementsStart(sCharacterAI, uiCanvas);
    }
}
