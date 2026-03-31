namespace stock_API.Models
{
    /// <summary>Metadata for one SC 13G / SC 13G/A entry from the submissions API.</summary>
    public class Sc13GFiling
    {
        /// <summary>Accession number with dashes, e.g. 0001234567-24-000001</summary>
        public string AccessionNo { get; set; } = string.Empty;

        /// <summary>Accession number without dashes — used in EDGAR archive paths.</summary>
        public string AccessionNoDashes => AccessionNo.Replace("-", "");

        public string PrimaryDocument { get; set; } = string.Empty;
        public string FilingDate { get; set; } = string.Empty;
        public string FormType { get; set; } = string.Empty;

        /// <summary>Period of report (e.g. 2024-12-31) from the submissions array.</summary>
        public string PeriodOfReport { get; set; } = string.Empty;
    }
}
