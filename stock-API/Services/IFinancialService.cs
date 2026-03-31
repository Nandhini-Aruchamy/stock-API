using stock_API.Models;

namespace stock_API.Services
{
    public interface IFinancialService
    {
        Task<IEnumerable<IncomeStatement>> GetIncomeStatementAsync(string symbol, string period = "annual", int limit = 5);
    }
}
