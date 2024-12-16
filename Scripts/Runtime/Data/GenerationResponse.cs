using System.Collections.Generic;
using DoubTech.AI.Art.Requests;
using Newtonsoft.Json;

namespace DoubTech.AI.Art.Data
{
    public class ImageJob
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("status")]
        [JsonConverter(typeof(CaseInsensitiveStringEnumConverter))]
        public GenerationStatus Status { get; set; }

        [JsonProperty("processor")]
        public string Processor { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("image-count")]
        public int ImageCount { get; set; }

        [JsonProperty("images")]
        public List<GeneratedImage> Images { get; set; }

        [JsonIgnore]
        public ImageGenerationRequest Request { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; } = null;
    }

    public enum GenerationStatus
    {
        Queued,
        Processing,
        Failed,
        Complete
    }

    public static class ImageJobExtensions
    {
        public static bool IsFinished(this GenerationStatus status)
        {
            return status == GenerationStatus.Complete || status == GenerationStatus.Failed;
        }
    }
}