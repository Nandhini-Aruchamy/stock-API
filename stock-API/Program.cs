using Microsoft.EntityFrameworkCore;
using stock_API.Data;
using stock_API.Services;

var builder = WebApplication.CreateBuilder(args);

// CORS — allow Angular dev server
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:4200"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Angular", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// HttpClient + Financial service
builder.Services.AddHttpClient<IFinancialService, FinancialService>();

// HttpClient + Ollama AI analysis service
builder.Services.AddHttpClient<IAiAnalysisService, OllamaAnalysisService>();

// SEC EDGAR filing service — used by TopInvestorsController
builder.Services.AddHttpClient<ISecEdgarFilingService, SecEdgarFilingService>(client =>
{
    // EFTS search + multiple XML fetches — 90 seconds total
    client.Timeout = TimeSpan.FromSeconds(90);
});

// Top-investor orchestration service
builder.Services.AddScoped<ITopInvestorService, TopInvestorService>();

// HttpClient + Claude AI analysis service
// Timeout set to 10 minutes — the agentic web-search loop runs multiple rounds
builder.Services.AddHttpClient<IClaudeAnalysisService, ClaudeAnalysisService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});

// SQLite database for storing Claude stock analyses
builder.Services.AddDbContext<StockAnalysisDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("StockAnalysis")
        ?? "Data Source=stock_analysis.db"));

// Controllers + Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Auto-create / migrate the SQLite database on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StockAnalysisDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Angular");
app.UseAuthorization();
app.MapControllers();

app.Run();
