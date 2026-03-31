namespace stock_API.Models
{
    public class TopHolder
    {
        public int    Rank        { get; set; }
        public string FirmName    { get; set; } = string.Empty;
        public string SharesOwned { get; set; } = string.Empty;
        public string ValueUsd    { get; set; } = string.Empty;
        public string FiledDate   { get; set; } = string.Empty;
    }
}
