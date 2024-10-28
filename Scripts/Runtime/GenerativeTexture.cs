using System;
using UnityEngine;

namespace DoubTech.AI.Art
{
    public class GenerativeTexture : BaseGenerativeImage
    {
        [Header("Target")]
        [SerializeField] private Renderer renderer;

        protected override void OnTextureReady(int index, Texture2D texture)
        {
            if(index == 0) renderer.material.mainTexture = texture;
        }
    }
}