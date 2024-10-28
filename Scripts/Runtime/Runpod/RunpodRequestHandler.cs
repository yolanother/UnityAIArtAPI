using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;
using UnityEngine;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;

namespace DoubTech.AI.Art.Runpod
{
    public class RunpodRequestHandler
    {
        private RunpodConfig config;
        private SynchronizationContext synchronizationContext;

        private string jobId;
        private bool jobCompleted = false;

        public RunpodRequestHandler(RunpodConfig config, SynchronizationContext synchronizationContext = null)
        {
            this.config = config;
            this.synchronizationContext = synchronizationContext ?? SynchronizationContext.Current;
        }

        /// <summary>
        /// Starts the request and polling process.
        /// </summary>
        /// <param name="variables">Optional variables to override the default ones.</param>
        /// <returns>A Task that represents the asynchronous operation. The task result contains the generated Texture2D and the job ID.</returns>
        public async Task<(Texture2D, string)> RunRequestWithJobId(Dictionary<string, string> variables = null, CancellationToken cancellationToken = default)
        {
            // Process the variables passed in parameters
            List<Variable> overrideVariables = new List<Variable>();
            if (variables != null)
            {
                foreach (var kvp in variables)
                {
                    // Find the dataType from the default variables
                    Variable defaultVar = config.variables.Find(v => v.key == kvp.Key);
                    DataType dataType = defaultVar != null ? defaultVar.dataType : DataType.Text;

                    overrideVariables.Add(new Variable { key = kvp.Key, value = kvp.Value, dataType = dataType });
                }
            }

            await SendPostRequest(overrideVariables);

            // Start polling for status updates
            Texture2D texture = await PollJobStatus(cancellationToken);

            return (texture, jobId);
        }

        private async Task SendPostRequest(List<Variable> overrideVariables)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + config.token);

                // Construct the POST URL by appending "/run"
                string postUrl = config.endpointUrl.TrimEnd('/') + "/run";

                // Process variables in inputJson
                string processedJson = ProcessVariables(config.inputJson.text, config.variables, overrideVariables);

                var content = new StringContent(processedJson, Encoding.UTF8, "application/json");

