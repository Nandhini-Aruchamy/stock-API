using System.Text;
using System.Text.Json;
using stock_API.Models;

namespace stock_API.Services
{
    /// <summary>
    /// Calls the Anthropic Claude Messages API with:
    ///  - web_search tool for live data
    ///  - prompt caching on the instruction file so input tokens are not re-billed
    ///    on every subsequent call for different stock symbols.
    /// </summary>
    public class ClaudeAnalysisService : IClaudeAnalysisService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly string _promptFilePath;
        private readonly ILogger<ClaudeAnalysisService> _logger;

        private const string ClaudeApiUrl = "https://api.anthropic.com/v1/messages";
        private const string AnthropicVersion = "2023-06-01";

        private static readonly object WebSearchTool = new
        {
            type = "web_search_20250305",
            name = "web_search",
            max_uses = 15
        };

        public ClaudeAnalysisService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<ClaudeAnalysisService> logger,
            IWebHostEnvironment env)
        {
            _httpClient = httpClient;
            _apiKey = configuration["Claude:ApiKey"]
                ?? throw new InvalidOperationException("Claude:ApiKey is not configured in appsettings.json.");
            _model = configuration["Claude:Model"] ?? "claude-sonnet-4-6";

            var configuredPath = configuration["Claude:PromptFilePath"];
            _promptFilePath = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(env.ContentRootPath, "Prompt", "stock_analysis_prompt.txt")
                : Path.IsPathRooted(configuredPath)
                    ? configuredPath
                    : Path.Combine(env.ContentRootPath, configuredPath);

            _logger = logger;

            _httpClient.DefaultRequestHeaders.Add("x-api-key", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);
            // Both betas in one header value
            _httpClient.DefaultRequestHeaders.Add("anthropic-beta",
                "web-search-2025-03-05,prompt-caching-2024-07-31");
        }

        // ── Public API ────────────────────────────────────────────────────────

        public async Task<StockAnalysis> AnalyzeStockAsync(string symbol)
        {
            symbol = symbol.ToUpper().Trim();

            var instruction = await LoadInstructionAsync();

            _logger.LogInformation("Starting Claude analysis for {Symbol}", symbol);
            var report = await RunAgenticLoopAsync(symbol, instruction);

            return new StockAnalysis
            {
                Symbol     = symbol,
                Report     = report,
                Model      = _model,
                AnalyzedAt = DateTime.UtcNow
            };
        }

        public async Task<string> AskAsync(string prompt)
        {
            var requestBody = new
            {
                model      = _model,
                max_tokens = 4096,
                messages   = new[] { new { role = "user", content = prompt } }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var httpResponse = await _httpClient.PostAsync(
                ClaudeApiUrl,
                new StringContent(json, Encoding.UTF8, "application/json"));

            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorBody = await httpResponse.Content.ReadAsStringAsync();
                _logger.LogError("Claude API error {Status}: {Body}", httpResponse.StatusCode, errorBody);
                throw new HttpRequestException(
                    $"Claude API returned {(int)httpResponse.StatusCode}: {errorBody}");
            }

            var responseJson = await httpResponse.Content.ReadAsStringAsync();
            using var doc    = JsonDocument.Parse(responseJson);

            if (doc.RootElement.TryGetProperty("content", out var content))
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                        && block.TryGetProperty("text", out var txt))
                        return txt.GetString() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        // ── Instruction loader ────────────────────────────────────────────────

        private async Task<string> LoadInstructionAsync()
        {
            if (File.Exists(_promptFilePath))
            {
                _logger.LogInformation("Loaded analyst prompt from {Path}", _promptFilePath);
                return await File.ReadAllTextAsync(_promptFilePath);
            }

            _logger.LogWarning("Prompt file not found at {Path}. Using fallback.", _promptFilePath);
            return "You are a senior equity research analyst.";
        }

        // ── Agentic loop ──────────────────────────────────────────────────────

        private async Task<string> RunAgenticLoopAsync(string symbol, string instruction)
        {
            // ── Prompt caching ────────────────────────────────────────────────
            // The instruction file is placed in the `system` block with
            // cache_control: ephemeral.  Anthropic caches it for 5 minutes.
            // Every call for any stock symbol reuses the cache — the large
            // instruction tokens are only billed once per cache window.
            var system = new[]
            {
                new
                {
                    type = "text",
                    text = instruction,
                    cache_control = new { type = "ephemeral" }
                }
            };

            // User message is only the stock symbol — tiny, never cached
            var messages = new List<object>
            {
                new { role = "user", content = $"STOCK TO ANALYZE: {symbol}" }
            };

            const int maxIterations = 20;

            for (int i = 0; i < maxIterations; i++)
            {
                var requestBody = new
                {
                    model      = _model,
                    max_tokens = 8192,
                    system,
                    tools      = new[] { WebSearchTool },
                    messages
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpResponse = await _httpClient.PostAsync(
                    ClaudeApiUrl,
                    new StringContent(json, Encoding.UTF8, "application/json"));

                if (!httpResponse.IsSuccessStatusCode)
                {
                    var errorBody = await httpResponse.Content.ReadAsStringAsync();
                    _logger.LogError("Claude API error {Status}: {Body}", httpResponse.StatusCode, errorBody);
                    throw new HttpRequestException(
                        $"Claude API returned {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}: {errorBody}");
                }

                var responseJson = await httpResponse.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                var stopReason = root.TryGetProperty("stop_reason", out var srProp)
                    ? srProp.GetString() : null;

                // Log cache usage on first iteration so you can verify caching works
                if (i == 0 && root.TryGetProperty("usage", out var usage))
                {
                    var cacheRead    = usage.TryGetProperty("cache_read_input_tokens", out var cr) ? cr.GetInt32() : 0;
                    var cacheCreated = usage.TryGetProperty("cache_creation_input_tokens", out var cc) ? cc.GetInt32() : 0;
                    _logger.LogInformation(
                        "Claude token usage — cache_read: {Read}, cache_created: {Created}",
                        cacheRead, cacheCreated);
                }

                _logger.LogInformation("Claude iteration {Iter}: stop_reason={Stop}", i + 1, stopReason);

                var contentArray = root.TryGetProperty("content", out var caProp)
                    ? JsonSerializer.Deserialize<JsonElement>(caProp.GetRawText())
                    : (JsonElement?)null;

                // Done — return the full text report
                if (stopReason == "end_turn")
                {
                    if (contentArray.HasValue)
                    {
                        foreach (var block in contentArray.Value.EnumerateArray())
                        {
                            if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                                && block.TryGetProperty("text", out var txt))
                                return txt.GetString() ?? string.Empty;
                        }
                    }
                    return string.Empty;
                }

                // Claude is running web searches — continue the loop
                if (stopReason == "tool_use" && contentArray.HasValue)
                {
                    messages.Add(new { role = "assistant", content = contentArray.Value });

                    var toolResults = new List<object>();
                    foreach (var block in contentArray.Value.EnumerateArray())
                    {
                        if (block.TryGetProperty("type", out var bt) && bt.GetString() == "tool_use"
                            && block.TryGetProperty("id", out var idProp))
                        {
                            toolResults.Add(new
                            {
                                type        = "tool_result",
                                tool_use_id = idProp.GetString() ?? "",
                                content     = ""
                            });
                        }
                    }

                    if (toolResults.Count > 0)
                        messages.Add(new { role = "user", content = toolResults });

                    continue;
                }

                _logger.LogWarning("Unexpected stop_reason '{Stop}' on iteration {Iter}", stopReason, i + 1);
                break;
            }

            throw new InvalidOperationException("Claude analysis did not complete within the maximum allowed iterations.");
        }
    }
}
