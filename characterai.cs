using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.UI;

public class CharacterAI : MonoBehaviour
{
    public string topicLabel;
    public string transcript;
    public TMP_InputField userInputField;
    public TMP_Text responseText;
    public Button submitButton;

    private const string OpenAIAPIKey = "OpenAikey";
    private const string OpenAIEndpoint = "https://api.openai.com/v1/chat/completions";
    private FirstPersonController firstPersonController;

    void Start()
    {
        firstPersonController = FindObjectOfType<FirstPersonController>();
        userInputField.onEndEdit.AddListener(OnSubmitQuestion);
    }

    public void Initialize(string label, string script)
    {
        topicLabel = label;
        transcript = script;
    }

    public void EnableInteraction()
    {
        userInputField.gameObject.SetActive(true);
        userInputField.Select();
        userInputField.ActivateInputField();
    }

    public void DisableInteraction()
    {
        userInputField.gameObject.SetActive(false);
    }

    private void OnSubmitQuestion(string userQuestion)
    {
        if (Input.GetKeyDown(KeyCode.Return) && !string.IsNullOrEmpty(userQuestion))
        {
            string prompt = $"Topic: {topicLabel}\nTranscript: {transcript}\n\nUser: {userQuestion}\nBot:";
            StartCoroutine(GetResponseFromAI(prompt));
        }
    }

    public void OnAskQuestion()
    {
        string userQuestion = userInputField.text;
        if (string.IsNullOrEmpty(userQuestion)) return;

        string prompt = $"Topic: {topicLabel}\nTranscript: {transcript}\n\nUser: {userQuestion}\nBot:";

        StartCoroutine(GetResponseFromAI(prompt));
    }

    private IEnumerator GetResponseFromAI(string prompt)
    {
        var requestData = new OpenAIRequest
        {
            model = "gpt-3.5-turbo",
            messages = new[]
            {
                new OpenAIMessage { role = "system", content = $"You are a game leader for the topic: {topicLabel}. Use the following transcript: {transcript} as information in order to create a game with questions lessons challenges etc. based on portraying the valid information from this video and based on valid user input." },
                new OpenAIMessage { role = "user", content = prompt }
            },
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
                    responseText.text = messageContent;
                    Debug.Log($"Setting response text to: {messageContent}");
                }
                else
                {
                    responseText.text = "No response from AI.";
                }
            }
        }

        // Re-enable movement after getting the response
        firstPersonController.EnableMovement();
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
