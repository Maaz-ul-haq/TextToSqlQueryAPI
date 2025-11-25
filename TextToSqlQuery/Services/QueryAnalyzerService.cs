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

        public QueryAnalyzerService(OllamaService ollamaService,DatabaseService databaseService, ILogger<QueryAnalyzerService> logger)
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

        private async Task<string> GenerateSqlQueryAsync(string ollamaUrl, string model,string prompt,DatabaseSchema schema)
        {
            // build the table in to string details
            var schemaDescription = BuildSchemaDescription(schema);

            var fullPrompt = $@"
                You are an expert SQL Server database assistant. 
                Your ONLY job is to generate a valid and optimized SQL Server query based on the user's question and the provided schema.

                DATABASE SCHEMA:
                {schemaDescription}

                USER QUESTION:
                {prompt}

                STRICT INSTRUCTIONS (READ CAREFULLY):
                1. Output ONLY a single valid SQL Server query — no explanations, no comments, no markdown, no text outside the query.
                2. Always use the **exact table and column names** from the provided schema.
                3. Always use **SQL Server syntax** (e.g., TOP instead of LIMIT, GETDATE() instead of NOW()).
                4. Always include **JOINs with ON clauses** when referencing multiple tables.
                5. Always include a **WHERE clause** when the question implies filtering (e.g., by year, date, name, status, etc.).
                6. Always include an **ORDER BY** clause when the question mentions top, highest, lowest, recent, latest, or oldest.
                7. Always include **GROUP BY** when using aggregate functions (SUM, COUNT, AVG, MAX, MIN) alongside non-aggregated columns.
                8. Use **aggregate aliases** like TotalSales, AvgPrice, OrderCount, etc., when appropriate.
                9. Handle plural words intelligently — e.g., 'customers' → Customers table, 'orders' → Orders table.
                10. Use **INNER JOIN** by default unless the context clearly implies LEFT JOIN (e.g., 'include customers with no orders').
                11. Use **meaningful column selections** — prefer descriptive names like CustomerName, OrderDate, ProductName, not just *.
                12. When filtering by date (e.g., “in 2024” or “last month”), use SQL Server date functions such as YEAR(), MONTH(), and DATEADD().
                13. If inserting, updating, or deleting, ensure proper syntax and reference to actual columns.

                EXAMPLES:
                User: Show top 5 customers by revenue
                SQL: SELECT TOP 5 c.CustomerID, c.CustomerName, SUM(o.OrderTotal) AS Revenue
                     FROM Customers c
                     JOIN Orders o ON c.CustomerID = o.CustomerID
                     GROUP BY c.CustomerID, c.CustomerName
                     ORDER BY Revenue DESC;

                User: How many orders were made in 2024
                SQL: SELECT COUNT(*) AS OrderCount FROM Orders WHERE YEAR(OrderDate) = 2024;

                User: Average product price by category
                SQL: SELECT cat.CategoryName, AVG(p.Price) AS AvgPrice
                     FROM Products p
                     JOIN Categories cat ON p.CategoryID = cat.CategoryID
                     GROUP BY cat.CategoryName;

                FINAL OUTPUT REQUIREMENTS:
                - Output must start directly with SELECT, INSERT, UPDATE, or DELETE.
                - Do not include explanations, commentary, markdown, or code fencing.
                - The query must be executable directly in SQL Server Management Studio.
                ";


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

            var analysisPrompt = $@"
                You are an expert business data analyst. Your task is to interpret SQL query results and provide clear, accurate insights in natural language.

                CONTEXT:
                - User Question: ""{originalPrompt}""
                - SQL Query: {sqlQuery}
                - Total Records Returned: {data.Count}

                DATA SUMMARY:
                {stats}

                SAMPLE DATA (First 5 Rows):
                {dataJson}

                YOUR TASK:
                Write a short professional analysis (2–3 paragraphs, up to 150 words) that:

                1. Directly answers the user's question using exact figures, counts, or percentages from the data.
                2. Highlights the most important insights, trends, or comparisons visible in the data.
                3. Points out any outliers, anomalies, or notable patterns (if relevant).
                4. Uses simple, business-friendly language (avoid technical or SQL jargon).
                5. Focuses on meaning and implications, not query logic.

                STYLE AND FORMAT:
                - Begin immediately with the answer to the user's question.
                - Be factual, specific, and concise.
                - Do NOT include SQL terms, code, or data column names.
                - Present insights as if explaining them to a business stakeholder.

                FINAL OUTPUT:
                A clear, well-structured written analysis only — no bullet points, no markdown, no code.

                Your Analysis:
                ";


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
