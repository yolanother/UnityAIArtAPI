#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEditor;
using System.Threading;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace DoubTech.AI.Art.Runpod
{
    /// <summary>
    /// Editor window that allows running Runpod requests with variable overrides.
    /// </summary>
    public class RunpodServerlessRunnerWindow : EditorWindow
    {
        private RunpodConfig config;
        private Texture2D generatedTexture;

        // Dictionary to store variable overrides
        private Dictionary<string, string> variableOverrides = new Dictionary<string, string>();

        // Job management
        private bool isGenerating = false;
        private string jobId;
        private CancellationTokenSource cancellationTokenSource;

        // Scroll position
        private Vector2 scrollPosition;

        [MenuItem("Tools/Runpod Serverless Runner Window")]
        public static void ShowWindow()
        {
            GetWindow<RunpodServerlessRunnerWindow>("Runpod Runner");
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("Runpod Serverless Runner", EditorStyles.boldLabel);

            // Select RunpodConfig
            config = (RunpodConfig)EditorGUILayout.ObjectField("Runpod Config", config, typeof(RunpodConfig), false);

            if (config != null)
            {
                // Adaptive layout
                if (position.width > position.height)
                {
                    // Horizontal layout
                    GUILayout.BeginHorizontal();

                    // Left panel - Inputs
                    GUILayout.BeginVertical(GUILayout.Width(position.width * 0.4f));

                    DrawInputPanel();

                    GUILayout.EndVertical();

                    // Right panel - Image
                    GUILayout.BeginVertical(GUILayout.Width(position.width * 0.6f));

                    DrawImagePanel();

                    GUILayout.EndVertical();

                    GUILayout.EndHorizontal();
                }
                else
                {
                    // Vertical layout
                    GUILayout.BeginVertical();

                    DrawInputPanel();

                    DrawImagePanel();

                    GUILayout.EndVertical();
                }
            }
            else
            {
                GUILayout.Label("Please select a RunpodConfig to proceed.");
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawInputPanel()
        {
            // Display variable fields
            GUILayout.Space(10);
            GUILayout.Label("Variables", EditorStyles.boldLabel);

            if (config.variables != null && config.variables.Count > 0)
            {
                foreach (var variable in config.variables)
                {
                    GUILayout.BeginVertical("box");

                    GUILayout.Label(variable.key);

                    // Get existing override value or use default
                    string overrideValue = variableOverrides.ContainsKey(variable.key) ? variableOverrides[variable.key] : variable.value;

                    // Display input field based on data type
                    string newValue = overrideValue;
                    switch (variable.dataType)
                    {
                        case DataType.Float:
                            if (float.TryParse(overrideValue, out float floatVal))
                            {
                                floatVal = EditorGUILayout.FloatField(floatVal);
                            }
                            else
                            {
                                floatVal = EditorGUILayout.FloatField(0f);
                            }
                            newValue = floatVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case DataType.Int:
                            if (int.TryParse(overrideValue, out int intVal))
                            {
                                intVal = EditorGUILayout.IntField(intVal);
                            }
                            else
                            {
                                intVal = EditorGUILayout.IntField(0);
                            }
                            newValue = intVal.ToString();
                            break;
                        case DataType.TextArea:
                            {
                                // Make the TextArea word-wrap and auto-expand
                                var style = new GUIStyle(EditorStyles.textArea)
                                {
                                    wordWrap = true
                                };
                                newValue = EditorGUILayout.TextArea(overrideValue, style, GUILayout.ExpandHeight(true));
                                break;
                            }
                        case DataType.Text:
                        default:
                            newValue = EditorGUILayout.TextField(overrideValue);
                            break;
                    }

                    // Update override value
                    if (newValue != overrideValue)
                    {
                        variableOverrides[variable.key] = newValue;
                    }

                    GUILayout.EndVertical();
                }
            }
            else
            {
                GUILayout.Label("No variables defined in the selected RunpodConfig.");
            }

            // Run Request and Cancel buttons
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();

            GUI.enabled = !isGenerating;
            if (GUILayout.Button(isGenerating ? "Generating..." : "Run Request"))
            {
                if (!string.IsNullOrEmpty(config.token) && !string.IsNullOrEmpty(config.endpointUrl))
                {
                    RunRequest();
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Please ensure the token and endpoint URL are set in the RunpodConfig.", "OK");
                }
            }
            GUI.enabled = isGenerating;
            if (GUILayout.Button("Cancel"))
            {
                CancelRequest();
            }
            GUI.enabled = true;

            GUILayout.EndHorizontal();
        }

        private void DrawImagePanel()
        {
            // Display generated image if available
            if (generatedTexture != null)
            {
                GUILayout.Space(20);
                GUILayout.Label("Generated Image:", EditorStyles.boldLabel);

                // Center the image
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                // Calculate the available height and adjust the image size accordingly
                float availableHeight = position.height - 200; // Adjust as needed
                float aspectRatio = (float)generatedTexture.width / generatedTexture.height;
                float imageHeight = Mathf.Min(availableHeight, generatedTexture.height);
                float imageWidth = imageHeight * aspectRatio;

                Rect textureRect = GUILayoutUtility.GetRect(imageWidth, imageHeight, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
                EditorGUI.DrawPreviewTexture(textureRect, generatedTexture);

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                // Save button
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Save Image", GUILayout.Width(100)))
                {
                    SaveGeneratedImage();
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        private void RunRequest()
        {
            RunRequestAsync();
        }

        private async void RunRequestAsync()
        {
            isGenerating = true;
            cancellationTokenSource = new CancellationTokenSource();

            // Capture the synchronization context of the Editor
            var editorSynchronizationContext = SynchronizationContext.Current;

            var requestHandler = new RunpodRequestHandler(config, editorSynchronizationContext);

            // Prepare variables to pass
            Dictionary<string, string> variablesDict = new Dictionary<string, string>(variableOverrides);

            // Run the request and await the result
            try
            {
                (generatedTexture, jobId) = await requestHandler.RunRequestWithJobId(variablesDict, cancellationTokenSource.Token);

                // Refresh the window to display the image
                Repaint();

                Debug.Log("Request completed. Texture received.");
            }
            catch (OperationCanceledException)
            {
                Debug.Log("Request was canceled.");
            }
            finally
            {
                isGenerating = false;
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }
        }

        private void CancelRequest()
        {
            if (cancellationTokenSource != null && !cancellationTokenSource.IsCancellationRequested)
            {
                cancellationTokenSource.Cancel();

                // Send cancel request to the API
                CancelJobAsync(jobId);
            }
        }

        private async void CancelJobAsync(string jobId)
        {
            if (string.IsNullOrEmpty(jobId)) return;

            string cancelUrl = $"{config.endpointUrl.TrimEnd('/')}/cancel/{jobId}";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + config.token);

                try
                {
                    HttpResponseMessage response = await client.PostAsync(cancelUrl, null);
                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();
                    Debug.Log("Cancel Response: " + responseBody);
                }
                catch (HttpRequestException e)
                {
                    Debug.LogError("Error in cancel request: " + e.Message);
                }
            }
        }

        private void SaveGeneratedImage()
        {
            // Prompt the user to choose a save location
            string path = EditorUtility.SaveFilePanel(
                "Save Generated Image",
                "Assets",
                "GeneratedImage.png",
                "png");

            if (!string.IsNullOrEmpty(path))
            {
                // Encode texture to PNG
                byte[] pngData = generatedTexture.EncodeToPNG();
                System.IO.File.WriteAllBytes(path, pngData);

                // Refresh the AssetDatabase if saved inside the Assets folder
                if (path.StartsWith(Application.dataPath))
                {
                    string assetPath = "Assets" + path.Substring(Application.dataPath.Length);
                    AssetDatabase.ImportAsset(assetPath);
                    Debug.Log("Texture saved at " + assetPath);
                }
                else
                {
                    Debug.Log("Texture saved at " + path);
                }
            }
        }
    }
}
#endif
