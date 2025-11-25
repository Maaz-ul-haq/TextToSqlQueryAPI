using System.Text;
using System.Text.Json;
using TextToSqlQuery.Models.Ollama;

namespace TextToSqlQuery.Services
{
    public class OllamaService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OllamaService> _logger;

        public OllamaService(HttpClient httpClient, ILogger<OllamaService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }


        public async Task<string> GenerateAsync(string ollamaUrl, string model, string prompt)
        {
            try
            {
                var request = new OllamaRequest
                {
                    model = model,
                    prompt = prompt,
                    stream = false
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{ollamaUrl}/api/generate", content);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseContent);

                return ollamaResponse?.response ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Ollama API");
                throw new Exception($"Failed to connect to Ollama: {ex.Message}");
            }
        }
    }
}
