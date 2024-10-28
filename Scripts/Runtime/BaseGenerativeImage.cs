using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DoubTech.AI.Art.Data;
using DoubTech.AI.Art.Requests;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
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
        [SerializeField] private bool cacheResponses;
        
        [Header("Events")]
        [SerializeField] private UnityEvent onRequestStarted = new UnityEvent();
        [SerializeField] private UnityEvent onRequestComplete = new UnityEvent();

        public UnityEvent OnRequestStarted => onRequestStarted;
        public UnityEvent OnRequestComplete => onRequestComplete;
        
        public GenerativeAIConfig Config
        {
            get { return config; }
            set { config = value; }
        }

        private ConcurrentQueue<Func<Task<object>>> _foregroundActions = new ConcurrentQueue<Func<Task<object>>>();

        private HashSet<GenerationResponse> _activeResponses = new HashSet<GenerationResponse>();

        private static SynchronizationContext mainThreadContext;
        
        private void Awake()
        {
            mainThreadContext = SynchronizationContext.Current;
        }

        public void Cancel()
        {
            StopAllCoroutines();
            onRequestComplete?.Invoke();
        }

        public void Prompt(string prompt)
        {
            if (Application.isPlaying)
            {
                StartCoroutine(Request(prompt));
            }
            else
            {
                _ = BackgroundTask(async () => await RequestAsync(prompt));
            }
        }

        private string GetCacheName(string prompt)
        {
            using (var sha1 = new SHA1Managed())
            {
                var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(prompt));
                var hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();
                return Path.Join(Application.persistentDataPath, hashString + ".png");
            }
        }
        public async Task<Texture2D> LoadImageFromFileAsync(string filePath)
        {
            var texture = new Texture2D(2, 2);
            texture.LoadImage(await File.ReadAllBytesAsync(filePath));
            return texture;
        }
        private async Task RequestAsync(string prompt)
        {
            _ = Foreground(() => onRequestStarted?.Invoke());
            /*var cacheName = GetCacheName(prompt);
            if (cacheResponses && File.Exists(cacheName))
            {
                Texture2D texture = await LoadImageFromFileAsync(cacheName);
                OnTextureReady(texture);
                return;
            }*/
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

                await FetchTextureAsync(GetImages(response));
            }
            _ = Foreground(() => onRequestComplete?.Invoke());
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

        public static async Task<T> BackgroundTask<T>(Func<Task<T>> func)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    return await func();
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    throw;
                }
            });
        }

        public static async Task BackgroundTask(Func<Task> func)
        {
            await Task.Run(async () =>
            {
                try
                {
                    await func();
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                    throw;
                }
            });
        }

        public Task Foreground(Action action)
        {
            var tcs = new TaskCompletionSource<object>();

            // Post the action to the main thread context
            mainThreadContext.Post(_ =>
            {
                StartCoroutine(ExecuteOnMainThread(() =>
                {
                    action();
                    return null;
                }, tcs));
            }, null);

            return tcs.Task;
        }
        
        public Task<T> Foreground<T>(Func<T> action)
        {
            var tcs = new TaskCompletionSource<T>();

            // Post the action to the main thread context
            mainThreadContext.Post(_ =>
            {
                StartCoroutine(ExecuteOnMainThread(action, tcs));
            }, null);

            return tcs.Task;
        }

        private static IEnumerator ExecuteOnMainThread<T>(Func<T> action, TaskCompletionSource<T> tcs)
        {
            // Execute the action safely and handle exceptions
            try
            { 
                var result = action();
                tcs.SetResult(result);
            }
            catch (Exception e)
            {
                tcs.SetException(e);
                yield break;
            }
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
            onRequestStarted?.Invoke();
            var cacheName = GetCacheName(prompt);
            
            /*if (cacheResponses && File.Exists(cacheName))
            {
                Task<Texture2D> texture = LoadImageFromFileAsync(cacheName);
                while (!texture.IsCompleted) yield return null;
                if (texture.IsCompletedSuccessfully)
                {
                    OnTextureReady(texture.Result);
                    yield break;
                }
            }*/
            
            var response = new GenerationResponse();
            yield return ImageGenerationRequest.Request(config, prompt, GetParameters(), response);
            var time = Time.time;
            while (!response.Status.IsFinished() && Time.time - time < maxWaitTime)
            {
                yield return new WaitForSeconds(5);
                yield return ImageGenerationRequest.UpdateStatus(response);
            }

            if (response.Status.IsFinished())
            {
                _activeResponses.Remove(response);

                var images = GetImages(response);
                if(null != images)
                {
                    yield return FetchTextureAsync(images);
                }
                else
                {
                    Debug.LogError("No images found in response.");
                }
            }
            onRequestComplete?.Invoke();
        }

        private string[] GetImages(GenerationResponse response)
        {
            if (response.Images.Count > 0)
            {
                return response.Images.Select(i => i.Url).ToArray();
            }
            else if(!string.IsNullOrEmpty(response.Url))
            {
                return new [] { response.Url };
            }

            return null;
        }

        IEnumerator FetchTexture(string url, params string[] urls)
        {
            Texture2D[] textures = new Texture2D[urls.Length + 1];

            for (int i = 0; i < textures.Length; i++)
            {
                var currentUrl = i == 0 ? url : urls[i - 1];
                UnityWebRequest request = UnityWebRequestTexture.GetTexture(currentUrl);
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.Log(request.error);
                }
                else
                {
                    Texture2D texture = ((DownloadHandlerTexture)request.downloadHandler).texture;
                    OnTextureReady(i, texture);
                }
            }
            
            OnTexturesReady(textures);
        }

        protected virtual void OnTexturesReady(Texture2D[] textures) {}
        protected virtual void OnTextureReady(int index, Texture2D textures) {}

        public async Task FetchTextureAsync(string[] urls, string cacheFile = null)
        {
            var textures = new Texture2D[urls.Length];
            for (int i = 0; i < urls.Length; i++)
            {
                using (var httpClient = new HttpClient())
                {
                    var url = urls[i];
                    byte[] imageBytes = await httpClient.GetByteArrayAsync(url);
                    //await File.WriteAllBytesAsync(cacheFile, imageBytes);
                    var texture = await Foreground<Texture2D>(() =>
                    {
                        Texture2D texture = new Texture2D(2, 2);
                        texture.LoadImage(imageBytes);
                        OnTextureReady(i, texture);
                        return texture;
                    });
                    textures[i] = texture;
                }
            }
            Foreground(() =>
            {
                OnTexturesReady(textures);
            });
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