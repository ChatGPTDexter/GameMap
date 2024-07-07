    using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.UI;

public class SpawnCharacterAI : MonoBehaviour, IInteractiveCharacter
{
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

    private List<OpenAIMessage> chatHistory = new List<OpenAIMessage>();

    private bool interactionEnabled = false;
    public Vector3 GetPosition() => transform.position;
    private List<string> clusterNamesHere;
    private bool clusterNamesInitialized = false;
    private TeleportBehavior teleportBehavior;  

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
        userInputField.onSubmit.AddListener(delegate 
        { 
            OnAskQuestion();
            Debug.Log("Question Asked!");
        });

        // Disable interaction initially
        DisableInteraction();
    }

    public void InitializeFirstOne()
    {
        // Initialize chat history with system message
        chatHistory.Add(new OpenAIMessage
        {
            role = "system",
            content = $"You are the first npc that the character interacts with in this game after they crash from a shaceship. Tell him about how he needs to follow the road to escape, and make it ominous. DON'T TELL THE PERSON ANYTHING ELSE!"
        });
    }

    public void InitializeSecondOne(List<string> clusterNames)
    {
        clusterNamesInitialized = true;
        clusterNamesHere = clusterNames;
        chatHistory.Add(new OpenAIMessage
        {
            role = "system",
            content = $"You are the second npc that the character interacts with in this game. Say to him that to escape from this world they need collect the scattered rocket ship parts, one from each region in this world, and bring them to the platform near here to assemble the rocket ship. Tell him to chose the region they would like to start in between {clusterNames[0]}, {clusterNames[1]}, {clusterNames[2]},{clusterNames[3]}, and {clusterNames[4]}. DON'T PROVIDE MORE INFORMATION!"
        });


    }

    public void EnableInteraction()
    {
        interactionEnabled = false;
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
        interactionEnabled = true;
        userInputField.gameObject.SetActive(false);

        if (miniMapController != null)
        {
            miniMapController.canMiniMap = true;
        }

        // Enable jumping
        if (jump != null)
        {
            jump.canJump = true;
        }

        if (teleportBehavior != null)
        {
            teleportBehavior.canTeleport = true;
        }

    }

    public void OnAskQuestion()
    {
        DisableInteraction();
        string userQuestion = userInputField.text;
        if (!string.IsNullOrEmpty(userQuestion))
        {
            chatHistory.Add(new OpenAIMessage { role = "user", content = userQuestion });

            string prompt = GetChatHistoryAsString();
            StartCoroutine(GetResponseFromAI(prompt, userQuestion));

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

    private IEnumerator GetResponseFromAI(string prompt, string question)
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
                    int index = 0;
                    if (clusterNamesInitialized)
                    {
                        foreach (var clusterName in clusterNamesHere)
                        {
                            if (question.ToLower().Trim('"').Trim().Contains(clusterName.ToLower().Trim()))
                            {
                                mapGenerator.TeleportTo(index);
                            }
                            index++;
                        }
                    }

                    gameCompletion.OnAskQuestion();
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

