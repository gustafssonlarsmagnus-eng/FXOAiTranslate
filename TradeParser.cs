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

                            case "Seagull_BuyPutSellPutSellCall":
                                LogDebug($"DEBUG: Processing Seagull_BuyPutSellPutSellCall pattern");
                                result.LegCount = 3;
                                result.OVML = $"OVML {result.Underlying} {result.Expiry} 3L B,S,S " +
                                              $"{match.Groups["strike1"].Value}P,{match.Groups["strike2"].Value}P,{match.Groups["strike3"].Value}C " +
                                              $"N{match.Groups["notional1"].Value}M,{match.Groups["notional2"].Value}M,{match.Groups["notional3"].Value}M" +
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
                            case "Straddle":
                                LogDebug("DEBUG: Processing Straddle pattern");
                                result.LegCount = 2;

                                // Side: default long (B,B); if 'sell' / 'sälj' → short (S,S)
                                {
                                    var sideToken = match.Groups["side"]?.Value?.ToLower() ?? "";
                                    string sidePair = (sideToken.StartsWith("sell") || sideToken.StartsWith("sälj")) ? "S,S" : "B,B";

                                    string n1 = match.Groups["notional1"].Value;
                                    string n2 = match.Groups["notional2"].Success ? match.Groups["notional2"].Value : n1;

                                    // Straddle: ATMS strikes, explicit types C,P
                                    result.OVML =
                                        $"OVML {result.Underlying} 2L {sidePair} ATMS, ATMS C,P {result.Expiry} N{n1}M,{n2}M VA" +
                                        (string.IsNullOrEmpty(spot) ? "" : $" SP{spot}");
                                }
                                break;

                            case "Strangle_Keyword":
                                LogDebug("DEBUG: Processing Strangle_Keyword pattern");
                                result.LegCount = 2;

                                {
                                    var sideToken = match.Groups["side"]?.Value?.ToLower() ?? "";
                                    string sidePair = (sideToken.StartsWith("sell") || sideToken.StartsWith("sälj")) ? "S,S" : "B,B";

                                    string n = match.Groups["notional"].Value;
                                    string kPut = match.Groups["strike1"].Value;
                                    string kCall = match.Groups["strike2"].Value;

                                    result.OVML =
                                        $"OVML {result.Underlying} 2L {sidePair} {kPut}P,{kCall}C {result.Expiry} N{n}M,{n}M" +
                                        (string.IsNullOrEmpty(spot) ? "" : $" SP{spot}");
                                }
                                break;

                            case "CallSpread_Market":
                                LogDebug($"DEBUG: Processing CallSpread_Market pattern");
                                result.LegCount = 2;

                                string notional = match.Groups["notional"].Value;
                                string strike1 = match.Groups["strike1"].Value;
                                string strike2 = match.Groups["strike2"].Value;

                                // For call spread: buy lower strike, sell higher strike
                                double s1 = double.Parse(strike1);
                                double s2 = double.Parse(strike2);

                                if (s1 < s2)
                                {
                                    // Standard order: buy lower, sell higher
                                    result.OVML = $"OVML {result.Underlying} {result.Expiry} 2L B,S " +
                                                  $"{strike1}C,{strike2}C N{notional}M,{notional}M VA" +
                                                  (string.IsNullOrEmpty(spot) ? "" : " SP" + spot);
                                }
                                else
                                {
                                    // Reverse order: buy higher, sell lower
                                    result.OVML = $"OVML {result.Underlying} {result.Expiry} 2L B,S " +
                                                  $"{strike2}C,{strike1}C N{notional}M,{notional}M VA" +
                                                  (string.IsNullOrEmpty(spot) ? "" : " SP" + spot);
                                }
                                break;
                            case "PutSpread_Short":
                                LogDebug("DEBUG: Processing PutSpread_Short pattern");
                                result.LegCount = 2;

                                // Normalize strikes (higher = buy put, lower = sell put)
                                double ps1 = double.Parse(match.Groups["strike1"].Value);
                                double ps2 = double.Parse(match.Groups["strike2"].Value);

                                string putLow = ps1 < ps2 ? match.Groups["strike1"].Value : match.Groups["strike2"].Value;
                                string putHigh = ps1 < ps2 ? match.Groups["strike2"].Value : match.Groups["strike1"].Value;

                                string notionalPS = match.Groups["notional"].Value;

                                result.OVML = $"OVML {result.Underlying} {result.Expiry} 2L B,S {putHigh}P,{putLow}P N{notionalPS}M,{notionalPS}M VA" +
                                              (string.IsNullOrEmpty(spot) ? "" : " SP" + spot);
                                break;

                            case "CallSpread_Short":
                                LogDebug("DEBUG: Processing CallSpread_Short pattern");
                                result.LegCount = 2;

                                // Normalize strikes (lower = buy call, higher = sell call)
                                double cs1 = double.Parse(match.Groups["strike1"].Value);
                                double cs2 = double.Parse(match.Groups["strike2"].Value);

                                string callLow = cs1 < cs2 ? match.Groups["strike1"].Value : match.Groups["strike2"].Value;
                                string callHigh = cs1 < cs2 ? match.Groups["strike2"].Value : match.Groups["strike1"].Value;

                                string notionalCS = match.Groups["notional"].Value;

                                result.OVML = $"OVML {result.Underlying} {result.Expiry} 2L B,S {callLow}C,{callHigh}C N{notionalCS}M,{notionalCS}M VA" +
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

            // Try to capture explicit spot from input
            string explicitSpot = "";
            var aiSpotMatch = RegexTradePatterns.SpotRegex.Match(input);
            if (aiSpotMatch.Success)
            {
                explicitSpot = aiSpotMatch.Groups["spot"].Value;
                LogDebug($"DEBUG: Explicit spot extracted for AI fallback: '{explicitSpot}'");
            }

            try
            {
                var aiResult = await ParseWithAI(input, explicitSpot);


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
        private async Task<TradeParseResult> ParseWithAI(string input, string explicitSpot = "")
        {
            if (_patternLearner == null)
            {
                await Task.Delay(10);
                throw new NotImplementedException("AI parser not configured - no OpenAI API key provided.");
            }

            string underlying = ExtractCurrencyPair(input);
            string expiry = ExtractExpiry(input);

            return await _patternLearner.ParseWithAI(input, underlying, expiry, explicitSpot);
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
            "USDSEK", "GBPNOK", "NOKSEK", "SEKEUR", "SEKNOK"
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

                // Clean input by removing option-related words AND buy/sell variants before currency extraction
                string cleanInput = Regex.Replace(input,
                    @"\b(put|call|spread|option|straddle|strangle|buy|sell|köp|köpa|köper|sälj|säljer|kjøp|kjøpe|kjøper|selg|selger|prisa|cs)\b",
                    " ",
                    RegexOptions.IgnoreCase);
                LogDebug($"DEBUG: Cleaned input for currency extraction: '{cleanInput}'");

                // Look for two 3-letter currency codes
                var matches = Regex.Matches(cleanInput.ToUpper(), @"\b([A-Z]{3})\b");
                if (matches.Count >= 2)
                {
                    string ccy1 = matches[0].Groups[1].Value;
                    string ccy2 = matches[1].Groups[1].Value;

                    // Validate they are actual currency codes (extended list)
                    string[] validCurrencies = {
                "EUR", "USD", "GBP", "JPY", "CHF", "AUD", "CAD", "NZD",
                "SEK", "NOK", "DKK", "PLN", "CZK", "HUF", "RUB", "CNY",
                "HKD", "SGD", "THB", "MXN", "ZAR", "BRL", "KRW", "INR"
            };

                    if (validCurrencies.Contains(ccy1) && validCurrencies.Contains(ccy2))
                    {
                        string result = ccy1 + ccy2;
                        LogDebug($"DEBUG: Extracted currency pair from clean input: '{result}'");
                        return result;
                    }
                }

                // Fallback: original regex pattern on original input
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
                LogDebug("DEBUG: EXPIRY METHOD UPDATED - Version 4.0");

                var monthNames = new[] { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };
                var fullMonthNames = new[] { "JANUARY", "FEBRUARY", "MARCH", "APRIL", "MAY", "JUNE", "JULY", "AUGUST", "SEPTEMBER", "OCTOBER", "NOVEMBER", "DECEMBER" };

                // Swedish months
                var swedishMonths = new[] { "JANUARI", "FEBRUARI", "MARS", "APRIL", "MAJ", "JUNI", "JULI", "AUGUSTI", "SEPTEMBER", "OKTOBER", "NOVEMBER", "DECEMBER" };
                var swedishShortMonths = new[] { "JAN", "FEB", "MAR", "APR", "MAJ", "JUN", "JUL", "AUG", "SEP", "OKT", "NOV", "DEC" };

                // Norwegian months
                var norwegianMonths = new[] { "JANUAR", "FEBRUAR", "MARS", "APRIL", "MAI", "JUNI", "JULI", "AUGUST", "SEPTEMBER", "OKTOBER", "NOVEMBER", "DESEMBER" };

                // Helper method to normalize month names
                string NormalizeMonth(string month)
                {
                    month = month.ToUpper();

                    // Check Norwegian months
                    for (int i = 0; i < norwegianMonths.Length; i++)
                    {
                        if (month == norwegianMonths[i]) return monthNames[i];
                    }

                    // Check Swedish months
                    for (int i = 0; i < swedishMonths.Length; i++)
                    {
                        if (month == swedishMonths[i]) return monthNames[i];
                    }

                    // Check Swedish short months
                    for (int i = 0; i < swedishShortMonths.Length; i++)
                    {
                        if (month == swedishShortMonths[i]) return monthNames[i];
                    }

                    // Check English months
                    for (int i = 0; i < fullMonthNames.Length; i++)
                    {
                        if (month == fullMonthNames[i]) return monthNames[i];
                    }

                    // If already 3-letter format, return as-is
                    if (month.Length == 3) return month;
                    if (month.Length > 3) return month.Substring(0, 3).ToUpper();

                    return month;
                }

                // 1. Full date with year: "11 nov 2025", "12e juni 2026", "2 feb 2026"
                LogDebug("DEBUG: Testing full date pattern...");
                var dateMatch = Regex.Match(input, @"\b(?<day>\d{1,2})e?\s+(?<month>[A-Za-zåäöæøé]+)\s+(?<year>\d{4})\b", RegexOptions.IgnoreCase);
                if (dateMatch.Success)
                {
                    string day = dateMatch.Groups["day"].Value;
                    string month = dateMatch.Groups["month"].Value;
                    string year = dateMatch.Groups["year"].Value;

                    LogDebug($"DEBUG: Full date pattern matched: {dateMatch.Value}");
                    LogDebug($"DEBUG: Matched date - day: '{day}', month: '{month}', year: '{year}'");

                    string normalizedMonth = NormalizeMonth(month);

                    int dayInt = int.Parse(day);
                    int yearInt = int.Parse(year);
                    int shortYear = yearInt % 100;

                    string result = $"{dayInt:D2}{normalizedMonth}{shortYear:D2}";
                    LogDebug($"DEBUG: Final expiry: '{result}'");
                    return result;
                }

                // 2. Bloomberg style: 17Sep25 - with month validation (English only)
                LogDebug("DEBUG: Testing Bloomberg date pattern...");
                var bloombergDateMatch = Regex.Match(input, @"\b(?<day>\d{1,2})(?<month>JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|SEPT|OCT|NOV|DEC)(?<year>\d{2})\b", RegexOptions.IgnoreCase);
                if (bloombergDateMatch.Success)
                {
                    LogDebug($"DEBUG: Bloomberg date pattern matched: {bloombergDateMatch.Value}");
                    string day = bloombergDateMatch.Groups["day"].Value;
                    string month = bloombergDateMatch.Groups["month"].Value.ToUpper();
                    string year = bloombergDateMatch.Groups["year"].Value;

                    if (month == "SEPT") month = "SEP";

                    return $"{int.Parse(day):D2}{month}{year}";
                }

                // 3. Day + month without year - multilingual support
                LogDebug("DEBUG: Testing day + month pattern...");
                var dayMonthMatch = Regex.Match(input, @"\b(?<day>\d{1,2})\s+(?<month>[A-Za-zåäöæøé]{3,})\b", RegexOptions.IgnoreCase);
                if (dayMonthMatch.Success)
                {
                    LogDebug($"DEBUG: Day + month pattern matched: {dayMonthMatch.Value}");
                    string day = dayMonthMatch.Groups["day"].Value;
                    string month = dayMonthMatch.Groups["month"].Value;

                    string normalizedMonth = NormalizeMonth(month);

                    // Skip if month normalization failed (not a real month)
                    if (normalizedMonth.Length != 3 || Array.IndexOf(monthNames, normalizedMonth) == -1)
                    {
                        LogDebug($"DEBUG: Skipping - '{month}' not recognized as a valid month");
                        // Continue to next pattern
                    }
                    else
                    {
                        int currentYear = DateTime.Now.Year;
                        int shortYear = currentYear % 100;

                        int monthIndex = Array.IndexOf(monthNames, normalizedMonth);
                        if (monthIndex >= 0)
                        {
                            DateTime target = new DateTime(currentYear, monthIndex + 1, int.Parse(day));
                            if (target < DateTime.Now.AddDays(-5))
                                currentYear++;
                        }

                        shortYear = currentYear % 100;
                        return $"{int.Parse(day):D2}{normalizedMonth}{shortYear:D2}";
                    }
                }

                // 4. FX date without year: 14Oct - with month validation (English only)
                LogDebug("DEBUG: Testing FX date pattern...");
                var fxDateMatch = Regex.Match(input, @"\b(?<day>\d{1,2})(?<month>JAN|FEB|MAR|APR|MAY|JUN|JUL|AUG|SEP|SEPT|OCT|NOV|DEC)\b", RegexOptions.IgnoreCase);
                if (fxDateMatch.Success)
                {
                    LogDebug($"DEBUG: FX date pattern matched: {fxDateMatch.Value}");
                    string day = fxDateMatch.Groups["day"].Value;
                    string month = fxDateMatch.Groups["month"].Value.ToUpper();

                    if (month == "SEPT") month = "SEP";

                    int currentYear = DateTime.Now.Year;
                    int shortYear = currentYear % 100;

                    int monthIndex = Array.IndexOf(monthNames, month);
                    if (monthIndex >= 0)
                    {
                        DateTime targetDate = new DateTime(currentYear, monthIndex + 1, int.Parse(day));
                        if (targetDate < DateTime.Now.AddDays(-30))
                            currentYear++;
                        shortYear = currentYear % 100;
                    }

                    return $"{int.Parse(day):D2}{month}{shortYear:D2}";
                }

                // 5. Tenor: 3M, 5MTH, 2Y, 10D, 2W - but NOT notional amounts like "500m"
                LogDebug("DEBUG: Testing tenor patterns...");

                // More restrictive: avoid matching large numbers that are likely notionals
                var tenorMatch = Regex.Match(
                    input,
                    @"\b(?<num>[1-9]\d{0,1})[\s-]*(?<unit>month|months|mth|mo|m|year|years|y|day|days|d|week|weeks|w)\b(?!\s*io)(?!\s*[A-Z]{6})",
                    RegexOptions.IgnoreCase
                );

                // Additional check: if number is > 99, it's likely a notional, not a tenor
                if (tenorMatch.Success)
                {
                    string number = tenorMatch.Groups["num"].Value;
                    string period = tenorMatch.Groups["unit"].Value.ToUpper();

                    int numValue = int.Parse(number);
                    if (numValue > 99)
                    {
                        LogDebug($"DEBUG: Skipping tenor match - number too large for tenor: {number}");
                    }
                    else
                    {
                        LogDebug($"DEBUG: Tenor match found - number: '{number}', period: '{period}'");

                        // Normalize period abbreviations
                        if (period.StartsWith("MONTH")) period = "M";
                        else if (period == "MTH") period = "M";
                        else if (period == "MO") period = "M";
                        else if (period.StartsWith("YEAR")) period = "Y";
                        else if (period.StartsWith("DAY")) period = "D";
                        else if (period.StartsWith("WEEK")) period = "W";

                        return number + period;
                    }
                }
                else
                {
                    LogDebug("DEBUG: No tenor match found");
                }

                LogDebug("DEBUG: No expiry patterns matched, using default");
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
        public string UBS { get; set; }
        public string Underlying { get; set; }
        public int LegCount { get; set; }
        public string Expiry { get; set; }
        public string ParseMethod { get; set; }
        public string AdditionalInfo { get; set; } // For AI responses, error messages, etc.

        public TradeParseResult()
        {
            OVML = "";
            UBS = "";
            Underlying = "";
            LegCount = 1;
            Expiry = "";
            ParseMethod = "";
            AdditionalInfo = "";
        }
    }
}
