using UnityEngine;
using UnityEngine.UI;

namespace DoubTech.AI.Art
{
    public class GenerativeRawImage : BaseGenerativeImage
    {
        [Header("Target")]
        [SerializeField] private RawImage rawImage;
        
        protected override void OnTextureReady(Texture2D texture)
        {
            rawImage.texture = texture;
        }
    }
}