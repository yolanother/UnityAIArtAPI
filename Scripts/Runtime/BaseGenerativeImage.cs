using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using DoubTech.AI.Art.Data;
using DoubTech.AI.Art.Requests;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace DoubTech.AI.Art
{
    public abstract class BaseGenerativeImage : MonoBehaviour
    {
        [Header("Generative Image Settings")]
        [SerializeField] GenerativeAIConfig config;
        [SerializeField] private float maxWaitTime = 60;
        [SerializeField] private int baseResolution = 512;
        [SerializeField] private float aspectRatio = 1;
        
        private ConcurrentQueue<Action> _foregroundActions = new ConcurrentQueue<Action>();

        private HashSet<GenerationResponse> _activeResponses = new HashSet<GenerationResponse>();

        public void Prompt(string prompt)
        {
            if (Application.isPlaying)
            {
                StartCoroutine(Request(prompt));
            }
            else
            {
                RequestAsync(prompt);
            }
        }

        private async void RequestAsync(string prompt)
        {
            var response = await ImageGenerationRequest.RequestAsync(config, prompt, GetParameters());
            _activeResponses.Add(response);
            var time = 0f;
            while (!response.Status.IsFinished() && time < maxWaitTime)
            {
                await Task.Delay(5000);
                time += 5;
                
                await ImageGenerationRequest.UpdateStatusAsync(response);
            }

            if (response.Status.IsFinished())
            {
                _activeResponses.Remove(response);

                await FetchTextureAsync(response.Url);
            }
        }

        private Dictionary<string,object> GetParameters()
        {
            var width = baseResolution * aspectRatio;
            var height = baseResolution;
            var parameters = new Dictionary<string, object>();
            parameters.Add("width", width);
            parameters.Add("height", height);
            return parameters;
        }

        private void Foreground(Action action)
        {
            _foregroundActions.Enqueue(action);
            #if UNITY_EDITOR
            EditorApplication.update += HandleForegroundAsync;
            #endif
            StartCoroutine(HandleForeground());
        }

        private void FlushForeground()
        {
            while (_foregroundActions.TryDequeue(out var action))
            {
                action();
            }
        }
        
        private IEnumerator HandleForeground()
        {
            yield return null;
            FlushForeground();
        }

        private void HandleForegroundAsync()
        {
#if UNITY_EDITOR
            EditorApplication.update -= HandleForegroundAsync;
#endif
            FlushForeground();
        }


        IEnumerator Request(string prompt)
        {
            var response = new GenerationResponse();
            yield return ImageGenerationRequest.Request(config, prompt, GetParameters(), response);
            var time = Time.time;
            while (!response.Status.IsFinished() && time < maxWaitTime)
            {
                yield return new WaitForSeconds(5);
                yield return ImageGenerationRequest.UpdateStatus(response);
            }

            if (response.Status.IsFinished())
            {
                _activeResponses.Remove(response);

                yield return FetchTextureAsync(response.Url);
            }
        }

        IEnumerator FetchTexture(string url)
        {
            UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.Log(request.error);
            }
            else
            {
                Texture2D texture = ((DownloadHandlerTexture)request.downloadHandler).texture;
                OnTextureReady(texture);
            }
        }

        protected abstract void OnTextureReady(Texture2D texture);

        public async Task FetchTextureAsync(string url)
        {
            using (var httpClient = new HttpClient())
            {
                byte[] imageBytes = await httpClient.GetByteArrayAsync(url);
                Foreground(() =>
                {
                    Texture2D texture = new Texture2D(2, 2);
                    texture.LoadImage(imageBytes);
                    OnTextureReady(texture);
                });
            }
        }
    }
    
    #if UNITY_EDITOR
    [CustomEditor(typeof(BaseGenerativeImage), true)]
    public class GenerativeTextureEditor : Editor
    {
        private string prompt;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            BaseGenerativeImage generativeTexture = (BaseGenerativeImage)target;

            GUILayout.Space(16);
            GUILayout.Label("Generation", EditorStyles.boldLabel);
            prompt = EditorGUILayout.TextField("Prompt", prompt);

            if (GUILayout.Button("Submit Prompt"))
            {
                generativeTexture.Prompt(prompt);
            }
        }
    }
    #endif
}