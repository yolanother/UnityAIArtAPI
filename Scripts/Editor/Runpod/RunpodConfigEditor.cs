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
    /// Custom editor for the RunpodConfig ScriptableObject, allowing image generation
    /// directly from the Inspector.
    /// </summary>
    [CustomEditor(typeof(RunpodConfig))]
    public class RunpodConfigEditor : Editor
    {
        private RunpodConfig config;
        private Texture2D generatedTexture;

        // Dictionary to store variable overrides
        private Dictionary<string, string> variableOverrides = new Dictionary<string, string>();

        // Job management
        private bool isGenerating = false;
        private string jobId;
        private CancellationTokenSource cancellationTokenSource;

        public override void OnInspectorGUI()
        {
            // Draw the default inspector
            DrawDefaultInspector();

            config = (RunpodConfig)target;

            // Add a space
            GUILayout.Space(10);

            // Display variable fields
            GUILayout.Label("Variable Overrides", EditorStyles.boldLabel);

            if (config.variables != null && config.variables.Count > 0)
            {
                foreach (var variable in config.variables)
                {
                    GUILayout.BeginHorizontal();

                    GUILayout.Label(variable.key, GUILayout.Width(100));

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
                                // Define the height of the text area
                                int textAreaLines = 5; // Adjust this number as needed
                                GUILayoutOption[] options = { GUILayout.Height(EditorGUIUtility.singleLineHeight * textAreaLines) };

                                newValue = EditorGUILayout.TextArea(overrideValue, options);
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

                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label("No variables defined.");
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
                    EditorUtility.DisplayDialog("Error", "Please ensure the token and endpoint URL are set.", "OK");
                }
            }
            GUI.enabled = isGenerating;
            if (GUILayout.Button("Cancel"))
            {
                CancelRequest();
            }
            GUI.enabled = true;

            GUILayout.EndHorizontal();

            // Display generated image if available
            if (generatedTexture != null)
            {
                GUILayout.Space(20);
                GUILayout.Label("Generated Image:", EditorStyles.boldLabel);

                // Display the generated image scaled to the inspector width
                float aspectRatio = (float)generatedTexture.width / generatedTexture.height;
                float imageWidth = EditorGUIUtility.currentViewWidth - 40;
                float imageHeight = imageWidth / aspectRatio;

                Rect textureRect = GUILayoutUtility.GetRect(imageWidth, imageHeight, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(textureRect, generatedTexture);

                // Save button
                if (GUILayout.Button("Save Image", GUILayout.Width(100)))
                {
                    SaveGeneratedImage();
                }
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

                // Refresh the inspector to display the image
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
