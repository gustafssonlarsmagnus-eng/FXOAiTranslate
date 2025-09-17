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
                var prompt = $@"Convert this FX options trading request to Bloomberg OVML format:

Input: ""{input}""

OVML SYNTAX RULES:
Single Leg: OVML (currency) (expiry) (P/C) (strike) (B/S) (notional) (style) [SP(spot)]
Multi-Leg: OVML (currency) (legs) (directions) (strikes) (expiry) (notionals) (style) [SP(spot)]

CRITICAL RULES:
- Strikes always 4 decimals
- Use SP(spot) if provided or live spot
- Expiry in MM/dd/yy format for dates
- B=Buy, S=Sell
- Append C for calls, P for puts

RESPOND WITH ONLY THE OVML COMMAND - NO EXPLANATIONS:";

                var response = await _openAI.GetChatCompletion(prompt);
                var aiResponse = response.choices[0].message.content.Trim();

                Console.WriteLine($"[AI] Raw response: {aiResponse}");

                var ovmlMatch = Regex.Match(aiResponse, @"OVML\s+[^\r\n]+");
                if (ovmlMatch.Success)
                {
                    var ovml = ovmlMatch.Value.Trim();

                    // normalize SPx.x
                    ovml = Regex.Replace(ovml, @"\bv(\d+\.\d+)", "SP$1");

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
