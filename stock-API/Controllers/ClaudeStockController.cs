using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using stock_API.Data;
using stock_API.Models;
using stock_API.Services;

namespace stock_API.Controllers
{
    [ApiController]
    [Route("api/claude")]
    public class ClaudeStockController : ControllerBase
    {
        private readonly IClaudeAnalysisService _claudeService;
        private readonly StockAnalysisDbContext _db;
        private readonly ILogger<ClaudeStockController> _logger;

        public ClaudeStockController(
            IClaudeAnalysisService claudeService,
            StockAnalysisDbContext db,
            ILogger<ClaudeStockController> logger)
        {
            _claudeService = claudeService;
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Returns the latest saved report from the database.
        /// If no report exists, automatically calls Claude API to generate one.
        /// </summary>
        [HttpGet("analyze/{symbol}")]
        [ProducesResponseType(typeof(StockAnalysis), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Analyze(string symbol)
        {
            symbol = symbol.ToUpper().Trim();

            var existing = await _db.StockAnalyses
                .Where(a => a.Symbol == symbol)
                .OrderByDescending(a => a.AnalyzedAt)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                _logger.LogInformation("Returning stored report for {Symbol} (Id={Id})", symbol, existing.Id);
                return Ok(existing);
            }

            // No report in DB — generate via Claude
            _logger.LogInformation("No stored report for {Symbol}. Calling Claude API.", symbol);
            try
            {
                var analysis = await _claudeService.AnalyzeStockAsync(symbol);
                _db.StockAnalyses.Add(analysis);
                await _db.SaveChangesAsync();

                _logger.LogInformation("Generated and saved report for {Symbol} (Id={Id})", symbol, analysis.Id);
                return Ok(analysis);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Upstream error while analyzing {Symbol}", symbol);
                return StatusCode(StatusCodes.Status502BadGateway,
                    new { error = "Claude API is unavailable.", detail = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while analyzing {Symbol}", symbol);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { error = "An unexpected error occurred.", detail = ex.Message });
            }
        }

        /// <summary>
        /// Upload a report from the UI and save it to the database.
        /// Replaces any existing report for the same symbol.
        /// </summary>
        [HttpPost("report/{symbol}")]
        [ProducesResponseType(typeof(StockAnalysis), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Upload(string symbol, [FromBody] UploadReportRequest request)
        {
            symbol = symbol.ToUpper().Trim();

            var analysis = new StockAnalysis
            {
                Symbol     = symbol,
                Report     = request.Report,
                Model      = request.Model,
                AnalyzedAt = DateTime.UtcNow
            };

            _db.StockAnalyses.Add(analysis);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Uploaded report for {Symbol} (Id={Id})", symbol, analysis.Id);
            return CreatedAtAction(nameof(GetById), new { id = analysis.Id }, analysis);
        }

        /// <summary>
        /// Get all saved reports for a stock (newest first).
        /// </summary>
        [HttpGet("history/{symbol}")]
        [ProducesResponseType(typeof(IEnumerable<StockAnalysis>), StatusCodes.Status200OK)]
        public async Task<IActionResult> History(string symbol, [FromQuery] int take = 10)
        {
            symbol = symbol.ToUpper().Trim();
            var results = await _db.StockAnalyses
                .Where(a => a.Symbol == symbol)
                .OrderByDescending(a => a.AnalyzedAt)
                .Take(take)
                .ToListAsync();

            return Ok(results);
        }

        /// <summary>
        /// Get a single saved report by database Id.
        /// </summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(StockAnalysis), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(int id)
        {
            var analysis = await _db.StockAnalyses.FindAsync(id);
            return analysis is null ? NotFound() : Ok(analysis);
        }

        /// <summary>
        /// Delete a saved report.
        /// </summary>
        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(int id)
        {
            var analysis = await _db.StockAnalyses.FindAsync(id);
            if (analysis is null) return NotFound();

            _db.StockAnalyses.Remove(analysis);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
