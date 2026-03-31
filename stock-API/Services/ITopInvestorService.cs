using stock_API.Models;

namespace stock_API.Services
{
    public interface ITopInvestorService
    {
        Task<TopInvestorsApiResponse> GetTopInvestorsAsync(string ticker);
    }
}