                try
                {
                    HttpResponseMessage response = await client.PostAsync(postUrl, content);
                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();
                    Debug.Log("POST Response: " + responseBody);

                    var postResponse = JsonConvert.DeserializeObject<JobResponse>(responseBody);
                    jobId = postResponse.id;
                }
                catch (HttpRequestException e)
                {
                    Debug.LogError($"Error in POST request: {e.Message}\nProcessed JSON:\n{processedJson}");
                }
            }
        }

        private string ProcessVariables(string inputJson, List<Variable> defaultVariables, List<Variable> overrideVariables)
        {
            string outputJson = inputJson;

            // Create a dictionary for variable replacement
            Dictionary<string, Variable> variableDict = new Dictionary<string, Variable>();

            // Add default variables
            foreach (var variable in defaultVariables)
            {
                variableDict[variable.key] = new Variable
                {
                    key = variable.key,
                    value = variable.value,
                    dataType = variable.dataType
                };
            }

            // Override variables
            foreach (var variable in overrideVariables)
            {
                if (variableDict.ContainsKey(variable.key))
                {
                    variableDict[variable.key].value = variable.value;
                }
                else
                {
                    variableDict[variable.key] = variable;
                }
            }

            // Replace placeholders
            foreach (var kvp in variableDict)
            {
                string key = kvp.Key;
                Variable variable = kvp.Value;
                string placeholder = "$(" + key + ")";

                string value = variable.value;

                // Type-check and format the value based on data type
                switch (variable.dataType)
                {
                    case DataType.Float:
                        if (float.TryParse(value, out float floatVal))
                        {
                            value = floatVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            Debug.LogError($"Variable '{key}' expects a float value.");
                        }
                        break;
                    case DataType.Int:
                        if (int.TryParse(value, out int intVal))
                        {
                            value = intVal.ToString();
                        }
                        else
                        {
                            Debug.LogError($"Variable '{key}' expects an integer value.");
                        }
                        break;
                    case DataType.Text:
                    case DataType.TextArea:
                        value = JsonConvert.ToString(value); // Adds quotes and escapes as necessary
                        break;
                    default:
                        value = JsonConvert.ToString(value);
                        break;
                }

                outputJson = outputJson.Replace(placeholder, value);
            }

            // Check for any unreplaced placeholders
            if (Regex.IsMatch(outputJson, @"\$\(.*?\)"))
            {
                Debug.LogError($"Error: Unresolved placeholders in inputJson. Please check your variables.\nProcessed JSON:\n{outputJson}");
            }

            return outputJson;
        }

        private async Task<Texture2D> PollJobStatus(CancellationToken cancellationToken)
        {
            using (var client = new HttpClient(new HttpClientHandler { UseCookies = false }))
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + config.token);

                // Add headers to prevent caching
                client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true,
                    MustRevalidate = true
                };

                while (!jobCompleted)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Construct the status URL by appending "/status/{jobId}"
                    string statusUrl = config.endpointUrl.TrimEnd('/') + "/status/" + jobId;

                    try
                    {
                        // Create a new HttpRequestMessage to set headers specific to this request
                        var request = new HttpRequestMessage(HttpMethod.Get, statusUrl);

                        // Add headers to prevent caching
                        request.Headers.CacheControl = new CacheControlHeaderValue
                        {
                            NoCache = true,
                            NoStore = true,
                            MustRevalidate = true
                        };
                        request.Headers.Pragma.ParseAdd("no-cache");

                        HttpResponseMessage response = await client.SendAsync(request);
                        response.EnsureSuccessStatusCode();

                        string responseBody = await response.Content.ReadAsStringAsync();
                        Debug.Log("Status Response: " + responseBody);

                        var statusResponse = JsonConvert.DeserializeObject<StatusResponse>(responseBody);

                        if (statusResponse.status == "COMPLETED")
                        {
                            jobCompleted = true;

                            // Handle the output
                            string base64Image = statusResponse.output.message;
                            byte[] imageData = Convert.FromBase64String(base64Image);

                            // Create the texture on the main thread
                            var tcs = new TaskCompletionSource<Texture2D>();

                            synchronizationContext.Post(_ =>
                            {
                                try
                                {
                                    Texture2D texture = new Texture2D(2, 2);
                                    texture.LoadImage(imageData);

                                    Debug.Log("Job completed successfully.");

                                    tcs.SetResult(texture);
                                }
                                catch (Exception ex)
                                {
                                    tcs.SetException(ex);
                                }
                            }, null);

                            return await tcs.Task;
                        }
                        else if (statusResponse.status == "FAILED")
                        {
                            Debug.LogError("Job failed to complete.");
                            jobCompleted = true;
                            return null;
                        }
                        else
                        {
                            // Use delayTime to wait before checking again
                            int delayTimeMs = statusResponse.delayTime;
                            await Task.Delay(delayTimeMs, cancellationToken);
                        }
                    }
                    catch (HttpRequestException e)
                    {
                        Debug.LogError("Error in status request: " + e.Message);
                        break;
                    }
                    catch (TaskCanceledException)
                    {
                        Debug.Log("Polling canceled.");
                        throw;
                    }
                }

                return null;
            }
        }

        // Classes for JSON deserialization
        private class JobResponse
        {
            public string id { get; set; }
            public string status { get; set; }
        }

        private class OutputData
        {
            public string message { get; set; }
            public string status { get; set; }
        }

        private class StatusResponse
        {
            public int delayTime { get; set; }
            public int executionTime { get; set; }
            public string id { get; set; }
            public OutputData output { get; set; }
            public string status { get; set; }
            public string workerId { get; set; }
        }
    }
}
