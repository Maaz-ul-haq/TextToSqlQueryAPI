namespace TextToSqlQuery.Models.Ollama
{
    public class OllamaRequest
    {
        public string model { get; set; } = string.Empty;
        public string prompt { get; set; } = string.Empty;
        public bool stream { get; set; } = false;
    }
}
