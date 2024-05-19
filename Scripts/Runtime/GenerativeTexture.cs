﻿using System;
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
    public class GenerativeTexture : MonoBehaviour
    {
        [SerializeField] GenerativeAIConfig config;
        [SerializeField] private float maxWaitTime = 60;
        
        public Renderer renderer;
        
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
            var response = await ImageGenerationRequest.RequestAsync(config, prompt);
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
            yield return ImageGenerationRequest.Request(config, prompt, response);
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
                renderer.material.mainTexture = texture;
            }
        }
        
        public async Task FetchTextureAsync(string url)
        {
            using (var httpClient = new HttpClient())
            {
                byte[] imageBytes = await httpClient.GetByteArrayAsync(url);
                Foreground(() =>
                {
                    Texture2D texture = new Texture2D(2, 2);
                    texture.LoadImage(imageBytes);
                    renderer.material.mainTexture = texture;
                });
            }
        }
    }
    
    #if UNITY_EDITOR
    [CustomEditor(typeof(GenerativeTexture))]
    public class GenerativeTextureEditor : Editor
    {
        private string prompt;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            GenerativeTexture generativeTexture = (GenerativeTexture)target;

            prompt = EditorGUILayout.TextField("Prompt", prompt);

            if (GUILayout.Button("Submit Prompt"))
            {
                generativeTexture.Prompt(prompt);
            }
        }
    }
    #endif
}