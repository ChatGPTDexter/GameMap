using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.Video;
using UnityEngine.UI;
using System.Linq;

public class CharacterAI : MonoBehaviour, IInteractiveCharacter
{
    public string topicLabel;
    public string transcript;
    public string url;
    public TMP_InputField userInputField;
    public TMP_Text responseText;

    private const string OpenAIAPIKey = "api";
    private const string OpenAIEndpoint = "https://api.openai.com/v1/chat/completions";
    private FirstPersonMovement firstPersonMovement;
    private MapGenerator mapGenerator;
    private Jump jump;
    private MiniMapController miniMapController;
    private CharacterSpawner characterSpawner;
    private GameCompletion gameCompletion;
    private TeleportBehavior teleportBehavior;
    private HouseManager houseManager; // Reference to HouseManager

    // List to maintain chat history
    private List<OpenAIMessage> chatHistory = new List<OpenAIMessage>();

    private bool interactionEnabled = false;
    public Vector3 GetPosition() => transform.position;

    private int currentPoints = 0; // Track the current points for this house
    private const int pointsThreshold = 40; // Points needed to complete the house
    private bool videoWatched = false; // Track if the video has been watched

    private VideoPlayer videoPlayer; // Video player for playing videos
    private RawImage videoDisplay; // RawImage for displaying the video

    void Start()
    {
        Debug.Log("CharacterAI Start method called.");

        firstPersonMovement = FindObjectOfType<FirstPersonMovement>();
        mapGenerator = FindObjectOfType<MapGenerator>();
        jump = FindObjectOfType<Jump>();
        miniMapController = FindObjectOfType<MiniMapController>();
        characterSpawner = FindObjectOfType<CharacterSpawner>();
        gameCompletion = FindObjectOfType<GameCompletion>();
        teleportBehavior = FindObjectOfType<TeleportBehavior>();
        houseManager = FindObjectOfType<HouseManager>(); // Initialize HouseManager

        videoDisplay = FindObjectOfType<RawImage>(); // Assuming there's only one RawImage in the scene
        if (videoDisplay != null)
        {
            videoPlayer = videoDisplay.GetComponent<VideoPlayer>();
            if (videoPlayer == null)
            {
                videoPlayer = videoDisplay.gameObject.AddComponent<VideoPlayer>();
            }
        }
        else
        {
            Debug.LogError("RawImage for video display not found.");
        }

        if (userInputField == null) Debug.LogError("userInputField is not assigned.");

        // Listen for the Return key to submit the question
        if (userInputField != null)
        {
            userInputField.onSubmit.AddListener(delegate { OnAskQuestion(); });
        }
        else
        {
            Debug.LogError("userInputField is null in Start method.");
        }

        // Disable interaction initially
        DisableInteraction();
    }

    public void Initialize(string label, string script, string url)
    {
        topicLabel = label;
        transcript = script;
        this.url = url;

        Debug.Log($"CharacterAI initialized with label: {label}, script: {script}, url: {url}");

        // Initialize chat history with system message
        chatHistory.Add(new OpenAIMessage
        {
            role = "system",
            content = $"You are an expert on the topic: {topicLabel}. Use the following transcript: {transcript} to assist the user in learning about this topic. To make the learning experience more engaging, based on the user's response prompt them with either a very challenging riddle, multiple-choice questions (one at a time), or the video link: {url} if they want to watch the video. After the player reaches 40 points congratulate them and tell them they have passed your house. If the user solves a riddle correctly, respond with 'Correct! You have solved the riddle'. If the user answers a multiple-choice question correctly, respond with 'Correct! You answered the multiple-choice question'."
        });

        // Add the initial question to the chat history
        chatHistory.Add(new OpenAIMessage
        {
            role = "assistant",
            content = $"Would you like to watch a video, solve a riddle, or answer multiple-choice questions (MCQs) about {topicLabel}? Type '1' for Riddle, '2' for MCQ, or '3' for Video."
        });
    }

