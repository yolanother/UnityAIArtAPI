using UnityEngine;
using UnityEngine.UI;

namespace DoubTech.AI.Art
{
    public class GenerativeRawImage : BaseGenerativeImage
    {
        [Header("Target")]
        [SerializeField] private RawImage[] rawImages;
        
        protected override void OnTexturesReady(Texture2D[] textures)
        {
            for (int i = 0; i < rawImages.Length; i++)
            {
                rawImages[i].texture = textures[i % textures.Length];
            }
        }
    }
}