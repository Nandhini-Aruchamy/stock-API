using stock_API.Models;

namespace stock_API.Services
{
    public interface IClaudeAnalysisService
    {
        /// <summary>
        /// Analyzes the given stock ticker using Claude AI with live web search.
        /// All data (financials, news, earnings calls, insider activity) is sourced
        /// in real-time by Claude — no external financial API required.
        /// </summary>
        Task<StockAnalysis> AnalyzeStockAsync(string symbol);

        /// <summary>
        /// Sends a single prompt to Claude (no web search) and returns the raw text response.
        /// Used for structured data formatting tasks such as ranking investor holdings.
        /// </summary>
        Task<string> AskAsync(string prompt);
    }
}
