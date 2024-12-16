using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DoubTech.AI.Art.Data;
using DoubTech.AI.Art.Threading;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Events;

namespace DoubTech.AI.Art.Requests
{
    public interface IGenerationTask
    {
        public string TaskName { get; }
        public string TaskDescription { get; }
        public TaskEvents Events { get; }
        void Cancel();
    }
    
    [Serializable]
    public class TaskEvents
    {
        [SerializeField] private UnityEvent<IGenerationTask> onRequestStarted = new UnityEvent<IGenerationTask>();
        [SerializeField] private UnityEvent<IGenerationTask> onRequestCompleted = new UnityEvent<IGenerationTask>();
        [SerializeField] private UnityEvent<IGenerationTask> onRequestSuccess = new UnityEvent<IGenerationTask>();
        [SerializeField] private UnityEvent<IGenerationTask> onRequestFailed = new UnityEvent<IGenerationTask>();
        [SerializeField] private UnityEvent<IGenerationTask> onRequestCancelled = new UnityEvent<IGenerationTask>();
        [SerializeField] private UnityEvent<IGenerationTask> onRequestTimedOut = new UnityEvent<IGenerationTask>();
        
        public UnityEvent<IGenerationTask> OnRequestStarted => onRequestStarted;
        public UnityEvent<IGenerationTask> OnRequestCompleted => onRequestCompleted;
        public UnityEvent<IGenerationTask> OnRequestSuccess => onRequestSuccess;
        public UnityEvent<IGenerationTask> OnRequestFailed => onRequestFailed;
        public UnityEvent<IGenerationTask> OnRequestCancelled => onRequestCancelled;
        public UnityEvent<IGenerationTask> OnRequestTimedOut => onRequestTimedOut;

        /// <summary>
        /// Triggers any events called on this event object to call the corresponding events on the provided events object.
        /// </summary>
        /// <param name="events">The events to invoke</param>
        public void Track(TaskEvents events)
        {
            onRequestStarted.AddListener(events.onRequestStarted.Invoke);
            onRequestCompleted.AddListener(events.onRequestCompleted.Invoke);
            onRequestSuccess.AddListener(events.onRequestSuccess.Invoke);
            onRequestFailed.AddListener(events.onRequestFailed.Invoke);
            onRequestCancelled.AddListener(events.onRequestCancelled.Invoke);
            onRequestTimedOut.AddListener(events.onRequestTimedOut.Invoke);
        }

        /// <summary>
        /// Stops tracking the provided events object.
        /// </summary>
        public void StopTracking(TaskEvents events)
        {
            onRequestStarted.RemoveListener(events.onRequestStarted.Invoke);
            onRequestCompleted.RemoveListener(events.onRequestCompleted.Invoke);
            onRequestSuccess.RemoveListener(events.onRequestSuccess.Invoke);
            onRequestFailed.RemoveListener(events.onRequestFailed.Invoke);
            onRequestCancelled.RemoveListener(events.onRequestCancelled.Invoke);
            onRequestTimedOut.RemoveListener(events.onRequestTimedOut.Invoke);
        }
    }
    
    public class ImageGenRequestManager : MonoBehaviour
    {
        [SerializeField] public float maxWaitTime = 300f;
        private static ImageGenRequestManager instance;

        [SerializeField] private TaskEvents taskEvents = new TaskEvents();

        public TaskEvents TaskEvents => taskEvents;
        
        private static HttpClient httpClient =  ImageGenerationRequest.HttpClientHelper.CreateHttpClient();
        
        public static ImageGenRequestManager Instance
        {
            get
            {
                if (!instance)
                {
                    instance = FindObjectOfType<ImageGenRequestManager>(true);
                    if(!instance) instance = new GameObject("ImageGenRequestManager").AddComponent<ImageGenRequestManager>();
                }
                return instance;
            }
        }

        private List<IGenerationTask> activeRequests = new List<IGenerationTask>();
        private ThreadContext _threadCtx;

        private void Awake()
        {
            _threadCtx = new ThreadContext(this);
        }

        private void OnEnable()
        {
            if (instance && instance != this)
            {
                Destroy(this);
            }
            else
            {
                instance = this;
            }
        }
        
