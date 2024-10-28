using Newtonsoft.Json;

namespace DoubTech.AI.Art.Data
{
    public class GeneratedImage
    {
        [JsonProperty("filename")]
        public string Filename { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("subfolder")]
        public string Subfolder { get; set; }
    }
}