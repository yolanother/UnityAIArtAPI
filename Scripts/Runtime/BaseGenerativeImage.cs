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
using DoubTech.AI.Art.Threading;
using DoubTech.ThirdParty.AI.Common.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using Random = UnityEngine.Random;

namespace DoubTech.AI.Art
{
    public abstract class BaseGenerativeImage : MonoBehaviour,IImageRequestTracker, ITextureReady, ITexturesReady, IRequestComplete
    {
        [Header("Generative Image Settings")]
        [SerializeField] GenerativeAIConfig config;
        [SerializeField] private float maxWaitTime = 60;
        [SerializeField] private int baseResolution = 512;
        [SerializeField] private string seed;
        [SerializeField] private float aspectRatio = 1;
        [SerializeField] private bool cacheResponses;
        
        [Header("Events")]
        [SerializeField] private UnityEvent onRequestStarted = new UnityEvent();
        [SerializeField] private UnityEvent onRequestComplete = new UnityEvent();
        [SerializeField] private UnityEvent<string> onRequestFailed = new UnityEvent<string>();

        public UnityEvent OnRequestStarted => onRequestStarted;
        public UnityEvent OnRequestComplete => onRequestComplete;
        public UnityEvent<string> OnRequestFailed => onRequestFailed;
        
        private static ConcurrentDictionary<GenerativeAIConfig, float> _lastRequestTimes = new ConcurrentDictionary<GenerativeAIConfig, float>();
        
        public int Seed
        {
            get { return int.TryParse(seed, out var v) ? v : Random.Range(0, Int32.MaxValue); }
            set { seed = value.ToString(); }
        }
        
        public GenerativeAIConfig Config
        {
            get { return config; }
            set { config = value; }
        }

        private ConcurrentQueue<Func<Task<object>>> _foregroundActions = new ConcurrentQueue<Func<Task<object>>>();

        private HashSet<ImageJob> _activeResponses = new HashSet<ImageJob>();

        private static SynchronizationContext mainThreadContext;
        private string _lastPrompt;
        private ImageJob _lastResponse;
        private string _lastId;
        private ThreadContext _threadUtils;
        public ThreadContext ThreadContext => _threadUtils;

        private void Awake()
        {
            _threadUtils = new ThreadContext(this);
        }

        public void Cancel()
        {
            StopAllCoroutines();
            onRequestComplete?.Invoke();
        }

        public void Prompt(string prompt)
        {
            _lastPrompt = prompt;
            if (Application.isPlaying)
            {
                StartCoroutine(Request(prompt));
            }
            else
            {
                _ = _threadUtils.Background(async () => await RequestAsync(prompt));
            }
        }
        
        public class AsyncResult
        {
            
        }
        
        /// <summary>
        /// Prompts the generative AI with the given prompt and returns the generated images.
        /// </summary>
        /// <param name="prompt"></param>
        /// <returns></returns>
        public async Task<Texture2D[]> PromptAsync(string prompt)
        {
            _lastPrompt = prompt;
            var task =  await RequestAsync(prompt);
            return await task.GetImages();
        }

        /// <summary>
        /// Starts a prompt and returns a task that can be awaited for the result.
        /// </summary>
        /// <param name="prompt"></param>
        /// <returns></returns>
        public async Task<TrackedImageRequestTask> StartPromptAsync(string prompt)
        {
            _lastPrompt = prompt;
            return await RequestAsync(prompt);
        }

        public void Retry()
        {
            StopAllCoroutines();
            StartCoroutine(RetryCoroutine());
        }
        
        private IEnumerator RetryCoroutine()
        {
            if (string.IsNullOrEmpty(_lastId)) yield break;
            
            onRequestStarted?.Invoke();
            var response = new ImageJob();
            response.Id = _lastId;
            yield return ImageGenerationRequest.UpdateStatus(Config, _lastId, response, error =>
            {
                onRequestComplete?.Invoke();
                onRequestFailed?.Invoke(error);
            });
            onRequestComplete?.Invoke();
            if (!response.Status.IsFinished() && string.IsNullOrEmpty(response.Error))
            {
                onRequestFailed?.Invoke("Image not ready yet.");
            }
        }