        public void TrackTask(IGenerationTask task)
        {
            Debug.Log($"Tracking: {task.TaskName}");
            task.Events.Track(taskEvents);
            activeRequests.Add(task);
        }

        public void UntrackTask(IGenerationTask task)
        {
            task.Events.StopTracking(taskEvents);
            activeRequests.Remove(task);
        }
        
        private async Task ThreadContextCheck()
        {
            await _threadCtx.Background(async () =>
            {
                while (null == _threadCtx)
                {
                    // Yield the thread until the thread context is set.
                    await Task.Yield();
                }
            });
        }

        public static async Task<ImageRequestTask> RequestAsync(ImageGenerationRequest request)
        {
            if (!request.Config) throw new Exception("Request must have a config.");
            if (!instance) throw new Exception("ImageGenRequestManager must be active to make requests.");
            
            return await instance.RequestAsyncInternal(request);
        }

        private async Task<ImageRequestTask> RequestAsyncInternal(ImageGenerationRequest request)
        {
            activeRequests.Add(request);
            TrackTask(request);
            await instance.ThreadContextCheck();
            request.State = RequestState.Queued;
            string url = request.Config.host + request.Config.jobEndpoint;
            return await ThreadContext.BackgroundTask<ImageRequestTask>(async () =>
            {
                var jsonContent = JsonConvert.SerializeObject(request);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                
                    httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                    httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", request.Config.apiKey);

                    try
                    {
                        _ = _threadCtx.Foreground(() => request.Events.OnRequestStarted.Invoke(request));
                        request.State = RequestState.Processing;
                        var responseMessage = await httpClient.PostAsync(url, httpContent);

                        if (responseMessage.IsSuccessStatusCode)
                        {
                            var responseContent = await responseMessage.Content.ReadAsStringAsync();
                            var response = JsonConvert.DeserializeObject<ImageJob>(responseContent);
                            response.Request = request;
                            request.State = RequestState.Complete;
                            activeRequests.Remove(request);
                            _ = _threadCtx.Foreground(() => request.Events.OnRequestSuccess.Invoke(request));
                            _ = _threadCtx.Foreground(() => request.Events.OnRequestCompleted.Invoke(request));
                            return new ImageRequestTask
                            {
                                response = response,
                                config = request.Config,
                            };
                        }

                        Debug.LogError($"Request {url} failed with status code {responseMessage.StatusCode}");
                        throw new Exception($"Request {url} failed with status code {responseMessage.StatusCode}");
                    }
                    catch (Exception e)
                    {
                        request.State = RequestState.Complete;
                        activeRequests.Remove(request);
                        _ = _threadCtx.Foreground(() => request.Events.OnRequestFailed.Invoke(request));
                        _ = _threadCtx.Foreground(() => request.Events.OnRequestCompleted.Invoke(request));
                        Debug.LogError($"Request {url} failed with exception {e.Message}");
                        throw e;
                    }
                    finally
                    {
                        UntrackTask(request);
                    }
            });
        }
        
        public static async Task<Texture2D[]> GetImages(ImageRequestTask requestTask)
        {
            if (!instance) throw new Exception("ImageGenRequestManager must be active to make requests.");
            return await instance.GetImagesInternal(requestTask);
        }
        
        public async Task<Texture2D[]> GetImagesInternal(ImageRequestTask requestTask)
        {
            TrackTask(requestTask);
            requestTask.canceled = false;
            var textures = Array.Empty<Texture2D>();
            var time = 0f;
            while (!requestTask.response.Status.IsFinished() && time < maxWaitTime)
            {
                if (requestTask.canceled)
                {
                    requestTask.Events.OnRequestCancelled?.Invoke(requestTask);
                    return null;
                }
                await Task.Delay(requestTask.statusDelayMs);
                time += 5;

                await ImageGenerationRequest.UpdateStatusAsync(requestTask.response);
            }

            if (requestTask.response.Status.IsFinished())
            {
                if (requestTask.canceled)
                {
                    requestTask.Events.OnRequestCancelled?.Invoke(requestTask);
                    return null;
                }
                textures = await _threadCtx.FetchTextureAsync(requestTask.GetImageUrls());
            }
            UntrackTask(requestTask);
            return textures;
        }
    }
}