using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using TextToSqlQuery.Models.Analyze;
using TextToSqlQuery.Models.Database;
using TextToSqlQuery.Services;

namespace TextToSqlQuery.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DatabaseAnalyzerController : ControllerBase
    {
        private readonly QueryAnalyzerService _analyzerService;
        private readonly DatabaseService _databaseService;
        private readonly ILogger<DatabaseAnalyzerController> _logger;
        private readonly IConfiguration _configuration;

        public DatabaseAnalyzerController(
            QueryAnalyzerService analyzerService,
            DatabaseService databaseService,
            IConfiguration configuration,
            ILogger<DatabaseAnalyzerController> logger)
        {
            _analyzerService = analyzerService;
            _databaseService = databaseService;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("analyze")]
        public async Task<ActionResult<AnalyzeResponse>> Analyze([FromBody] string prompt)
        {
            // Read values from appsettings.json
            var connectionString = _configuration["AnalyzeSettings:ConnectionString"];
            var ollamaUrl = _configuration["AnalyzeSettings:OllamaUrl"];
            var model = _configuration["AnalyzeSettings:Model"];

            // Build your request object
            var request = new AnalyzeRequest
            {
                Prompt = prompt,
                ConnectionString = connectionString ?? string.Empty,
                OllamaUrl = ollamaUrl,
                Model = model
            };
            if (string.IsNullOrEmpty(request.ConnectionString))
            {
                return BadRequest(new AnalyzeResponse
                {
                    Success = false,
                    Error = "Connection string is required"
                });
            }

            if (string.IsNullOrEmpty(request.Prompt))
            {
                return BadRequest(new AnalyzeResponse
                {
                    Success = false,
                    Error = "Prompt is required"
                });
            }

            var result = await _analyzerService.AnalyzeAsync(request);

            if (!result.Success)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

        [HttpPost("test-connection")]
        public async Task<ActionResult<object>> TestConnection([FromBody] DatabaseConnection connection)
        {
            var isConnected = await _databaseService.TestConnectionAsync(connection.ConnectionString);

            return Ok(new
            {
                success = isConnected,
                message = isConnected ? "Connection successful" : "Connection failed"
            });
        }

        [HttpPost("get-schema")]
        public async Task<ActionResult<DatabaseSchema>> GetSchema([FromBody] DatabaseConnection connection)
        {
            try
            {
                var schema = await _databaseService.GetDatabaseSchemaAsync(connection.ConnectionString);
                return Ok(schema);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting schema");
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
