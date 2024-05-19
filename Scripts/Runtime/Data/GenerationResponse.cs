using DoubTech.AI.Art.Requests;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DoubTech.AI.Art.Data
{
    public class GenerationResponse
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

        [JsonIgnore]
        public ImageGenerationRequest Request { get; set; }
    }
    
    public enum GenerationStatus
    {
        Queued,
        Processing,
        Failed,
        Complete
    }
    
    public static class GenerationStatusExtensions
    {
        public static bool IsFinished(this GenerationStatus status)
        {
            return status == GenerationStatus.Complete || status == GenerationStatus.Failed;
        }
    }
}