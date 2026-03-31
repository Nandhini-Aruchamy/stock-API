using stock_API.Models;

namespace stock_API.Services
{
    public interface ISecEdgarFilingService
    {
        /// <summary>Resolves a ticker to (paddedCik, cikInt, companyName) via company_tickers.json.</summary>
        Task<(string? CikPadded, string? CikInt, string? CompanyName)> GetCompanyInfoAsync(string ticker);

        /// <summary>
        /// Fetches SC 13G filings from the submissions API and extracts the CUSIP from
        /// the most recent filing document.
        /// </summary>
        Task<string?> GetCusipFromSc13GAsync(string cikPadded, string cikInt);

        /// <summary>
        /// Searches EFTS for 13F-HR filings that mention the CUSIP, fetches each filer's
        /// infotable XML, parses the matching CUSIP row, and returns the top holders sorted
        /// by shares descending.
        /// </summary>
        Task<List<TopHolder>> GetTopHoldersFromEftsAsync(
            string cusip, DateOnly startDate, DateOnly today);
    }
}
