using System.Text.Json.Serialization;

namespace Sudachi.Data
{
    public class ImageTagData
    {
        [JsonPropertyName("images")]
        public List<string> Images { set; get; }

        [JsonPropertyName("definition")]
        public string Definition { set; get; }
    }
}
