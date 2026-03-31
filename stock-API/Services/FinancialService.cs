using System.Text.Json;
using stock_API.Models;

namespace stock_API.Services
{
    public class FinancialService : IFinancialService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string BaseUrl = "https://financialmodelingprep.com/stable";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public FinancialService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["FMP:ApiKey"]
                ?? throw new InvalidOperationException("FMP:ApiKey is not configured.");
        }

        public async Task<IEnumerable<IncomeStatement>> GetIncomeStatementAsync(
            string symbol, string period = "annual", int limit = 5)
        {
            var url = $"{BaseUrl}/income-statement/?symbol={symbol}&period={period}&limit={limit}&apikey={_apiKey}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<IEnumerable<IncomeStatement>>(json, JsonOptions)
                   ?? Enumerable.Empty<IncomeStatement>();
        }
    }
}
