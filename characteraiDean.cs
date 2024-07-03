using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.UI;

public class CharacterAI : MonoBehaviour, IInteractiveCharacter
{
    public string topicLabel;
    public string transcript;
    public TMP_InputField userInputField;
    public TMP_Text responseText;


    private const string OpenAIAPIKey = "api-key";
    private const string OpenAIEndpoint = "https://api.openai.com/v1/chat/completions";
    private FirstPersonMovement firstPersonMovement;
    private MapGenerator mapGenerator;
    private Jump jump;
    private MiniMapController miniMapController;
    private CharacterSpawner characterSpawner;
    private GameCompletion gameCompletion;
    private TeleportBehavior teleportBehavior;

    // List to maintain chat history
    private List<OpenAIMessage> chatHistory = new List<OpenAIMessage>();

    private bool interactionEnabled = false;
    public Vector3 GetPosition() => transform.position;


    void Start()
    {
        firstPersonMovement = FindObjectOfType<FirstPersonMovement>();
        mapGenerator = FindObjectOfType<MapGenerator>();
        jump = FindObjectOfType<Jump>();  // Assuming Jump script controls jumping behavior
        miniMapController = FindObjectOfType<MiniMapController>();  // Assuming MiniMapController script handles mini-map functionality
        characterSpawner = FindObjectOfType<CharacterSpawner>();  // Ensure characterSpawner is initialized
        gameCompletion = FindObjectOfType<GameCompletion>();
        teleportBehavior = FindObjectOfType<TeleportBehavior>();

        // Listen for the Return key to submit the question
        userInputField.onSubmit.AddListener(delegate { OnAskQuestion(); });

        // Disable interaction initially
        DisableInteraction();
    }

    public void Initialize(string label, string script)
    {
        topicLabel = label;
        transcript = script;

        // Initialize chat history with system message
        chatHistory.Add(new OpenAIMessage
        {
            role = "system",
            content = $"You are an expert on the topic: {topicLabel}. Use the following transcript: {transcript} to assist the user in learning about this topic. To make the learning experience more engaging, present the user with a very challenging riddle related to the transcript. Once the the responder answers the riddle sufficiently say 'Correct! You are on your way to mastering Chemistry!'"
        });
    }

    public void EnableInteraction()
    {
        interactionEnabled = true;
        userInputField.gameObject.SetActive(true);
        userInputField.Select();
        userInputField.ActivateInputField();


        if (miniMapController != null)
        {
            miniMapController.canMiniMap = false;
        }

        // Disable jumping
        if (jump != null)
        {
            jump.canJump = false;
        }

        if (teleportBehavior != null)
        {
            teleportBehavior.canTeleport = false;
        }
    }

    public void DisableInteraction()
    {
        interactionEnabled = false;
        userInputField.gameObject.SetActive(false);


        if (miniMapController != null)
        {
            miniMapController.canMiniMap = true;
        }

        if (jump != null)
        {
            jump.canJump = true;
        }

        // Enable jumping
        if (teleportBehavior != null)
        {
            teleportBehavior.canTeleport = true;
        }
    }

    public void OnAskQuestion()
    {
        EnableInteraction();
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
                responseText.text = "There was an error processing your request. Please check the console for details.";
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

                    responseText.text = messageContent;
                    Debug.Log($"Setting response text to: {messageContent}");

                    // Debugging: Log the message content
                    Debug.Log($"Message Content: {messageContent}");
                    Debug.Log($"Trimmed and Lowercase Message Content: {messageContent.ToLower().Trim()}");

                    if (messageContent.ToLower().Trim().Contains("correct! you are on your way to mastering chemistry!".ToLower().Trim()))
                    {
                        Debug.Log($"Masted {topicLabel}");
                        if (!mapGenerator.MasteredTopics.ContainsKey(topicLabel)) // Access through characterSpawner instance
                        {
                            Debug.Log($"Mastered {topicLabel}");
                            mapGenerator.MasteredTopics.Add(topicLabel, true); // Track this topic as mastered
                            StartCoroutine(DelayedOnAskQuestion());
                        }
                    }
                }
                else
                {
                    responseText.text = "No response from AI.";
                }
            }
        }

        // Re-enable movement after getting the response, only if interaction is still enabled
        if (interactionEnabled && firstPersonMovement != null)
        {
            firstPersonMovement.EnableMovement();
        }
    }


    private IEnumerator DelayedOnAskQuestion()
    {
        Debug.Log("Waiting before calling OnAskQuestion...");
        yield return new WaitForSeconds(1f); // Wait for 1 second
        Debug.Log("Delay finished, calling OnAskQuestion");
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
