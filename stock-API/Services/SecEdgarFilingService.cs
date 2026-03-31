using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using stock_API.Models;

namespace stock_API.Services
{
    public class SecEdgarFilingService : ISecEdgarFilingService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<SecEdgarFilingService> _logger;

        private const string CompanyTickersUrl = "https://www.sec.gov/files/company_tickers.json";
        private const string SubmissionsBase   = "https://data.sec.gov/submissions";
        private const string EdgarArchiveBase  = "https://www.sec.gov/Archives/edgar/data";
        private const string EftsSearchBase    = "https://efts.sec.gov/LATEST/search-index";

        public SecEdgarFilingService(
            HttpClient httpClient,
            ILogger<SecEdgarFilingService> logger)
        {
            _httpClient = httpClient;
            _logger     = logger;

            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", "StockApp contact@yourdomain.com");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "Accept", "*/*");
        }

        // ── Step 1: ticker → CIK ─────────────────────────────────────────────

        public async Task<(string? CikPadded, string? CikInt, string? CompanyName)>
            GetCompanyInfoAsync(string ticker)
        {
            _logger.LogInformation("Resolving CIK for ticker {Ticker}", ticker);
            try
            {
                var resp = await _httpClient.GetAsync(CompanyTickersUrl);
                if (!resp.IsSuccessStatusCode) return (null, null, null);

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    var obj = entry.Value;
                    if (!obj.TryGetProperty("ticker", out var tProp)) continue;
                    if (!string.Equals(tProp.GetString(), ticker, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!obj.TryGetProperty("cik_str", out var cikProp)) continue;

                    var cikInt = cikProp.ValueKind == JsonValueKind.Number
                        ? cikProp.GetInt64().ToString()
                        : cikProp.GetString() ?? "";

                    if (string.IsNullOrEmpty(cikInt)) continue;

                    var cikPadded   = cikInt.PadLeft(10, '0');
                    var companyName = obj.TryGetProperty("title", out var titleProp)
                        ? titleProp.GetString() ?? ticker : ticker;

                    _logger.LogInformation("{Ticker} → CIK {Cik} ({Name})", ticker, cikPadded, companyName);
                    return (cikPadded, cikInt, companyName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetCompanyInfoAsync failed for {Ticker}", ticker);
            }

            return (null, null, null);
        }

        // ── Step 2: CUSIP from SC 13G document ───────────────────────────────

        public async Task<string?> GetCusipFromSc13GAsync(string cikPadded, string cikInt)
        {
            _logger.LogInformation("Looking for CUSIP via SC 13G, CIK {Cik}", cikPadded);

            var subUrl = $"{SubmissionsBase}/CIK{cikPadded}.json";
            List<Sc13GFiling> filings;
            try
            {
                var resp = await _httpClient.GetAsync(subUrl);
                if (!resp.IsSuccessStatusCode) return null;

                var json = await resp.Content.ReadAsStringAsync();
                filings = ParseSc13GFilings(json, maxCount: 10);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch submissions for CIK {Cik}", cikPadded);
                return null;
            }

            if (filings.Count == 0) return null;

            foreach (var filing in filings)
            {
                var url = $"{EdgarArchiveBase}/{cikInt}/{filing.AccessionNoDashes}/{filing.PrimaryDocument}";
                try
                {
                    var resp = await _httpClient.GetAsync(url);
                    if (!resp.IsSuccessStatusCode) continue;

                    var html  = await resp.Content.ReadAsStringAsync();
                    var text  = CleanHtml(html);
                    var cusip = ExtractCusip(text);

                    if (!string.IsNullOrEmpty(cusip))
                    {
                        _logger.LogInformation("CUSIP {Cusip} found in SC 13G {Acc}", cusip, filing.AccessionNo);
                        return cusip;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to fetch SC 13G doc {Acc}", filing.AccessionNo);
                }

                await Task.Delay(120);
            }

            _logger.LogWarning("No CUSIP found in any SC 13G for CIK {Cik}", cikPadded);
            return null;
        }

        // ── Step 3: EFTS search + per-filer XML parse ─────────────────────────

        private const int EftsPageSize = 100;

        public async Task<List<TopHolder>> GetTopHoldersFromEftsAsync(
            string cusip, DateOnly startDate, DateOnly today)
        {
            // STEP 1 — Paginate through ALL EFTS pages for this CUSIP
            var allStubs  = new List<EftsFilerStub>();
            int from      = 0;
            int totalHits = int.MaxValue; // updated on first response

            while (from < totalHits)
            {
                var eftsUrl = $"{EftsSearchBase}" +
                              $"?q=%22{Uri.EscapeDataString(cusip)}%22" +
                              $"&forms=13F-HR" +
                              $"&dateRange=custom" +
                              $"&startdt={startDate:yyyy-MM-dd}" +
                              $"&enddt={today:yyyy-MM-dd}" +
                              $"&_size={EftsPageSize}" +
                              $"&_from={from}";

                _logger.LogInformation("EFTS page from={From}: {Url}", from, eftsUrl);

                try
                {
                    var resp = await _httpClient.GetAsync(eftsUrl);
                    if (!resp.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("EFTS returned {Status} at from={From}", resp.StatusCode, from);
                        break;
                    }

                    var json = await resp.Content.ReadAsStringAsync();
                    var (pageStubs, pageTotal) = ParseEftsPage(json);

                    // Update total from first response
                    if (from == 0)
                    {
                        totalHits = pageTotal;
                        _logger.LogInformation("EFTS total hits for CUSIP {Cusip}: {Total}", cusip, totalHits);
                    }

                    if (pageStubs.Count == 0) break; // no more results

                    allStubs.AddRange(pageStubs);
                    from += pageStubs.Count;

                    if (from < totalHits)
                        await Task.Delay(200); // brief pause between pages
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "EFTS page fetch failed at from={From}", from);
                    break;
                }
            }

            // STEP 2 — Combine all pages
            _logger.LogInformation("EFTS fetched {Count} total stubs for CUSIP {Cusip}",
                allStubs.Count, cusip);

            var stubs = allStubs;

            if (stubs.Count == 0) return [];

            // Deduplicate — keep the latest filing per firm
            var latest = stubs
                .GroupBy(s => s.FirmName.ToUpperInvariant().Trim())
                .Select(g => g.OrderByDescending(s => s.FileDate).First())
                .ToList();

            // STEP 2 + 3 — fetch each filer's infotable XML and parse CUSIP row
            var rawHoldings = new List<RawHolding>();

            foreach (var stub in latest)
            {
                var accNoDashes = stub.AccessionNumber.Replace("-", "");
                var xmlUrl = $"{EdgarArchiveBase}/{stub.CikInt}/{accNoDashes}/{stub.XmlFileName}";

                try
                {
                    _logger.LogDebug("Fetching infotable XML: {Url}", xmlUrl);
                    var resp = await _httpClient.GetAsync(xmlUrl);
                    if (!resp.IsSuccessStatusCode)
                    {
                        _logger.LogDebug("XML fetch {Status}: {Url}", resp.StatusCode, xmlUrl);
                        continue;
                    }

                    var xmlText = await resp.Content.ReadAsStringAsync();
                    var holding = ParseCusipRowFromXml(xmlText, cusip);

                    if (holding.HasValue)
                    {
                        rawHoldings.Add(new RawHolding
                        {
                            FirmName       = stub.FirmName,
                            FileDate       = stub.FileDate,
                            AccessionNumber = stub.AccessionNumber,
                            SharesOwned    = holding.Value.Shares,
                            ValueThousands = holding.Value.Value
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to process XML for {Firm}", stub.FirmName);
                }

                await Task.Delay(110); // SEC fair-use rate limit
            }

            // STEP 4 + 5 — combine, sort by shares, top 10
            var top10 = rawHoldings
                .OrderByDescending(h => h.SharesOwned)
                .Take(10)
                .ToList();

            // STEP 6 — format
            return top10.Select((h, i) => new TopHolder
            {
                Rank        = i + 1,
                FirmName    = FixAbbreviations(ToTitleCase(StripCikSuffix(h.FirmName))),
                SharesOwned = h.SharesOwned.ToString("N0"),
                ValueUsd    = FormatValueUsd(h.ValueThousands),
                FiledDate   = h.FileDate
            }).ToList();
        }

        // ── EFTS response parser ──────────────────────────────────────────────

        /// <summary>Parses one EFTS page. Returns (stubs, totalHits).</summary>
        private (List<EftsFilerStub> Stubs, int Total) ParseEftsPage(string json)
        {
            var result = new List<EftsFilerStub>();
            int total  = 0;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("hits", out var outer)) return (result, total);

                // Total hit count lives at hits.total.value
                if (outer.TryGetProperty("total", out var totalObj))
                {
                    if (totalObj.ValueKind == JsonValueKind.Object &&
                        totalObj.TryGetProperty("value", out var tv))
                        total = tv.GetInt32();
                    else if (totalObj.ValueKind == JsonValueKind.Number)
                        total = totalObj.GetInt32();
                }

                if (!outer.TryGetProperty("hits", out var hits)) return (result, total);

                foreach (var hit in hits.EnumerateArray())
                {
                    // _id = "accession-number:xml-filename"
                    var id = hit.TryGetProperty("_id", out var idProp) ? idProp.GetString() ?? "" : "";

                    var colonIdx = id.IndexOf(':');
                    if (colonIdx < 0) continue;

                    var accessionNumber = id[..colonIdx].Trim();
                    var xmlFileName     = id[(colonIdx + 1)..].Trim();

                    if (string.IsNullOrEmpty(accessionNumber) || string.IsNullOrEmpty(xmlFileName)) continue;

                    if (!hit.TryGetProperty("_source", out var src)) continue;

                    // ciks — take first element
                    var cikInt = "";
                    if (src.TryGetProperty("ciks", out var ciks) && ciks.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in ciks.EnumerateArray())
                        {
                            var raw = c.GetString() ?? "";
                            cikInt = raw.TrimStart('0');
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(cikInt)) continue;

                    // display_names — take first element
                    var firmName = "";
                    if (src.TryGetProperty("display_names", out var names) && names.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var n in names.EnumerateArray())
                        {
                            firmName = n.GetString() ?? "";
                            break;
                        }
                    }

                    var fileDate = src.TryGetProperty("file_date", out var fd) ? fd.GetString() ?? "" : "";

                    result.Add(new EftsFilerStub
                    {
                        AccessionNumber = accessionNumber,
                        XmlFileName     = xmlFileName,
                        CikInt          = cikInt,
                        FirmName        = firmName,
                        FileDate        = fileDate
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ParseEftsPage failed");
            }

            return (result, total);
        }

        // ── Infotable XML parser ──────────────────────────────────────────────

        private static (long Shares, long Value)? ParseCusipRowFromXml(string xml, string cusip)
        {
            try
            {
                var xdoc = XDocument.Parse(xml);
                var ns   = xdoc.Root?.Name.Namespace ?? XNamespace.None;

                var rows = xdoc.Descendants(ns + "infoTable")
                               .Concat(xdoc.Descendants("infoTable"))
                               .ToList();

                foreach (var row in rows)
                {
                    var rowCusip = row.Element(ns + "cusip")?.Value
                                ?? row.Element("cusip")?.Value ?? "";

                    if (!string.Equals(rowCusip.Trim(), cusip.Trim(),
                            StringComparison.OrdinalIgnoreCase)) continue;

                    // Skip options (non-empty putCall)
                    var putCall = row.Element(ns + "putCall")?.Value
                               ?? row.Element("putCall")?.Value ?? "";
                    if (!string.IsNullOrWhiteSpace(putCall)) continue;
                    var shrsOrPrnAmt = row.Elements().FirstOrDefault(x => x.Name.LocalName == "shrsOrPrnAmt");

                    var sharesRaw = shrsOrPrnAmt?.Elements()
                        .FirstOrDefault(x => x.Name.LocalName == "sshPrnamt")?.Value
                        ?? "0";

                    var valueRaw  = row.Element(ns + "value")?.Value
                                 ?? row.Element("value")?.Value  ?? "0";

                    long.TryParse(sharesRaw.Replace(",", ""), out var shares);
                    long.TryParse(valueRaw.Replace(",", ""),  out var value);

                    return (shares, value);
                }
            }
            catch { }

            return null;
        }

        // ── SC 13G helpers ────────────────────────────────────────────────────

        private static List<Sc13GFiling> ParseSc13GFilings(string json, int maxCount)
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("filings", out var filings) ||
                !filings.TryGetProperty("recent",          out var recent))
                return [];

            if (!recent.TryGetProperty("form",            out var formArr) ||
                !recent.TryGetProperty("accessionNumber", out var accArr)  ||
                !recent.TryGetProperty("primaryDocument", out var docArr)  ||
                !recent.TryGetProperty("filingDate",      out var dateArr))
                return [];

            var forms = formArr.EnumerateArray().ToList();
            var accs  = accArr.EnumerateArray().ToList();
            var docs  = docArr.EnumerateArray().ToList();
            var dates = dateArr.EnumerateArray().ToList();

            var result = new List<Sc13GFiling>();
            var count  = Math.Min(forms.Count, Math.Min(accs.Count, docs.Count));

            for (int i = 0; i < count; i++)
            {
                var form = forms[i].GetString() ?? "";
                if (!form.Equals("SC 13G",   StringComparison.OrdinalIgnoreCase) &&
                    !form.Equals("SC 13G/A", StringComparison.OrdinalIgnoreCase))
                    continue;

                var acc  = accs[i].GetString() ?? "";
                var pDoc = docs[i].GetString() ?? "";
                var date = i < dates.Count ? dates[i].GetString() ?? "" : "";

                if (string.IsNullOrEmpty(acc) || string.IsNullOrEmpty(pDoc)) continue;

                result.Add(new Sc13GFiling
                {
                    AccessionNo     = acc,
                    PrimaryDocument = pDoc,
                    FilingDate      = date,
                    FormType        = form
                });
            }

            return result.OrderByDescending(f => f.FilingDate).Take(maxCount).ToList();
        }

        private static string CleanHtml(string html)
        {
            var c = Regex.Replace(html, @"<(script|style)[^>]*>[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
            c = Regex.Replace(c, @"<(?:br|p|div|tr|td|th|li|h[1-6])\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            c = Regex.Replace(c, @"<[^>]+>", " ");
            c = WebUtility.HtmlDecode(c);
            c = Regex.Replace(c, @"[ \t]+", " ");
            c = Regex.Replace(c, @"\n{3,}", "\n\n");
            return c.Trim();
        }

        private static string? ExtractCusip(string text)
        {
            var m = Regex.Match(text, @"CUSIP\s+No\.?\s+([0-9A-Z]{9})\b", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();

            m = Regex.Match(text, @"([0-9A-Z]{9})\s*\n\s*\(CUSIP\s+Number\)", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();

            m = Regex.Match(text, @"CUSIP[^0-9A-Z]*([0-9A-Z]{9})\b", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();

            return null;
        }

        // ── Formatting helpers ────────────────────────────────────────────────

        private static string ToTitleCase(string s) =>
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());

        /// <summary>Remove "(CIK XXXXXXXXXX)" suffix that EDGAR sometimes appends to display_names.</summary>
        private static string StripCikSuffix(string name) =>
            Regex.Replace(name, @"\s*\(CIK\s+\d+\)\s*$", "", RegexOptions.IgnoreCase).Trim();

        // Abbreviations that title-case mangles — map lower-title form → correct form
        private static readonly (string Wrong, string Right)[] Abbreviations =
        [
            ("Lp",      "LP"),
            ("Llc",     "LLC"),
            ("Llp",     "LLP"),
            ("L.p.",    "L.P."),
            ("L.l.c.",  "L.L.C."),
            ("L.l.p.",  "L.L.P."),
            ("Plc",     "PLC"),
            ("Etf",     "ETF"),
            ("Adr",     "ADR"),
            ("Na",      "NA"),
            ("Inc.",    "Inc."),   // already correct — keeps period
        ];

        /// <summary>
        /// Fixes known abbreviations that ToTitleCase converts incorrectly,
        /// e.g. "Lp" → "LP", "Llc" → "LLC".
        /// </summary>
        private static string FixAbbreviations(string name)
        {
            foreach (var (wrong, right) in Abbreviations)
                name = Regex.Replace(name, $@"\b{Regex.Escape(wrong)}\b", right);
            return name;
        }

        private static string FormatValueUsd(long valueThousands)
        {
            var dollars = valueThousands * 1000L;
            if (dollars >= 1_000_000_000) return $"${dollars / 1_000_000_000.0:F1}B";
            if (dollars >= 1_000_000)     return $"${dollars / 1_000_000.0:F1}M";
            return $"${dollars / 1_000.0:F0}K";
        }

        // ── Internal DTOs ─────────────────────────────────────────────────────

        private sealed class EftsFilerStub
        {
            public string AccessionNumber { get; set; } = string.Empty;
            public string XmlFileName     { get; set; } = string.Empty;
            public string CikInt          { get; set; } = string.Empty;
            public string FirmName        { get; set; } = string.Empty;
            public string FileDate        { get; set; } = string.Empty;
        }

        private sealed class RawHolding
        {
            public string FirmName        { get; set; } = string.Empty;
            public string FileDate        { get; set; } = string.Empty;
            public string AccessionNumber { get; set; } = string.Empty;
            public long   SharesOwned     { get; set; }
            public long   ValueThousands  { get; set; }
        }
    }
}
