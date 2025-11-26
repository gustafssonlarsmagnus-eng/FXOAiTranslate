using QuickFix.Fields;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
            _sanityChecker = new TradeSanityChecker(openAI, bloombergService);
            LoadLearnedPatterns();
            Console.WriteLine($"[AI] Pattern learner initialized with {_learnedPatterns.Count} patterns");
        }

        public async Task<TradeParseResult> ParseWithAI(string input, string underlying, string expiry, string explicitSpot = "", bool bypassPatternMatching = false)
        {
            Console.WriteLine("[AI] ParseWithAI called - VERSION 2025-09-29-NEW");
            try
            {
                LoadLearnedPatterns();

                bool patternMatched = false;
                string matchedPatternName = "";

                if (!bypassPatternMatching && _learnedPatterns != null && _learnedPatterns.Count > 0)
                {
                    Console.WriteLine($"[AI] Checking {_learnedPatterns.Count} learned patterns...");
                    foreach (var pattern in _learnedPatterns.OrderByDescending(p => p.UsageCount))
                    {
                        try
                        {
                            // Simple similarity check instead of regex
                            if (IsSimilarTrade(input, pattern.ExampleInput))
                            {
                                Console.WriteLine($"[AI] ✓ Similar pattern found: {pattern.Description}");
                                pattern.UsageCount++;
                                SaveLearnedPatterns();
                                patternMatched = true;
                                matchedPatternName = pattern.Name;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[AI] Error testing pattern: {ex.Message}");
                        }
                    }

                }
                else if (bypassPatternMatching)
                {
                    Console.WriteLine("[AI] Pattern matching bypassed - using AI directly");
                }

                var liveSpot = await GetDefaultSpotRateAsync(underlying);
                // ... rest of your existing code

                string spotInfo = !string.IsNullOrEmpty(explicitSpot) ? explicitSpot
                    : !string.IsNullOrEmpty(liveSpot) ? liveSpot
                    : "9.8000";

                var spotInstruction = string.IsNullOrEmpty(explicitSpot)
                    ? "NO spot reference in input - do NOT include SP in output"
                    : $"Spot reference in input: {explicitSpot} - include SP{explicitSpot} in output";

                // Then build the prompt ONCE
                var prompt = $@"You are an expert FX options trader and OVML parser. Convert this natural language trading request into STRICT Bloomberg OVML format.

TODAY'S DATE: {DateTime.Now:dddd, MMMM dd, yyyy}
CRITICAL: Never generate expiry dates in the past. All expiries must be FUTURE dates after {DateTime.Now:MMMM dd, yyyy}.

Input: ""{input}""

SPOT REFERENCE INSTRUCTION:
{spotInstruction}

LIVE SPOT RATE for {underlying}: {spotInfo}
(Use this ONLY to determine if an option is a call or put when not explicitly stated. Do NOT include in output unless instructed above.)

COMMON DATE INTERPRETATION ERRORS TO AVOID:
- If today is October 2025, June 2025 is IN THE PAST - DO NOT USE IT
- now likely means Nov Example:  ""6 now 2025"" should be interpreted as November 6
- When uncertain about a date, choose the NEXT occurrence of that date in the future


Input: ""{input}""
LIVE SPOT RATE for {underlying}: {spotInfo}

MANDATORY OVML SYNTAX:
- Single Leg: OVML (currency) (expiry) (direction) (strike)(C/P) N(notional)M (style) [SP(spot)]
- Multi-Leg: OVML (currency) (expiry) (legs)L (directions) (strikes) N(notionals) (style) [SP(spot)]

CRITICAL FORMAT RULES:
1. STRUCTURE: OVML (currency) (expiry) [legs] [directions] [strikes] [notionals] [style] [spot]
2. NOTIONAL: Always format as N(amount)M (e.g., N150M) - NEVER omit the N prefix
3. DIRECTION: Always B (buy) or S (sell) - place AFTER expiry, not before strikes
4. STYLE: Always include VA (Vanilla) or EU (European) at the end before spot reference

SINGLE LEG FORMAT:
OVML (currency) (expiry) (direction) (strike)(C/P) N(notional)M VA [SP(spot)]
Example: OVML NOKSEK 08/14/26 B 0.9500C N150M VA SP0.9463

EXPIRY FORMAT CRITICAL RULE:
The expiry MUST be normalized to MM/DD/YY format (e.g., 06/11/26, not 11Jun).
Position: OVML (currency) (MM/DD/YY) (rest of trade)
WRONG: OVML USDNOK 11Jun B 9.9000P 06/11/26 VA
RIGHT: OVML USDNOK 06/11/26 B 9.9000P N10M VA

CRITICAL: Single-leg OVML structure is ALWAYS:
OVML (currency) (expiry) (direction) (strike)(C/P) N(notional)M VA [SP(spot)]

NEVER use format: OVML (currency) (expiry) (direction) (strike)(C/P) (expiry) VA
The expiry must appear EXACTLY ONCE - immediately after the currency pair.

MULTI-LEG FORMAT:
OVML (currency) (expiry) (legs)L (directions) (strikes) N(notionals) VA [SP(spot)]
Example: OVML USDNOK 12/03/25 2L B,S 9.7500P,9.5000P N100M,150M VA SP10.3950

CRITICAL MULTI-LEG RULES:
1. PUT SPREAD: Buy higher strike put, Sell lower strike put → B,S (strikes high,low)
2. CALL SPREAD: Buy lower strike call, Sell higher strike call → B,S (strikes low,high)
3. NOTIONALS: Format as N(amount1)M,(amount2)M (e.g., N5M,20M)
4. Unless explicitly stated otherwise, each leg has the SAME notional amount

CRITICAL STRIKE ORDERING:
- Strikes MUST appear in the exact same order as listed in the input text
- The first leg mentioned is used for premium pricing, so order is economically significant
- Do NOT reorder strikes by size or any other logic
- Example: ""buy 10.40 and 10.80 put"" → B,B 10.4000P,10.8000P (NOT 10.8000P,10.4000P)

STRADDLE RULES:
- Keyword ""straddle"" indicates a straddle structure
- ALWAYS 2 legs: Buy Call + Buy Put at SAME strike (or Sell Call + Sell Put)
- Format: OVML (currency) (expiry) 2L B,B (strike)C,(strike)P N(notional)M,(notional)M VA [SP(spot)]
- CRITICAL: The strike value must be IDENTICAL for both legs (call and put)
- CRITICAL: ""per leg"" means each leg has that notional (not total)
- DEFAULT: If no buy/sell specified, assume LONG straddle (B,B)
- Examples:
  * ""USDSEK 1 yr straddle 9.3180 strike in 5m USD per leg"" → OVML USDSEK 1Y 2L B,B 9.3180C,9.3180P N5M,5M VA
  * ""buy 10m EURNOK 3 month straddle at 11.50"" → OVML EURNOK 3M 2L B,B 11.5000C,11.5000P N10M,10M VA
  * ""sell straddle GBPUSD 6M 1.2500 for 20m"" → OVML GBPUSD 6M 2L S,S 1.2500C,1.2500P N20M,20M VA

STRANGLE RULES:
- Similar to straddle but with DIFFERENT strikes
- 2 legs: Buy/Sell Call + Buy/Sell Put at different strikes
- Format: OVML (currency) (expiry) 2L B,B (putStrike)P,(callStrike)C N(notional)M,(notional)M VA
- CRITICAL: Put strike < Call strike typically
- Example: ""buy 10m strangle EURUSD 3M 1.08 put / 1.12 call"" → OVML EURUSD 3M 2L B,B 1.0800P,1.1200C N10M,10M VA

SEAGULL STRUCTURE RULES:
- 3 legs: Buy Put (protection), Sell Put (financing), Sell Call (financing)
- Example: '40m seagull' → N40M,40M,40M (40M on EACH leg, not 40M total)
- For zero-cost: solve for the sold call strike that makes net premium = 0

VERTICAL SPREAD WITH ""VS"" NOTATION:
- When ""VS"" appears with SAME expiry on both legs, this is a VERTICAL SPREAD (not calendar)
- PUT SPREAD: Higher strike VS Lower strike → Buy high, Sell low → B,S
  * Example: ""PUT 11.7 VS PUT 11.5"" (same expiry) → B,S 11.7000P,11.5000P
- CALL SPREAD: Lower strike VS Higher strike → Buy low, Sell high → B,S
  * Example: ""CALL 11.5 VS CALL 11.7"" (same expiry) → B,S 11.5000C,11.7000C
- CRITICAL: Maintain input strike order, apply correct B,S directions based on spread type

CALENDAR SPREAD RULES:
- ""VS"" notation indicates calendar structure with different expiries
- DEFAULT DIRECTION: Buy near expiry, Sell far expiry (B,S) unless explicitly stated otherwise
- Example: ""12 Nov 12.15 CALL VS 20 Nov 12.23 CALL"" → B,S (buy 12 Nov, sell 20 Nov)
- If user specifies ""buy both"" or ""sell both"", use B,B or S,S accordingly
- Count all individual strikes/options as separate legs
- Different expiries per leg: list expiries separated by commas matching leg count
- Format: OVML EURNOK 11/12/25,11/20/25 2L B,S 12.1500C,12.2300C N50M,50M VA
- CRITICAL: ""9.70-9.50 ps"" or ""11.60-12.20 cs"" means TWO separate strikes, NOT a range
  * First number is one strike, second number is another strike
  * ""9.70-9.50"" = two strikes: 9.70 AND 9.50 (not ""between 9.70 and 9.50"")
- Example: ""1m 9.85 put vs 2m 9.70-9.50 ps"" = 3 legs total (1 at 1M, 2 at 2M)
- Format: OVML USDNOK 1M,2M,2M 3L B,S,S 9.8500P,9.7000P,9.5000P N100M,100M,100M VA

RISK REVERSAL (RR) RULES:
- 2 legs: Buy Put + Sell Call OR Buy Call + Sell Put
- Common notation: '10.70 vs ?' means one strike given, solve for the other
- For zero-cost RR: Premium(Long option) = Premium(Short option)
- Calculate the unknown strike to achieve zero net cost

SPOT REFERENCE AND DELTA NOTATION:
- 'Sr: 10.9250', 'fwd ref 0.9463', or 'delta 20m @ 9.3890' provides the spot reference for pricing
- The @ or Sr: or fwd ref value becomes the SP field in OVML output
- CRITICAL: Spot reference (SP) is NOT an option strike - never use it as a strike price
- Delta information is for risk management only, not for OVML structure
- Example: 'delta 20m @ 9.3890' → use SP9.3890, but calculate actual strike prices separately

NOTIONAL PARSING:
- ""150nok"" or ""150m"" or ""150 mio"" → N150M
- ""10mil notl"" or ""10mil"" or ""10 mil"" → N10M
- ""15x10mio"" or ""15x10m"" → N15M,10M (first number = first leg, second number = second leg)
- ""15/10mio"" or ""15-10m"" → N15M,10M (alternative notation)
- Always include the N prefix and M suffix
- For multi-leg: N100M,150M (comma-separated)

OPTION TYPE INFERENCE (when call/put not specified):
- If strike > spot reference → Default to CALL (out-of-the-money call)
- If strike < spot reference → Default to PUT (out-of-the-money put)
- CRITICAL: Use the spot reference provided in the input (Sr:, fwd ref, @) for this comparison
- If no spot reference in input, use the LIVE SPOT provided above
- Rationale: Traders typically buy OTM options (cheaper premium) unless ITM is explicitly stated
- Example: Spot ref 11.0518, Strike 11.0000 → Strike < Spot → Default to PUT

ZERO-COST CALCULATION:
- Solve for unknown strikes so net premium = 0
- Assume typical FX implied volatility of 8-10% if not specified
- Use spot reference (not live spot) for pricing calculations
- Zero-cost strikes will typically be above spot for calls, below spot for puts
- Example: Spot ref 9.3890, buy put 9.25, sell put 8.95 → zero-cost call around 9.50-9.55
- Always output calculated numeric strikes (4 decimals), never '??', 'X.xx', or the spot reference itself

FORMAT ENFORCEMENT:
- No placeholders, brackets, or tokens (e.g., <ZC_CALL>, [SP...])
- Strikes: 4 decimals (0.9500 not 0.95)
- Notionals: N(amount)M format - NEVER omit N prefix
- Expiry: Only ONE expiry date in the entire OVML string
SPOT REFERENCE (SP) RULES:
- If user provides spot reference (keywords: 'sr', 's.r', 'fwd ref', 'spot', 'v', '@') → Use that value for SP
- If NO spot reference in input → Use the LIVE SPOT provided above for SP
- CRITICAL: ALWAYS include SP in the output (either explicit or live)
- Example: Input with ""sr 1.1535"" → SP1.1535
- Example: Input without spot ref, live spot 1.1548 → SP1.1548
- Output exactly one OVML line - no quotes, no commentary
- Expiry: Single expiry for standard trades, OR comma-separated expiries for calendar spreads (e.g., 1M,3M,3M for 3-leg calendar)

EXAMPLES:
Single: OVML NOKSEK 08/14/26 B 0.9500C N150M VA SP0.9463
Single: OVML USDSEK 12/12/25 B 9.6000C N10M VA SP9.4034
Put Spread: OVML USDSEK 12/12/25 2L B,S 9.6000P,9.1500P N5M,20M VA SP9.3600
Call Spread: OVML EURSEK 3M 2L B,S 11.2000C,11.8000C N50M,50M VA
Risk Reversal: OVML EURSEK 04/22/26 2L B,S 10.7000P,11.1500C N10M,10M VA SP10.9250
Risk Reversal (unequal notionals): OVML USDNOK 08/14/26 2L B,S 9.7000P,10.0000C N15M,10M VA SP9.9055
Seagull: OVML USDSEK 02/19/26 3L B,S,S 9.2500P,8.9500P,9.5200C N40M,40M,40M VA SP9.3890
Calendar Spread: OVML USDSEK 1M,3M,3M 3L B,S,S 9.5000P,9.3000P,8.9000P N50M,50M,50M VA SP9.697

Output ONLY the OVML line:";

                var response = await _openAI.GetChatCompletion(prompt);
                var aiResponse = response.choices[0].message.content.Trim();

                var ovmlMatch = Regex.Match(aiResponse, @"OVML\s+[^\r\n]+");
                if (!ovmlMatch.Success)
                {
                    return new TradeParseResult
                    {
                        OVML = aiResponse,
                        Underlying = underlying,
                        Expiry = expiry,
                        ParseMethod = "AI-Raw",
                        AdditionalInfo = "Returned raw AI response since no OVML detected"
                    };
                }

                var ovml = ovmlMatch.Value.Trim();

                // Replace ATM notation with actual spot rate
                if (!string.IsNullOrEmpty(liveSpot))
                {
                    var atmMatch = Regex.Match(ovml, @"ATM[A-Z]*([CP])", RegexOptions.IgnoreCase);
                    if (atmMatch.Success)
                    {
                        string optionType = atmMatch.Groups[1].Value.ToUpper();
                        string actualStrike = $"{liveSpot}{optionType}";

                        ovml = Regex.Replace(ovml, @"ATM[A-Z]*[CP]", actualStrike, RegexOptions.IgnoreCase);

                        Console.WriteLine($"[AI] Replaced ATM notation with spot rate: {actualStrike}");
                    }
                }

                ovml = Regex.Replace(ovml, @"\bv(\d+\.\d+)", "SP$1");

                // CLEANUP: Remove duplicate expiry formats (e.g., both "12Dec" and "12/12/25")
                var ovmlParts = ovml.Split(' ').ToList();
                int expiryCount = 0;
                int expiryFoundAt = -1;

                for (int i = 0; i < ovmlParts.Count; i++)
                {
                    // Check if part looks like an expiry (date format or tenor shorthand like "12Dec")
                    if (Regex.IsMatch(ovmlParts[i], @"^\d{1,2}/\d{1,2}/\d{2,4}$") ||
                        Regex.IsMatch(ovmlParts[i], @"^\d{1,2}[A-Z]{3}\d{0,2}$", RegexOptions.IgnoreCase) ||
                        Regex.IsMatch(ovmlParts[i], @"^\d+[MWYD]$", RegexOptions.IgnoreCase))
                    {
                        expiryCount++;
                        if (expiryCount == 1)
                        {
                            expiryFoundAt = i;
                        }
                        else
                        {
                            // Keep the properly formatted one (MM/dd/yy format is preferred)
                            if (Regex.IsMatch(ovmlParts[i], @"^\d{1,2}/\d{1,2}/\d{2}$"))
                            {
                                // This is the proper format, remove the earlier one
                                ovmlParts.RemoveAt(expiryFoundAt);
                                expiryFoundAt = i - 1; // Adjust index after removal
                                Console.WriteLine($"[AI] Kept proper date format, removed duplicate at position {expiryFoundAt}");
                            }
                            else
                            {
                                // Remove this duplicate
                                ovmlParts.RemoveAt(i);
                                i--; // Adjust loop counter
                                Console.WriteLine($"[AI] Removed duplicate expiry at position {i + 1}");
                            }
                        }
                    }
                }
                ovml = string.Join(" ", ovmlParts);

                // Only correct option type if user didn't specify call/put AND didn't provide spot reference
                bool userSpecifiedOptionType = Regex.IsMatch(input, @"\b(call|put)\b", RegexOptions.IgnoreCase);
                bool userProvidedSpotRef = !string.IsNullOrEmpty(explicitSpot);

                if (!userSpecifiedOptionType && !userProvidedSpotRef)
                {
                    var ovmlPartsForType = ovml.Split(' ');
                    if (ovmlPartsForType.Length >= 4 && !string.IsNullOrEmpty(liveSpot))
                    {
                        if (double.TryParse(ovmlPartsForType[4], out double strike) && double.TryParse(liveSpot, out double spot))
                        {
                            bool isCall = ovml.Contains(" C ");
                            bool shouldBeCall = strike > spot;

                            if (isCall != shouldBeCall)
                            {
                                ovml = ovml.Replace(isCall ? " C " : " P ", shouldBeCall ? " C " : " P ");
                            }
                        }
                    }
                }

                if (!ovml.Contains("SP"))
                {
                    // Prioritize user-provided spot reference over live API spot
                    string spotToUse = !string.IsNullOrEmpty(explicitSpot) ? explicitSpot : liveSpot;

                    if (!string.IsNullOrEmpty(spotToUse))
                    {
                        ovml += $" SP{spotToUse}";
                    }
                }

                var result = new TradeParseResult
                {
                    OVML = ovml,
                    Underlying = ExtractUnderlyingFromOVML(ovml),
                    Expiry = ExtractExpiryFromOVML(ovml),
                    LegCount = ExtractLegCountFromOVML(ovml),
                    ParseMethod = patternMatched ? $"Learned-Pattern-{matchedPatternName}" : "AI-Success",
                    AdditionalInfo = aiResponse
                };
                try
                {
                    var sanityCheck = await _sanityChecker.ValidateTradeAsync(input, result);
                    result.ValidationResult = sanityCheck;
                    if (sanityCheck.IsValid)
                    {
                        result.ParseMethod = patternMatched
                            ? $"Learned-Pattern-{matchedPatternName} (Validated - {sanityCheck.Confidence:P0})"
                            : $"AI-Success (Validated - {sanityCheck.Confidence:P0})";

                        if (!patternMatched)
                        {
                            Console.WriteLine($"[AI] >>> About to learn pattern <<<");
                            Console.WriteLine($"[AI] Input: {input}");
                            Console.WriteLine($"[AI] LegCount: {result.LegCount}");

                            string learnedPatternName = await LearnFromSuccessfulExample(input, result);

                            Console.WriteLine($"[AI] >>> Learning completed <<<");

                            // Update parse method to show it's a learned pattern
                            if (!string.IsNullOrEmpty(learnedPatternName))
                            {
                                result.ParseMethod = $"Learned-Pattern-{learnedPatternName.Replace("Learned-", "")} (Validated - {sanityCheck.Confidence:P0})";
                            }
                        }
                    }
                    else
                    {
                        result.ParseMethod = $"AI-Warning (Failed Validation - {sanityCheck.Confidence:P0})";
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AI] Sanity check error: {ex.Message}");
                }

                return result;
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
                        return spotRate.Value.ToString("F4");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] Could not fetch live rate for {underlying}: {ex.Message}");
            }
            return "";
        }

        public async Task<TradeParseResult> TryLearnedPatterns(string input, string underlying, string expiry)
        {
            if (_learnedPatterns == null || _learnedPatterns.Count == 0)
            {
                return null;
            }

            Console.WriteLine($"[AI] Testing {_learnedPatterns.Count} learned patterns...");

            foreach (var pattern in _learnedPatterns.OrderByDescending(p => p.UsageCount))
            {
                try
                {
                    // Use similarity matching instead of regex (since regex pattern may be empty)
                    if (IsSimilarTrade(input, pattern.ExampleInput))
                    {
                        Console.WriteLine($"[AI] ✓ Learned pattern matched: {pattern.Name}");
                        Console.WriteLine($"[AI]   Using stored OVML: {pattern.ExampleOVML}");
                        pattern.UsageCount++;
                        SaveLearnedPatterns();

                        // Return the stored OVML result
                        var result = new TradeParseResult
                        {
                            OVML = pattern.ExampleOVML,
                            Underlying = underlying,
                            Expiry = expiry
                        };
                        result.GenerateUBS();
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AI] Error testing pattern {pattern.Name}: {ex.Message}");
                }
            }

            return null;
        }

        public bool RemovePattern(string patternName)
        {
            try
            {
                Console.WriteLine($"[AI] RemovePattern called with: '{patternName}'");
                Console.WriteLine($"[AI] Current patterns count: {_learnedPatterns.Count}");

                foreach (var p in _learnedPatterns)
                {
                    Console.WriteLine($"[AI] - Pattern in list: '{p.Name}'");
                }

                var patternToRemove = _learnedPatterns.FirstOrDefault(p => p.Name == patternName);

                if (patternToRemove != null)
                {
                    Console.WriteLine($"[AI] Found pattern to remove: '{patternToRemove.Name}'");
                    _learnedPatterns.Remove(patternToRemove);
                    SaveLearnedPatterns();
                    Console.WriteLine($"[AI] Removed pattern and saved. Remaining: {_learnedPatterns.Count}");
                    return true;
                }

                Console.WriteLine($"[AI] Pattern NOT FOUND: '{patternName}'");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] Error removing pattern: {ex.Message}");
                return false;
            }
        }

        private void LoadLearnedPatterns()
        {
            try
            {
                if (File.Exists(_patternsFilePath))
                {
                    var json = File.ReadAllText(_patternsFilePath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    _learnedPatterns = JsonSerializer.Deserialize<List<LearnedPattern>>(json, options) ?? new List<LearnedPattern>();
                }
                else
                {
                    _learnedPatterns = new List<LearnedPattern>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] Error loading patterns: {ex.Message}");
                _learnedPatterns = new List<LearnedPattern>();
            }
        }

        private void SaveLearnedPatterns()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(_learnedPatterns, options);
                File.WriteAllText(_patternsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] Error saving patterns: {ex.Message}");
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

            var strikePart = parts.FirstOrDefault(p => p.Contains("P") || p.Contains("C"));
            if (!string.IsNullOrEmpty(strikePart) && strikePart.Contains(","))
            {
                var legs = strikePart.Split(',').Length;
                if (legs > 1) return legs;
            }

            return 1;
        }

        private async Task<string> LearnFromSuccessfulExample(string input, TradeParseResult result)
        {
            try
            {
                var pattern = new LearnedPattern
                {
                    Name = $"Learned-{DateTime.Now:yyyyMMdd-HHmmss}",
                    RegexPattern = "",
                    Description = $"{result.LegCount}-leg trade",
                    CreatedAt = DateTime.Now,
                    UsageCount = 1,
                    ExampleInput = input.Trim(),
                    ExampleOVML = result.OVML  // Save the OVML output
                };
                _learnedPatterns.Add(pattern);
                SaveLearnedPatterns();
                Console.WriteLine($"[AI] ✓ Learned pattern from successful {result.LegCount}-leg trade");

                return pattern.Name;  // <-- Return the pattern name
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] Error learning pattern: {ex.Message}");
                return null;  // <-- Return null on error
            }
        }

        private bool IsSimilarTrade(string input1, string input2)
        {
            var normalized1 = input1.ToLower().Replace("\n", " ").Replace("\r", " ");
            var normalized2 = input2.ToLower().Replace("\n", " ").Replace("\r", " ");

            var tokens1 = normalized1.Split(new[] { ' ', ',', '.', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var tokens2 = normalized2.Split(new[] { ' ', ',', '.', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            var commonTokens = tokens1.Intersect(tokens2).Count();
            var totalTokens = Math.Max(tokens1.Length, tokens2.Length);

            return (double)commonTokens / totalTokens > 0.6;
        }
    }


    public class LearnedPattern
    {
        public string Name { get; set; }
        public string RegexPattern { get; set; }
        public string Description { get; set; }
        public string ExampleInput { get; set; }
        public string ExampleOVML { get; set; }  // Store the OVML output for this pattern
        public DateTime CreatedAt { get; set; }
        public int UsageCount { get; set; }
    }


}