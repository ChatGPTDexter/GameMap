using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class CharacterDesigner : MonoBehaviour
{
    private const string readyPlayerMeApiUrl = "https://models.readyplayer.me";
    private ApiRequest apiRequest;

    [SerializeField] private string partnerSubdomain = "mentix"; // Your subdomain
    [SerializeField] private string OpenAIAPIKey = "apikey"; // Your OpenAI API key
    [SerializeField] private string readyPlayerMeApiKey = "sk_live_snjW5fo5YwoT564MqwxIuuQZlapHCzbBd7hw"; // Your Ready Player Me API key
    private const string OpenAIEndpoint = "https://api.openai.com/v1/chat/completions";
    [SerializeField] private string applicationId = "your-application-id-here";

    void Start()
    {
        apiRequest = FindObjectOfType<ApiRequest>();
        if (apiRequest == null)
        {
            Debug.LogError("ApiRequest component not found in the scene.");
        }
    }

    public async Task<string> DesignCharacterFromTranscriptAsync(string transcript)
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
        string accessToken = null;
        yield return StartCoroutine(CreateAnonymousUser(result => accessToken = result));
        if (string.IsNullOrEmpty(accessToken))
        {
            Debug.LogError("Failed to get access token.");
            yield break;
        }

        // Step 2: Fetch all possible templates
        string templateId = null;
        JArray outfits = null;
        yield return StartCoroutine(FetchAllTemplates(transcript, accessToken, result => templateId = result));
        if (string.IsNullOrEmpty(templateId))
        {
            Debug.LogError("Failed to get template ID.");
            yield break;
        }

        // New Step: Fetch all outfits
        yield return StartCoroutine(FetchAllOutfits(accessToken, userId, applicationId, result => outfits = result));
        if (outfits == null)
        {
            Debug.LogError("Failed to fetch outfits.");
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

        // New Step: Apply the chosen outfit to the avatar
        string chosenOutfitId = null;
        yield return StartCoroutine(AskForBestOutfit(outfits, transcript, result => chosenOutfitId = result));
        if (!string.IsNullOrEmpty(chosenOutfitId))
        {
            yield return StartCoroutine(ApplyOutfitToAvatar(accessToken, avatarId, chosenOutfitId));
        }

        // Step 4: Fetch and save the draft avatar
        yield return StartCoroutine(FetchAndSaveDraftAvatar(accessToken, avatarId, result => avatarId = result));
        if (string.IsNullOrEmpty(avatarId))
        {
            Debug.LogError("Failed to fetch and save draft avatar.");
            yield break;
        }

        // Step 5: Fetch the avatar in GLB format
        string avatarUrl = null;
        yield return StartCoroutine(FetchAvatarGLB(accessToken, avatarId, result => avatarUrl = result));
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
    private IEnumerator FetchAllOutfits(string token, string userId, string applicationId, System.Action<JArray> onComplete)
    {
        string outfitsEndpoint = $"https://api.readyplayer.me/v1/assets?filter=usable-by-user-and-app&filterApplicationId={applicationId}&filterUserId={userId}";

        using (UnityWebRequest request = UnityWebRequest.Get(outfitsEndpoint))
        {
            request.SetRequestHeader("Authorization", $"Bearer {token}");
            request.SetRequestHeader("X-APP-ID", applicationId);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to fetch outfits: {request.error}");
                Debug.LogError($"Response: {request.downloadHandler.text}");
                onComplete?.Invoke(null);
            }
            else
            {
                string responseJson = request.downloadHandler.text;
                Debug.Log($"FetchAllOutfits response: {responseJson}");
                
                try
                {
                    JObject response = JObject.Parse(responseJson);
                    JArray assetsArray = (JArray)response["data"];
                    if (assetsArray != null)
                    {
                        // Filter for outfits only
                        JArray outfitsArray = new JArray(assetsArray.Where(a => (string)a["type"] == "outfit"));
                        onComplete?.Invoke(outfitsArray);
                    }
                    else
                    {
                        Debug.LogError("No assets data found in the response.");
                        onComplete?.Invoke(null);
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
    private IEnumerator AskForBestOutfit(JArray outfits, string transcript, System.Action<string> onComplete)
    {
        var requestData = new OpenAIRequest
        {
            model = "gpt-4-mini",
            messages = new OpenAIMessage[]
            {
                new OpenAIMessage
                {
                    role = "system",
                    content = "Given a transcript and a list of outfits, choose the best outfit ID for the character saying the transcript in a videogame."
                },
                new OpenAIMessage
                {
                    role = "user",
                    content = $"Transcript: {transcript}\nOutfits: {outfits.ToString()}"
                }
            },
            max_tokens = 50,
            temperature = 0.0f
        };

        string jsonData = JsonUtility.ToJson(requestData);

        using (UnityWebRequest request = new UnityWebRequest(OpenAIEndpoint, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {OpenAIAPIKey}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Error: {request.error}\nResponse: {request.downloadHandler.text}");
                onComplete?.Invoke(null);
            }
            else
            {
                var jsonResponse = JsonUtility.FromJson<OpenAIResponse>(request.downloadHandler.text);
                if (jsonResponse.choices != null && jsonResponse.choices.Count > 0)
                {
                    var chosenOutfitId = jsonResponse.choices[0].message.content;
                    onComplete?.Invoke(chosenOutfitId);
                }
                else
                {
                    Debug.LogError("No valid response from AI for the chosen outfit.");
                    onComplete?.Invoke(null);
                }
            }
        }
    }

    private IEnumerator ApplyOutfitToAvatar(string token, string avatarId, string outfitId)
    {
        JObject dataObject = new JObject
        {
            { "outfit", outfitId }
        };

        string url = $"https://api.readyplayer.me/v2/avatars/{avatarId}";
        using (UnityWebRequest request = UnityWebRequest.Put(url, dataObject.ToString()))
        {
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {token}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Failed to apply outfit to avatar: " + request.error);
                Debug.LogError("ApplyOutfitToAvatar response: " + request.downloadHandler.text);
            }
            else
            {
                Debug.Log("Successfully applied outfit to avatar.");
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
                    content = "Given a transcript, say EXACTLY the id of the character that would be best to say that given transcript in a videogame."
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
}
