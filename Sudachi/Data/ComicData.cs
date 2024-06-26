using System.Text.Json.Serialization;

namespace Sudachi.Data
{
    public class ComicContainer
    {
        [JsonPropertyName("metadata")]
        public ComicData Metadata { set; get; }

        [JsonPropertyName("id")]
        public string Id { set; get; }
    }

    public class ComicData
    {
        [JsonPropertyName("name")]
        public string Name { set; get; }

        [JsonPropertyName("description")]
        public string Description { set; get; }

        [JsonPropertyName("preview")]
        public string Preview { set; get; }

        [JsonPropertyName("contentWarnings")]
        public string[] ContentWarnings { set; get; }

        [JsonPropertyName("members")]
        public string[] Members { set; get; }
    }
}
