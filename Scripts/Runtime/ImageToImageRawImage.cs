using DoubTech.AI.Art.Data;
using DoubTech.AI.Art.Requests;
using DoubTech.AI.Art.Threading;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DoubTech.AI.Art
{
    public class ImageToImageRawImage : MonoBehaviour
    {
        [SerializeField] private GenerativeAIConfig config;
        [SerializeField] private RawImage rawImage;
        [SerializeField] private GameObject loadingUI;
        [SerializeField] private string prompt;
        [SerializeField] private bool updateSourceImage = true;

        [Header("Events")]
        [SerializeField] private UnityEvent onStartProcessingImage = new UnityEvent();
        [SerializeField] private UnityEvent<Texture2D> onImageProcessed = new UnityEvent<Texture2D>();
        
        private ThreadContext _threadCtx;
        
        public string Prompt
        {
            get => prompt;
            set => prompt = value;
        }

        private void Awake()
        {
            _threadCtx = new ThreadContext(this);
        }
        
        public void ProcessImage()
        {
            ProcessImage((Texture2D) rawImage.texture);
        }

        public void ProcessImage(Texture2D texture)
        {
            onStartProcessingImage.Invoke();
            _ = _threadCtx.Background(async () =>
            {
                var request = new ImageGenerationRequest();
                request.Config = config;
                request.Prompt = Prompt;
                await _threadCtx.Foreground(() =>
                {
                    request.SetImage(texture);
                });
                var removedTask = await ImageGenRequestManager.RequestAsync(request);
                var removedImages = await ImageGenRequestManager.GetImages(removedTask);
                _ = _threadCtx.Foreground(() =>
                {
                    if(updateSourceImage) rawImage.texture = removedImages[0];
                    onImageProcessed?.Invoke(removedImages[0]);
                });
            });
        }
    }
}