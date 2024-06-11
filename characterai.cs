using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro; // For TextMeshPro UI elements

public class CharacterAI : MonoBehaviour
{
    public string topicLabel;
    public string transcript; // The context for AI interactions
    public TMP_InputField userInputField; // The input field for user questions
    public TMP_Text responseText; // The text field for AI responses

    private const string OpenAIAPIKey = "your-openai-api-key"; // Replace with your OpenAI API key
    private const string OpenAIEndpoint = "https://api.openai.com/v1/completions"; // GPT-3/4 Endpoint

    // Method to initialize the character with its specific data
    public void Initialize(string label, string script)
    {
        topicLabel = label;
        transcript = script;
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
            model = "gpt-4o", // Replace with the model you're using
            prompt = prompt,
            max_tokens = 150,
            temperature = 0.7,
            stop = new[] { "\n", "User:" } // Stop generation after the bot response
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
                Debug.LogError($"Error: {request.error}");
                responseText.text = "There was an error processing your request.";
            }
            else
            {
                var jsonResponse = JsonUtility.FromJson<OpenAIResponse>(request.downloadHandler.text);
                responseText.text = jsonResponse.choices[0].text.Trim();
            }
        }
    }
}

// Helper class to parse OpenAI response
[System.Serializable]
public class OpenAIResponse
{
    public List<Choice> choices;
}

[System.Serializable]
public class Choice
{
    public string text;
}
