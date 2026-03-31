using Microsoft.AspNetCore.Mvc;
using stock_API.Services;

namespace stock_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnalysisController : ControllerBase
    {
        private readonly IFinancialService _financialService;
        private readonly IAiAnalysisService _aiAnalysisService;
        private readonly ILogger<AnalysisController> _logger;

        public AnalysisController(
            IFinancialService financialService,
            IAiAnalysisService aiAnalysisService,
            ILogger<AnalysisController> logger)
        {
            _financialService = financialService;
            _aiAnalysisService = aiAnalysisService;
            _logger = logger;
        }

        /// <summary>
        /// Returns an AI-generated analysis (summary, risk, sentiment, recommendation)
        /// for the given stock symbol based on its income statement history.
        /// </summary>
        /// <param name="symbol">Stock ticker (e.g. AAPL)</param>
        /// <param name="period">annual | quarter  (default: annual)</param>
        /// <param name="limit">Number of periods to analyse (default: 5)</param>
        [HttpGet("{symbol}")]
        public async Task<IActionResult> Analyze(
            string symbol,
            [FromQuery] string period = "annual",
            [FromQuery] int limit = 5)
        {
            try
            {
                var statements = await _financialService.GetIncomeStatementAsync(symbol, period, limit);
                var analysis = await _aiAnalysisService.AnalyzeAsync(symbol, statements);
                return Ok(analysis);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error while analysing {Symbol}", symbol);
                return StatusCode(502, new { error = "Upstream service unavailable. Make sure Ollama is running." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while analysing {Symbol}", symbol);
                return StatusCode(500, new { error = "Internal server error." });
            }
        }
    }
}
