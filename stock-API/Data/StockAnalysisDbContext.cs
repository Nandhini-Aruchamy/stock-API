using Microsoft.EntityFrameworkCore;
using stock_API.Models;

namespace stock_API.Data
{
    public class StockAnalysisDbContext : DbContext
    {
        public StockAnalysisDbContext(DbContextOptions<StockAnalysisDbContext> options)
            : base(options) { }

        public DbSet<StockAnalysis> StockAnalyses => Set<StockAnalysis>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<StockAnalysis>(entity =>
            {
                entity.HasIndex(e => e.Symbol);
                entity.HasIndex(e => e.AnalyzedAt);
            });
        }
    }
}
