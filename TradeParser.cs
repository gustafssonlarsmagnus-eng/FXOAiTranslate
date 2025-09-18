using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FXOAiTranslator
{
    public class TradeParser
    {
        private BloombergService _bloombergService;
        private OpenAIService _openAI;
        private HybridPatternLearner _patternLearner;
        private readonly Dictionary<string, TradeParseResult> _cache = new(); // Cache for parsed results

        public Action<string> DebugCallback { get; set; }

        public TradeParser(BloombergService bloombergService, string openAIApiKey = null)
        {
            _bloombergService = bloombergService;

            if (!string.IsNullOrEmpty(openAIApiKey))
            {
                _openAI = new OpenAIService(openAIApiKey);
                _patternLearner = new HybridPatternLearner(_openAI, _bloombergService, "");
                Console.WriteLine("[AI] OpenAI integration enabled");
            }
            else
            {
                Console.WriteLine("[AI] OpenAI integration disabled - no API key provided");
            }
        }

        private void LogDebug(string message)
        {
            Console.WriteLine(message);
            DebugCallback?.Invoke(message);
        }

        public async Task<TradeParseResult> ParseTradeAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            // Prevent infinite loop: skip if it's already OVML
            if (input.StartsWith("OVML", StringComparison.OrdinalIgnoreCase))
            {
                LogDebug("DEBUG: Input already OVML, skipping AI/regex parse.");
                var already = new TradeParseResult
                {
                    OVML = input,
                    Underlying = ExtractCurrencyPair(input),
                    Expiry = NormalizeExpiry(ExtractExpiry(input)), // normalized here
                    ParseMethod = "Already-OVML"
                };
                _cache[input] = already;
                return already;
            }

            input = input.Trim();

            // Check cache first
            if (_cache.TryGetValue(input, out var cached))
            {
                LogDebug("DEBUG: Returning cached result for input.");
                return cached;
            }

            LogDebug($"DEBUG: Processing input: '{input}'");

            // Extract basic info first
            string underlying = ExtractCurrencyPair(input);
            string expiry = ExtractExpiry(input);

            LogDebug($"DEBUG: Extracted underlying: '{underlying}'");
            LogDebug($"DEBUG: Extracted expiry: '{expiry}'");

            // Test against our known regex patterns
            foreach (var pattern in RegexTradePatterns.Patterns)
            {
                LogDebug($"DEBUG: Testing pattern: {pattern.Name}");
                var match = pattern.Regex.Match(input);

                if (match.Success)
                {
                    LogDebug($"DEBUG: MATCH FOUND for pattern: {pattern.Name}");

                    // Debug all captured groups
                    foreach (string groupName in pattern.Regex.GetGroupNames())
                    {
                        var group = match.Groups[groupName];
                        if (group.Success && groupName != "0")
                        {
                            LogDebug($"DEBUG: Group '{groupName}': '{group.Value}'");
                        }
                    }

                    var result = new TradeParseResult
                    {
                        ParseMethod = "Regex-" + pattern.Name,
                        Underlying = underlying,
                        Expiry = expiry
                    };

                    // Spot reference
                    string spot = "";
                    var spotMatch = RegexTradePatterns.SpotRegex.Match(input);
                    if (spotMatch.Success)
                    {
                        LogDebug($"DEBUG: Spot reference found: '{spotMatch.Groups["spot"].Value}'");
                        spot = spotMatch.Groups["spot"].Value;
                    }
                    else
                    {
                        // If no explicit spot in input, fetch live Bloomberg spot
                        if (_bloombergService != null && _bloombergService.IsConnected)
                        {
                            try
                            {
                                var liveSpotTask = _bloombergService.GetSpotRate(underlying);
                                double? liveSpot = await liveSpotTask;

                                if (liveSpot.HasValue)
                                {
                                    LogDebug($"DEBUG: No spot in input, using live Bloomberg spot: {liveSpot.Value}");
                                    spot = liveSpot.Value.ToString("0.####", CultureInfo.InvariantCulture);
                                }
                                else
                                {
                                    LogDebug("DEBUG: Live spot returned null, skipping spot ref.");
                                }
                            }
                            catch (Exception ex)
                            {
                                LogDebug($"DEBUG: Failed to fetch live spot: {ex.Message}");
                            }
                        }
                    }

                    try
                    {
                        switch (pattern.Name)
                        {
                            case "Collar_BuyCallSellCallSellPut":
                                LogDebug($"DEBUG: Processing Collar_BuyCallSellCallSellPut pattern");
                                result.LegCount = 3;
                                result.OVML = $"OVML {result.Underlying} 3L B,S,S " +
                                              $"{match.Groups["strike1"].Value}C,{match.Groups["strike2"].Value}C,{match.Groups["strike3"].Value}P " +
                                              $"{result.Expiry} N{match.Groups["notional1"].Value}M,{match.Groups["notional2"].Value}M,{match.Groups["notional3"].Value}M" +
                                              (string.IsNullOrEmpty(spot) ? "" : " SP" + spot);
                                break;

                            case "RiskReversal_PutCall":
                            case "RiskReversal_CallPut":
                                LogDebug($"DEBUG: Processing {pattern.Name} pattern");
                                result.LegCount = 2;
                                result.OVML = RegexTradePatterns.BuildRiskReversalOVML(result.Underlying, match, result.Expiry, spot);
                                break;

                            case "Vanilla":
                                LogDebug($"DEBUG: Processing Vanilla pattern");
                                result.LegCount = 1;
                                string side = match.Groups["side"].Value.ToLower() == "buy" ? "B" : "S";
                                string optionType = match.Groups["type"].Value.Substring(0, 1).ToUpper();
                                result.OVML = $"OVML {result.Underlying} 1L {side} " +
                                              $"{match.Groups["strike"].Value}{optionType} " +
                                              $"{result.Expiry} N{match.Groups["notional"].Value}M" +
                                              (string.IsNullOrEmpty(spot) ? "" : " SP" + spot);
                                break;

                            case "Simple_Vanilla":
                                LogDebug($"DEBUG: Processing Simple_Vanilla pattern");
                                result.LegCount = 1;
                                string s = match.Groups["side"].Value.ToLower() == "buy" ? "B" : "S";
                                string t = match.Groups["type"].Value.Substring(0, 1).ToUpper();
                                result.OVML = $"OVML {result.Underlying} 1L {s} " +
                                              $"{match.Groups["strike"].Value}{t} " +
                                              $"{result.Expiry} N{match.Groups["notional"].Value}M" +
                                              (string.IsNullOrEmpty(spot) ? "" : " SP" + spot);
                                break;

                            default:
                                LogDebug($"DEBUG: Unknown pattern name: {pattern.Name}");
                                throw new Exception($"Unknown pattern: {pattern.Name}");
                        }

                        // Normalize outputs
                        result.OVML = NormalizeOVMLDates(result.OVML);
                        result.Expiry = NormalizeExpiry(result.Expiry);

                        LogDebug($"DEBUG: Generated OVML: '{result.OVML}'");
                        LogDebug($"[RegexParser] Matched {pattern.Name}: {result.OVML}");

                        _cache[input] = result; // Cache the result
                        return result;
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"DEBUG: Exception in switch statement: {ex.Message}");
                        LogDebug($"DEBUG: Exception stack trace: {ex.StackTrace}");
                        throw;
                    }
                }
                else
                {
                    LogDebug($"DEBUG: No match for pattern: {pattern.Name}");
                }
            }

            // === AI fallback ===
            LogDebug($"DEBUG: No regex patterns matched. Falling back to AI...");
            LogDebug("[Parser] Falling back to AI...");
            try
            {
                var aiResult = await ParseWithAI(input);

                // Correction: override AI expiry if regex extracted a better one
                if (!string.IsNullOrWhiteSpace(expiry) &&
                    expiry != "3M" &&
                    aiResult.Expiry != expiry)
                {
                    LogDebug($"DEBUG: Corrected AI expiry from {aiResult.Expiry} → {expiry}");
                    aiResult.Expiry = expiry;

                    // Replace expiry in OVML too
                    var parts = aiResult.OVML.Split(' ').ToList();
                    for (int i = 0; i < parts.Count; i++)
                    {
                        if (parts[i].Contains("/") || Regex.IsMatch(parts[i], @"\d+[MY]$"))
                        {
                            parts[i] = expiry;
                            break;
                        }
                    }
                    aiResult.OVML = string.Join(" ", parts);
                }

                // Normalize both expiry + OVML
                aiResult.OVML = NormalizeOVMLDates(aiResult.OVML);
                aiResult.Expiry = NormalizeExpiry(aiResult.Expiry);

                LogDebug($"DEBUG: AI Success - ParseMethod: '{aiResult.ParseMethod}'");
                LogDebug($"DEBUG: AI Success - OVML: '{aiResult.OVML}'");

                _cache[input] = aiResult; // Cache AI result
                return aiResult;
            }
            catch (Exception ex)
            {
                LogDebug("AI parse error: " + ex.Message);
                var errorResult = new TradeParseResult
                {
                    OVML = "",
                    Underlying = ExtractCurrencyPair(input),
                    Expiry = NormalizeExpiry(ExtractExpiry(input)),
                    ParseMethod = "AI-Error"
                };

                LogDebug($"DEBUG: AI Error - ParseMethod: '{errorResult.ParseMethod}'");
                LogDebug($"DEBUG: AI Error - OVML: '{errorResult.OVML}'");

                _cache[input] = errorResult; // Cache error result
                return errorResult;
            }
        }

        // === AI integration ===
        private async Task<TradeParseResult> ParseWithAI(string input)
        {
            if (_patternLearner == null)
            {
                await Task.Delay(10);
                throw new NotImplementedException("AI parser not configured - no OpenAI API key provided.");
            }

            string underlying = ExtractCurrencyPair(input);
            string expiry = ExtractExpiry(input);

            return await _patternLearner.ParseWithAI(input, underlying, expiry);
        }

        // === Currency extraction ===
        private string ExtractCurrencyPair(string input)
        {
            try
            {
                LogDebug($"DEBUG: ExtractCurrencyPair input: '{input}'");

                string[] commonPairs = {
                    "EURUSD", "USDJPY", "GBPUSD", "USDCHF", "AUDUSD",
                    "USDCAD", "NZDUSD", "EURSEK", "EURNOK", "USDNOK",
                    "EURJPY", "GBPJPY", "AUDJPY", "EURAUD", "EURGBP",
                    "USDSEK"
                };

                string upper = input.ToUpper();
                foreach (var pair in commonPairs)
                {
                    if (upper.Contains(pair))
                    {
                        LogDebug($"DEBUG: Found currency pair: '{pair}'");
                        return pair;
                    }
                }

                var match = Regex.Match(upper, @"\b([A-Z]{3})\s*[/]?\s*([A-Z]{3})\b");
                if (match.Success)
                {
                    string result = match.Groups[1].Value + match.Groups[2].Value;
                    LogDebug($"DEBUG: Regex found currency pair: '{result}'");
                    return result;
                }

                LogDebug($"DEBUG: No currency pair found, using default: EURUSD");
                return "EURUSD"; // Default
            }
            catch (Exception ex)
            {
                LogDebug($"DEBUG: Exception in ExtractCurrencyPair: {ex.Message}");
                throw;
            }
        }

        // === Expiry extraction ===
        private string ExtractExpiry(string input)
        {
            try
            {
                LogDebug($"DEBUG: ExtractExpiry input: '{input}'");

                var monthNames = new[] { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
                var fullMonthNames = new[] { "JANUARY", "FEBRUARY", "MARCH", "APRIL", "MAY", "JUNE", "JULY", "AUGUST", "SEPTEMBER", "OCTOBER", "NOVEMBER", "DECEMBER" };

                // Full date with year: "11 nov 2025"
                var dateMatch = Regex.Match(input, @"\b(?<day>\d{1,2})\s+(?<month>[A-Za-z]+)\s+(?<year>\d{4})\b", RegexOptions.IgnoreCase);
                if (dateMatch.Success)
                {
                    string day = dateMatch.Groups["day"].Value;
                    string month = dateMatch.Groups["month"].Value.ToUpper();
                    string year = dateMatch.Groups["year"].Value;

                    LogDebug($"DEBUG: Matched date - day: '{day}', month: '{month}', year: '{year}'");

                    for (int i = 0; i < fullMonthNames.Length; i++)
                    {
                        if (month == fullMonthNames[i])
                        {
                            month = monthNames[i];
                            break;
                        }
                    }

                    if (month.Length > 3) month = month.Substring(0, 3).ToUpper();

                    int dayInt = int.Parse(day);
                    int yearInt = int.Parse(year);
                    int shortYear = yearInt % 100;

                    string result = $"{dayInt:D2}{month}{shortYear:D2}";
                    LogDebug($"DEBUG: Final expiry: '{result}'");
                    return result;
                }

                // Bloomberg style: 17Sep25
                var bloombergDateMatch = Regex.Match(input, @"\b(?<day>\d{1,2})(?<month>[A-Za-z]{3})(?<year>\d{2})\b", RegexOptions.IgnoreCase);
                if (bloombergDateMatch.Success)
                {
                    string day = bloombergDateMatch.Groups["day"].Value;
                    string month = bloombergDateMatch.Groups["month"].Value.ToUpper();
                    string year = bloombergDateMatch.Groups["year"].Value;
                    return $"{int.Parse(day):D2}{month}{year}";
                }

                // FX date without year: 14Oct
                var fxDateMatch = Regex.Match(input, @"\b(?<day>\d{1,2})(?<month>[A-Za-z]{3})\b", RegexOptions.IgnoreCase);
                if (fxDateMatch.Success)
                {
                    string day = fxDateMatch.Groups["day"].Value;
                    string month = fxDateMatch.Groups["month"].Value.ToUpper();

                    int currentYear = DateTime.Now.Year;
                    int shortYear = currentYear % 100;

                    int monthIndex = Array.IndexOf(monthNames, month);
                    DateTime targetDate = (monthIndex == -1)
                        ? new DateTime(currentYear, 1, int.Parse(day))
                        : new DateTime(currentYear, monthIndex + 1, int.Parse(day));

                    if (targetDate < DateTime.Now.AddDays(-30))
                        currentYear++;

                    shortYear = currentYear % 100;
                    return $"{int.Parse(day):D2}{month}{shortYear:D2}";
                }

                // Tenor: 3M, 2Y
                var tenorMatch = Regex.Match(input, @"\b(\d+)\s*(mth|[DWMY])\b", RegexOptions.IgnoreCase);
                if (tenorMatch.Success)
                {
                    string number = tenorMatch.Groups[1].Value;
                    string period = tenorMatch.Groups[2].Value.ToUpper();
                    if (period == "MTH") period = "M";
                    return number + period;
                }

                return "3M"; // Default
            }
            catch (Exception ex)
            {
                LogDebug($"DEBUG: Exception in ExtractExpiry: {ex.Message}");
                throw;
            }
        }

        // === Normalization helpers ===
        private string NormalizeOVMLDates(string ovml)
        {
            if (string.IsNullOrWhiteSpace(ovml))
                return ovml;

            var parts = ovml.Split(' ');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Contains("/") || Regex.IsMatch(parts[i], @"\d{1,2}[A-Za-z]{3}\d{2}"))
                {
                    string normalized = TryNormalizeDate(parts[i]);
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        parts[i] = normalized;
                    }
                }
            }
            return string.Join(" ", parts);
        }

        private string NormalizeExpiry(string expiry)
        {
            if (string.IsNullOrWhiteSpace(expiry))
                return expiry;

            var normalized = TryNormalizeDate(expiry);
            if (!string.IsNullOrEmpty(normalized))
                return normalized;

            return expiry;
        }

        private string TryNormalizeDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            string[] formats = { "ddMMMyy", "dMMMyy", "ddMMMyyyy", "M/d/yy", "MM/dd/yy", "M/d/yyyy", "MM/dd/yyyy" };
            if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out DateTime dt))
            {
                return dt.ToString("MM/dd/yy", CultureInfo.InvariantCulture);
            }

            if (DateTime.TryParse(raw, out dt))
            {
                return dt.ToString("MM/dd/yy", CultureInfo.InvariantCulture);
            }

            return null;
        }
    }

    public class MarketEnhancementResult
    {
        public string EnhancedInput { get; set; }
        public string DeterminedType { get; set; } // "CALL" or "PUT"
        public double OriginalStrike { get; set; }
        public string CurrencyPair { get; set; }
    }

    public class TradeParseResult
    {
        public string OVML { get; set; }
        public string Underlying { get; set; }
        public int LegCount { get; set; }
        public string Expiry { get; set; }
        public string ParseMethod { get; set; }
        public string AdditionalInfo { get; set; } // For AI responses, error messages, etc.

        public TradeParseResult()
        {
            OVML = "";
            Underlying = "";
            LegCount = 1;
            Expiry = "";
            ParseMethod = "";
            AdditionalInfo = "";
        }
    }
}
