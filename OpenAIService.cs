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

        public async Task<TradeParseResult> ParseWithAI(string input, string underlying, string expiry, string explicitSpot = "")
        {
            Console.WriteLine("[AI] ParseWithAI called - VERSION 2025-09-29-NEW");
            try
            {
                LoadLearnedPatterns();

                bool patternMatched = false;
                string matchedPatternName = "";

                if (_learnedPatterns != null && _learnedPatterns.Count > 0)
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

                var liveSpot = await GetDefaultSpotRateAsync(underlying);
                // ... rest of your existing code

                string spotInfo = !string.IsNullOrEmpty(explicitSpot) ? explicitSpot
                    : !string.IsNullOrEmpty(liveSpot) ? liveSpot
                    : "9.8000";

                var prompt = $@"You are an expert FX options trader and OVML parser. Convert this natural language trading request into STRICT Bloomberg OVML format.

Input: ""{input}""
LIVE SPOT RATE for {underlying}: {spotInfo}

MANDATORY OVML SYNTAX:
- Single Leg: OVML (currency) (expiry) C/P (strike) B/S (notional) (style) [SP(spot)]
- Multi-Leg: OVML (currency) (expiry) (legs)L (directions) (strikes) (notionals) (style) [SP(spot)]

CRITICAL MULTI-LEG RULES:
1. EXPIRY: Use ONE expiry format only - NEVER include both tenor and date
2. PUT SPREAD: Buy higher strike put, Sell lower strike put → B,S (strikes high,low)
3. CALL SPREAD: Buy lower strike call, Sell higher strike call → B,S (strikes low,high)
4. NOTIONALS: Must have format N(amount)M,(amount)M (e.g., N5M,20M)

ZERO-COST RULES:

If input says “zero cost” or a strike is “X.xx”, replace X.xx with the numeric strike that makes the net premium = 0 versus the other leg(s).

Assume equal notionals unless stated; if notionals differ, solve for zero-cost using given notionals.

If no vols given, equate Black–Scholes premiums using the same σ for both legs.

Always output a number (never X.xx). Keep existing expiry/style. Include SP if provided.

Rounding: show strikes to 4 decimals.

FORMAT ENFORCEMENT (NO PLACEHOLDERS):

Never output angle/square brackets or placeholder tokens (e.g., <ZC_CALL>, [SP…], <…>).

If SPOT is provided, include as SPnumber without brackets; if not provided, omit SP entirely.

For zero-cost cases, solve and output a numeric strike (4 decimals). Do not write “X.xx” or any placeholder.

One line only, exactly the OVML string—no quotes, no commentary.

ZERO-COST SOLVE (if needed):

Assume equal vols for legs; if vols not given, use the same σ for both.

Use equal notionals unless specified; if notionals differ, solve net premium = 0 using given notionals.

Use forward-pricing if you have rates; otherwise use spot as proxy for forward.

ROUNDING & VALIDATION:

Strikes: 4 decimals; Notionals: Namount M; Expiry: single format only (either MM/DD/YY or tenor like 3M).

Multi-leg RR order: B,S (putStrikeP,callStrikeC) with computed numeric call strike.

If any required number is missing after solving, do not output—instead approximate using the rules above so a number is always present.

EXAMPLE (zero-cost RR):

Input: 10M EURSEK RR, Exp 15/1 26, BUY Put 10.95, Sell Call X.xx zero cost, Spot 11.0573

Output: OVML EURSEK 01/15/26 2L B,S 10.9500P,11.5500C N10M,10M VA SP11.0573



Example:
Input: “10M EURSEK RR, Exp 15/1 26, BUY Put 10.95, Sell Call X.xx zero cost”
Output: OVML EURSEK 01/15/26 2L B,S 10.9500P,<ZC_CALL>C N10M,10M VA [SP<spot if given>]

EXAMPLES:
Single: OVML USDSEK 12/12/25 C 9.6000 B N10M VA SP9.4034
Put Spread: OVML USDSEK 12/12/25 2L B,S 9.6000P,9.1500P N5M,20M VA SP9.3600
Call Spread: OVML EURSEK 3M 2L B,S 11.2000C,11.8000C N50M,50M VA

STRICT REQUIREMENTS:
- ONE expiry only (either MM/dd/yy OR tenor like 3M, never both)
- Notionals MUST start with N
- Put spread: B,S with higher strike first
- Call spread: B,S with lower strike first
- No explanations, only OVML'

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

                if (!ovml.Contains("SP") && !string.IsNullOrEmpty(liveSpot))
                {
                    ovml += $" SP{liveSpot}";
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

                            await LearnFromSuccessfulExample(input, result);

                            Console.WriteLine($"[AI] >>> Learning completed <<<");
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
                    var regex = new Regex(pattern.RegexPattern, RegexOptions.IgnoreCase);

                    if (regex.IsMatch(input))
                    {
                        Console.WriteLine($"[AI] ✓ Pattern matched: {pattern.Description}");
                        pattern.UsageCount++;
                        SaveLearnedPatterns();

                        // Pattern matched - let AI handle the actual OVML generation with correct values
                        return null;
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
                var patternToRemove = _learnedPatterns.FirstOrDefault(p => p.Name == patternName);
                if (patternToRemove != null)
                {
                    _learnedPatterns.Remove(patternToRemove);
                    SaveLearnedPatterns();
                    Console.WriteLine($"[AI] Removed pattern: {patternName}");
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

        private async Task LearnFromSuccessfulExample(string input, TradeParseResult result)
        {
            try
            {
                // Simple similarity-based learning - no complex regex needed
                // If AI successfully parsed it and validation passed, just remember it

                var pattern = new LearnedPattern
                {
                    Name = $"Learned-{DateTime.Now:yyyyMMdd-HHmmss}",
                    RegexPattern = "", // Not used for matching
                    Description = $"{result.LegCount}-leg trade",
                    CreatedAt = DateTime.Now,
                    UsageCount = 1,
                    ExampleInput = input.Trim()
                };

                _learnedPatterns.Add(pattern);
                SaveLearnedPatterns();
                Console.WriteLine($"[AI] ✓ Learned pattern from successful {result.LegCount}-leg trade");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] Error learning pattern: {ex.Message}");
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
        public DateTime CreatedAt { get; set; }
        public int UsageCount { get; set; }
    }


}