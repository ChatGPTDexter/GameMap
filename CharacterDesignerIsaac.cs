using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using System;

public class CharacterDesigner : MonoBehaviour
{
    private const string readyPlayerMeApiUrl = "https://models.readyplayer.me";
    private ApiRequest apiRequest;

    private string partnerSubdomain = "mentix"; // Your subdomain
    private string OpenAIAPIKey = "api-key"; // Your OpenAI API key
    private string readyPlayerMeApiKey = "sk_live_snjW5fo5YwoT564MqwxIuuQZlapHCzbBd7hw"; // Your Ready Player Me API key
    private string applicationId = "669e15168c3ebffbad05ba66";
    private const string OpenAIEndpoint = "https://api.openai.com/v1/chat/completions";
    private string userID;
    private string accessToken = null;


    void Start()
    {
        apiRequest = FindObjectOfType<ApiRequest>();
        if (apiRequest == null)
        {
            Debug.LogError("ApiRequest component not found in the scene.");
        }
    }

    public async Task<string> MakeCharacterFromTranscriptAsync(string transcript)
    {
        var tcs = new TaskCompletionSource<string>();

        StartCoroutine(WaitForFilesAndGenerateCharacter(transcript, modelUrl =>
        {
            tcs.SetResult(modelUrl);
        }));

        return await tcs.Task;
    }

    private IEnumerator WaitForFilesAndGenerateCharacter(string transcript, System.Action<string> onCharacterDesigned)
    {
        yield return new WaitUntil(() => apiRequest.filesAssigned);

        // Step 1: Create an anonymous user and get the access token
        yield return StartCoroutine(CreateAnonymousUser(result => accessToken = result));
        if (string.IsNullOrEmpty(accessToken))
        {
            Debug.LogError("Failed to get access token.");
            yield break;
        }

        // Step 2: Fetch all possible templates
        string templateId = null;
        yield return StartCoroutine(FetchAllTemplates(transcript, accessToken, result => templateId = result));
        if (string.IsNullOrEmpty(templateId))
        {
            Debug.LogError("Failed to get template ID.");
            yield break;
        }

        // Step 3: Create a draft avatar from a template
        string avatarId = null;
        yield return StartCoroutine(CreateDraftAvatarFromTemplate(accessToken, templateId, result => avatarId = result));
        if (string.IsNullOrEmpty(avatarId))
        {
            Debug.LogError("Failed to create draft avatar.");
            yield break;
        }

        // Step 4: Fetch and save the draft avatar
        yield return StartCoroutine(FetchAndSaveDraftAvatar(accessToken, avatarId, result => avatarId = result));
        if (string.IsNullOrEmpty(avatarId))
        {
            Debug.LogError("Failed to fetch and save draft avatar.");
            yield break;
        }

        JArray outfits = null;
        //Step 5: killing myslef
        yield return StartCoroutine(FetchAllOutfits(accessToken, result => outfits = result));
        if (outfits == null)
        {
            Debug.LogError("Failed to fetch outfits.");
            yield break;
        }

        string outfitID = null;
        yield return StartCoroutine(AskForBestOutfit(outfits, transcript, result => outfitID = result));
        if (outfitID == null)
        {
            Debug.LogError("Failed to fetch outfits.");
            yield break;
        }

        string processedID = null;
        yield return StartCoroutine(EquipOutfitToAvatar(accessToken, avatarId, outfitID, result => processedID = result));
        if (processedID == null)
        {
            Debug.LogError("Failed to combine outfit and avatar");
            yield break;
        }
    

        string avatarUrl = null;
        yield return StartCoroutine(FetchAvatarGLB(accessToken, processedID, result => avatarUrl = result));
        if (string.IsNullOrEmpty(avatarUrl))
        {
            Debug.LogError("Failed to fetch avatar GLB.");
            yield break;
        }

        onCharacterDesigned?.Invoke(avatarUrl);
    }