        public void Reroll()
        {
            Prompt(_lastPrompt);
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

        private async Task<TrackedImageRequestTask> RequestAsync(string prompt)
        {
            _ = _threadUtils.Foreground(() => onRequestStarted?.Invoke());
            /*var cacheName = GetCacheName(prompt);
            if (cacheResponses && File.Exists(cacheName))
            {
                Texture2D texture = await LoadImageFromFileAsync(cacheName);
                OnTextureReady(texture);
                return;
            }*/
            try
            {
                // Rate limit individual requests
                while (_lastRequestTimes.TryGetValue(config, out float lastRequestTime))
                {
                    var timeSinceLastRequest = Time.time - lastRequestTime;
                    if (timeSinceLastRequest < 5)
                    {
                        await Task.Delay((int)(1000 - timeSinceLastRequest * 1000));
                    }
                    else
                    {
                        lock (_lastRequestTimes)
                        {
                            lastRequestTime = _lastRequestTimes[config];
                            if (Time.time - lastRequestTime > 1)
                            {
                                _lastRequestTimes[config] = Time.time;
                                break;
                            }
                        }
                    }
                }

                var response = await ImageGenerationRequest.RequestAsync(config, prompt, GetParameters());
                return new TrackedImageRequestTask(this)
                {
                    response = response,
                    config = config
                };
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message + "\n" + e.StackTrace);
                onRequestFailed?.Invoke(e.Message);
            }

            return null;
        }

        private Dictionary<string,object> GetParameters()
        {
            var width = baseResolution * aspectRatio;
            var height = baseResolution;
            var parameters = new Dictionary<string, object>();
            parameters.Add("width", width);
            parameters.Add("height", height);
            if(!string.IsNullOrEmpty(seed)) parameters.Add("seed", seed);
            return parameters;
        }

        public void RandomizeSeed()
        {
            // Random 32 bit seed
            seed = Random.Range(0, int.MaxValue).ToString();
        }

        
        
