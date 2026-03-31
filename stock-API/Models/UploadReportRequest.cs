using System.ComponentModel.DataAnnotations;

namespace stock_API.Models
{
    public class UploadReportRequest
    {
        [Required]
        public string Report { get; set; } = string.Empty;

        public string Model { get; set; } = "manual";
    }
}
