using Microsoft.AspNetCore.Mvc;
using stock_API.Services;

namespace stock_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class IncomeStatementController : ControllerBase
    {
        private readonly IFinancialService _financialService;
        private readonly ILogger<IncomeStatementController> _logger;

        public IncomeStatementController(IFinancialService financialService, ILogger<IncomeStatementController> logger)
        {
            _financialService = financialService;
            _logger = logger;
        }

        /// <summary>
        /// Get income statement data for a stock symbol.
        /// </summary>
        /// <param name="symbol">Stock ticker symbol (e.g. AAPL)</param>
        /// <param name="period">Reporting period: "annual" or "quarter" (default: annual)</param>
        /// <param name="limit">Number of records to return (default: 5)</param>
        [HttpGet("{symbol}")]
        public async Task<IActionResult> Get(
            string symbol,
            [FromQuery] string period = "annual",
            [FromQuery] int limit = 5)
        {
            try
            {
                var data = await _financialService.GetIncomeStatementAsync(symbol, period, limit);
                return Ok(data);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to fetch income statement for {Symbol}", symbol);
                return StatusCode(502, new { error = "Failed to retrieve data from financial provider." });
            }
        }
    }
}
