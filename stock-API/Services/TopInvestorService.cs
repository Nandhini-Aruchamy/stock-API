using stock_API.Models;
using stock_API.Utils;

namespace stock_API.Services
{
    public class TopInvestorService : ITopInvestorService
    {
        private readonly ISecEdgarFilingService _edgarService;
        private readonly ILogger<TopInvestorService> _logger;

        public TopInvestorService(
            ISecEdgarFilingService edgarService,
            ILogger<TopInvestorService> logger)
        {
            _edgarService = edgarService;
            _logger       = logger;
        }

        public async Task<TopInvestorsApiResponse> GetTopInvestorsAsync(string ticker)
        {
            ticker = ticker.ToUpper().Trim();

            // ── Step 1: ticker → CIK ─────────────────────────────────────────
            var (cikPadded, cikInt, companyName) =
                await _edgarService.GetCompanyInfoAsync(ticker);

            if (cikPadded is null || cikInt is null)
                throw new InvalidOperationException(
                    $"Ticker '{ticker}' was not found in SEC EDGAR company_tickers.json.");

            // ── Step 2: CUSIP from SC 13G ─────────────────────────────────────
            var cusip = await _edgarService.GetCusipFromSc13GAsync(cikPadded, cikInt);

            if (string.IsNullOrEmpty(cusip))
                throw new InvalidOperationException(
                    $"Could not resolve CUSIP for '{ticker}' from SEC EDGAR SC 13G filings.");

            _logger.LogInformation("{Ticker} → CUSIP {Cusip}", ticker, cusip);

            // ── Step 3: EFTS search → fetch XMLs → top 10 holders ────────────
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var (year, quarter, filingPeriod) = FilingPeriodHelper.GetLatestFilingPeriod(today);

            // Start date = first day of the quarter (Q1=Jan1, Q2=Apr1, Q3=Jul1, Q4=Oct1)
            var quarterStartMonth = (quarter - 1) * 3 + 1;
            var startDate = new DateOnly(year, quarterStartMonth, 1);

            var holders = await _edgarService.GetTopHoldersFromEftsAsync(
                cusip, startDate, today);

            if (holders.Count == 0)
                _logger.LogWarning("No 13F holders found for CUSIP {Cusip}", cusip);

            return new TopInvestorsApiResponse
            {
                Ticker       = ticker,
                CompanyName  = companyName ?? ticker,
                Cik          = cikPadded,
                Cusip        = cusip,
                FilingPeriod = filingPeriod,
                AsOfDate     = today.ToString("yyyy-MM-dd"),
                DataSource   = "SEC EDGAR 13F-HR via EFTS",
                TotalHolders = holders.Count,
                Holders      = holders
            };
        }
    }
}
