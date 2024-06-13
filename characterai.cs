using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro; // For TextMeshPro UI elements
using UnityEngine.UI; // For UI Button
using Newtonsoft.Json.Linq; // For JSON parsing

public class CharacterAI : MonoBehaviour
{
    public string topicLabel;
    public string transcript; // The context for AI interactions
    public TMP_InputField userInputField; // The input field for user questions
    public TMP_Text responseText; // The text field for AI responses
    public Button submitButton; // The button to submit the question

    private const string OpenAIAPIKey = "Openai-key"; // Replace with your OpenAI API key
    private const string OpenAIEndpoint = "https://api.openai.com/v1/chat/completions"; // GPT-3.5-turbo/4 Endpoint

    // Method to initialize the character with its specific data
    public void Initialize(string label, string script)
    {
        topicLabel = label;
        transcript = script;

        // Attach the button click event to OnAskQuestion method
        if (submitButton != null)
        {
            submitButton.onClick.AddListener(OnAskQuestion);
        }
    }

    // Method called when the user submits a question
    public void OnAskQuestion()
    {
        string userQuestion = userInputField.text;
        if (string.IsNullOrEmpty(userQuestion)) return;

        string prompt = $"Topic: {topicLabel}\nTranscript: {transcript}\n\nUser: {userQuestion}\nBot:";

        StartCoroutine(GetResponseFromAI(prompt));
    }

    // Coroutine to call the OpenAI API and get the response
    private IEnumerator GetResponseFromAI(string prompt)
    {
        var requestData = new
        {
            model = "gpt-3.5-turbo", // Replace with the correct model
            messages = new[]
            {
                new { role = "system", content = $"You are a game leader for the topic: {topicLabel}. Use the following transcript: {transcript} as information in order to create a game with questions lessons challenges etc. based on portraying the valid information from this video and based on valid user input." },
                new { role = "user", content = prompt }
            },
            max_tokens = 150,
            temperature = 0.7
        };

        string jsonData = JObject.FromObject(requestData).ToString();

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
                var jsonResponse = JObject.Parse(request.downloadHandler.text);
                var choices = jsonResponse["choices"];
                if (choices != null && choices.HasValues)
                {
                    var firstChoice = choices[0];
                    var messageContent = firstChoice["message"]["content"].ToString().Trim();
                    responseText.text = messageContent; // Update the TMP_Text component with the response
                    Debug.Log($"Setting response text to: {messageContent}");
                }
                else
                {
                    responseText.text = "No response from AI.";
                }
            }
        }
    }
}

// Helper classes to parse OpenAI response
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
