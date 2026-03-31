using Microsoft.AspNetCore.Mvc;
using stock_API.Services;

namespace stock_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TopInvestorsController : ControllerBase
    {
        private readonly ITopInvestorService _topInvestorService;
        private readonly ILogger<TopInvestorsController> _logger;

        public TopInvestorsController(
            ITopInvestorService topInvestorService,
            ILogger<TopInvestorsController> logger)
        {
            _topInvestorService = topInvestorService;
            _logger             = logger;
        }

        /// <summary>
        /// Returns top 10 institutional investors for a stock ticker.
        /// CUSIP is resolved via SC 13G; holder data comes from 13F-HR filings on EFTS.
        /// No date parameters required — always returns the latest available data.
        /// </summary>
        /// <param name="ticker">Stock ticker, e.g. ABCL</param>
        [HttpGet("{ticker}")]
        public async Task<IActionResult> GetTopInvestors(string ticker)
        {
            ticker = ticker.Trim().ToUpper();
            _logger.LogInformation("TopInvestors request for {Ticker}", ticker);

            try
            {
                var result = await _topInvestorService.GetTopInvestorsAsync(ticker);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Ticker not found: {Ticker}", ticker);
                return NotFound(new { error = ex.Message });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Upstream error for {Ticker}", ticker);
                return StatusCode(502, new { error = "Failed to reach SEC EDGAR.", detail = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error for {Ticker}", ticker);
                return StatusCode(500, new { error = "An unexpected error occurred.", detail = ex.Message });
            }
        }
    }
}
