﻿using System;
using UnityEngine;

namespace DoubTech.AI.Art
{
    public class GenerativeTexture : BaseGenerativeImage
    {
        [Header("Target")]
        [SerializeField] private Renderer renderer;
        
        protected override void OnTextureReady(Texture2D texture)
        {
            renderer.material.mainTexture = texture;
        }
    }
}