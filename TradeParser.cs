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
                    Expiry = ExtractExpiry(input),
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
                    foreach (Group group in match.Groups)
                    {
                        if (group.Success && !string.IsNullOrEmpty(group.Name) && group.Name != "0")
                        {
                            LogDebug($"DEBUG: Group '{group.Name}': '{group.Value}'");
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
                        spot = " SP" + spotMatch.Groups["spot"].Value;
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
                                    spot = " SP" + liveSpot.Value.ToString("0.####", CultureInfo.InvariantCulture);
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
                                              spot;
                                break;

                            case "PutSpread_Market":
                                LogDebug($"DEBUG: Processing PutSpread_Market pattern");
                                result.LegCount = 2;
                                result.OVML = $"OVML {result.Underlying} 2L S,B " +
                                              $"{match.Groups["strike1"].Value}P,{match.Groups["strike2"].Value}P " +
                                              $"{result.Expiry} N{match.Groups["notional"].Value}M,{match.Groups["notional"].Value}M" +
                                              spot;
                                break;

                            case "CallSpread_Market":
                                LogDebug($"DEBUG: Processing CallSpread_Market pattern");
                                result.LegCount = 2;
                                result.OVML = $"OVML {result.Underlying} 2L B,S " +
                                              $"{match.Groups["strike1"].Value}C,{match.Groups["strike2"].Value}C " +
                                              $"{result.Expiry} N{match.Groups["notional"].Value}M,{match.Groups["notional"].Value}M" +
                                              spot;
                                break;

                            case "RiskReversal_Market":
                                LogDebug($"DEBUG: Processing RiskReversal_Market pattern");
                                result.LegCount = 2;
                                result.OVML = $"OVML {result.Underlying} 2L B,S " +
                                              $"{match.Groups["strike1"].Value}P,{match.Groups["strike2"].Value}C " +
                                              $"{result.Expiry} N{match.Groups["notional"].Value}M,{match.Groups["notional"].Value}M" +
                                              spot;
                                break;

                            case "Vanilla":
                                LogDebug($"DEBUG: Processing Vanilla pattern");
                                result.LegCount = 1;
                                result.OVML = $"OVML {result.Underlying} 1L " +
                                              (match.Groups["side"].Value.ToLower() == "buy" ? "B" : "S") +
                                              $" {match.Groups["strike"].Value}{match.Groups["type"].Value.Substring(0, 1).ToUpper()} " +
                                              $"{result.Expiry} N{match.Groups["notional"].Value}M" +
                                              spot;
                                break;

                            case "Simple_Vanilla":
                                LogDebug($"DEBUG: Processing Simple_Vanilla pattern");
                                result.LegCount = 1;
                                string side = match.Groups["side"].Value.ToLower() == "buy" ? "B" : "S";
                                string optionType = match.Groups["type"].Value.Substring(0, 1).ToUpper();
                                result.OVML = $"OVML {result.Underlying} 1L {side} " +
                                              $"{match.Groups["strike"].Value}{optionType} " +
                                              $"{result.Expiry} N{match.Groups["notional"].Value}M" +
                                              spot;
                                break;

                            case "RiskReversal_PutCall":
                                LogDebug($"DEBUG: Processing RiskReversal_PutCall pattern");
                                result.LegCount = 2;
                                result.OVML = RegexTradePatterns.BuildRiskReversalOVML(result.Underlying, match, result.Expiry, spot.Replace(" SP", ""));
                                break;

                            case "RiskReversal_CallPut":
                                LogDebug($"DEBUG: Processing RiskReversal_CallPut pattern");
                                result.LegCount = 2;
                                result.OVML = RegexTradePatterns.BuildRiskReversalOVML(result.Underlying, match, result.Expiry, spot.Replace(" SP", ""));
                                break;


                            case "Strangle_Long":
                                LogDebug($"DEBUG: Processing Strangle_Long pattern");
                                result.LegCount = 2;
                                result.OVML = $"OVML {result.Underlying} 2L B,B " +
                                              $"{match.Groups["strike1"].Value}P,{match.Groups["strike2"].Value}C " +
                                              $"{result.Expiry} N{match.Groups["notional1"].Value}M,{match.Groups["notional2"].Value}M" +
                                              spot;
                                break;

                            case "Strangle_Short":
                                LogDebug($"DEBUG: Processing Strangle_Short pattern");
                                result.LegCount = 2;
                                result.OVML = $"OVML {result.Underlying} 2L S,S " +
                                              $"{match.Groups["strike1"].Value}P,{match.Groups["strike2"].Value}C " +
                                              $"{result.Expiry} N{match.Groups["notional1"].Value}M,{match.Groups["notional2"].Value}M" +
                                              spot;
                                break;

                            case "Seagull":
                                LogDebug($"DEBUG: Processing Seagull pattern");
                                result.LegCount = 3;
                                result.OVML = $"OVML {result.Underlying} 3L B,S,S " +
                                              $"{match.Groups["strike1"].Value}P,{match.Groups["strike2"].Value}P,{match.Groups["strike3"].Value}C " +
                                              $"{result.Expiry} N{match.Groups["notional1"].Value}M,{match.Groups["notional2"].Value}M,{match.Groups["notional3"].Value}M" +
                                              spot;
                                break;

                            case "Seagull_CallLed":
                                LogDebug($"DEBUG: Processing Seagull_CallLed pattern");
                                result.LegCount = 3;
                                result.OVML = $"OVML {result.Underlying} 3L B,S,S " +
                                              $"{match.Groups["strike1"].Value}C,{match.Groups["strike2"].Value}C,{match.Groups["strike3"].Value}P " +
                                              $"{result.Expiry} N{match.Groups["notional1"].Value}M,{match.Groups["notional2"].Value}M,{match.Groups["notional3"].Value}M" +
                                              spot;
                                break;

                            case "Seagull_BuyPutSellPutSellCall":
                                LogDebug($"DEBUG: Processing Seagull_BuyPutSellPutSellCall pattern");
                                result.LegCount = 3;
                                result.OVML = $"OVML {result.Underlying} 3L B,S,S " +
                                              $"{match.Groups["strike1"].Value}P,{match.Groups["strike2"].Value}P,{match.Groups["strike3"].Value}C " +
                                              $"{result.Expiry} N{match.Groups["notional1"].Value}M,{match.Groups["notional2"].Value}M,{match.Groups["notional3"].Value}M" +
                                              spot;
                                break;

                            default:
                                LogDebug($"DEBUG: Unknown pattern name: {pattern.Name}");
                                throw new Exception($"Unknown pattern: {pattern.Name}");
                        }

                        // Normalize date format in OVML before returning
                        result.OVML = NormalizeOVMLDates(result.OVML);

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

                // Normalize OVML dates
                aiResult.OVML = NormalizeOVMLDates(aiResult.OVML);

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
                    Expiry = ExtractExpiry(input),
                    ParseMethod = "AI-Error"
                };

                LogDebug($"DEBUG: AI Error - ParseMethod: '{errorResult.ParseMethod}'");
                LogDebug($"DEBUG: AI Error - OVML: '{errorResult.OVML}'");

                _cache[input] = errorResult; // Cache error result
                return errorResult;
            }
        }

        // OpenAI integration
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

        private string ExtractExpiry(string input)
        {
            try
            {
                LogDebug($"DEBUG: ExtractExpiry input: '{input}'");

                var monthNames = new[] { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
                var fullMonthNames = new[] { "JANUARY", "FEBRUARY", "MARCH", "APRIL", "MAY", "JUNE", "JULY", "AUGUST", "SEPTEMBER", "OCTOBER", "NOVEMBER", "DECEMBER" };

                // FIRST: Format "2 feb 2026" - Full date with year (MOVED TO FIRST POSITION)
                var dateMatch = Regex.Match(input, @"\b(?<day>\d{1,2})\s+(?<month>[A-Za-z]+)\s+(?<year>\d{4})\b", RegexOptions.IgnoreCase);
                if (dateMatch.Success)
                {
                    string day = dateMatch.Groups["day"].Value;
                    string month = dateMatch.Groups["month"].Value.ToUpper();
                    string year = dateMatch.Groups["year"].Value;

                    LogDebug($"DEBUG: Matched date - day: '{day}', month: '{month}', year: '{year}'");

                    // Convert full month names to 3-letter abbreviations
                    for (int i = 0; i < fullMonthNames.Length; i++)
                    {
                        if (month == fullMonthNames[i])
                        {
                            month = monthNames[i];
                            break;
                        }
                    }

                    // Ensure month is 3 characters max
                    if (month.Length > 3)
                    {
                        month = month.Substring(0, 3).ToUpper();
                    }

                    LogDebug($"DEBUG: Converted month: '{month}'");

                    int dayInt = int.Parse(day);
                    int yearInt = int.Parse(year);
                    int shortYear = yearInt % 100;

                    string result = $"{dayInt:D2}{month}{shortYear:D2}";
                    LogDebug($"DEBUG: Final expiry: '{result}'");
                    return result;
                }

                // SECOND: Bloomberg-style date: 17Sep25
                var bloombergDateMatch = Regex.Match(input, @"\b(?<day>\d{1,2})(?<month>[A-Za-z]{3})(?<year>\d{2})\b", RegexOptions.IgnoreCase);
                if (bloombergDateMatch.Success)
                {
                    string day = bloombergDateMatch.Groups["day"].Value;
                    string month = bloombergDateMatch.Groups["month"].Value.ToUpper();
                    string year = bloombergDateMatch.Groups["year"].Value;

                    LogDebug($"DEBUG: Matched Bloomberg date - day: '{day}', month: '{month}', year: '{year}'");
                    return $"{int.Parse(day):D2}{month}{year}";
                }

                // THIRD: FX Market date format: 14Oct (day + 3-letter month, no year) - MOVED TO THIRD POSITION
                var fxDateMatch = Regex.Match(input, @"\b(?<day>\d{1,2})(?<month>[A-Za-z]{3})\b", RegexOptions.IgnoreCase);
                if (fxDateMatch.Success)
                {
                    string day = fxDateMatch.Groups["day"].Value;
                    string month = fxDateMatch.Groups["month"].Value.ToUpper();

                    LogDebug($"DEBUG: Matched FX date - day: '{day}', month: '{month}' (assuming current year)");

                    // Get current year dynamically
                    int currentYear = DateTime.Now.Year;
                    int shortYear = currentYear % 100;

                    // Handle year rollover logic
                    DateTime currentDate = DateTime.Now;
                    DateTime targetDate;

                    // Parse the month to get the month number
                    int monthIndex = Array.IndexOf(monthNames, month);
                    if (monthIndex == -1)
                    {
                        LogDebug($"DEBUG: Invalid month '{month}', defaulting to current year");
                        targetDate = new DateTime(currentYear, 1, int.Parse(day)); // Default to January if month not found
                    }
                    else
                    {
                        targetDate = new DateTime(currentYear, monthIndex + 1, int.Parse(day));
                    }

                    // If the target date is in the past (more than 30 days ago), assume next year
                    if (targetDate < currentDate.AddDays(-30))
                    {
                        currentYear++;
                        shortYear = currentYear % 100;
                        LogDebug($"DEBUG: Date appears to be in the past, using next year: {currentYear}");
                    }

                    string result = $"{int.Parse(day):D2}{month}{shortYear:D2}";
                    LogDebug($"DEBUG: FX date result: '{result}' (year: {currentYear})");
                    return result;
                }

                // FOURTH: Tenor: 3M, 2Y, etc.
                var tenorMatch = Regex.Match(input, @"\b(\d+)\s*(mth|[DWMY])\b", RegexOptions.IgnoreCase);
                if (tenorMatch.Success)
                {
                    string number = tenorMatch.Groups[1].Value;
                    string period = tenorMatch.Groups[2].Value.ToUpper();

                    // Convert "mth" to "M"
                    if (period == "MTH") period = "M";

                    string result = number + period;
                    LogDebug($"DEBUG: Found tenor: '{result}'");
                    return result;
                }

                LogDebug($"DEBUG: No expiry found, using default: 3M");
                return "3M"; // Default
            }
            catch (Exception ex)
            {
                LogDebug($"DEBUG: Exception in ExtractExpiry: {ex.Message}");
                throw;
            }
        }

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