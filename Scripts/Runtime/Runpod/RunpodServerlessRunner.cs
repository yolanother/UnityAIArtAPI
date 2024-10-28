using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;

namespace DoubTech.AI.Art.Runpod
{
    /// <summary>
    /// MonoBehaviour that uses RunpodRequestHandler to generate images at runtime
    /// and update UI elements with the generated textures.
    /// </summary>
    public class RunpodServerlessRunner : MonoBehaviour
    {
        [Header("Configuration")]
        public RunpodConfig config;

        [Header("UI Elements")]
        public RawImage[] rawImages;

        private SynchronizationContext unitySynchronizationContext;

        async void Start()
        {
            // Capture the Unity synchronization context
            unitySynchronizationContext = SynchronizationContext.Current;

            // Create an instance of the handler
            var requestHandler = new RunpodRequestHandler(config, unitySynchronizationContext);

            // Start the request and await the result
            var (texture, jobId) = await requestHandler.RunRequestWithJobId();

            // Update the UI elements
            foreach (var rawImage in rawImages)
            {
                if (rawImage != null)
                {
                    rawImage.texture = texture;
                    rawImage.SetNativeSize();
                }
            }

            Debug.Log("Images updated with generated texture.");
        }
    }
}