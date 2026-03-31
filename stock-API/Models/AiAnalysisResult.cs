namespace stock_API.Models
{
    public class AiAnalysisResult
    {
        public string Symbol { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string RiskAnalysis { get; set; } = string.Empty;
        public string Sentiment { get; set; } = string.Empty;   // Bullish / Neutral / Bearish
        public string Recommendation { get; set; } = string.Empty; // Buy / Hold / Sell
        public string Model { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}
