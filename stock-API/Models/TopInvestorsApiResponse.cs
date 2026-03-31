namespace stock_API.Models
{
    public class TopInvestorsApiResponse
    {
        public string          Ticker        { get; set; } = string.Empty;
        public string          CompanyName   { get; set; } = string.Empty;
        public string          Cik           { get; set; } = string.Empty;
        public string          Cusip         { get; set; } = string.Empty;
        public string          FilingPeriod  { get; set; } = string.Empty;
        public string          AsOfDate      { get; set; } = string.Empty;
        public string          DataSource    { get; set; } = "SEC EDGAR 13F-HR via EFTS";
        public int             TotalHolders  { get; set; }
        public List<TopHolder> Holders       { get; set; } = [];
    }
}
