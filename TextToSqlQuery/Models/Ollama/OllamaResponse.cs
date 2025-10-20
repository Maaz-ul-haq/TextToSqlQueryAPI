namespace TextToSqlQuery.Models.Ollama
{
    public class OllamaResponse
    {
        public string model { get; set; } = string.Empty;
        public string response { get; set; } = string.Empty;
        public bool done { get; set; }
    }
}