    private IEnumerator CreateAnonymousUser(System.Action<string> onComplete)
    {
        using (UnityWebRequest request = UnityWebRequest.PostWwwForm($"https://{partnerSubdomain}.readyplayer.me/api/users", ""))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Failed to create anonymous user: " + request.error);
                onComplete?.Invoke(null);
            }
            else
            {
                string responseJson = request.downloadHandler.text;
                Debug.Log("CreateAnonymousUser response: " + responseJson); // Log the response for debugging
                JObject response = JObject.Parse(responseJson);
                string token = response["data"]["token"].ToString();
                userID = response["data"]["id"].ToString(); // Assign userID here
                onComplete?.Invoke(token);
            }
        }
    }

    private IEnumerator FetchAllTemplates(string transcript, string token, System.Action<string> onComplete)
    {
        using (UnityWebRequest request = UnityWebRequest.Get("https://api.readyplayer.me/v2/avatars/templates"))
        {
            request.SetRequestHeader("Authorization", $"Bearer {token}");
            request.SetRequestHeader("x-api-key", readyPlayerMeApiKey);  // Use the x-api-key header

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Failed to fetch templates: " + request.error);
                Debug.LogError("FetchAllTemplates response: " + request.downloadHandler.text); // Log the response for debugging
                onComplete?.Invoke(null);
            }
            else
            {
                string responseJson = request.downloadHandler.text;
                Debug.Log("FetchAllTemplates response: " + responseJson); // Log the response for debugging
                JObject response = JObject.Parse(responseJson);
                string templateId = response["data"][0]["id"].ToString(); // Assuming you want the first template

                // Start the AskForBestCharacter coroutine
                yield return StartCoroutine(AskForBestCharacter(response, transcript, characterId =>
                {
                    if (!string.IsNullOrEmpty(characterId))
                    {
                        Debug.Log($"Chosen Character ID: {characterId}");
                        templateId = characterId; // Assign the new ID to initialTemplateId
                        onComplete?.Invoke(templateId); // Return the new template ID
                    }
                    else
                    {
                        Debug.LogError("Failed to get a valid character ID.");
                        onComplete?.Invoke(null);
                    }
                }));
            }
        }
    }

    private IEnumerator CreateDraftAvatarFromTemplate(string token, string templateId, System.Action<string> onComplete)
    {
        // Create a JSON object for the data property
        JObject dataObject = new JObject
    {
        {
            "data", new JObject
            {
                { "partner", partnerSubdomain },
                { "bodyType", "fullbody" }
            }
        }
    };

        string url = $"https://api.readyplayer.me/v2/avatars/templates/{templateId}";
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(dataObject.ToString());
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {token}");
            request.SetRequestHeader("x-api-key", readyPlayerMeApiKey);  // Use the x-api-key header

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Failed to create draft avatar: " + request.error);
                Debug.LogError("CreateDraftAvatarFromTemplate response: " + request.downloadHandler.text); // Log the response for debugging
                onComplete?.Invoke(null);
            }
            else
            {
                string responseJson = request.downloadHandler.text;
                Debug.Log("CreateDraftAvatarFromTemplate response: " + responseJson); // Log the response for debugging
                JObject response = JObject.Parse(responseJson);
                string avatarId = response["data"]["id"].ToString();
                onComplete?.Invoke(avatarId);
            }
        }
    }

    private IEnumerator FetchAndSaveDraftAvatar(string token, string avatarId, System.Action<string> onComplete)
    {
        // Fetch the draft avatar
        using (UnityWebRequest request = UnityWebRequest.Get($"https://api.readyplayer.me/v2/avatars/{avatarId}.glb?preview=true"))
        {
            request.SetRequestHeader("Authorization", $"Bearer {token}");
            request.SetRequestHeader("x-api-key", readyPlayerMeApiKey);  // Use the x-api-key header


            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Failed to fetch draft avatar: " + request.error);
                Debug.LogError("FetchAndSaveDraftAvatar response: " + request.downloadHandler.text); // Log the response for debugging
                onComplete?.Invoke(null);
            }
            else
            {
                // Save the draft avatar
                using (UnityWebRequest saveRequest = UnityWebRequest.Put($"https://api.readyplayer.me/v2/avatars/{avatarId}", ""))
                {
                    saveRequest.SetRequestHeader("Authorization", $"Bearer {token}");

                    yield return saveRequest.SendWebRequest();

                    if (saveRequest.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError("Failed to save draft avatar: " + saveRequest.error);
                        Debug.LogError("SaveDraftAvatar response: " + saveRequest.downloadHandler.text); // Log the response for debugging
                        onComplete?.Invoke(null);
                    }
                    else
                    {
                        onComplete?.Invoke(avatarId);
                    }
                }
            }
        }
    }

    public IEnumerator AskForBestCharacter(object charObject, string transcript, System.Action<string> onComplete)
    {
        var followUpRequestData = new OpenAIRequest
        {
            model = "gpt-4o-mini",
            messages = new OpenAIMessage[]
            {
                new OpenAIMessage
                {
                    role = "system",
                    content = "Given a transcript, say EXACTLY AND ONLY the id of the character that would be best to say that given transcript in a videogame."
                },
                new OpenAIMessage
                {
                    role = "user",
                    content = $"Transcript: {transcript}\n object: {charObject}"
                }
            },
            max_tokens = 50,
            temperature = 0.0f
        };

        string followUpJsonData = JsonUtility.ToJson(followUpRequestData);

        using (UnityWebRequest followUpRequest = new UnityWebRequest(OpenAIEndpoint, "POST"))
        {
            byte[] followUpBodyRaw = Encoding.UTF8.GetBytes(followUpJsonData);
            followUpRequest.uploadHandler = new UploadHandlerRaw(followUpBodyRaw);
            followUpRequest.downloadHandler = new DownloadHandlerBuffer();
            followUpRequest.SetRequestHeader("Content-Type", "application/json");
            followUpRequest.SetRequestHeader("Authorization", $"Bearer {OpenAIAPIKey}");

            yield return followUpRequest.SendWebRequest();

            if (followUpRequest.result == UnityWebRequest.Result.ConnectionError || followUpRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"Error: {followUpRequest.error}\nResponse: {followUpRequest.downloadHandler.text}");
                onComplete?.Invoke(null);
            }
            else
            {
                Debug.Log($"Follow-up Response: {followUpRequest.downloadHandler.text}");
                var followUpJsonResponse = JsonUtility.FromJson<OpenAIResponse>(followUpRequest.downloadHandler.text);
                if (followUpJsonResponse.choices != null && followUpJsonResponse.choices.Count > 0)
                {
                    var followUpChoice = followUpJsonResponse.choices[0];
                    var chosenID = followUpChoice.message.content;
                    onComplete?.Invoke(chosenID);
                }
                else
                {
                    Debug.LogError("No valid response from AI for the chosen cluster.");
                    onComplete?.Invoke(null);
                }
            }
        }
    }

    private IEnumerator FetchAllOutfits(string token, System.Action<JArray> onComplete)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(applicationId) || string.IsNullOrEmpty(userID))
        {
            Debug.LogError("Invalid parameters for fetching outfits. Please check token, applicationId, and userID.");
            onComplete?.Invoke(null);
            yield break;
        }
        Debug.Log($"{userID}");
        string outfitsEndpoint = $"https://api.readyplayer.me/v1/assets?filter=usable-by-user-and-app&filterApplicationId={applicationId}&filterUserId={userID}&limit=100&type=outfit";

        using (UnityWebRequest request = UnityWebRequest.Get(outfitsEndpoint))
        {
            request.SetRequestHeader("Authorization", $"Bearer {token}");
            request.SetRequestHeader("X-APP-ID", applicationId);
            request.SetRequestHeader("x-api-key", readyPlayerMeApiKey);  // Use the x-api-key header

            Debug.Log($"Request URL: {request.url}");
            Debug.Log($"Authorization Header: Bearer {token.Substring(0, 10)}..."); // Only log first 10 characters of token
            Debug.Log($"X-API-KEY Header: {readyPlayerMeApiKey}");

            yield return request.SendWebRequest();

            Debug.Log($"Response Code: {request.responseCode}");

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to fetch outfits: {request.error}");
                Debug.LogError($"Response Code: {request.responseCode}");
                Debug.LogError($"Full Response: {request.downloadHandler.text}");

                onComplete?.Invoke(null);
            }
            else
            {
                string responseJson = request.downloadHandler.text;
                Debug.Log($"FetchAllOutfits response: {responseJson}");
                try
                {
                    JObject response = JObject.Parse(responseJson);
                    JArray outfitsArray = (JArray)response["data"];
                    if (outfitsArray != null && outfitsArray.Count > 0)
                    {
                        onComplete?.Invoke(outfitsArray);
                    }
                    else
                    {
                        Debug.LogWarning("No outfits found in the response.");
                        onComplete?.Invoke(new JArray());
                    }
                }
                catch (JsonException e)
                {
                    Debug.LogError($"Error parsing JSON response: {e.Message}");
                    onComplete?.Invoke(null);
                }
            }
        }
    }

    public IEnumerator AskForBestOutfit(JArray outfitObject, string transcript, System.Action<string> onComplete)
    {
        var followUpRequestData = new OpenAIRequest
        {
            model = "gpt-4o-mini",
            messages = new OpenAIMessage[]
            {
                new OpenAIMessage
                {
                    role = "system",
                    content = "Given a transcript, say EXACTLY AND ONLY the name of the of the costume for a character that would be most appropriate saying the transcript in an interactive video game."
                },
                new OpenAIMessage
                {
                    role = "user",
                    content = $"Transcript: {transcript}\n array of costumes: {outfitObject}"
                }
            },
            max_tokens = 50,
            temperature = 0.0f
        };

        string followUpJsonData = JsonUtility.ToJson(followUpRequestData);

        using (UnityWebRequest followUpRequest = new UnityWebRequest(OpenAIEndpoint, "POST"))
        {
            byte[] followUpBodyRaw = Encoding.UTF8.GetBytes(followUpJsonData);
            followUpRequest.uploadHandler = new UploadHandlerRaw(followUpBodyRaw);
            followUpRequest.downloadHandler = new DownloadHandlerBuffer();
            followUpRequest.SetRequestHeader("Content-Type", "application/json");
            followUpRequest.SetRequestHeader("Authorization", $"Bearer {OpenAIAPIKey}");

            yield return followUpRequest.SendWebRequest();

            if (followUpRequest.result == UnityWebRequest.Result.ConnectionError || followUpRequest.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"Error: {followUpRequest.error}\nResponse: {followUpRequest.downloadHandler.text}");
                onComplete?.Invoke(null);
            }
            else
            {
                Debug.Log($"Follow-up Response: {followUpRequest.downloadHandler.text}");
                var followUpJsonResponse = JsonUtility.FromJson<OpenAIResponse>(followUpRequest.downloadHandler.text);
                if (followUpJsonResponse.choices != null && followUpJsonResponse.choices.Count > 0)
                {
                    var followUpChoice = followUpJsonResponse.choices[0];
                    var chosenName = followUpChoice.message.content;
                    string chosenID = GetOutfitIdByName(outfitObject, chosenName);
                    Debug.Log(chosenID);
                    Debug.Log(chosenName);
                    onComplete?.Invoke(chosenID);
                }
                else
                {
                    Debug.LogError("No valid response from AI for the chosen cluster.");
                    onComplete?.Invoke(null);
                }
            }
        }
    }

    private IEnumerator EquipOutfitToAvatar(string token, string avatarId, string outfitId, System.Action<string> onComplete)
    {
        string url = $"https://api.readyplayer.me/v2/avatars/{avatarId}";
        JObject dataObject = new JObject
        {
            ["data"] = new JObject
            {
                ["assets"] = new JObject
                {
                    ["outfit"] = outfitId
                }
            }
        };

        using (UnityWebRequest request = new UnityWebRequest(url, "PATCH"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(dataObject.ToString());
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {token}");
            request.SetRequestHeader("x-api-key", readyPlayerMeApiKey);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to equip outfit: {request.error}");
                Debug.LogError($"Response: {request.downloadHandler.text}");
                onComplete?.Invoke(null);
            }
            else
            {
                string responseJson = request.downloadHandler.text;
                Debug.Log($"Successfully equipped outfit. Response: {responseJson}");

                // Parse the response to get the updated avatar ID or details
                JObject response = JObject.Parse(responseJson);
                string updatedAvatarId = response["data"]["id"].ToString(); // Extract updated avatar ID or other details if needed

                // Step 3: Prepare the updated avatar data for saving
                JObject updatedAvatarData = new JObject
                {
                    ["data"] = new JObject
                    {
                        ["assets"] = new JObject
                        {
                            ["outfit"] = outfitId
                            // Include other updated properties if necessary
                        }
                    }
                };

                yield return StartCoroutine(SaveAvatar(token, updatedAvatarId, updatedAvatarData, (success) =>
                {
                    if (success)
                    {
                        // Step 5: Invoke the callback with the updated avatar ID
                        onComplete?.Invoke(updatedAvatarId);
                    }
                    else
                    {
                        Debug.LogError("Failed to save the avatar.");
                        onComplete?.Invoke(null);
                    }
                }));
            }
        }
    }

    private IEnumerator FetchAvatarGLB(string token, string avatarId, System.Action<string> onComplete)
    {
        using (UnityWebRequest request = UnityWebRequest.Get($"https://models.readyplayer.me/{avatarId}.glb"))
        {
            request.SetRequestHeader("Authorization", $"Bearer {token}");
            request.SetRequestHeader("x-api-key", readyPlayerMeApiKey);  // Use the x-api-key header


            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Failed to fetch avatar GLB: " + request.error);
                Debug.LogError("FetchAvatarGLB response: " + request.downloadHandler.text); // Log the response for debugging
                onComplete?.Invoke(null);
            }
            else
            {
                onComplete?.Invoke(request.url); // The URL to the fetched GLB file
            }
        }
    }

    private string GetOutfitIdByName(JArray outfits, string outfitName)
    {
        foreach (JObject outfit in outfits)
        {
            string name = outfit["name"]?.ToString();
            string id = outfit["id"]?.ToString();

            if (name != null && name.Equals(outfitName, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }
        return null; // Return null if the outfit was not found
    }

    private IEnumerator SaveAvatar(string token, string avatarId, JObject updatedAvatarData, System.Action<bool> onComplete)
    {
        string url = $"https://api.readyplayer.me/v2/avatars/{avatarId}";

        using (UnityWebRequest request = new UnityWebRequest(url, "PUT"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(updatedAvatarData.ToString());
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {token}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to save avatar: {request.error}");
                Debug.LogError($"Response: {request.downloadHandler.text}");
                onComplete?.Invoke(false);
            }
            else
            {
                Debug.Log("Successfully saved avatar.");
                onComplete?.Invoke(true);
            }
        }
    }


}
