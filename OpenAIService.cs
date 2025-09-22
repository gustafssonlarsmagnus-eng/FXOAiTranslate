using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FXOAiTranslator
{
    public class OpenAIService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string OPENAI_API_URL = "https://api.openai.com/v1/chat/completions";

        public OpenAIService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        }

        public async Task<OpenAIResponse> GetChatCompletion(string prompt, string model = "gpt-4")
        {
            var requestBody = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = "You are an expert FX options trading assistant. Convert natural language trading requests into Bloomberg OVML format." },
                    new { role = "user", content = prompt }
                },
                max_tokens = 500,
                temperature = 0.1
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(OPENAI_API_URL, content);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"OpenAI API error: {response.StatusCode} - {responseString}");
            }

            return JsonSerializer.Deserialize<OpenAIResponse>(responseString);
        }
    }

    public class OpenAIResponse
    {
        public Choice[] choices { get; set; }
    }

    public class Choice
    {
        public Message message { get; set; }
    }

    public class Message
    {
        public string content { get; set; }
    }

    public class HybridPatternLearner
    {
        private readonly OpenAIService _openAI;
        private readonly string _patternsFilePath;
        private List<LearnedPattern> _learnedPatterns;
        private readonly BloombergService _bloombergService;
        private readonly TradeSanityChecker _sanityChecker;

        public HybridPatternLearner(OpenAIService openAI, BloombergService bloombergService, string patternsFilePath = "learned_patterns.json")
        {
            _openAI = openAI;
            _bloombergService = bloombergService;
            _patternsFilePath = patternsFilePath;
            _sanityChecker = new TradeSanityChecker(openAI, bloombergService); // ADD THIS LINE
            LoadLearnedPatterns();
        }

        public async Task<TradeParseResult> ParseWithAI(string input, string underlying, string expiry, string explicitSpot = "")


        {
            try
            {
                var learnedResult = await TryLearnedPatterns(input, underlying, expiry);
                if (learnedResult != null)
                {
                    Console.WriteLine($"[AI] Used learned pattern: {learnedResult.ParseMethod}");
                    return learnedResult;
                }

                Console.WriteLine("[AI] No learned pattern found, calling OpenAI...");
                // Fetch live spot rate BEFORE calling AI
                var liveSpot = await GetDefaultSpotRateAsync(underlying);

                string spotInfo;
                string spotSource;

                if (!string.IsNullOrEmpty(explicitSpot))
                {
                    spotInfo = explicitSpot;
                    spotSource = "user input (s.ref / spot ref)";
                }
                else if (!string.IsNullOrEmpty(liveSpot))
                {
                    spotInfo = liveSpot;
                    spotSource = "Bloomberg API";
                }
                else
                {
                    spotInfo = "9.8000"; // fallback
                    spotSource = "default fallback";
                }

                Console.WriteLine($"DEBUG: Spot source = {spotSource}, value = {spotInfo}");


                var prompt = $@"You are an expert FX options trader and OVML parser. Convert this natural language trading request into STRICT Bloomberg OVML format.

Input: ""{input}""
LIVE SPOT RATE for {underlying}: {spotInfo} (Use this exact rate for strike comparison)

MANDATORY OVML SYNTAX (Bloomberg Terminal Official):
- Single Leg: OVML (currency pair) (expiry) (call/put) (strike) (buy/sell) (notional amount) (option style code) [SP(spot)]
- Multi-Leg: OVML (currency pair) (expiry) (legs)L (directions) (strikes) N(notionals) (style) [SP(spot)]

CRITICAL FORMAT RULES:
1. DATE/TENOR FORMAT:
   - Allow Bloomberg tenor shorthand (1W, 2M, 3M, 6M, 1Y) or explicit dates (MM/dd/yy).
   - If input is a tenor like ""3M"", keep it as ""3M"" in output.
   - Today’s date is {DateTime.Now:MM/dd/yyyy} for reference.

2. CURRENCY PAIR: Two 3-letter ISO codes without separator (EURUSD, USDNOK, EURSEK).

3. OPTION TYPE: C (call) or P (put) only.

4. STRIKE FORMAT:
   - Numeric: 10.0000 (trim unnecessary zeros but keep up to 4 decimals).
   - Delta: DS25 (for 25 delta), DF25 (forward delta).
   - ATM: If input says ATM, use literal ""ATM"".

5. DIRECTION: B (buy) or S (sell) only.

6. NOTIONAL: N + amount + M (e.g., N100M).
   - ""100mm"" → N100M
   - ""50 mio"" → N50M
   - ""25M"" → N25M
   - Multiple notionals for multi-leg: N100M,50M
   - Default to N10M per leg if not specified.

7. OPTION STYLE: VA (vanilla), DKI (double knock-in), DKO (double knock-out).
   - Default to VA unless specified.

8. SPOT REFERENCE: SP + rate (e.g., SP9.8190).
   - Always at the end if provided.
   - Omit if no spot is given.

9. MULTI-LEG STRUCTURES (always one OVML line):
   - Risk Reversal: Buy Call + Sell Put (or opposite).
   - Call Spread: Buy lower strike Call + Sell higher strike Call.
   - Put Spread: Buy higher strike Put + Sell lower strike Put.
   - Straddle: Buy Call + Buy Put (same strike).
   - Strangle: Buy Call + Buy Put (different strikes).
   - Collar: Buy Put + Sell Call (or as described).
   - Directions: comma-separated (B,S).
   - Strikes: comma-separated (e.g., 11.50P,11.30P).
   - Notionals: comma-separated (e.g., N25M,25M).

10. LANGUAGE SUPPORT:
    - Swedish: säljer=sell, köper=buy, mio=million, mån=months.

11. SHORTHAND MAPPINGS:
    - ""PS"" = Put Spread (buy higher strike Put, sell lower strike Put).
    - ""CS"" = Call Spread (buy lower strike Call, sell higher strike Call).
    - These shorthands ALWAYS override other interpretations.


EXAMPLES:
Input: ""USDNOK 1 week 10.00 call in 100mm, spot ref 9.8190""
Output: OVML USDNOK 1W C 10.0000 B N100M VA SP9.8190

Input: ""EURSEK 3M buy 11.50 put 50M, sell 11.80 call 50M""
Output: OVML EURSEK 3M 2L B,S 11.5000P,11.8000C N50M,50M VA

Input: ""EURUSD risk reversal, buy call 1.10, sell put 1.05, 100M each, spotref 1.0833""
Output: OVML EURUSD 1M 2L B,S 1.1000C,1.0500P N100M,100M VA SP1.0833

Input: ""3-month EUR put spread with 11.50 and 11.30 in 10 mio""
Output: OVML EURNOK 3M 2L B,S 11.5000P,11.3000P N10M,10M VA

Input: ""USDSEK call spread 9.20-9.40 2 months""
Output: OVML USDSEK 2M 2L B,S 9.2000C,9.4000C N10M,10M VA

Input: ""6M GBPNOK call spread buy 15.20 sell 15.80""
Output: OVML GBPNOK 6M 2L B,S 15.2000C,15.8000C N10M,10M VA

Input: ""Straddle EURUSD ATM 1M 25M each""
Output: OVML EURUSD 1M 2L B,B ATM C,ATM P N25M,25M VA

STRICT REQUIREMENTS:
- Always produce exactly one OVML line.
- Use single-line multi-leg format for all strategies.
- Keep tenor notation (1W, 3M, 6M) if given.
- Do not output explanations, only the OVML line.


";


                var response = await _openAI.GetChatCompletion(prompt);
                var aiResponse = response.choices[0].message.content.Trim();

                Console.WriteLine($"[AI] Raw response: {aiResponse}");

                var ovmlMatch = Regex.Match(aiResponse, @"OVML\s+[^\r\n]+");
                if (ovmlMatch.Success)
                {
                    var ovml = ovmlMatch.Value.Trim();

                    // normalize SPx.x
                    ovml = Regex.Replace(ovml, @"\bv(\d+\.\d+)", "SP$1");

                    // Add post-processing correction for option type
                    var ovmlParts = ovml.Split(' ');
                    if (ovmlParts.Length >= 4 && !string.IsNullOrEmpty(liveSpot))
                    {
                        if (double.TryParse(ovmlParts[4], out double strike) && double.TryParse(liveSpot, out double spot))
                        {
                            bool isCall = ovml.Contains(" C ");
                            bool shouldBeCall = strike > spot;

                            if (isCall != shouldBeCall)
                            {
                                ovml = ovml.Replace(isCall ? " C " : " P ", shouldBeCall ? " C " : " P ");
                                Console.WriteLine($"[AI] Corrected option type: strike {strike} vs spot {spot} → {(shouldBeCall ? "CALL" : "PUT")}");
                            }
                        }
                    }

                    // add live spot if missing
                    if (!ovml.Contains("SP"))
                    {
                        var liveRate = await GetDefaultSpotRateAsync(underlying);
                        if (!string.IsNullOrEmpty(liveRate))
                        {
                            ovml += $" SP{liveRate}";
                            Console.WriteLine($"[AI] Added live Bloomberg spot rate for {underlying}: SP{liveRate}");
                        }
                    }

                    var result = new TradeParseResult
                    {
                        OVML = ovml,
                        Underlying = ExtractUnderlyingFromOVML(ovml),
                        Expiry = ExtractExpiryFromOVML(ovml),
                        LegCount = ExtractLegCountFromOVML(ovml),
                        ParseMethod = "AI-Success",
                        AdditionalInfo = aiResponse
                    };

                    // ADD SANITY CHECK HERE:
                    try
                    {
                        var sanityCheck = await _sanityChecker.ValidateTradeAsync(input, result);
                        result.ValidationResult = sanityCheck;

                        if (sanityCheck.IsValid)
                        {
                            result.ParseMethod = $"AI-Success (Validated - {sanityCheck.Confidence:P0})";
                            Console.WriteLine($"[AI] Sanity check PASSED: {sanityCheck.Reason}");

                            // ADD THIS LINE:
                            LearnFromSuccessfulExample(input, result);
                        }
                        else
                        {
                            result.ParseMethod = $"AI-Warning (Failed Validation - {sanityCheck.Confidence:P0})";
                            Console.WriteLine($"[AI] Sanity check FAILED: {sanityCheck.Reason}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[AI] Sanity check error: {ex.Message}");
                        // Keep original result if sanity check fails
                    }

                    return result;
                }

                return new TradeParseResult
                {
                    OVML = aiResponse,
                    Underlying = underlying,
                    Expiry = expiry,
                    ParseMethod = "AI-Raw",
                    AdditionalInfo = "Returned raw AI response since no OVML detected"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] Error: {ex.Message}");
                return new TradeParseResult
                {
                    OVML = "",
                    Underlying = underlying,
                    Expiry = expiry,
                    ParseMethod = "AI-Error",
                    AdditionalInfo = ex.Message
                };
            }
        }

        private async Task<string> GetDefaultSpotRateAsync(string underlying)
        {
            try
            {
                if (_bloombergService != null)
                {
                    var spotRate = await _bloombergService.GetSpotRate(underlying);
                    if (spotRate.HasValue)
                    {
                        string liveRate = spotRate.Value.ToString("F4");
                        Console.WriteLine($"[AI] Using live Bloomberg spot rate for {underlying}: {liveRate}");
                        return liveRate;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] Could not fetch live rate for {underlying}: {ex.Message}");
            }
            return "";
        }

        private async Task<TradeParseResult> TryLearnedPatterns(string input, string underlying, string expiry)
        {
            foreach (var pattern in _learnedPatterns.OrderByDescending(p => p.UsageCount))
            {
                try
                {
                    var regex = new Regex(pattern.RegexPattern, RegexOptions.IgnoreCase);
                    var match = regex.Match(input);

                    if (match.Success)
                    {
                        pattern.UsageCount++;
                        SaveLearnedPatterns();

                        // Execute the learned logic instead of using a template
                        var result = await ExecuteLearnedLogic(pattern, match, input, underlying, expiry);

                        if (result != null)
                        {
                            result.ParseMethod = $"Learned-{pattern.Name}";
                            return result;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AI] Error applying learned pattern {pattern.Name}: {ex.Message}");
                }
            }
            return null;
        }

        private async Task<TradeParseResult> ExecuteLearnedLogic(LearnedPattern pattern, Match match, string input, string underlying, string expiry)
        {
            try
            {
                if (pattern.Logic.StrategyType == "VANILLA")
                {
                    // Extract components from the match using learned rules
                    var components = ExtractComponentsFromMatch(pattern.Logic, match);

                    // Get live spot rate (replicating AI logic)
                    string spotRate = "";
                    if (pattern.Logic.RequiresSpotLookup)
                    {
                        spotRate = await GetDefaultSpotRateAsync(underlying);
                    }

                    // Determine option type using learned logic
                    string optionType = "C"; // Default
                    if (pattern.Logic.MoneynessDetermination == "STRIKE_VS_SPOT" && !string.IsNullOrEmpty(spotRate))
                    {
                        if (double.TryParse(components["STRIKE"], out double strike) &&
                            double.TryParse(spotRate, out double spot))
                        {
                            optionType = strike > spot ? "C" : "P"; // ITM put, OTM call
                        }
                    }

                    // Build OVML using extracted values and determined logic
                    var ovml = $"OVML {underlying} {expiry} {optionType} {components["STRIKE"]} {pattern.Logic.DefaultDirection} N{components["NOTIONAL"]}M VA";

                    if (!string.IsNullOrEmpty(spotRate))
                    {
                        ovml += $" SP{spotRate}";
                    }

                    return new TradeParseResult
                    {
                        OVML = ovml,
                        Underlying = underlying,
                        Expiry = expiry,
                        LegCount = 1,
                        ParseMethod = $"Learned-{pattern.Name}",
                        AdditionalInfo = "Generated using learned logic pattern"
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] Error executing learned logic: {ex.Message}");
                return null;
            }
        }

        private Dictionary<string, string> ExtractComponentsFromMatch(PatternLogic logic, Match match)
        {
            var components = new Dictionary<string, string>();

            foreach (var rule in logic.ExtractionRules)
            {
                switch (rule)
                {
                    case "CURRENCY_FROM_GROUP_1":
                        components["CURRENCY"] = match.Groups[1].Value;
                        break;
                    case "EXPIRY_FROM_GROUP_2":
                        components["EXPIRY"] = match.Groups[2].Value;
                        break;
                    case "STRIKE_FROM_GROUP_3":
                        components["STRIKE"] = match.Groups[3].Value;
                        break;
                    case "NOTIONAL_FROM_GROUP_4":
                        components["NOTIONAL"] = match.Groups[4].Value;
                        break;
                }
            }

            return components;
        }

        public bool RemovePattern(string patternName)
        {
            try
            {
                var patternToRemove = _learnedPatterns.FirstOrDefault(p => p.Name == patternName);
                if (patternToRemove != null)
                {
                    _learnedPatterns.Remove(patternToRemove);
                    SaveLearnedPatterns();
                    Console.WriteLine($"[AI] Removed learned pattern: {patternName}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] Error removing pattern: {ex.Message}");
                return false;
            }
        
        }


        private string AdaptOVMLTemplate(string templateOVML, Match match, string underlying, string expiry)
        {
            var parts = templateOVML.Split(' ');
            if (parts.Length > 1) parts[1] = underlying;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].EndsWith("M") || parts[i].EndsWith("Y") || parts[i].Contains("/"))
                {
                    parts[i] = expiry;
                    break;
                }
            }
            return string.Join(" ", parts);
        }

        private void LoadLearnedPatterns()
        {
            try
            {
                if (File.Exists(_patternsFilePath))
                {
                    var json = File.ReadAllText(_patternsFilePath);
                    _learnedPatterns = JsonSerializer.Deserialize<List<LearnedPattern>>(json) ?? new List<LearnedPattern>();
                    Console.WriteLine($"[AI] Loaded {_learnedPatterns.Count} learned patterns");
                }
                else
                {
                    _learnedPatterns = new List<LearnedPattern>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] Error loading learned patterns: {ex.Message}");
                _learnedPatterns = new List<LearnedPattern>();
            }
        }

        private void SaveLearnedPatterns()
        {
            try
            {
                var json = JsonSerializer.Serialize(_learnedPatterns, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_patternsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] Error saving learned patterns: {ex.Message}");
            }
        }

        private string ExtractUnderlyingFromOVML(string ovml)
        {
            var parts = ovml.Split(' ');
            return parts.Length > 1 ? parts[1] : "EURUSD";
        }

        private string ExtractExpiryFromOVML(string ovml)
        {
            var parts = ovml.Split(' ');
            if (parts.Length < 3) return "3M";
            bool isMultiLeg = parts.Length > 2 && parts[2].EndsWith("L") && char.IsDigit(parts[2][0]);
            return isMultiLeg ? parts[5] : parts[2];
        }

        private int ExtractLegCountFromOVML(string ovml)
        {
            if (string.IsNullOrWhiteSpace(ovml))
                return 1;

            var parts = ovml.Split(' ');

            // 1. Look for explicit "2L", "3L", etc.
            for (int i = 2; i < Math.Min(parts.Length, 8); i++)
            {
                if (parts[i].EndsWith("L") && parts[i].Length > 1 && char.IsDigit(parts[i][0]))
                {
                    if (int.TryParse(parts[i].Substring(0, parts[i].Length - 1), out int legs))
                    {
                        return legs;
                    }
                }
            }

            // 2. Fallback: infer from comma-separated strikes
            // e.g. "11.5000P,11.3000P" → 2 legs
            var strikePart = parts.FirstOrDefault(p => p.Contains("P") || p.Contains("C"));
            if (!string.IsNullOrEmpty(strikePart) && strikePart.Contains(","))
            {
                var legs = strikePart.Split(',').Length;
                if (legs > 1) return legs;
            }

            // 3. Default: assume single leg
            return 1;
        }
        private void LearnFromSuccessfulExample(string input, TradeParseResult result)
        {
            try
            {
                // Analyze the successful example to extract the logic pattern
                var pattern = AnalyzeSuccessfulPattern(input, result);

                if (pattern != null)
                {
                    _learnedPatterns.Add(pattern);
                    SaveLearnedPatterns();
                    Console.WriteLine($"[AI] Auto-learned new logic pattern: {pattern.Name}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] Error learning pattern: {ex.Message}");
            }
        }

        private LearnedPattern AnalyzeSuccessfulPattern(string input, TradeParseResult result)
        {
            // Detect the input pattern type
            if (IsVanillaQuotePattern(input))
            {
                return new LearnedPattern
                {
                    Name = "Auto-Vanilla-Quote",
                    RegexPattern = @"^(\w{6})\s+(\d{1,2}\w{3}\d{2})\s+([\d.]+)\s+in\s+(\d+)m?io\s*(pls|please)?$",
                    Description = "Simple vanilla option quote request",
                    CreatedAt = DateTime.Now,
                    UsageCount = 1,
                    Logic = new PatternLogic
                    {
                        StrategyType = "VANILLA",
                        RequiresSpotLookup = true,
                        MoneynessDetermination = "STRIKE_VS_SPOT",
                        DefaultDirection = "B", // Quote = buy request
                        ExtractionRules = new List<string>
                {
                    "CURRENCY_FROM_GROUP_1",
                    "EXPIRY_FROM_GROUP_2",
                    "STRIKE_FROM_GROUP_3",
                    "NOTIONAL_FROM_GROUP_4"
                }
                    }
                };
            }

            // Could add detection for other pattern types here
            return null;
        }

        private bool IsVanillaQuotePattern(string input)
        {
            // Simple vanilla quote: currency + date + strike + "in" + notional + politeness
            return Regex.IsMatch(input, @"^\w{6}\s+\d{1,2}\w{3}\d{2}\s+[\d.]+\s+in\s+\d+m?io", RegexOptions.IgnoreCase);
        }

    }

    public class LearnedPattern
    {
        public string Name { get; set; }
        public string RegexPattern { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public int UsageCount { get; set; }
        public PatternLogic Logic { get; set; }
    }

    public class PatternLogic
    {
        public string StrategyType { get; set; }
        public bool RequiresSpotLookup { get; set; } = true;
        public string MoneynessDetermination { get; set; }
        public string DefaultDirection { get; set; } = "B";
        public List<string> ExtractionRules { get; set; } = new List<string>();
    }
}