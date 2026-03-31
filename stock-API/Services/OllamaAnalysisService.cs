using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using stock_API.Models;

namespace stock_API.Services
{
    public class OllamaAnalysisService : IAiAnalysisService
    {
        private readonly HttpClient _httpClient;
        private readonly string _model;
        private readonly string _baseUrl;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public OllamaAnalysisService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _model = configuration["Ollama:Model"] ?? "llama3";
            _baseUrl = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
        }

        public async Task<AiAnalysisResult> AnalyzeAsync(string symbol, IEnumerable<IncomeStatement> statements)
        {
            var latest = statements.FirstOrDefault();
            if (latest == null)
                return new AiAnalysisResult { Symbol = symbol, Summary = "No data available." };

            var prompt = BuildPrompt(symbol, statements);
            var ollamaResponse = await CallOllamaAsync(prompt);
            return ParseResponse(symbol, ollamaResponse);
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private string BuildPrompt(string symbol, IEnumerable<IncomeStatement> statements)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You are a financial analyst. Analyze the following income statement data for {symbol} and respond ONLY with a valid JSON object — no extra text, no markdown code blocks.");
            sb.AppendLine();
            sb.AppendLine("Income Statements (most recent first):");

            foreach (var s in statements)
            {
                sb.AppendLine($"  Period: {s.Period} {s.CalendarYear}");
                sb.AppendLine($"    Revenue:        ${s.Revenue:N0}");
                sb.AppendLine($"    Gross Profit:   ${s.GrossProfit:N0}  ({s.GrossProfitRatio:P1})");
                sb.AppendLine($"    Operating Income: ${s.OperatingIncome:N0}  ({s.OperatingIncomeRatio:P1})");
                sb.AppendLine($"    Net Income:     ${s.NetIncome:N0}  ({s.NetIncomeRatio:P1})");
                sb.AppendLine($"    EPS:            {s.Eps:F2}  |  EPS Diluted: {s.EpsDiluted:F2}");
                sb.AppendLine($"    EBITDA:         ${s.Ebitda:N0}");
                sb.AppendLine();
            }

            sb.AppendLine("Return ONLY this JSON structure:");
            sb.AppendLine("{");
            sb.AppendLine("  \"summary\": \"<2-3 sentence executive summary of financial health>\",");
            sb.AppendLine("  \"riskAnalysis\": \"<key financial risks based on the data>\",");
            sb.AppendLine("  \"sentiment\": \"<Bullish | Neutral | Bearish>\",");
            sb.AppendLine("  \"recommendation\": \"<Buy | Hold | Sell>\"");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private async Task<string> CallOllamaAsync(string prompt)
        {
            var request = new OllamaChatRequest
            {
                Model = _model,
                Messages = [new OllamaMessage { Role = "user", Content = prompt }],
                Stream = false
            };

            var body = JsonSerializer.Serialize(request);
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var parsed = JsonSerializer.Deserialize<OllamaChatResponse>(json, JsonOptions);
            return parsed?.Message?.Content ?? string.Empty;
        }

        private AiAnalysisResult ParseResponse(string symbol, string rawText)
        {
            // Strip any markdown code fences the model may have added
            var cleaned = rawText
                .Replace("```json", "").Replace("```", "")
                .Trim();

            try
            {
                var parsed = JsonSerializer.Deserialize<LlmAnalysis>(cleaned, JsonOptions);
                if (parsed != null)
                {
                    return new AiAnalysisResult
                    {
                        Symbol = symbol,
                        Summary = parsed.Summary ?? string.Empty,
                        RiskAnalysis = parsed.RiskAnalysis ?? string.Empty,
                        Sentiment = parsed.Sentiment ?? string.Empty,
                        Recommendation = parsed.Recommendation ?? string.Empty,
                        Model = _model
                    };
                }
            }
            catch
            {
                // If the model didn't return valid JSON, return raw text as summary
            }

            return new AiAnalysisResult
            {
                Symbol = symbol,
                Summary = cleaned,
                Model = _model
            };
        }

        // ── Ollama API DTOs ──────────────────────────────────────────────────

        private class OllamaChatRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;

            [JsonPropertyName("messages")]
            public List<OllamaMessage> Messages { get; set; } = [];

            [JsonPropertyName("stream")]
            public bool Stream { get; set; } = false;
        }

        private class OllamaMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; } = string.Empty;

            [JsonPropertyName("content")]
            public string Content { get; set; } = string.Empty;
        }

        private class OllamaChatResponse
        {
            [JsonPropertyName("message")]
            public OllamaMessage? Message { get; set; }
        }

        private class LlmAnalysis
        {
            [JsonPropertyName("summary")]
            public string? Summary { get; set; }

            [JsonPropertyName("riskAnalysis")]
            public string? RiskAnalysis { get; set; }

            [JsonPropertyName("sentiment")]
            public string? Sentiment { get; set; }

            [JsonPropertyName("recommendation")]
            public string? Recommendation { get; set; }
        }
    }
}
