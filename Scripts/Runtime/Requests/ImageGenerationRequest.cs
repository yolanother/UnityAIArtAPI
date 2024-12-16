using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DoubTech.AI.Art.Data;
using DoubTech.AI.Art.Threading;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace DoubTech.AI.Art.Requests
{
    public enum RequestState
    {
        Idle,
        Queued,
        Processing,
        Complete,
    }
    
    public class ImageGenerationRequest : IGenerationTask
    {
        [JsonIgnore]
        public GenerativeAIConfig Config { get; set; }

        [JsonIgnore]
        public Dictionary<string,object> Parameters { get; set; }

        [JsonProperty("prompt")]
        public string Prompt { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }
        
        [JsonProperty("image")]
        public string Image { get; set; }

        [JsonIgnore]
        public RequestState State { get; set; } = RequestState.Idle;

        public void SetImage(Texture2D texture)
        {
            // Convert the texture into a base64 string.
            Image = "data:image/png;base64," + Convert.ToBase64String(texture.EncodeToPNG());
        }
        
        private static ConcurrentDictionary<GenerativeAIConfig, float> _lastRequestTimes = new ConcurrentDictionary<GenerativeAIConfig, float>();

        public static IEnumerator UpdateStatus(GenerativeAIConfig config, string id, ImageJob response, Action<string> onError)
        {
            if (string.IsNullOrEmpty(id))
            {
                onError?.Invoke("Cannot update status without an id.");
            }
            string url = config.host + config.statusEndpoint;
            var request = new ImageGenerationRequest();
            request.Config = config;
            request.Id = id;
            response.Request = request;
            response.Id = id;
            yield return SendRequest(url, request, response, onError);
        }

        public static IEnumerator Request(GenerativeAIConfig config, string prompt, ImageJob response, Action<string> onError)
        {
            return Request(config, prompt, null, response, onError);
        }

        private static string CreateUrl(GenerativeAIConfig config, Dictionary<string,object> parameters)
        {
            string url = config.host + config.jobEndpoint;
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
            Dictionary<string, object> parameters, ImageJob response, Action<string> onError)
        {
            var requestBody = new ImageGenerationRequest
            {
                Config = config,
                Prompt = prompt,
                Parameters = parameters
            };
            
            var url = CreateUrl(config, parameters);

            yield return SendRequest(url, requestBody, response, onError);
        }

        private static IEnumerator SendRequest(string url, ImageGenerationRequest requestBody, ImageJob response, Action<string> onError)
        {
            while(_lastRequestTimes.TryGetValue(requestBody.Config, out float lastRequestTime))
            {
                lock (_lastRequestTimes)
                {
                    lastRequestTime = _lastRequestTimes[requestBody.Config];
                    if (Time.time - lastRequestTime > 1)
                    {
                        _lastRequestTimes[requestBody.Config] = Time.time;
                        break;
                    }
                }
                yield return new WaitForSeconds(1);
            }
            
            _lastRequestTimes[requestBody.Config] = Time.time;
            
            var jsonContent = JsonConvert.SerializeObject(requestBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonContent);

            Debug.Log("Request: " + url + "\n" + jsonContent);
            using (UnityWebRequest request = new UnityWebRequest(url, "GET"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {requestBody.Config.apiKey}");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    response.Error = $"Request failed with status code {request.responseCode}";
                    Debug.LogError(response.Error);
                    onError?.Invoke(response.Error);
                }
                else
                {
                    var responseContent = request.downloadHandler.text;
                    Debug.Log(responseContent);
                    JsonConvert.PopulateObject(responseContent, response);
                    response.Request = requestBody;
                    requestBody.Id = response.Id;
                    if (!string.IsNullOrEmpty(response.Error))
                    {
                        onError?.Invoke(response.Error);
                    }
                }
            }
        }

        public static Task<ImageJob> UpdateStatusAsync(ImageJob lastResponse)
        {
            if(string.IsNullOrEmpty(lastResponse.Id)) {throw new Exception("Cannot update status without an id.");}
            string url = lastResponse.Request.Config.host + lastResponse.Request.Config.statusEndpoint;
            lastResponse.Request.Id = lastResponse.Id;
            return SendRequestAsync(url, lastResponse.Request, lastResponse);
        }

        public static Task<ImageJob> RequestAsync(GenerativeAIConfig config, string prompt)
        {
            return RequestAsync(config, prompt, null);
        }

        public static Task<ImageJob> RequestAsync(GenerativeAIConfig config, string prompt,
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
        
        public class HttpClientHelper
        {
            public static HttpClient CreateHttpClient()
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                HttpClientHandler handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                };

                return new HttpClient(handler);
            }
        }

        private static HttpClient httpClient =  ImageGenerationRequest.HttpClientHelper.CreateHttpClient();
        private static async Task<ImageJob> SendRequestAsync(string url, ImageGenerationRequest requestBody,
            ImageJob response = null)
        {
            return await ThreadContext.BackgroundTask<ImageJob>(async () =>
            {
                var jsonContent = JsonConvert.SerializeObject(requestBody);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                httpClient.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", requestBody.Config.apiKey);

                try
                {
                    var responseMessage = await httpClient.PostAsync(url, httpContent);

                    if (responseMessage.IsSuccessStatusCode)
                    {
                        var responseContent = await responseMessage.Content.ReadAsStringAsync();
                        if (null == response)
                        {
                            response = JsonConvert.DeserializeObject<ImageJob>(responseContent);
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
                        Debug.LogError($"Request {url} failed with status code {responseMessage.StatusCode}");
                        throw new Exception($"Request {url} failed with status code {responseMessage.StatusCode}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Request {url} failed with exception {e.Message}");
                    throw e;
                }
            });
        }

        [JsonIgnore] public string TaskName => Config.Name;
        [JsonIgnore] public string TaskDescription => $"Request to create a new {TaskName} job.";
        [JsonIgnore] public TaskEvents Events { get; } = new TaskEvents(); 
        public void Cancel()
        {
            // Single http request cannot be canceled.
        }
    }
}