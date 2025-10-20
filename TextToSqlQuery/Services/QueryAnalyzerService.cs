using System.Text;
using System.Text.RegularExpressions;
using TextToSqlQuery.Models.Analyze;
using TextToSqlQuery.Models.Database;

namespace TextToSqlQuery.Services
{
    public class QueryAnalyzerService
    {
        private readonly OllamaService _ollamaService;
        private readonly DatabaseService _databaseService;
        private readonly ILogger<QueryAnalyzerService> _logger;

        public QueryAnalyzerService(
            OllamaService ollamaService,
            DatabaseService databaseService,
            ILogger<QueryAnalyzerService> logger)
        {
            _ollamaService = ollamaService;
            _databaseService = databaseService;
            _logger = logger;
        }

        public async Task<AnalyzeResponse> AnalyzeAsync(AnalyzeRequest request)
        {
            var response = new AnalyzeResponse();

            try
            {
                // Test connection
                var isConnected = await _databaseService.TestConnectionAsync(request.ConnectionString);
                if (!isConnected)
                {
                    response.Error = "Failed to connect to database. Check your connection string.";
                    return response;
                }

                // Get database schema
                var schema = await _databaseService.GetDatabaseSchemaAsync(request.ConnectionString);
                response.Schema = schema;

                // Generate SQL query using Ollama
                var sqlQuery = await GenerateSqlQueryAsync(
                    request.OllamaUrl ?? "http://localhost:11434",
                    request.Model ?? "llama3",
                    request.Prompt,
                    schema
                );

                response.GeneratedQuery = sqlQuery;

                // Execute the query
                var data = await _databaseService.ExecuteQueryAsync(request.ConnectionString, sqlQuery);
                response.Data = data;

                // Analyze the results
                var analysis = await AnalyzeResultsAsync(
                    request.OllamaUrl ?? "http://localhost:11434",
                    request.Model ?? "llama3",
                    request.Prompt,
                    sqlQuery,
                    data
                );

                response.Analysis = analysis;
                response.Success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during analysis");
                response.Error = ex.Message;
                response.Success = false;
            }

            return response;
        }

        private async Task<string> GenerateSqlQueryAsync(
            string ollamaUrl,
            string model,
            string prompt,
            DatabaseSchema schema)
        {
            var schemaDescription = BuildSchemaDescription(schema);

            var fullPrompt = $@"You are a SQL expert. Generate ONLY a valid SQL Server query based on the user's request.

Database Schema:
{schemaDescription}

User Request: {prompt}

Important Rules:
1. Return ONLY the SQL query, no explanations
2. Use proper SQL Server syntax
3. Use appropriate JOINs when querying multiple tables
4. Include WHERE clauses when filtering is needed
5. Use TOP if limiting results
6. Do not include markdown formatting or code blocks
7. Start directly with SELECT, INSERT, UPDATE, or DELETE

SQL Query:";

            var sqlResponse = await _ollamaService.GenerateAsync(ollamaUrl, model, fullPrompt);

            // Clean up the response
            var cleanedQuery = CleanSqlQuery(sqlResponse);

            return cleanedQuery;
        }

        private async Task<string> AnalyzeResultsAsync(
            string ollamaUrl,
            string model,
            string originalPrompt,
            string sqlQuery,
            List<Dictionary<string, object>> data)
        {
            var dataPreview = data.Take(5).ToList();
            var dataJson = System.Text.Json.JsonSerializer.Serialize(dataPreview,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            var analysisPrompt = $@"You are a data analyst. Analyze the following query results and provide insights.

Original Question: {originalPrompt}

SQL Query Executed:
{sqlQuery}

Results (showing first 5 rows):
{dataJson}

Total Rows: {data.Count}

Provide a clear, concise analysis that:
1. Summarizes the key findings
2. Answers the original question
3. Highlights any interesting patterns or insights
4. Uses plain language that non-technical users can understand

Analysis:";

            var analysis = await _ollamaService.GenerateAsync(ollamaUrl, model, analysisPrompt);
            return analysis;
        }

        private string BuildSchemaDescription(DatabaseSchema schema)
        {
            var sb = new StringBuilder();

            foreach (var table in schema.Tables)
            {
                sb.AppendLine($"\nTable: {table.TableName}");
                sb.AppendLine("Columns:");

                foreach (var column in table.Columns)
                {
                    var pk = column.IsPrimaryKey ? " [PRIMARY KEY]" : "";
                    var nullable = column.IsNullable ? "NULL" : "NOT NULL";
                    sb.AppendLine($"  - {column.ColumnName} ({column.DataType}, {nullable}){pk}");
                }
            }

            return sb.ToString();
        }

        private string CleanSqlQuery(string query)
        {
            // Remove markdown code blocks
            query = Regex.Replace(query, @"```sql\s*", "", RegexOptions.IgnoreCase);
            query = Regex.Replace(query, @"```\s*", "", RegexOptions.IgnoreCase);

            // Remove leading/trailing whitespace
            query = query.Trim();

            // Remove any explanatory text before SELECT/INSERT/UPDATE/DELETE
            var sqlKeywords = new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "WITH" };
            foreach (var keyword in sqlKeywords)
            {
                var index = query.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                if (index > 0)
                {
                    query = query.Substring(index);
                    break;
                }
            }

            return query;
        }
    }
}
