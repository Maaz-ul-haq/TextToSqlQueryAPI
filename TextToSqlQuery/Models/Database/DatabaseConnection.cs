namespace TextToSqlQuery.Models.Database
{
    public class DatabaseConnection
    {
        public string ConnectionString { get; set; } = string.Empty;
        public string? OllamaUrl { get; set; }
        public string? Model { get; set; } = "llama3";
    }
}