        public Task<T> ForegroundCoroutine<T>(Func<T> action)
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
                Debug.LogError(e);
                yield break;
            }
        }

        private void FlushForeground()
        {
            while (_foregroundActions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
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
            
            var response = new ImageJob();
            yield return ImageGenerationRequest.Request(config, prompt, GetParameters(), response, error =>
            {
                onRequestFailed?.Invoke(error);
            });
            var id = _lastId = response.Id;
            if(!string.IsNullOrEmpty(response.Error))
            {
                onRequestFailed?.Invoke(response.Error);
                yield break;
            }
            var time = Time.time;
            bool failed = false;
            while (!response.Status.IsFinished() && Time.time - time < maxWaitTime)
            {
                yield return new WaitForSeconds(5);
                yield return ImageGenerationRequest.UpdateStatus(Config, id, response, error =>
                {
                    onRequestComplete?.Invoke();
                    onRequestFailed?.Invoke(error);
                    failed = true;
                });
                if (failed) yield break;
            }

            if (response.Status.IsFinished())
            {
                _activeResponses.Remove(response);

                var images = GetImages(response);
                if(null != images)
                {
                    yield return FetchTexture(images[0], images);
                }
                else
                {
                    Debug.LogError("No images found in response.");
                }
            }
            onRequestComplete?.Invoke();
        }

        private string[] GetImages(ImageJob response)
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
                    texture.name = currentUrl;
                    OnTextureReady(i, texture);
                }
            }
            
            OnTexturesReady(textures);
        }

        protected virtual void OnTexturesReady(Texture2D[] textures) {}
        protected virtual void OnTextureReady(int index, Texture2D textures) {}


        public void AddTrackedImageJob(ImageJob job)
        {
            _activeResponses.Add(job);
        }

        public void RemoveTrackedImageJob(ImageJob job)
        {
            _activeResponses.Remove(job);
        }

        public float MaxWaitTime => maxWaitTime;
        public void OnTextureReady(Texture2D texture)
        {
            OnTextureReady(0, texture);
        }

        void ITexturesReady.OnTexturesReady(Texture2D[] textures)
        {
            OnTexturesReady(textures);
        }

        void IRequestComplete.OnRequestComplete()
        {
            onRequestComplete?.Invoke();
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

    public interface IImageRequestTracker
    {
        void AddTrackedImageJob(ImageJob job);
        void RemoveTrackedImageJob(ImageJob job);
        float MaxWaitTime { get; }
    }
    
    public interface ITextureReady
    {
        void OnTextureReady(Texture2D texture);
    }
    
    public interface ITexturesReady
    {
        void OnTexturesReady(Texture2D[] textures);
    }
    
    public interface IRequestComplete
    {
        void OnRequestComplete();
    }
    
    public class TrackedImageRequestTask
    {
        public string name;
        public GenerativeAIConfig config;
        public ImageJob response;
        public IImageRequestTracker genImage;
        public ITextureReady onTextureReadyContext;
        public ITexturesReady onTexturesReady;
        public IRequestComplete onRequestComplete;
        public Action<Texture2D> onTextureReady;

        public TrackedImageRequestTask(object context)
        {
            if(context is IImageRequestTracker tracker)
            {
                genImage = tracker;
            }
            
            if(context is ITextureReady textureReady)
            {
                onTextureReadyContext = textureReady;
            }
            
            if(context is ITexturesReady texturesReady)
            {
                onTexturesReady = texturesReady;
            }
        }

        public string[] ImageUrls
        {
            get
            {
                if (response.Images.Count > 0)
                {
                    return response.Images.Select(i => i.Url).ToArray();
                }
                else if (!string.IsNullOrEmpty(response.Url))
                {
                    return new[] { response.Url };
                }

                return null;
            }
        }

        public async Task<Texture2D[]> GetImages()
        {
            var textures = new Texture2D[0];
            genImage?.AddTrackedImageJob(response);
            var time = 0f;
            while (!response.Status.IsFinished() && time < (genImage?.MaxWaitTime ?? 60))
            {
                await Task.Delay(5000);
                time += 5;

                await ImageGenerationRequest.UpdateStatusAsync(response);
            }

            if (response.Status.IsFinished())
            {
                genImage?.RemoveTrackedImageJob(response);

                textures = await FetchTextureAsync(ImageUrls);
            }
            _ = ThreadUtils.RunOnForegroundThread(() => onRequestComplete?.OnRequestComplete());
            return textures;
        }
        
        public async Task<Texture2D[]> FetchTextureAsync(string[] urls, string cacheFile = null)
        {
            var textures = new Texture2D[urls.Length];
            for (int i = 0; i < urls.Length; i++)
            {
                using (var httpClient = new HttpClient())
                {
                    var url = urls[i];
                    byte[] imageBytes = await httpClient.GetByteArrayAsync(url);
                    //await File.WriteAllBytesAsync(cacheFile, imageBytes);
                    var texture = await ThreadUtils.RunOnForegroundThread<Texture2D>(() =>
                    {
                        Texture2D texture = new Texture2D(2, 2);
                        texture.name = name ?? url; 
                        texture.LoadImage(imageBytes);
                        onTextureReadyContext?.OnTextureReady(texture);
                        onTextureReady?.Invoke(texture);
                        return texture;
                    });
                    textures[i] = texture;
                }
            }
            _ = ThreadUtils.RunOnForegroundThread(() =>
            {
                onTexturesReady?.OnTexturesReady(textures);
            });
            return textures;
        }
    }
}