using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DoubTech.AI.Art.Threading
{
    public class ThreadContext
    {
        private SynchronizationContext mainThreadContext;
        private MonoBehaviour parent;
        
        public ThreadContext(MonoBehaviour parent)
        {
            // If not on the main thread throw an exception
            if (SynchronizationContext.Current == null)
            {
                throw new Exception("ThreadUtils must be initialized on the main thread.");
            }
            mainThreadContext = SynchronizationContext.Current;
        }


        public async Task<T> Background<T>(Func<Task<T>> func) => await BackgroundTask(func);
        
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

        public async Task Background(Func<Task> func) => await BackgroundTask(func);
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

        public Task<bool> Foreground(Action action)
        {
            var tcs = new TaskCompletionSource<bool>();

            // Post the action to the main thread context
            mainThreadContext.Post(_ =>
            {
                try
                {
                    action();
                    tcs.SetResult(true);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                    tcs.SetResult(false);
                    Debug.LogError(e);
                }
            }, null);

            return tcs.Task;
        }

        public Task<T> Foreground<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>();

            // Post the action to the main thread context
            mainThreadContext.Post(_ =>
            {
                try
                {
                    T val = func();
                    tcs.SetResult(val);
                }
                catch (Exception e)
                {
                    tcs.SetException(e);
                    Debug.LogError(e);
                }
            }, null);

            return tcs.Task;
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
                    var texture = await Foreground<Texture2D>(() =>
                    {
                        Texture2D texture = new Texture2D(2, 2);
                        texture.LoadImage(imageBytes);
                        return texture;
                    });
                    textures[i] = texture;
                }
            }
            return textures;
        }
    }
}