    void Update()
    {
        if (interactionEnabled)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                RequestRiddle();
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                RequestMCQ();
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                WatchVideo();
            }
        }
    }

    public void EnableInteraction()
    {
        interactionEnabled = true;
        if (userInputField != null)
        {
            userInputField.gameObject.SetActive(true);
            userInputField.Select();
            userInputField.ActivateInputField();
        }
        else
        {
            Debug.LogError("userInputField is null in EnableInteraction.");
        }

        if (miniMapController != null)
        {
            miniMapController.canMiniMap = false;
            Debug.Log("MiniMap disabled.");
        }

        if (jump != null)
        {
            jump.canJump = false;
            Debug.Log("Jump disabled.");
        }

        if (teleportBehavior != null)
        {
            teleportBehavior.canTeleport = false;
            Debug.Log("Teleport disabled.");
        }
    }

    public void DisableInteraction()
    {
        interactionEnabled = false;
        if (userInputField != null)
        {
            userInputField.gameObject.SetActive(false);
        }
        else
        {
            Debug.LogError("userInputField is null in DisableInteraction.");
        }

        if (miniMapController != null)
        {
            miniMapController.canMiniMap = true;
            Debug.Log("MiniMap enabled.");
        }

        if (jump != null)
        {
            jump.canJump = true;
            Debug.Log("Jump enabled.");
        }

        if (teleportBehavior != null)
        {
            teleportBehavior.canTeleport = true;
            Debug.Log("Teleport enabled.");
        }
    }

    public void OnAskQuestion()
    {
        EnableInteraction();
        if (userInputField == null)
        {
            Debug.LogError("userInputField is not assigned.");
            return;
        }

        string userQuestion = userInputField.text;
        if (!string.IsNullOrEmpty(userQuestion))
        {
            chatHistory.Add(new OpenAIMessage { role = "user", content = userQuestion });

            string prompt = GetChatHistoryAsString();
            StartCoroutine(GetResponseFromAI(prompt));

            // Clear the input field after submission
            userInputField.text = string.Empty;
            userInputField.ActivateInputField();
        }
        else
        {
            Debug.LogError("userQuestion is null or empty in OnAskQuestion.");
        }
    }

    private string GetChatHistoryAsString()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (var message in chatHistory)
        {
            sb.AppendLine($"{message.role}: {message.content}");
        }
        return sb.ToString();
    }

    private IEnumerator GetResponseFromAI(string prompt)
    {
        var requestData = new OpenAIRequest
        {
            model = "gpt-3.5-turbo",
            messages = chatHistory.ToArray(),
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
                if (responseText != null)
                {
                    responseText.text = "There was an error processing your request. Please check the console for details.";
                }
            }
            else
            {
                Debug.Log($"Response: {request.downloadHandler.text}");
                var jsonResponse = JsonUtility.FromJson<OpenAIResponse>(request.downloadHandler.text);
                if (jsonResponse.choices != null && jsonResponse.choices.Count > 0)
                {
                    var firstChoice = jsonResponse.choices[0];
                    var messageContent = firstChoice.message.content.Trim();

                    // Update chat history with AI response
                    chatHistory.Add(new OpenAIMessage { role = "assistant", content = messageContent });

                    if (responseText != null)
                    {
                        responseText.text = messageContent;
                    }
                    Debug.Log($"Setting response text to: {messageContent}");

                    // Simplified check for correct answers
                    if (messageContent.ToLower().Contains("correct"))
                    {
                        if (chatHistory[chatHistory.Count - 2].content.ToLower().Contains("riddle"))
                        {
                            SolveRiddle(); // Add 40 points for solving a riddle
                        }
                        else if (chatHistory[chatHistory.Count - 2].content.ToLower().Contains("multiple-choice question"))
                        {
                            AddPoints(10); // Add 10 points for answering an MCQ
                        }
                    }
                }
                else
                {
                    if (responseText != null)
                    {
                        responseText.text = "No response from AI.";
                    }
                }
            }

            if (interactionEnabled && firstPersonMovement != null)
            {
                firstPersonMovement.EnableMovement();
            }
        }
    }

    public void RequestRiddle()
    {
        chatHistory.Add(new OpenAIMessage { role = "user", content = "I would like to solve a riddle." });
        string prompt = GetChatHistoryAsString();
        StartCoroutine(GetResponseFromAI(prompt));
    }

    public void RequestMCQ()
    {
        chatHistory.Add(new OpenAIMessage { role = "user", content = "I would like to answer a multiple-choice question." });
        string prompt = GetChatHistoryAsString();
        StartCoroutine(GetResponseFromAI(prompt));
    }

    public void WatchVideo()
    {
        if (!videoWatched && !string.IsNullOrEmpty(url))
        {
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = url;
            videoPlayer.Play();

            // Display the video UI element
            videoDisplay.gameObject.SetActive(true);

            // Add event listener to hide the video UI element when the video finishes
            videoPlayer.loopPointReached += OnVideoFinished;

            // Add points for watching the video
            AddPoints(10);
            videoWatched = true;
        }
        else
        {
            Debug.LogWarning("Video has already been watched or URL is empty.");
        }
    }

    private void OnVideoFinished(VideoPlayer vp)
    {
        // Hide the video UI element when the video finishes
        videoDisplay.gameObject.SetActive(false);
    }

    public void SolveRiddle()
    {
        AddPoints(40);
        DisableInteraction();
    }

    public void AnswerMCQ()
    {
        AddPoints(10);
        DisableInteraction();
    }

    private void AddPoints(int points)
    {
        currentPoints += points;
        Debug.Log($"Points added: {points}. Current points: {currentPoints}");

        if (currentPoints >= pointsThreshold)
        {
            CompleteHouse();
        }
    }

    private void CompleteHouse()
    {
        if (!mapGenerator.MasteredTopics.ContainsKey(topicLabel))
        {
            Debug.Log($"Mastered {topicLabel}");
            mapGenerator.MasteredTopics.Add(topicLabel, true);

            // Update the mastery status in HouseManager
            if (houseManager != null)
            {
                houseManager.UpdateClusterMasteryStatus();
            }

            // Notify the user immediately
            if (responseText != null)
            {
                responseText.text = $"Congratulations! You have completed the points total for {topicLabel}.";
            }

            // Check if all houses related to the same label are completed
            if (mapGenerator.AllHousesRelatedToLabelMastered(topicLabel))
            {
                gameCompletion.OnGameComplete();
            }
            else
            {
                StartCoroutine(DelayedOnAskQuestion());
            }
        }
    }

    private IEnumerator DelayedOnAskQuestion()
    {
        yield return new WaitForSeconds(1f);
        gameCompletion.OnAskQuestion();
    }
}

// Helper classes to parse OpenAI response
[System.Serializable]
public class OpenAIRequest
{
    public string model;
    public OpenAIMessage[] messages;
    public int max_tokens;
    public float temperature;
}

[System.Serializable]
public class OpenAIMessage
{
    public string role;
    public string content;
}

[System.Serializable]
public class OpenAIResponse
{
    public List<Choice> choices;
}

[System.Serializable]
public class Choice
{
    public Message message;
}

[System.Serializable]
public class Message
{
    public string content;
}
