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

        public HybridPatternLearner(OpenAIService openAI, BloombergService bloombergService, string patternsFilePath = "learned_patterns.json")
        {
            _openAI = openAI;
            _bloombergService = bloombergService;
            _patternsFilePath = patternsFilePath;
            LoadLearnedPatterns();
        }

        public async Task<TradeParseResult> ParseWithAI(string input, string underlying, string expiry)
        {
            try
            {
                var learnedResult = TryLearnedPatterns(input, underlying, expiry);
                if (learnedResult != null)
                {
                    Console.WriteLine($"[AI] Used learned pattern: {learnedResult.ParseMethod}");
                    return learnedResult;
                }

                Console.WriteLine("[AI] No learned pattern found, calling OpenAI...");
                // Fetch live spot rate BEFORE calling AI
                var liveSpot = await GetDefaultSpotRateAsync(underlying);
                string spotInfo = !string.IsNullOrEmpty(liveSpot) ? liveSpot : "9.8000";

                var prompt = $@"You are an expert FX options trader and OVML parser. Convert this natural language trading request into STRICT Bloomberg OVML format.

Input: ""{input}""
LIVE SPOT RATE for {underlying}: {spotInfo} (Use this exact rate for strike comparison)

MANDATORY OVML SYNTAX (Bloomberg Terminal Official):
Single Leg: OVML (currency pair) (expiration date) (call/put) (strike) (buy/sell) (notional amount) (option style code) [SP(spot)]
Multi-Leg: Use multiple OVML commands or strategy codes

CRITICAL FORMAT RULES:
1. DATE FORMAT: Always MM/dd/yy (NOT tenors like 1W, 3M in final output)
   - ""1 week"" → calculate actual date as MM/dd/yy
   - ""3M"" → calculate 3 months from today as MM/dd/yy
   - Current date reference: Today is {DateTime.Now:MM/dd/yyyy}

2. CURRENCY PAIR: Two 3-letter ISO codes without separator (EURUSD, USDNOK, EURSEK)

3. OPTION TYPE: C (call) or P (put) - NEVER ""Call"" or ""Put""

4. STRIKE FORMAT:
   - Numeric: 10.0000 (trim unnecessary zeros)
   - Delta: DS25 (for 25 delta), DF25 (forward delta)
   - ATM: Use actual spot rate if provided

5. DIRECTION: B (buy) or S (sell) - NEVER ""Buy"" or ""Sell""

6. NOTIONAL: N + amount + M (e.g., N100M)
   - ""100mm"" → N100M
   - ""50 mio"" → N50M
   - ""25M"" → N25M

7. OPTION STYLE: VA (vanilla), DKI (double knock-in), DKO (double knock-out)
   - Default to VA unless specified

8. SPOT REFERENCE: SP + rate (e.g., SP9.8190)
   - NO brackets: [SP9.8190] is WRONG
   - Extract from: ""spot ref"", ""s.r."", ""sp ref"", ""spotref""

9. MULTI-LEG DETECTION:
   - Use single-line multi-leg format: OVML (pair) (expiry) (legs)L (directions) (strikes) (notionals) (style) [SP(spot)]
   - Risk Reversal: ""gbp put nok call 20 delta"" → OVML GBPNOK 5M 2L B,S DS20P,DS20C N25M,25M VA SP9.8474
   - Directions: comma-separated (B,S)
   - Strikes: comma-separated with option type (DS20P,DS20C)
   - Notionals: comma-separated (N25M,25M)

10. LANGUAGE SUPPORT:
    - Swedish: säljer=sell, köper=buy, mio=million, mån=months
    - Always use explicit option types when stated (""call"" → C, ""put"" → P)

STRUCTURE PATTERNS:
- Risk Reversal: Buy Call + Sell Put (or Sell Call + Buy Put)
- Call Spread: Buy lower strike Call + Sell higher strike Call  
- Put Spread: Buy higher strike Put + Sell lower strike Put
- Collar: Buy Call + Sell Call + Sell Put (3-leg)
- Straddle: Buy Call + Buy Put (same strike)
- Strangle: Buy Call + Buy Put (different strikes)

NOTIONAL PARSING:
- ""mio"", ""milj"", ""m"", ""mm"", ""MUSD"", ""MEUR"" → M
- Multiple notionals: ""100M x 50M"" → first leg N100M, second leg N50M
- ""per ben"", ""per leg"" → same notional for all legs

TENOR TO DATE CONVERSION:
- 1W = 7 days from today
- 1M = 1 month from today  
- 3M = 3 months from today
- 6M = 6 months from today
- 1Y = 1 year from today
Calculate exact MM/dd/yy format

EXAMPLES:
Input: ""USDNOK 1 week 10.00 call in 100mm, spot ref 9.8190""
Output: OVML USDNOK {DateTime.Now.AddDays(7):MM/dd/yy} C 10.0000 B N100M VA SP9.8190

Input: ""EURSEK 3M buy 11.50 put 50M, sell 11.80 call 50M""
Output: OVML EURSEK {DateTime.Now.AddMonths(3):MM/dd/yy} P 11.5000 B N50M VA
OVML EURSEK {DateTime.Now.AddMonths(3):MM/dd/yy} C 11.8000 S N50M VA

Input: ""EURUSD risk reversal, buy call 1.10, sell put 1.05, 100M each""
Output: OVML EURUSD {DateTime.Now.AddMonths(1):MM/dd/yy} C 1.1000 B N100M VA
OVML EURUSD {DateTime.Now.AddMonths(1):MM/dd/yy} P 1.0500 S N100M VA

Input: ""GBPNOK: 25 mio 5mth gbp put nok call 20 delta""
Output: OVML GBPNOK 5M P DS20 B N25M VA SP9.8166

Input: ""3-month EUR put/NOK call spread with 11.50 and 11.30""
Output: OVML EURNOK 3M 2L B,S 11.5000P,11.3000P N25M,25M VA

Multi-leg format: OVML (pair) (expiry) (legs)L (directions) (strikes) N(notionals) (style) SP(spot)
Example: OVML GBPNOK 02/18/26 2L B,S DS20P,DS20C N25M,25M VA SP9.8285

STRICT REQUIREMENTS:
- Always use exact Bloomberg OVML syntax
- Convert ALL tenors to MM/dd/yy dates
- Use only B/S for direction, C/P for option type
- Format notionals as N + amount + M
- Include VA style code unless specified otherwise
- Spot reference as SP + rate (no brackets)
- For multi-leg: separate OVML command per leg

Response with ONLY the OVML command(s) - ONE PER LINE FOR MULTI-LEG:";

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

                    return new TradeParseResult
                    {
                        OVML = ovml,
                        Underlying = ExtractUnderlyingFromOVML(ovml),
                        Expiry = ExtractExpiryFromOVML(ovml),
                        LegCount = ExtractLegCountFromOVML(ovml),
                        ParseMethod = "AI-Success",
                        AdditionalInfo = aiResponse
                    };
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

        private TradeParseResult TryLearnedPatterns(string input, string underlying, string expiry)
        {
            foreach (var pattern in _learnedPatterns.OrderByDescending(p => p.UsageCount))
            {
                try
                {
                    var regex = new Regex(
                        pattern.RegexPattern.Trim('@', '"'),
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);

                    var match = regex.Match(input);
                    if (match.Success)
                    {
                        pattern.UsageCount++;
                        SaveLearnedPatterns();

                        var templateOVML = pattern.ExampleOutput;
                        var generatedOVML = AdaptOVMLTemplate(templateOVML, match, underlying, expiry);

                        return new TradeParseResult
                        {
                            OVML = generatedOVML,
                            Underlying = underlying,
                            Expiry = expiry,
                            ParseMethod = $"Learned-{pattern.Name}",
                            LegCount = ExtractLegCountFromOVML(generatedOVML)
                        };
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AI] Error applying learned pattern {pattern.Name}: {ex.Message}");
                }
            }
            return null;
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
            var parts = ovml.Split(' ');
            if (parts.Length > 2 && parts[2].EndsWith("L"))
            {
                if (int.TryParse(parts[2].Substring(0, parts[2].Length - 1), out int legs))
                    return legs;
            }
            return 1;
        }
    }

    public class LearnedPattern
    {
        public string Name { get; set; }
        public string RegexPattern { get; set; }
        public string ExampleInput { get; set; }
        public string ExampleOutput { get; set; }
        public DateTime CreatedAt { get; set; }
        public int UsageCount { get; set; }
        public string AdditionalInfo { get; set; }
    }
}