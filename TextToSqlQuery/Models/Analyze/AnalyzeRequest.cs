namespace TextToSqlQuery.Models.Analyze
{
    public class AnalyzeRequest
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public string? OllamaUrl { get; set; }
        public string? Model { get; set; } = "llama3";
    }
}
