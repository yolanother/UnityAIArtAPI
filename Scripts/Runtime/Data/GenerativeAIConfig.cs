using System;
using DoubTech.ThirdParty.AI.Common.Attributes;
using UnityEngine;

namespace DoubTech.AI.Art.Data
{
    [CreateAssetMenu(fileName = "ImageGenConfig", menuName = "DoubTech/AI APIs/Config/DoubTech.ai Image Generation", order = 0)]
    public class GenerativeAIConfig : ScriptableObject
    {
        [Header("Host Configuration")]
        public string host = "https://api.aiart.doubtech.com";
        public string jobEndpoint = "/art-api/job";
        public string statusEndpoint = "/art-api/status";
        
        [Header("Authentication")]
        [Password]
        public string apiKey;

        [Header("Configuration Details")]
        public string displayName;
        public string briefDescription;
        [TextArea] public string description;

        private string _name;
        public string Name => _name;
        private void OnEnable()
        {
            _name = name;
        }
    }
}