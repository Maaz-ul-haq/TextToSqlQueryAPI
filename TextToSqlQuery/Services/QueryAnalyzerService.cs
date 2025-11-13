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
            // build the table in to string details
            var schemaDescription = BuildSchemaDescription(schema);

            var fullPrompt = $@"You are an expert SQL Server database assistant. Your ONLY job is to generate a valid SQL query.

                DATABASE SCHEMA:
                {schemaDescription}

                USER QUESTION: {prompt}

                CRITICAL RULES - READ CAREFULLY:
                1. Output ONLY the SQL query - nothing else
                2. No explanations, no markdown, no commentary
                3. Use EXACT table and column names from the schema above
                4. Use SQL Server syntax (TOP instead of LIMIT)
                5. Always use proper JOINs with ON clauses
                6. Include WHERE clauses for filtering
                7. Use aggregate functions (SUM, COUNT, AVG) when asking for totals or averages
                8. Use ORDER BY when asking for 'top' or 'highest' or 'lowest'
                9. Use GROUP BY when using aggregate functions with non-aggregated columns

                EXAMPLES:
                Question: ""Show top 5 customers by revenue""
                Answer: SELECT TOP 5 CustomerID, CustomerName, SUM(OrderTotal) AS Revenue FROM Customers JOIN Orders ON Customers.CustomerID = Orders.CustomerID GROUP BY CustomerID, CustomerName ORDER BY Revenue DESC

                Question: ""How many orders in 2024""
                Answer: SELECT COUNT(*) AS OrderCount FROM Orders WHERE YEAR(OrderDate) = 2024

                Question: ""Average product price by category""
                Answer: SELECT CategoryName, AVG(Price) AS AvgPrice FROM Products JOIN Categories ON Products.CategoryID = Categories.CategoryID GROUP BY CategoryName

                NOW GENERATE THE SQL QUERY FOR THE USER'S QUESTION.
                REMEMBER: Output ONLY the SQL query, starting with SELECT, INSERT, UPDATE, or DELETE:";

            // Send the request to Ollama to generate query
            var sqlResponse = await _ollamaService.GenerateAsync(ollamaUrl, model, fullPrompt);

            // Clean up the response
            var cleanedQuery = CleanSqlQuery(sqlResponse);

            // Validate the query
            if (!IsValidSqlQuery(cleanedQuery))
            {
                _logger.LogWarning($"Generated invalid query, retrying... Original: {cleanedQuery}");

                // Retry with stricter prompt
                var retryPrompt = $@"GENERATE ONLY A VALID SQL QUERY. NO EXPLANATIONS.

                    Schema: {schemaDescription}
                    Question: {prompt}

                    Output format: SELECT ... FROM ... WHERE ...
                    Start your response with SELECT:";

                sqlResponse = await _ollamaService.GenerateAsync(ollamaUrl, model, retryPrompt);
                cleanedQuery = CleanSqlQuery(sqlResponse);
            }

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

            // Calculate statistics for better analysis
            var stats = new StringBuilder();
            stats.AppendLine($"Total Rows: {data.Count}");

            if (data.Any())
            {
                var firstRow = data.First();
                foreach (var key in firstRow.Keys)
                {
                    var values = data.Select(r => r[key]).Where(v => v != null && v != DBNull.Value).ToList();
                    if (values.Any())
                    {
                        if (IsNumeric(values.First()))
                        {
                            var numValues = values.Select(v => Convert.ToDouble(v)).ToList();
                            stats.AppendLine($"- {key}: Min={numValues.Min():N2}, Max={numValues.Max():N2}, Avg={numValues.Average():N2}");
                        }
                    }
                }
            }

            var analysisPrompt = $@"You are an expert data analyst. Analyze the query results and provide clear insights.

                CONTEXT:
                - User Question: ""{originalPrompt}""
                - SQL Query: {sqlQuery}
                - Total Records: {data.Count}

                DATA STATISTICS:
                {stats}

                SAMPLE DATA (first 5 rows):
                {dataJson}

                TASK:
                Provide a professional analysis in 2-3 paragraphs that:

                1. DIRECTLY ANSWERS the user's original question with specific numbers/facts from the data
                2. Highlights the most important insights and patterns
                3. Mentions any notable trends, outliers, or interesting findings
                4. Uses simple, non-technical language
                5. Is concise but informative (maximum 150 words)

                IMPORTANT:
                - Start with the direct answer to their question
                - Use actual numbers from the data
                - Be specific, not generic
                - Don't explain SQL or technical details
                - Focus on business insights

                Your Analysis:";

            var analysis = await _ollamaService.GenerateAsync(ollamaUrl, model, analysisPrompt);
            return analysis.Trim();
        }


        #region Helper

        // Here we convert the table details in to description string
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


        private bool IsValidSqlQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return false;

            var upperQuery = query.Trim().ToUpper();

            // Must start with valid SQL keyword
            var validStarts = new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "WITH" };
            if (!validStarts.Any(k => upperQuery.StartsWith(k))) return false;

            // For SELECT queries, must have FROM
            if (upperQuery.StartsWith("SELECT") && !upperQuery.Contains("FROM"))
                return false;

            // Should not contain explanatory phrases
            var invalidPhrases = new[] { "here is", "this query", "explanation", "note that", "this will" };
            if (invalidPhrases.Any(p => upperQuery.Contains(p.ToUpper())))
                return false;

            return true;
        }

        private bool IsNumeric(object value)
        {
            return value is int || value is long || value is float || value is double || value is decimal;
        }

        #endregion

    }
}
