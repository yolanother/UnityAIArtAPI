using DoubTech.ThirdParty.AI.Common.Attributes;
using UnityEngine;

namespace DoubTech.AI.Art.Data
{
    [CreateAssetMenu(fileName = "ImageGenConfig", menuName = "DoubTech/AI APIs/Config/DoubTech.ai Image Generation", order = 0)]
    public class GenerativeAIConfig : ScriptableObject
    {
        public string endpoint = "https://api.aiart.doubtech.com/art-api/job";
        
        [Password]
        public string apiKey;
    }
}