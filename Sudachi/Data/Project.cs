using System.Text.Json.Serialization;

namespace Sudachi.Data
{
    public class ProjectContainer
    {
        [JsonPropertyName("projects")]
        public Project[] Projects { set; get; }
    }

    public class Project
    {
        [JsonPropertyName("name")]
        public string Name { set; get; }

        [JsonPropertyName("description")]
        public string Description { set; get; }

        [JsonPropertyName("type")]
        public string Type { set; get; }

        [JsonPropertyName("asset")]
        public string Asset { set; get; }

        [JsonPropertyName("gameGenre")]
        public string GameGenre { set; get; }

        [JsonPropertyName("contentWarnings")]
        public string[] ContentWarnings { set; get; }

        [JsonPropertyName("baseFolder")]
        public string BaseFolder { set; get; }

        [JsonPropertyName("preview")]
        public string Preview { set; get; }

        [JsonPropertyName("links")]
        public LinkInfo[] Links { set; get; }

        [JsonPropertyName("members")]
        public string[] Members { set; get; }
    }

    public class LinkInfo
    {
        [JsonPropertyName("name")]
        public string Name { set; get; }

        [JsonPropertyName("content")]
        public string Content { set; get; }
    }
}
