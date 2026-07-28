using System.Text.Json.Serialization;

namespace PosPrintService.Models
{
    public class PrintRequestEnvelope
    {
        [JsonPropertyName("DocumentType")]
        public string? DocumentType { get; set; }

        [JsonPropertyName("JobId")]
        public string? JobId { get; set; }
    }
}
