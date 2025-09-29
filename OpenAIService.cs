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
                            var regex = new Regex(pattern.RegexPattern, RegexOptions.IgnoreCase);
                            if (regex.IsMatch(input))
                            {
                                Console.WriteLine($"[AI] ✓ Pattern matched: {pattern.Description}");
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
- Single Leg: OVML (currency pair) (expiry) (call/put) (strike) (buy/sell) (notional amount) (option style code) [SP(spot)]
- Multi-Leg: OVML (currency pair) (expiry) (legs)L (directions) (strikes) N(notionals) (style) [SP(spot)]

CRITICAL FORMAT RULES:
1. DATE/TENOR FORMAT: Allow Bloomberg tenor shorthand (1W, 2M, 3M, 6M, 1Y) or explicit dates (MM/dd/yy).
2. CURRENCY PAIR: Two 3-letter ISO codes without separator (EURUSD, USDNOK, EURSEK).
3. OPTION TYPE: C (call) or P (put) only.
4. STRIKE FORMAT: Numeric: 10.0000 (trim unnecessary zeros but keep up to 4 decimals), Delta: DS25, ATM: literal ""ATM"".
5. DIRECTION: B (buy) or S (sell) only.
6. NOTIONAL: N + amount + M (e.g., N100M). ""100mm"" → N100M, ""50 mio"" → N50M.
7. OPTION STYLE: VA (vanilla), DKI (double knock-in), DKO (double knock-out). Default to VA.
8. SPOT REFERENCE: SP + rate (e.g., SP9.8190). Always at the end if provided.

EXAMPLES:
Input: ""USDNOK 1 week 10.00 call in 100mm, spot ref 9.8190""
Output: OVML USDNOK 1W C 10.0000 B N100M VA SP9.8190

Input: ""EURSEK 3M buy 11.50 put 50M, sell 11.80 call 50M""
Output: OVML EURSEK 3M 2L B,S 11.5000P,11.8000C N50M,50M VA

STRICT REQUIREMENTS:
- Always produce exactly one OVML line.
- Use single-line multi-leg format for all strategies.
- Do not output explanations, only the OVML line.";

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

                // Only correct option type if user didn't specify call/put AND didn't provide spot reference
                bool userSpecifiedOptionType = Regex.IsMatch(input, @"\b(call|put)\b", RegexOptions.IgnoreCase);
                bool userProvidedSpotRef = !string.IsNullOrEmpty(explicitSpot);

                if (!userSpecifiedOptionType && !userProvidedSpotRef)
                {
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
                            await LearnFromSuccessfulExample(input, result);
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
                // Simple hardcoded order-independent pattern for vanilla FX options
                var pattern = new LearnedPattern
                {
                    Name = $"Vanilla-{DateTime.Now:yyyyMMdd-HHmmss}",
                    RegexPattern = @"^(?=.*\b[A-Z]{6}\b)(?=.*\d+\s*(?:mio|M|mm|mi|million))(?=.*\d+\s*(?:w|W|wks|weeks|M|mth|months|d|D|days))(?=.*\d+\.?\d*)(?=.*\b(?:call|put|Call|Put)\b).*$",
                    Description = "Order-independent vanilla FX option",
                    CreatedAt = DateTime.Now,
                    UsageCount = 1,
                    ExampleInput = input
                };

                // Check if we already have this pattern
                if (!_learnedPatterns.Any(p => p.RegexPattern == pattern.RegexPattern))
                {
                    // Verify pattern matches the input
                    var testRegex = new Regex(pattern.RegexPattern, RegexOptions.IgnoreCase);
                    if (testRegex.IsMatch(input))
                    {
                        _learnedPatterns.Add(pattern);
                        SaveLearnedPatterns();
                        Console.WriteLine($"[AI] ✓ Learned pattern from: {input}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AI] Error learning pattern: {ex.Message}");
            }
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