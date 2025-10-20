using TextToSqlQuery.Models.Database;

namespace TextToSqlQuery.Models.Analyze
{
    public class AnalyzeResponse
    {
        public bool Success { get; set; }
        public string? GeneratedQuery { get; set; }
        public List<Dictionary<string, object>>? Data { get; set; }
        public string? Analysis { get; set; }
        public string? Error { get; set; }
        public DatabaseSchema? Schema { get; set; }
    }
}
