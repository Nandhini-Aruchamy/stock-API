using stock_API.Models;

namespace stock_API.Services
{
    public interface IAiAnalysisService
    {
        Task<AiAnalysisResult> AnalyzeAsync(string symbol, IEnumerable<IncomeStatement> statements);
    }
}
