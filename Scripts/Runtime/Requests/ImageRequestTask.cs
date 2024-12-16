using System;
using System.Linq;
using System.Threading.Tasks;
using DoubTech.AI.Art.Data;
using DoubTech.AI.Art.Threading;
using UnityEngine;

namespace DoubTech.AI.Art.Requests
{
    public class ImageRequestTask : IGenerationTask
    {
        public GenerativeAIConfig config;
        public ImageJob response;
        public int statusDelayMs = 5000;
        
        internal bool canceled = false;
        private bool isFetchingImages;

        public string[] GetImageUrls()
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

        public string TaskName => response.Request.Config.Name;
        public string TaskDescription => $"Getting images from {TaskName}";
        public GenerationStatus Status => response.Status;
        
        public TaskEvents Events { get; } = new TaskEvents();
        public void Cancel()
        {
            if (!isFetchingImages) return;
            canceled = true;
        }
    }
}