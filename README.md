# Stock Analysis API

ASP.NET Core (.NET 10) REST API for stock research. Uses **Claude AI** with live web search to generate equity analysis reports, queries **SEC EDGAR** for top institutional holders, and fetches income statements via **Financial Modeling Prep (FMP)**. Results are persisted in SQLite.

---

## Features

- **AI Stock Analysis** — Claude (claude-sonnet-4-6) runs an agentic web-search loop (up to 15 searches) and produces a detailed equity research report. Prompt caching keeps costs low across multiple symbols.
- **Top Institutional Investors** — Resolves a ticker's CUSIP via SC 13G filings, then pulls the top 10 holders from 13F-HR filings on SEC EDGAR.
- **Income Statements** — Proxies annual/quarterly income statement data from Financial Modeling Prep.
- **Report Storage** — Analyses are saved in SQLite and served from cache on repeat requests. Full history and delete endpoints included.
- **Swagger UI** — Auto-generated docs available in development at `/swagger`.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core (.NET 10) |
| Language | C# |
| AI | Anthropic Claude API (web search + prompt caching) |
| Local AI | Ollama (llama3.2) |
| SEC Data | SEC EDGAR EFTS |
| Financial Data | Financial Modeling Prep (FMP) |
| Database | SQLite via EF Core |
| Docs | Swagger / OpenAPI |

---

## API Endpoints

### Claude AI Analysis — `/api/claude`

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/claude/analyze/{symbol}` | Returns stored report or generates a new one via Claude |
| `POST` | `/api/claude/report/{symbol}` | Upload and save a report from the UI |
| `GET` | `/api/claude/history/{symbol}` | List saved reports (newest first, `?take=10`) |
| `GET` | `/api/claude/{id}` | Get a single report by database ID |
| `DELETE` | `/api/claude/{id}` | Delete a saved report |

### Top Institutional Investors — `/api/topinvestors`

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/topinvestors/{ticker}` | Top 10 institutional holders from latest 13F filings |

### Income Statement — `/api/incomestatement`

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/incomestatement/{symbol}` | Income statement data (`?period=annual\|quarter&limit=5`) |

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An [Anthropic API key](https://console.anthropic.com/)
- A [Financial Modeling Prep API key](https://financialmodelingprep.com/)
- (Optional) [Ollama](https://ollama.com/) running locally for the local AI endpoint

### Configuration

Add your keys using .NET User Secrets (recommended) instead of editing `appsettings.json` directly:

```bash
cd stock-API
dotnet user-secrets set "Claude:ApiKey" "your-claude-api-key"
dotnet user-secrets set "FMP:ApiKey" "your-fmp-api-key"
```

Or set them as environment variables:

```bash
export Claude__ApiKey="your-claude-api-key"
export FMP__ApiKey="your-fmp-api-key"
```

Other settings in `appsettings.json`:

```json
{
  "Claude": {
    "Model": "claude-sonnet-4-6",
    "PromptFilePath": "Prompt/stock_analysis_prompt_test.txt"
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "llama3.2"
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:4200"]
  },
  "ConnectionStrings": {
    "StockAnalysis": "Data Source=stock_analysis.db"
  }
}
```

### Run

```bash
cd stock-API
dotnet run
```

The API starts on `https://localhost:7xxx` (exact port shown in terminal). Swagger UI is available at `/swagger` in development.

---

## Project Structure

```
stock-API/
├── Controllers/
│   ├── ClaudeStockController.cs      # AI analysis endpoints
│   ├── TopInvestorsController.cs     # SEC EDGAR institutional holders
│   ├── IncomeStatementController.cs  # FMP income statement proxy
│   └── AnalysisController.cs
├── Services/
│   ├── ClaudeAnalysisService.cs      # Anthropic API + agentic web-search loop
│   ├── OllamaAnalysisService.cs      # Local LLM via Ollama
│   ├── SecEdgarFilingService.cs      # SEC EDGAR SC 13G / 13F-HR fetcher
│   ├── TopInvestorService.cs         # Orchestrates EDGAR lookup
│   └── FinancialService.cs           # FMP API client
├── Models/                           # Request/response models
├── Data/
│   └── StockAnalysisDbContext.cs     # EF Core SQLite context
├── Prompt/
│   └── stock_analysis_prompt_test.txt  # Analyst system prompt for Claude
└── Program.cs
```

---

## Frontend

CORS is pre-configured for an Angular dev server at `http://localhost:4200`. Update `Cors:AllowedOrigins` in `appsettings.json` for other origins.
