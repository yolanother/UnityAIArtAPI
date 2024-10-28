using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DoubTech.AI.Art.Data;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace DoubTech.AI.Art.Requests
{
    public class ImageGenerationRequest
    {
        [JsonIgnore]
        public GenerativeAIConfig Config { get; set; }

        [JsonIgnore]
        public Dictionary<string,object> Parameters { get; set; }

        [JsonProperty("prompt")]
        public string Prompt { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        public static IEnumerator UpdateStatus(GenerationResponse response)
        {
            if(string.IsNullOrEmpty(response.Id)) throw new Exception("Cannot update status without an id.");
            string url = response.Request.Config.endpoint;
            response.Request.Id = response.Id;
            yield return SendRequest(url, response.Request, response);
        }

        public static IEnumerator Request(GenerativeAIConfig config, string prompt, GenerationResponse response)
        {
            return Request(config, prompt, null, response);
        }

        private static string CreateUrl(GenerativeAIConfig config, Dictionary<string,object> parameters)
        {
            string url = config.endpoint;
            if (parameters != null && parameters.Count > 0)
            {
                url += url.Contains("?") ? "&" : "?";
                foreach (var parameter in parameters)
                {
                    string encodedValue = UnityWebRequest.EscapeURL(parameter.Value.ToString());
                    url += $"{parameter.Key}={encodedValue}&";
                }

                url = url.TrimEnd('&');
            }

            return url;
        }

        public static IEnumerator Request(GenerativeAIConfig config, string prompt,
            Dictionary<string, object> parameters, GenerationResponse response)
        {
            var requestBody = new ImageGenerationRequest
            {
                Config = config,
                Prompt = prompt,
                Parameters = parameters
            };
            
            var url = CreateUrl(config, parameters);

            yield return SendRequest(url, requestBody, response);
        }

        private static IEnumerator SendRequest(string url, ImageGenerationRequest requestBody, GenerationResponse response)
        {
            var jsonContent = JsonConvert.SerializeObject(requestBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonContent);

            Debug.Log("Request: " + url);
            using (UnityWebRequest request = new UnityWebRequest(url, "GET"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {requestBody.Config.apiKey}");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Request failed with status code {request.responseCode}");
                }
                else
                {
                    var responseContent = request.downloadHandler.text;
                    JsonConvert.PopulateObject(responseContent, response);
                    response.Request = requestBody;
                    requestBody.Id = response.Id;
                }
            }
        }

        public static Task<GenerationResponse> UpdateStatusAsync(GenerationResponse lastResponse)
        {
            if(string.IsNullOrEmpty(lastResponse.Id)) throw new Exception("Cannot update status without an id.");
            string url = lastResponse.Request.Config.endpoint;
            lastResponse.Request.Id = lastResponse.Id;
            return SendRequestAsync(url, lastResponse.Request, lastResponse);
        }

        public static Task<GenerationResponse> RequestAsync(GenerativeAIConfig config, string prompt)
        {
            return RequestAsync(config, prompt, null);
        }

        public static Task<GenerationResponse> RequestAsync(GenerativeAIConfig config, string prompt,
            Dictionary<string, object> parameters)
        {
            var requestBody = new ImageGenerationRequest
            {
                Config = config,
                Prompt = prompt
            };
            string url = CreateUrl(config, parameters);

            return SendRequestAsync(url, requestBody);
        }

        private static async Task<GenerationResponse> SendRequestAsync(string url, ImageGenerationRequest requestBody, GenerationResponse response = null)
        {
            return await BaseGenerativeImage.BackgroundTask<GenerationResponse>(async () =>
            {
                var jsonContent = JsonConvert.SerializeObject(requestBody);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", requestBody.Config.apiKey);

                    var responseMessage = await httpClient.PostAsync(url, httpContent);

                    if (responseMessage.IsSuccessStatusCode)
                    {
                        var responseContent = await responseMessage.Content.ReadAsStringAsync();
                        if (null == response)
                        {
                            response = JsonConvert.DeserializeObject<GenerationResponse>(responseContent);
                        }
                        else
                        {
                            JsonConvert.PopulateObject(responseContent, response);
                        }

                        response.Request = requestBody;
                        return response;
                    }
                    else
                    {
                        throw new Exception($"Request failed with status code {responseMessage.StatusCode}");
                    }
                }
            });
        }
    }
}