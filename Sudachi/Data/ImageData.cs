using System.Text.Json.Serialization;

namespace Sudachi.Data
{
    public class ImageData
    {
        [JsonPropertyName("id")]
        public string Id { set; get; }

        [JsonPropertyName("format")]
        public string Format { set; get; }

        [JsonPropertyName("parent")]
        public string Parent { set; get; }

        [JsonPropertyName("author")]
        public string Author { set; get; }

        [JsonPropertyName("rating")]
        public int Rating { set; get; }

        [JsonPropertyName("text")]
        public Text Text { set; get; }

        [JsonPropertyName("tags")]
        public Tag Tags { set; get; }

        [JsonPropertyName("comment")]
        public string Comment { set; get; }

        [JsonPropertyName("title")]
        public string Title { set; get; }

        [JsonPropertyName("tags_cleaned")]
        public CleanedTagContainer TagsCleaned { set; get; }

        [JsonPropertyName("isCanon")]
        public bool? IsCanon { set; get; }
    }

    public class CleanedTagContainer
    {
        [JsonPropertyName("authors")]
        public CountInfo[] Authors { set; get; }

        [JsonPropertyName("names")]
        public CountInfo[] Names { set; get; }

        [JsonPropertyName("parodies")]
        public CountInfo[] Parodies { set; get; }

        [JsonPropertyName("others")]
        public CountInfo[] Others { set; get; }
    }

    public class CountInfo
    {
        [JsonPropertyName("name")]
        public string Name { set; get; }

        [JsonPropertyName("count")]
        public int Count { set; get; }
    }

    public class CleanedTag
    {
        [JsonPropertyName("names")]
        public CountInfo[] Names { set; get; }

        [JsonPropertyName("authors")]
        public CountInfo[] Authors { set; get; }
    }

    public class Tag
    {
        [JsonPropertyName("parodies")]
        public string[] Parodies { set; get; }

        [JsonPropertyName("characters")]
        public string[] Characters { set; get; }

        [JsonPropertyName("others")]
        public string[] Others { set; get; }
    }

    public class Text
    {
        [JsonPropertyName("lang")]
        public string Language { set; get; }

        [JsonPropertyName("content")]
        public string[] Content { set; get; }
    }
}
