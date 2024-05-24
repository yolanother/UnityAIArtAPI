using UnityEngine;
using UnityEngine.UI;

namespace DoubTech.AI.Art
{
    public class GenerativeRawImage : BaseGenerativeImage
    {
        [Header("Target")]
        [SerializeField] private RawImage[] rawImages;
        
        protected override void OnTextureReady(Texture2D texture)
        {
            foreach (var image in rawImages)
            {
                image.texture = texture;
            }
        }
    }
}