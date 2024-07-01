using UnityEngine.UI;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using TMPro;
using System.Collections;

public class CharacterSpawner : MonoBehaviour
{
    [SerializeField] private GameObject characterPrefab;
    [SerializeField] private GameObject spawnCharacterPrefab; // Assign the spawnCharacterAI here
    [SerializeField] private GameObject uiCanvasPrefab;
    [SerializeField] public TextAsset coordinatesCsvFile;
    [SerializeField] public TextAsset transcriptsCsvFile;
    [SerializeField] private MapGenerator mapGenerator; // Reference to MapGenerator script

    private class TopicInfo
    {
        public string Label { get; set; }
        public string Transcript { get; set; }
    }

    private List<CharacterAI> spawnedCharacters = new List<CharacterAI>();
    private List<SpawnCharacterAI> startSpawnedCharacters = new List<SpawnCharacterAI>();
    public Vector3 secondCord;

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
            string trimmedLabel = coordinate.Label.Trim().Trim('"');

            if (transcripts.TryGetValue(trimmedLabel, out string transcript))
            {
                topics.Add(new TopicInfo
                {
                    Label = trimmedLabel,
                    Transcript = transcript
                });
            }
            else
            {
                Debug.LogWarning($"No transcript found for label: {trimmedLabel}");
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
            Debug.Log("Number of lines read from coordinates CSV: " + lines.Length);

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
                        Debug.Log($"Parsed coordinate: {label} with normalized transcript length: {normalizedTranscriptLength}");
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
                    string label = values[0].Trim().Trim('"');
                    string transcript = values[3].Trim().Trim('"');
                    transcripts[label] = transcript;
                    Debug.Log($"Parsed transcript for label: {label} - {transcript}");
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

            for (int i = 0; i < housePositions.Count; i++)
            {
                Vector3 housePosition = housePositions[i];
                Quaternion houseRotation = houseRotations[i];

                // Add 1f to the y-coordinate of the character's position
                Vector3 characterPosition = new Vector3(housePosition.x, housePosition.y + 1f, housePosition.z);

                GameObject character = Instantiate(characterPrefab, characterPosition, houseRotation);

                // Start the coroutine to move the character
                StartCoroutine(MoveCharacter(character, 5f));

                var characterAI = character.GetComponent<CharacterAI>();
                if (characterAI != null)
                {
                    Debug.Log($"Character {topic.Label} initialized with CharacterAI component.");
                    string characterTranscript = GetTranscriptSegment(topic.Transcript, housePositions.Count, i);
                    characterAI.Initialize($"{topic.Label} - Part {i + 1}", characterTranscript);
                    spawnedCharacters.Add(characterAI);
                }
                else
                {
                    Debug.LogError($"Character {topic.Label} does not have a CharacterAI component attached.");
                }

                character.name = $"{topic.Label} - Part {i + 1}";

                // Position the UI Canvas slightly in front of the character
                Vector3 uiPosition = character.transform.position + character.transform.forward * 0.5f + new Vector3(0, 2, 0);
                GameObject uiCanvas = Instantiate(uiCanvasPrefab, uiPosition, Quaternion.identity);

                Canvas canvasComponent = uiCanvas.GetComponent<Canvas>();
                canvasComponent.renderMode = RenderMode.WorldSpace;

                // Set the canvas as a child of the character to follow its movements
                uiCanvas.transform.SetParent(character.transform);

                // Apply a 180-degree rotation to the canvas around the Y-axis
                uiCanvas.transform.localRotation = Quaternion.Euler(0, 180, 0);

                uiCanvas.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

                // Set the canvas sorting order
                canvasComponent.sortingOrder = 100; // Higher value to ensure it renders on top

                AssignUIElements(characterAI, uiCanvas);
            }
        }
    }

    public void spawnSpawnCharacter(Vector3 position, int index)
    {
        GameObject character = Instantiate(spawnCharacterPrefab, position, Quaternion.identity);

        // Rotate character to face the same direction as the house
        Vector3 nearestPointOnMainRoad = mapGenerator.GetNearestPointOnMainRoad(position);
        Vector3 directionToMainRoad = (nearestPointOnMainRoad - position).normalized;
        character.transform.rotation = Quaternion.LookRotation(directionToMainRoad, Vector3.up); // Ensure character is right side up

        // Calculate the forward vector with zero y-component
        Vector3 forward = new Vector3(character.transform.forward.x, 0, character.transform.forward.z).normalized;

        // Calculate the new position 5 units forward
        Vector3 newPosition = character.transform.position + forward * 5f;
        newPosition.y = position.y; // Ensure the y-coordinate remains the same
        character.transform.position = newPosition;

        var sCharacterAI = character.GetComponent<SpawnCharacterAI>();

        if (sCharacterAI != null)
        {
            if (index == 0)
            {
                sCharacterAI.InitializeFirstOne();
                startSpawnedCharacters.Add(sCharacterAI);
            }
            else if (index == 1)
            {
                Destroy(character.gameObject); // Destroy the GameObject associated with the character AI
                secondCord = newPosition;
            }
        }
        else
        {
            Debug.LogError($"Character does not have a SpawnCharacterAI component attached.");
        }

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

        AssignUIElementsStart(sCharacterAI, uiCanvas);
    }

    public void spawnSecondCharacter(List<string> clusterNames)
    {
        Debug.Log("Spawning second character.");
        GameObject character = Instantiate(spawnCharacterPrefab, secondCord, Quaternion.identity);

        // Rotate character to face the same direction as the house
        Vector3 nearestPointOnMainRoad = mapGenerator.GetNearestPointOnMainRoad(secondCord);
        Vector3 directionToMainRoad = (nearestPointOnMainRoad - secondCord).normalized;
        character.transform.rotation = Quaternion.LookRotation(directionToMainRoad, Vector3.up); // Ensure character is right side up

        // Calculate the forward vector with zero y-component
        Vector3 forward = new Vector3(character.transform.forward.x, 0, character.transform.forward.z).normalized;

        // Calculate the new position 5 units forward
        Vector3 newPosition = character.transform.position + forward * 5f;
        newPosition.y = secondCord.y; // Ensure the y-coordinate remains the same
        character.transform.position = newPosition;

        var sCharacterAI = character.GetComponent<SpawnCharacterAI>();
        sCharacterAI.InitializeSecondOne(clusterNames);

        startSpawnedCharacters.Add(sCharacterAI);

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

        AssignUIElementsStart(sCharacterAI, uiCanvas);
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
        Debug.Log("Character moved to: " + character.transform.position);
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
        }
        else
        {
            Debug.LogWarning("No TMP_Text found in the UI Canvas.");
        }
    }

    private void AssignUIElementsStart(SpawnCharacterAI sCharacterAI, GameObject uiCanvas)
    {
        if (sCharacterAI == null || uiCanvas == null)
        {
            Debug.LogError("SpawnCharacterAI or uiCanvas is null.");
            return;
        }

        uiCanvas.transform.rotation = Quaternion.Euler(0, 180, 0);

        TMP_InputField inputField = uiCanvas.GetComponentInChildren<TMP_InputField>();

        TMP_Text responseText = uiCanvas.GetComponentsInChildren<TMP_Text>()
                                         .FirstOrDefault(t => t.gameObject.name != "Placeholder" && t.gameObject.name != inputField.textComponent.gameObject.name);

        Button submitButton = uiCanvas.GetComponentInChildren<Button>();

        if (inputField != null)
        {
            sCharacterAI.userInputField = inputField;
            // Manually set the position and size of the input field
            RectTransform inputFieldRect = inputField.GetComponent<RectTransform>();
            inputFieldRect.anchoredPosition = new Vector2(0, -40); // Adjust position
            inputFieldRect.sizeDelta = new Vector2(400, 60); // Adjust size
            inputField.textComponent.alignment = TextAlignmentOptions.TopLeft; // Align text to the top left
            inputField.textComponent.fontSize = 20; // Set font size
            inputField.textComponent.color = Color.black; // Set text color to black

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
            // Adjust the position and size of the response text box
            RectTransform responseTextRect = responseText.GetComponent<RectTransform>();
            responseTextRect.anchoredPosition = new Vector2(0, 60); // Adjust position
            responseTextRect.sizeDelta = new Vector2(400, 100); // Adjust size
        }
        else
        {
            Debug.LogWarning("No TMP_Text found in the UI Canvas.");
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
