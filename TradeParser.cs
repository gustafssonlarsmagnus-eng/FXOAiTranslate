using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FXOptionsSimulator;

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
            Console.WriteLine("CONSTRUCTOR CALLED - VERSION 2024");
            _bloombergService = bloombergService;

            if (!string.IsNullOrEmpty(openAIApiKey))
            {
                _openAI = new OpenAIService(openAIApiKey);
                _patternLearner = new HybridPatternLearner(_openAI, _bloombergService, "learned_patterns.json");
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

        public async Task<TradeParseResult> ParseTradeAsync(string input, bool forceAI = false)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            // Preprocess input to clean up common issues
            input = PreprocessInput(input);

            // Prevent infinite loop: skip if it's already OVML
            if (input.StartsWith("OVML", StringComparison.OrdinalIgnoreCase))
            {
                LogDebug("DEBUG: Input already OVML, skipping AI/regex parse.");
                var already = new TradeParseResult
                {
                    OVML = input,
                    Underlying = ExtractCurrencyPair(input),
                    Expiry = NormalizeExpiry(ExtractExpiry(input)),
                    ParseMethod = "Already-OVML"
                };
                already.GenerateUBS();
                _cache[input] = already;
                return already;
            }

            input = input.Trim();

            // Check cache first (skip cache if forcing AI)
         // IMPORTANT: For learned patterns, we must re-parse to extract current input's values
  if (!forceAI && _cache.TryGetValue(input, out var cached))
  {
     // If the cached result was from a learned pattern, we should NOT use the cache
           // because we need to re-extract values (strike, notional, expiry) from the current input
   if (cached.ParseMethod != null && cached.ParseMethod.StartsWith("Learned-Pattern-"))
 {
            LogDebug("DEBUG: Cached result was from learned pattern - clearing cache entry to re-parse with current values");
        _cache.Remove(input);  // Remove the stale cache entry
      // Continue to re-parse - don't return cached
    }
          else
           {
      LogDebug("DEBUG: Returning cached result for input.");
                return cached;
   }
    }

     LogDebug($"DEBUG: Processing input: '{input}'");

            // Extract basic info first
            string underlying = ExtractCurrencyPair(input);
            string expiry = ExtractExpiry(input);

            LogDebug($"DEBUG: Extracted underlying: '{underlying}'");
            LogDebug($"DEBUG: Extracted expiry: '{expiry}'");

            // Fetch live Bloomberg spot for option type inference and pricing
            string liveSpotForInference = "";
            if (_bloombergService != null && _bloombergService.IsConnected)
            {
                try
                {
                    var liveSpotTask = _bloombergService.GetSpotRate(underlying);
                    double? liveSpot = await liveSpotTask;

                    if (liveSpot.HasValue)
                    {
                        liveSpotForInference = liveSpot.Value.ToString("0.####", CultureInfo.InvariantCulture);
                        LogDebug($"DEBUG: Fetched live Bloomberg spot for option type inference: {liveSpotForInference}");
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"DEBUG: Failed to fetch live spot for inference: {ex.Message}");
                }
            }

            // ADD EXPIRY VALIDATION HERE - Check if expiry extraction failed
            if (expiry == "3M" && !input.ToLower().Contains("3m") && !input.ToLower().Contains("three month") && !forceAI)
            {
                LogDebug("DEBUG: Expiry extraction failed, forcing AI to interpret date");

                // Show warning in UI (non-blocking)
                MessageBox.Show(
                    "Could not extract expiry date from input.\n\n" +
                    "Using AI to interpret the date...",
                    "Expiry Extraction Issue",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );

                // Force AI to handle it - recursively call with forceAI=true
                return await ParseTradeAsync(input, forceAI: true);
            }

            // Skip regex patterns if forceAI is true
            if (!forceAI)
            {
                // Try learned patterns first
                if (_patternLearner != null)
                {
                    LogDebug("[Parser] Checking learned patterns...");
                    var learnedResult = await _patternLearner.TryLearnedPatterns(input, underlying, expiry, liveSpotForInference);
                    if (learnedResult != null)
                    {
                        LogDebug($"[Parser] ✓ Used learned pattern");

                        // Post-process: Handle forward reference
                        var learnedFwdRefMatch = RegexTradePatterns.ForwardRefRegex.Match(input);
                        if (learnedFwdRefMatch.Success)
                        {
                            string fwdRefValue = learnedFwdRefMatch.Groups["fwdref"].Value.Replace(",", ".");
                          
                            // Remove any existing SP (spot reference) from OVML when FW is specified
                          // Spot should only be included if explicitly requested, not when fwd ref is provided
                           learnedResult.OVML = Regex.Replace(learnedResult.OVML, @"\s*SP[\d.,]+", "");
                          
                            // Add FW if not already present
                            if (!learnedResult.OVML.Contains("FW"))
                             {
                           learnedResult.OVML = learnedResult.OVML.TrimEnd() + " FW" + fwdRefValue;
                         }
                            LogDebug($"DEBUG: Forward reference applied to learned pattern OVML: FW{fwdRefValue}");
                            learnedResult.GenerateUBS(); // Regenerate UBS with updated OVML
                        }

                        _cache[input] = learnedResult;
                        return learnedResult;
                    }
                }

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

                        // Spot reference - track if explicitly provided
                        string spot = "";
    bool spotExplicitlyProvided = false;

    var spotMatch = RegexTradePatterns.SpotRegex.Match(input);
      if (spotMatch.Success)
 {
          spot = spotMatch.Groups["spot"].Value.Replace(",", ".");
   spotExplicitlyProvided = true;
        LogDebug($"DEBUG: Spot reference found in input: '{spot}'");
     }

   // Forward reference - track if explicitly provided
           string forwardRef = "";
   bool fwdRefExplicitlyProvided = false;

    var fwdRefMatch = RegexTradePatterns.ForwardRefRegex.Match(input);
     if (fwdRefMatch.Success)
     {
         forwardRef = fwdRefMatch.Groups["fwdref"].Value.Replace(",", ".");
     fwdRefExplicitlyProvided = true;
        LogDebug($"DEBUG: Forward reference found in input: '{forwardRef}'");
 }

    if (!spotExplicitlyProvided && !fwdRefExplicitlyProvided)
{
       // If no explicit spot/fwd in input, fetch live Bloomberg spot FOR CALCULATIONS ONLY
               if (_bloombergService != null && _bloombergService.IsConnected)
        {
               try
    {
              var liveSpotTask = _bloombergService.GetSpotRate(underlying);
         double? liveSpot = await liveSpotTask;

    if (liveSpot.HasValue)
        {
        LogDebug($"DEBUG: Fetched Bloomberg spot for calculations (not for output): {liveSpot.Value}");
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
                                case "CompactMultiLeg_DirectionCodes":
                                    LogDebug($"DEBUG: Processing CompactMultiLeg_DirectionCodes pattern");

                                    // Parse individual legs
                                    string legsText = match.Groups["legs"].Value;
                                    var legMatches = Regex.Matches(legsText, @"(\d+(?:\.\d+)?)mm?\s+([A-Z]{6})\s+([\d.]+)\s+(?:usd|eur|nok|sek|gbp|aud|cad|chf|jpy|nzd)?\s*(call|put)",
                                        RegexOptions.IgnoreCase);

                                    // Parse direction codes (sb → B,S; sbb → S,B,B)
                                    string directionCodes = match.Groups["directions"].Value.ToLower();
                                    var directions = new List<string>();
                                    foreach (char c in directionCodes)
                                    {
                                        directions.Add(c == 'b' ? "B" : "S");
                                    }

                                    // Extract expiry and forward reference
                                    string expiryRaw = match.Groups["expiry"].Value;
                                    string fwdRef = match.Groups["fwdref"].Value;

                                    // Pre-process expiry: remove dashes (15-Jun-26 → 15Jun26)
                                    expiryRaw = expiryRaw.Replace("-", "");
                                    LogDebug($"DEBUG: Preprocessed expiry: {expiryRaw}");

                                    // Convert expiry to OVML date format
                                    string ovmlExpiryCompact = ConvertExpiryToOVMLDate(expiryRaw, result.Underlying);

                                    // Build strikes and notionals
                                    var strikes = new List<string>();
                                    var notionals = new List<string>();

                                    for (int i = 0; i < legMatches.Count; i++)
                                    {
                                        var legMatch = legMatches[i];
                                        string legNotional = legMatch.Groups[1].Value;
                                        string legStrike = legMatch.Groups[3].Value;
                                        string legType = legMatch.Groups[4].Value.ToUpper().StartsWith("C") ? "C" : "P";

                                        strikes.Add($"{legStrike}{legType}");
                                        notionals.Add($"{legNotional}M");
                                    }

                                    result.LegCount = legMatches.Count;

                                    // Build OVML: OVML USDNOK 06/15/26 2L B,S 10.0000C,11.5000C N15M,23M VA FW10.1045
                                    result.OVML = $"OVML {result.Underlying} {ovmlExpiryCompact} {result.LegCount}L " +
                                                  $"{string.Join(",", directions)} {string.Join(",", strikes)} " +
                                                  $"N{string.Join(",", notionals)} VA FW{fwdRef}";

                                    LogDebug($"DEBUG: Built OVML: {result.OVML}");
                                    break;

                                case "Vanilla_Delta":
                                    LogDebug($"DEBUG: Processing Vanilla_Delta pattern");
                                    result.LegCount = 1;

                                    // Determine delta type: DS (spot delta) or DF (forward delta)
                                    string deltaTypeRaw = match.Groups["deltaType"].Value.ToLower();
                                    string deltaPrefix;
                                    if (deltaTypeRaw.Contains("f") || deltaTypeRaw.Contains("forward"))
                                    {
                                        deltaPrefix = "DF";  // Forward delta
                                    }
                                    else
                                    {
                                        deltaPrefix = "DS";  // Spot delta (default)
                                    }

                                    string deltaValue = match.Groups["delta"].Value;
                                    string deltaOptionType = match.Groups["type"].Value.ToUpper().StartsWith("C") ? "C" : "P";

                                    // Convert expiry to OVML date format (tenor → date)
                                    string ovmlExpiry = ConvertExpiryToOVMLDate(result.Expiry, result.Underlying);

                                    // Format: OVML EURUSD 12/02/24 B DS25 C N20M VA1.1572
                                    // Note: Space between delta value and option type
                                    result.OVML = $"OVML {result.Underlying} {ovmlExpiry} " +
                                                  $"B {deltaPrefix}{deltaValue} {deltaOptionType} " +
                                                  $"N{match.Groups["notional"].Value}M VA" +
                                                  spot;
                                    break;

                                case "Vanilla_CurrencyLed":
                                    LogDebug($"DEBUG: Processing Vanilla_CurrencyLed pattern");
                                    result.LegCount = 1;

                                    // Default to Buy (no explicit side in this pattern)
                                    string optionType = match.Groups["type"].Value.ToUpper().StartsWith("C") ? "C" : "P";

                                    // Convert expiry to OVML date format
                                    string ovmlExpiryVC = ConvertExpiryToOVMLDate(result.Expiry, result.Underlying);

                                    result.OVML = $"OVML {result.Underlying} {ovmlExpiryVC} " +
                                                  $"B {match.Groups["strike"].Value}{optionType} " +
                                                  $"N{match.Groups["notional"].Value}M VA" +
                                                  spot;
                                    break;

                                case "Vanilla_NoType":
                                    LogDebug($"DEBUG: Processing Vanilla_NoType pattern");
                                    result.LegCount = 1;

          string strikeValue = match.Groups["strike"].Value;
      string notionalValue = match.Groups["notional"].Value;
 
          LogDebug($"DEBUG: Vanilla_NoType - Raw match: '{match.Value}'");
         LogDebug($"DEBUG: Vanilla_NoType - Strike group: '{strikeValue}'");
           LogDebug($"DEBUG: Vanilla_NoType - Notional group: '{notionalValue}'");

          // Validate notional was captured
         if (string.IsNullOrWhiteSpace(notionalValue))
   {
    LogDebug($"DEBUG: WARNING - Notional not captured, using default 10");
           notionalValue = "10";
         }

   // Determine option type from strike vs spot
          string inferredType = "C"; // Default to Call
          if (!string.IsNullOrEmpty(spot) && double.TryParse(strikeValue, out double strikeNum) && 
        double.TryParse(spot, out double spotNum))
    {
      // If strike < spot -> PUT (OTM put), else CALL (OTM call)
   inferredType = strikeNum < spotNum ? "P" : "C";
LogDebug($"DEBUG: Inferred option type: Strike {strikeNum} vs Spot {spotNum} = {inferredType}");
     }
      else
{
             LogDebug($"DEBUG: Could not infer option type (no spot), using default CALL");
     }

             // Convert expiry to OVML date format
            string ovmlExpiryNT = ConvertExpiryToOVMLDate(result.Expiry, result.Underlying);

        result.OVML = $"OVML {result.Underlying} {ovmlExpiryNT} " +
             $"B {strikeValue}{inferredType} " +
$"N{notionalValue}M VA";
             
 LogDebug($"DEBUG: Vanilla_NoType - Generated OVML: '{result.OVML}'");
         break;

                                case "Delta_RiskReversal":
   LogDebug($"DEBUG: Processing Delta_RiskReversal pattern");
       result.LegCount = 2;

  // Determine delta type: DS (spot delta) or DF (forward delta)
         string rrDeltaTypeRaw = match.Groups["deltaType"].Value?.ToLower() ?? "";
         string rrDeltaPrefix;
      if (rrDeltaTypeRaw.Contains("f") || rrDeltaTypeRaw.Contains("forward"))
      {
           rrDeltaPrefix = "DF";  // Forward delta
}
              else
       {
     rrDeltaPrefix = "DS";  // Spot delta (default)
       }

             string rrDeltaValue = match.Groups["delta"].Value;

   // Convert expiry to OVML date format
    string ovmlExpiryDRR = ConvertExpiryToOVMLDate(result.Expiry, result.Underlying);

            // Format: OVML EURNOK 6M 2L B,S DS25P,DS25C RR VA
         // Risk reversal: Buy put, Sell call at same delta
 result.OVML = $"OVML {result.Underlying} {ovmlExpiryDRR} 2L B,S " +
         $"{rrDeltaPrefix}{rrDeltaValue}P,{rrDeltaPrefix}{rrDeltaValue}C RR VA" +
             (spotExplicitlyProvided ? " SP" + spot : "");
    break;

                                case "Collar_BuyCallSellCallSellPut":
                                    LogDebug($"DEBUG: Processing Collar_BuyCallSellCallSellPut pattern");
                                    result.LegCount = 3;

                                    // Convert expiry to OVML date format
                                    string ovmlExpiryCollar = ConvertExpiryToOVMLDate(result.Expiry, result.Underlying);

                                    result.OVML = $"OVML {result.Underlying} 3L B,S,S " +
                                                  $"{match.Groups["strike1"].Value}C,{match.Groups["strike2"].Value}C,{match.Groups["strike3"].Value}P " +
                                                  $"{ovmlExpiryCollar} N{match.Groups["notional1"].Value}M,{match.Groups["notional2"].Value}M,{match.Groups["notional3"].Value}M" +
                                                  (spotExplicitlyProvided ? " SP" + spot : "");
                                    break;

                                case "Seagull_BuyPutSellPutSellCall":
                                    LogDebug($"DEBUG: Processing Seagull_BuyPutSellPutSellCall pattern");
                                    result.LegCount = 3;

                                    // Convert expiry to OVML date format
                                    string ovmlExpirySeagull = ConvertExpiryToOVMLDate(result.Expiry, result.Underlying);

                                    result.OVML = $"OVML {result.Underlying} {ovmlExpirySeagull} 3L B,S,S " +
                                                  $"{match.Groups["strike1"].Value}P,{match.Groups["strike2"].Value}P,{match.Groups["strike3"].Value}C " +
                                                  $"N{match.Groups["notional1"].Value}M,{match.Groups["notional2"].Value}M,{match.Groups["notional3"].Value}M" +
                                                  (spotExplicitlyProvided ? " SP" + spot : "");
                                    break;

                                case "PutSpread_DifferentNotionals":
                                    LogDebug("DEBUG: Processing PutSpread_DifferentNotionals pattern");
                                    result.LegCount = 2;

                                    double psd1 = double.Parse(match.Groups["strike1"].Value);
                                    double psd2 = double.Parse(match.Groups["strike2"].Value);

                                    string buyStrikePut, sellStrikePut, buyNotionalPut, sellNotionalPut;
                                    if (psd1 > psd2)
                                    {
                                        buyStrikePut = match.Groups["strike1"].Value;
                                        sellStrikePut = match.Groups["strike2"].Value;
                                        buyNotionalPut = match.Groups["notional1"].Value;
                                        sellNotionalPut = match.Groups["notional2"].Value;
                                    }
                                    else
                                    {
                                        buyStrikePut = match.Groups["strike2"].Value;
                                        sellStrikePut = match.Groups["strike1"].Value;
                                        buyNotionalPut = match.Groups["notional2"].Value;
                                        sellNotionalPut = match.Groups["notional1"].Value;
                                    }

                                    // Convert expiry to OVML date format
                                    string ovmlExpiryPS = ConvertExpiryToOVMLDate(result.Expiry, result.Underlying);

                                    result.OVML = $"OVML {result.Underlying} {ovmlExpiryPS} 2L B,S {buyStrikePut}P,{sellStrikePut}P N{buyNotionalPut}M,{sellNotionalPut}M VA" +
                                                  (spotExplicitlyProvided ? " SP" + spot : "");
                                    break;

                                case "CallSpread_DifferentNotionals":
                                    LogDebug("DEBUG: Processing CallSpread_DifferentNotionals pattern");
                                    result.LegCount = 2;

                                    double csd1 = double.Parse(match.Groups["strike1"].Value);
                                    double csd2 = double.Parse(match.Groups["strike2"].Value);

                                    string buyStrikeCall, sellStrikeCall, buyNotionalCall, sellNotionalCall;
                                    if (csd1 < csd2)
                                    {
                                        buyStrikeCall = match.Groups["strike1"].Value;
                                        sellStrikeCall = match.Groups["strike2"].Value;
                                        buyNotionalCall = match.Groups["notional1"].Value;
                                        sellNotionalCall = match.Groups["notional2"].Value;
                                    }
                                    else
                                    {
                                        buyStrikeCall = match.Groups["strike2"].Value;
                                        sellStrikeCall = match.Groups["strike1"].Value;
                                        buyNotionalCall = match.Groups["notional2"].Value;
                                        sellNotionalCall = match.Groups["notional1"].Value;
                                    }

                                    // Convert expiry to OVML date format
                                    string ovmlExpiryCS = ConvertExpiryToOVMLDate(result.Expiry, result.Underlying);

                                    result.OVML = $"OVML {result.Underlying} {ovmlExpiryCS} 2L B,S {buyStrikeCall}C,{sellStrikeCall}C N{buyNotionalCall}M,{sellNotionalCall}M VA" +
                                                  (spotExplicitlyProvided ? " SP" + spot : "");
                                    break;

                                case "RiskReversal_PutCall":
                                case "RiskReversal_CallPut":
                                    LogDebug($"DEBUG: Processing {pattern.Name} pattern");
                                    result.LegCount = 2;

                                    // Convert expiry to OVML date format
                                    string ovmlExpiryRR = ConvertExpiryToOVMLDate(result.Expiry, result.Underlying);

                                    result.OVML = RegexTradePatterns.BuildRiskReversalOVML(result.Underlying, match, ovmlExpiryRR, spot);
                                    break;

                                case "Vanilla":
                                    LogDebug($"DEBUG: Processing Vanilla pattern");
                                    result.LegCount = 1;
                                    string side = match.Groups["side"].Value.ToLower() == "buy" ? "B" : "S";
                                    string vanillaType = match.Groups["type"].Value.Substring(0, 1).ToUpper();

    // Convert expiry to OVML date format
             string ovmlExpiryV = ConvertExpiryToOVMLDate(result.Expiry, result.Underlying);

    result.OVML = $"OVML {result.Underlying} 1L {side} " +
            $"{match.Groups["strike"].Value}{vanillaType} " +
   $"{ovmlExpiryV} N{match.Groups["notional"].Value}M" +
    (fwdRefExplicitlyProvided ? " FW" + forwardRef : 
          spotExplicitlyProvided ? " SP" + spot : "");

      break;

         case "Simple_Vanilla":
         LogDebug($"DEBUG: Processing Simple_Vanilla pattern");
               result.LegCount = 1;
  string s = match.Groups["side"].Value.ToLower() == "buy" ? "B" : "S";
               string t = match.Groups["type"].Value.Substring(0, 1).ToUpper();

    // Convert expiry to OVML date format
           string ovmlExpirySV = ConvertExpiryToOVMLDate(result.Expiry, result.Underlying);

     result.OVML = $"OVML {result.Underlying} 1L {s} " +
         $"{match.Groups["strike"].Value}{t} " +
       $"{ovmlExpirySV} N{match.Groups["notional"].Value}M" +
      (fwdRefExplicitlyProvided ? " FW" + forwardRef : 
        spotExplicitlyProvided ? " SP" + spot : "");
    break;

                                default:
                                    LogDebug($"DEBUG: Unknown pattern name: {pattern.Name}");
                                    throw new Exception($"Unknown pattern: {pattern.Name}");
                            }

                            // Normalize outputs
                            result.OVML = NormalizeOVMLDates(result.OVML);
                            result.Expiry = NormalizeExpiry(result.Expiry);
                            result.GenerateUBS(); // Generate UBS format

                            LogDebug($"DEBUG: Generated OVML: '{result.OVML}'");
                            LogDebug($"DEBUG: Generated UBS: '{result.UBS}'");
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
            }
            else
            {
                LogDebug("DEBUG: Force AI requested, skipping regex patterns...");
            }

            // === AI fallback ===
            LogDebug($"DEBUG: No regex patterns matched. Falling back to AI...");
            LogDebug("[Parser] Falling back to AI...");

            // Try to capture explicit spot from input
            string explicitSpot = "";
            bool aiSpotExplicit = false;  // ADD THIS LINE

            var aiSpotMatch = RegexTradePatterns.SpotRegex.Match(input);
            if (aiSpotMatch.Success)
            {
                explicitSpot = aiSpotMatch.Groups["spot"].Value.Replace(",", ".");
                aiSpotExplicit = true;  // ADD THIS LINE
                LogDebug($"DEBUG: Explicit spot extracted for AI fallback: '{explicitSpot}'");
            }
            else
            {
                // Check for "s.r" format with space: "s.r 9.697"
                var srDotMatch = Regex.Match(input, @"\bs\.?\s*r\.?\s+(?<spot>\d+\.?\d*)", RegexOptions.IgnoreCase);
                if (srDotMatch.Success)
                {
                    explicitSpot = srDotMatch.Groups["spot"].Value;
                    aiSpotExplicit = true;  // ADD THIS LINE
                    LogDebug($"DEBUG: Explicit spot (s.r format) extracted: '{explicitSpot}'");
                }
                else
                {
                    // Also check for "v" format: "v9.9600"
                    var vMatch = Regex.Match(input, @"\bv\s*(?<spot>\d+\.?\d*)", RegexOptions.IgnoreCase);
                    if (vMatch.Success)
                    {
                        explicitSpot = vMatch.Groups["spot"].Value;
                        aiSpotExplicit = true;  // ADD THIS LINE
                        LogDebug($"DEBUG: Explicit spot (v format) extracted: '{explicitSpot}'");
                    }
                }
            }
            try
            {
                var aiResult = await ParseWithAI(input, explicitSpot, bypassPatternMatching: forceAI);

          
                // Correction: override AI expiry if regex extracted a better one
                if (!string.IsNullOrWhiteSpace(expiry) &&
                    expiry != "3M" &&
                    aiResult.Expiry != expiry &&
                    !aiResult.Expiry.Contains(",") &&      // ADD THIS - Don't override calendar spreads
                    !aiResult.OVML.Contains(","))          // ADD THIS - Don't override if OVML has multiple expiries
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

                // Post-process: Add forward reference to OVML if found in input but not in OVML
    var aiFwdRefMatch = RegexTradePatterns.ForwardRefRegex.Match(input);
    if (aiFwdRefMatch.Success)
        {
    string fwdRefValue = aiFwdRefMatch.Groups["fwdref"].Value.Replace(",", ".");
  
    // Remove any existing SP (spot reference) from OVML when FW is specified
         aiResult.OVML = Regex.Replace(aiResult.OVML, @"\s*SP[\d.,]+", "");
   
  // Add FW if not already present
    if (!aiResult.OVML.Contains("FW"))
          {
     aiResult.OVML = aiResult.OVML.TrimEnd() + " FW" + fwdRefValue;
}
  LogDebug($"DEBUG: Forward reference applied to AI OVML: FW{fwdRefValue}");
   }

    // Normalize both expiry + OVML
      aiResult.OVML = NormalizeOVMLDates(aiResult.OVML);
                aiResult.OVML = aiResult.OVML.Trim('"', '\'');  // Remove stray quotes
                aiResult.Expiry = NormalizeExpiry(aiResult.Expiry);
                aiResult.GenerateUBS(); // Generate UBS format

                LogDebug($"DEBUG: AI Success - ParseMethod: '{aiResult.ParseMethod}'");
                LogDebug($"DEBUG: AI Success - OVML: '{aiResult.OVML}'");
                LogDebug($"DEBUG: AI Success - UBS: '{aiResult.UBS}'");

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

        private string PreprocessInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            // 1. Strip extra whitespace
            input = Regex.Replace(input, @"\s+", " "); // Multiple spaces to single space
            input = Regex.Replace(input, @"[\r\n]+", "\n"); // Multiple newlines to single newline
            input = input.Trim();

            // 2a. Join separated currency pairs (usd nok → USDNOK)
            input = Regex.Replace(input, @"\b(USD|EUR|GBP|NOK|SEK|CNH)\s+(NOK|SEK|USD|EUR|GBP|CNH)\b", "$1$2", RegexOptions.IgnoreCase);

            // 2b. Normalize currency pair capitalization
            var currencyPairs = new[] { "EURSEK", "USDSEK", "USDNOK", "EURNOK", "GBPSEK", "NOKSEK", "EURUSD", "GBPUSD", "CNHSEK", "CNHNOK", "USDCNH", "EURCNH" };
            foreach (var pair in currencyPairs)
            {
                input = Regex.Replace(input, pair, pair, RegexOptions.IgnoreCase);
            }

            // 3. Fix common Swedish/Norwegian character issues
            var charReplacements = new Dictionary<string, string>
    {
        { "kopa", "köpa" },
        { "salja", "sälja" },
        { "salj", "sälj" },
        { "forfall", "förfall" },
      
        { "mio", "mio" }, // Already correct, but included for completeness
    };

            foreach (var replacement in charReplacements)
            {
                input = Regex.Replace(input, $@"\b{replacement.Key}\b", replacement.Value, RegexOptions.IgnoreCase);
            }

            // 4. Normalize decimal separators in context (handle European format)
            // Only replace commas with periods when they're clearly decimals (between digits)
            input = Regex.Replace(input, @"(\d),(\d)", "$1.$2");

            LogDebug($"DEBUG: Preprocessed input: '{input}'");

            return input;
        }

        // === AI integration ===
        private async Task<TradeParseResult> ParseWithAI(string input, string explicitSpot = "", bool bypassPatternMatching = false)
        {
            Console.WriteLine($"[DEBUG] _patternLearner is null: {_patternLearner == null}");
            if (_patternLearner == null)
            {
                await Task.Delay(10);
                throw new NotImplementedException("AI parser not configured - no OpenAI API key provided.");
            }
            string underlying = ExtractCurrencyPair(input);
            string expiry = ExtractExpiry(input);
            LogDebug("[TradeParser] About to call HybridPatternLearner.ParseWithAI...");
            var result = await _patternLearner.ParseWithAI(input, underlying, expiry, explicitSpot, bypassPatternMatching);
            LogDebug($"[TradeParser] HybridPatternLearner returned: {result.ParseMethod}");
            return result;
        }

        // === Learning Test Method ===
        public async Task<string> TestAILearning()
        {
            Console.WriteLine("=== TESTING AI LEARNING SYSTEM ===");

            try
            {
                // Check if AI is available
                if (_openAI == null)
                {
                    return "FAILED: OpenAI not initialized";
                }

                Console.WriteLine("OpenAI service is available");

                // Create test input/output pair
                string testInput = "GBPNOK: 25 mio\\n5mth gbp put nok call 20 delta";
                string testOVML = "OVML GBPNOK 5M P DS20 B N25M VA SP13.3454";

                Console.WriteLine($"Test Input: {testInput}");
                Console.WriteLine($"Test OVML: {testOVML}");

                // Test AI pattern analysis
                var analysisPrompt = $@"Analyze this FX options parsing example and create a regex pattern:

INPUT: ""{testInput}""
OUTPUT: ""{testOVML}""

Generate a regex pattern for similar inputs. Respond in JSON format:
{{
    ""name"": ""Test-Pattern-{DateTime.Now:yyyyMMdd-HHmmss}"",
    ""regexPattern"": ""your regex here"",
    ""description"": ""brief description"",
    ""strategyType"": ""RISK_REVERSAL""
}}";

                Console.WriteLine("Calling OpenAI for pattern analysis...");
                var response = await _openAI.GetChatCompletion(analysisPrompt, "gpt-4o-mini");
                var aiAnalysis = response.choices[0].message.content.Trim();

                Console.WriteLine($"AI Response: {aiAnalysis}");

                // Try to extract JSON
                var jsonMatch = Regex.Match(aiAnalysis, @"\{.*\}", RegexOptions.Singleline);
                if (jsonMatch.Success)
                {
                    Console.WriteLine($"JSON extracted successfully: {jsonMatch.Value}");
                    return "LEARNING TEST PASSED: AI created pattern successfully";
                }
                else
                {
                    Console.WriteLine("No valid JSON found in AI response");
                    return "LEARNING TEST PARTIAL: AI responded but no JSON found";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Learning Test Error: {ex.Message}");
                return $"LEARNING TEST ERROR: {ex.Message}";
            }
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
            "USDSEK", "GBPNOK", "NOKSEK", "SEKEUR", "SEKNOK",
            "CNHSEK", "USDCNH", "EURCNH", "CNHNOK"  // CNH (offshore yuan) pairs
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
                "HKD", "SGD", "THB", "MXN", "ZAR", "BRL", "KRW", "INR",
                "CNH"  // Offshore Chinese Yuan (important for FX options)
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
                var norwegianMonths = new[] { "JANUAR", "FEBRUARI", "MARS", "APRIL", "MAI", "JUNI", "JULI", "AUGUST", "SEPTEMBER", "OKTOBER", "NOVEMBER", "DESEMBER" };

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

                    // Validate it's a real month
                    if (normalizedMonth.Length != 3 || Array.IndexOf(monthNames, normalizedMonth) == -1)
                    {
                        LogDebug($"DEBUG: Skipping - '{month}' not recognized as a valid month");
                        // Don't return, continue to next pattern
                    }
                    else
                    {
                        int dayInt = int.Parse(day);
                        int yearInt = int.Parse(year);
                        int shortYear = yearInt % 100;

                        string result = $"{dayInt:D2}{normalizedMonth}{shortYear:D2}";
                        LogDebug($"DEBUG: Final expiry: '{result}'");
                        return result;
                    }
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
                var dayMonthMatches = Regex.Matches(input, @"\b(?<day>\d{1,2})e?\s+(?<month>[A-Za-zåäöæøé]{3,})\b", RegexOptions.IgnoreCase);

                foreach (Match dayMonthMatch in dayMonthMatches)
                {
                    LogDebug($"DEBUG: Day + month pattern matched: {dayMonthMatch.Value}");
                    string day = dayMonthMatch.Groups["day"].Value;
                    string month = dayMonthMatch.Groups["month"].Value;

                    string normalizedMonth = NormalizeMonth(month);

                    // Skip if month normalization failed (not a real month)
                    if (normalizedMonth.Length != 3 || Array.IndexOf(monthNames, normalizedMonth) == -1)
                    {
                        LogDebug($"DEBUG: Skipping - '{month}' not recognized as a valid month");
                        continue;
                    }

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

                        // If date is in the past, use next year
                        if (targetDate < DateTime.Now.Date)
                        {
                            currentYear++;
                            LogDebug($"DEBUG: Date in past, using next year: {currentYear}");
                        }

                        shortYear = currentYear % 100;
                        shortYear = currentYear % 100;
                    }

                    return $"{int.Parse(day):D2}{month}{shortYear:D2}";
                }
                // 5. Four-digit format without year (MMDD or DDMM): exp 0612
                LogDebug("DEBUG: Testing four-digit date format...");
                var shortDateMatch = Regex.Match(input, @"\bexp\s*(\d{4})\b", RegexOptions.IgnoreCase);
                if (shortDateMatch.Success)
                {
                    string digits = shortDateMatch.Groups[1].Value;
                    int month = int.Parse(digits.Substring(0, 2));
                    int day = int.Parse(digits.Substring(2, 2));

                    LogDebug($"DEBUG: Four-digit format matched: {digits}");

                    // Swap if month > 12 (it's DDMM format)
                    if (month > 12)
                    {
                        (month, day) = (day, month);
                        LogDebug($"DEBUG: Swapped to DDMM format - day: {day}, month: {month}");
                    }

                    // Determine year: use current year if date hasn't passed, otherwise next year
                    int currentYear = DateTime.Now.Year;
                    DateTime targetDate = new DateTime(currentYear, month, day);

                    if (targetDate < DateTime.Now)
                    {
                        targetDate = targetDate.AddYears(1);
                        LogDebug($"DEBUG: Date in past, using next year: {targetDate.Year}");
                    }

                    string monthAbbr = monthNames[month - 1];
                    string shortYear = targetDate.Year.ToString().Substring(2);
                    string result = $"{day:D2}{monthAbbr}{shortYear}";

                    LogDebug($"DEBUG: Extracted expiry from MMDD format: {result}");
                    return result;
                }

                // 6. Tenor extraction with context awareness
                LogDebug("DEBUG: Testing tenor patterns...");
                LogDebug($"DEBUG: Full input for tenor matching: '{input}'");

                // PRIORITY 1: Explicit words (ALWAYS expiry, never notional)
                var explicitTenorMatch = Regex.Match(
                    input,
                    @"\b(?<num>\d+)[\s-]*(?<unit>year|years|yr|yrs|month|months|mth|wks|wk|day|days)\b",
                    RegexOptions.IgnoreCase
                );

                if (explicitTenorMatch.Success)
                {
                    string number = explicitTenorMatch.Groups["num"].Value;
                    string unit = explicitTenorMatch.Groups["unit"].Value.ToUpper();

                    LogDebug($"DEBUG: Explicit tenor found - {number} {unit}");

                    // Normalize
                    string period = "M";
                    if (unit.StartsWith("Y")) period = "Y";
                    else if (unit.StartsWith("M")) period = "M";
                    else if (unit.StartsWith("W")) period = "W";
                    else if (unit.StartsWith("D")) period = "D";

                    return number + period;
                }

                // PRIORITY 2: Ambiguous single-letter with CONTEXT CHECK
                var ambiguousTenorMatches = Regex.Matches(
                    input,
                    @"\b(?<num>\d+)[\s-]*(?<unit>[mMwWyYdD])\b",
                    RegexOptions.IgnoreCase
                );

                foreach (Match match in ambiguousTenorMatches)
                {
                    string number = match.Groups["num"].Value;
                    string unit = match.Groups["unit"].Value.ToUpper();
                    int matchEnd = match.Index + match.Length;

                    // Look at what comes AFTER this match
                    string afterMatch = matchEnd < input.Length
                        ? input.Substring(matchEnd).Trim()
                        : "";

                    LogDebug($"DEBUG: Found ambiguous '{number}{unit}', checking context: '{afterMatch.Substring(0, Math.Min(20, afterMatch.Length))}'");

                    // SKIP if followed by clear notional indicators (mio/mil/million or "per leg")
                    if (Regex.IsMatch(afterMatch, @"^\s*(mio|mil|million|per\s+leg)\b", RegexOptions.IgnoreCase))
                    {
                        LogDebug($"DEBUG: Skipping '{number}{unit}' - notional context detected");
                        continue;
                    }

                    // SKIP if this is likely a delta specification (e.g., "25d call" = "25 delta call")
                    // Common delta values: 10, 15, 25, 35, 40, 50
                    // But small values like "2d" could be "2 days", so only skip typical delta values
                    if ((unit == "D" || unit == "M") && Regex.IsMatch(afterMatch, @"^\s*(call|put)\b", RegexOptions.IgnoreCase))
                    {
                        int deltaValue = int.Parse(number);
                        // Common delta values that are unlikely to be day/month tenors
                        if (deltaValue == 10 || deltaValue == 15 || deltaValue == 25 || deltaValue == 35 || deltaValue == 40 || deltaValue == 50)
                        {
                            LogDebug($"DEBUG: Skipping '{number}{unit}' - likely delta specification (common delta value before call/put)");
                            continue;
                        }
                    }

                    // ACCEPT if followed by expiry-related context (but not call/put directly, handled above)
                    if (Regex.IsMatch(afterMatch, @"^\s*(option|strike|straddle|spread|expir)", RegexOptions.IgnoreCase))
                    {
                        LogDebug($"DEBUG: Accepting '{number}{unit}' - expiry context confirmed");
                        return number + unit;
                    }

                    // ACCEPT if number is small (tenors rarely > 36 months, 5 years, 52 weeks)
                    int numValue = int.Parse(number);
                    if (unit == "M" && numValue <= 36)
                    {
                        LogDebug($"DEBUG: Accepting '{number}M' - reasonable tenor duration");
                        return number + unit;
                    }
                    if (unit == "Y" && numValue <= 10)
                    {
                        LogDebug($"DEBUG: Accepting '{number}Y' - reasonable tenor duration");
                        return number + unit;
                    }
                    if (unit == "W" && numValue <= 52)
                    {
                        LogDebug($"DEBUG: Accepting '{number}W' - reasonable tenor duration");
                        return number + unit;
                    }
                    if (unit == "D" && numValue <= 365)
                    {
                        LogDebug($"DEBUG: Accepting '{number}D' - reasonable tenor duration");
                        return number + unit;
                    }

                    LogDebug($"DEBUG: Skipping '{number}{unit}' - ambiguous without clear context");
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

        /// <summary>
        /// Convert expiry to OVML date format (MM/dd/yy).
        /// Handles both explicit dates (17Dec25) and tenors (1M, 3M).
        /// </summary>
        private string ConvertExpiryToOVMLDate(string expiry, string currencyPair = "EURUSD")
        {
            if (string.IsNullOrWhiteSpace(expiry))
                return "";

            // Check if it's a tenor (1M, 3M, 1W, 2D, etc.)
            var tenorMatch = Regex.Match(expiry, @"^(\d+)([MYWD])$", RegexOptions.IgnoreCase);
            if (tenorMatch.Success)
            {
                try
                {
                    // Use FxCalendarService for database-backed business day calculation
                    var expiryDate = FX.Infrastructure.Calendars.Legacy.FxCalendarService.Instance.CalculateExpiry(
                        DateTime.UtcNow,
                        currencyPair,
                        expiry.ToUpper()
                    );

                    // Return in OVML format: MM/dd/yy
                    return expiryDate.ToString("MM/dd/yy", CultureInfo.InvariantCulture);
                }
                catch (Exception ex)
                {
                    LogDebug($"DEBUG: Error calculating date from tenor '{expiry}': {ex.Message}");

                    // Fallback with simple calculation
                    int amount = int.Parse(tenorMatch.Groups[1].Value);
                    string unit = tenorMatch.Groups[2].Value.ToUpper();

                    DateTime result = DateTime.Today;
                    result = unit switch
                    {
                        "D" => result.AddDays(amount),
                        "W" => result.AddDays(amount * 7),
                        "M" => result.AddMonths(amount),
                        "Y" => result.AddYears(amount),
                        _ => result
                    };

                    // Simple weekend adjustment
                    while (result.DayOfWeek == DayOfWeek.Saturday || result.DayOfWeek == DayOfWeek.Sunday)
                    {
                        result = result.AddDays(1);
                    }

                    // FX Market Rule - January 1st can NEVER be an expiry or settlement date
                    // If expiry falls on Jan 1st, move BACK to last business day of December
                    if (result.Month == 1 && result.Day == 1)
                    {
                        result = result.AddDays(-1);
                        // Adjust again for weekends
                        while (result.DayOfWeek == DayOfWeek.Saturday || result.DayOfWeek == DayOfWeek.Sunday)
                        {
                            result = result.AddDays(-1);
                        }
                    }

                    return result.ToString("MM/dd/yy", CultureInfo.InvariantCulture);
                }
            }

            // It's already a date - normalize it to MM/dd/yy format
            var normalized = TryNormalizeDate(expiry);
            if (!string.IsNullOrEmpty(normalized))
                return normalized;

            // If we can't parse it, return as-is
            return expiry;
        }

        // === Normalization helpers ===
        private string NormalizeOVMLDates(string ovml)
        {
            if (string.IsNullOrWhiteSpace(ovml))
                return ovml;

            var parts = ovml.Split(' ', StringSplitOptions.RemoveEmptyEntries);

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

            // Preserve tenor formats (1M, 2W, 3D, 1Y, etc.) - these should not be normalized to dates
            if (Regex.IsMatch(expiry, @"^\d+[MWYD]$", RegexOptions.IgnoreCase))
            {
                return expiry.ToUpperInvariant();
            }

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

        public bool RemoveLearnedPattern(string patternName)
        {
            try
            {
                Console.WriteLine($"[TradeParser] RemoveLearnedPattern called with: '{patternName}'");

                if (_patternLearner != null)
                {
                    string fullPatternName = $"Learned-{patternName}";
                    Console.WriteLine($"[TradeParser] Looking for pattern: '{fullPatternName}'");

                    bool result = _patternLearner.RemovePattern(fullPatternName);
                    Console.WriteLine($"[TradeParser] RemovePattern returned: {result}");
                    return result;
                }
                Console.WriteLine($"[TradeParser] _patternLearner is null");
                return false;
            }
            catch (Exception ex)
            {
                LogDebug($"Error removing pattern {patternName}: {ex.Message}");
                return false;
            }
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
        public string AdditionalInfo { get; set; }

        // Validation properties
        public SanityCheckResult ValidationResult { get; set; }
        public bool IsValidated => ValidationResult?.IsValid ?? false;
        public string ValidationWarning => ValidationResult?.IsValid == false ? ValidationResult.Reason : null;

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

        // Generate UBS format from OVML
        public void GenerateUBS()
        {
            if (string.IsNullOrEmpty(OVML))
            {
                UBS = "";
                return;
            }

            try
            {
                UBS = ConvertOVMLToUBS(OVML, Underlying, Expiry, LegCount);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating UBS format: {ex.Message}");
                UBS = "";
            }
        }

        private string ConvertOVMLToUBS(string ovml, string underlying, string expiry, int legCount)
        {
            var parts = ovml.Split(' ');
            if (parts.Length < 4) return "";

            string currency = ExtractBaseCurrency(underlying); // EUR from EURNOK
            string spot = ExtractSpotFromOVML(ovml);

            // 🔑 Decide if expiry is tenor or real date
            string formattedExpiry = "";
            if (Regex.IsMatch(expiry, @"^\d+[WwMmYyDd]$"))
            {
                // It’s a tenor → keep as-is
                formattedExpiry = expiry.ToUpper();
            }
            else
            {
                // Try to format as a date
                formattedExpiry = FormatDateForUBS(expiry);
            }

            if (legCount == 1)
                return BuildSingleLegUBS(parts, underlying, currency, formattedExpiry, spot);
            else
                return BuildMultiLegUBS(parts, underlying, currency, formattedExpiry, spot, legCount);
        }






        private string BuildSingleLegUBS(string[] parts, string underlying, string currency, string formattedDate, string spot)
        {
            string strike = "";
            string optionType = "";
            string notional = "";

            for (int i = 0; i < parts.Length; i++)
            {
                var token = parts[i].Trim().TrimEnd(',', ';');

                // Case 1: option type appears as a separate token ("C" or "P")
                if (token.Equals("C", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("P", StringComparison.OrdinalIgnoreCase))
                {
                    optionType = token.ToUpperInvariant();
                    if (i > 0) strike = parts[i - 1].Trim().TrimEnd(',', ';');
                    continue;
                }

                // Case 2: option type is attached to the strike ("1.1625C" or "1.1625P")
                if (token.EndsWith("C", StringComparison.OrdinalIgnoreCase) ||
                    token.EndsWith("P", StringComparison.OrdinalIgnoreCase))
                {
                    optionType = char.ToUpperInvariant(token[^1]).ToString();
                    strike = token[..^1]; // everything except the last char
                    continue;
                }

                // Notional like "N15M"
                if (token.StartsWith("N", StringComparison.OrdinalIgnoreCase) &&
                    token.EndsWith("M", StringComparison.OrdinalIgnoreCase))
                {
                    notional = token.Substring(1, token.Length - 2); // strip N...M
                    continue;
                }
            }

            // Need all three to produce output
            if (string.IsNullOrEmpty(strike) || string.IsNullOrEmpty(optionType) || string.IsNullOrEmpty(notional))
                return "";

            bool isTenor = Regex.IsMatch(formattedDate, @"^\d+[WwMmYyDd]$");

            // If tenor: "<underlying> <tenor> <C|P> <CCY>"
            // Else:     "<underlying> <strike><C|P> <CCY>"
            string result = isTenor
                ? $"{underlying} {formattedDate.ToUpperInvariant()} {optionType} {currency}"
                : $"{underlying} {strike}{optionType} {currency}";

            // Notional ÷10, culture-safe, with a space before "MIO"
            if (double.TryParse(notional, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
            {
                double scaled = amount / 10.0;
                result += $" {scaled.ToString("0.########", CultureInfo.InvariantCulture)} MIO";
            }

            // Append date only when it's a real date (not a tenor)
            if (!isTenor && !string.IsNullOrEmpty(formattedDate))
                result += $" {formattedDate}";

            // Append SPOT if present
            if (!string.IsNullOrWhiteSpace(spot))
                result += $" SPOT {spot}";

            return result;
        }

        private string BuildMultiLegUBS(string[] parts, string underlying, string currency, string formattedDate, string spot, int legCount)
        {
            string strikes = FindStrikesInOVML(parts);
            string notionals = FindNotionalsInOVML(parts);

            if (string.IsNullOrEmpty(strikes) || string.IsNullOrEmpty(notionals))
                return "";

            var strikeList = strikes.Split(',');
            var notionalList = notionals.Replace("N", "").Replace("M", "").Split(',');

            var legs = new List<string>();

            for (int i = 0; i < Math.Min(strikeList.Length, notionalList.Length); i++)
            {
                string strike = strikeList[i].Trim();
                string notional = notionalList[i].Trim();

                char optionType = strike.EndsWith("P") ? 'P' : 'C';
                strike = strike.TrimEnd('C', 'P');

                string leg = $"{underlying} {strike}{optionType}";

                if (i == 0) leg += $" {currency}";

                if (double.TryParse(notional, out double amount))
                {
                    double scaledAmount = amount / 10.0;
                    leg += $" {scaledAmount}MIO";
                }

                // Only first leg gets date if it's real date (not tenor)
                if (i == 0 && !string.IsNullOrEmpty(formattedDate) && !Regex.IsMatch(formattedDate, @"^\d+[WwMmYyDd]$"))
                {
                    leg += $" {formattedDate}";
                }

                legs.Add(leg);
            }

            string result = string.Join(", ", legs);

            if (!string.IsNullOrEmpty(spot))
            {
                result += $" SPOT {spot}";
            }

            return result;
        }







        private string ExtractBaseCurrency(string currencyPair)
        {
            // Extract base currency (first 3 letters)
            if (currencyPair.Length >= 3)
            {
                return currencyPair.Substring(0, 3);
            }
            return "EUR"; // Default fallback
        }

        private string FormatDateForUBS(string expiry)

        {
            try
            {
                // Case 1: Tenor like 1W, 2W, 3M, 1Y → return as-is
                if (Regex.IsMatch(expiry, @"^\d+[WwDdMmYy]$"))
                {
                    return expiry.ToUpper();
                }

                // Case 2: MM/dd/yy (US style date)
                if (Regex.IsMatch(expiry, @"^\d{2}/\d{2}/\d{2}$") &&
                    DateTime.TryParseExact(expiry, "MM/dd/yy", null, DateTimeStyles.None, out DateTime dtUS))
                {
                    return dtUS.ToString("dd-MMM-yy", CultureInfo.InvariantCulture);
                }

                // Case 3: ddMMMyy (e.g. 11NOV25)
                if (Regex.IsMatch(expiry, @"^\d{1,2}[A-Za-z]{3}\d{2}$") &&
                    DateTime.TryParseExact(expiry, new[] { "ddMMMyy", "dMMMyy" },
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dtFX))
                {
                    return dtFX.ToString("dd-MMM-yy", CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error formatting date for UBS: {ex.Message}");
            }

            return ""; // no expiry fallback
        }






        private DateTime? CalculateFutureDate(string tenor, string currencyPair = "EURUSD")
        {
            if (string.IsNullOrEmpty(tenor)) return null;

            var match = Regex.Match(tenor, @"^(\d+)([MYWWD])$");
            if (!match.Success) return null;

            try
            {
                // Use FxCalendarService for database-backed business day calculation
                var expiryDate = FX.Infrastructure.Calendars.Legacy.FxCalendarService.Instance.CalculateExpiry(
                    DateTime.UtcNow,
                    currencyPair,
                    tenor
                );

                Console.WriteLine($"[CALENDAR] Tenor {tenor} for {currencyPair}: {expiryDate:yyyy-MM-dd (ddd)} (adjusted for business days)");
                return expiryDate;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CALENDAR] FxCalendarService failed for {tenor}/{currencyPair}: {ex.Message}");

                // Fallback to simple calculation if FxDateService fails
                int amount = int.Parse(match.Groups[1].Value);
                char period = match.Groups[2].Value[0];
                var baseDate = DateTime.Now;

                var result = period switch
                {
                    'D' => baseDate.AddDays(amount),
                    'W' => baseDate.AddDays(amount * 7),
                    'M' => baseDate.AddMonths(amount),
                    'Y' => baseDate.AddYears(amount),
                    _ => (DateTime?)null
                };

                // Simple weekend adjustment for fallback
                if (result.HasValue)
                {
                    while (result.Value.DayOfWeek == DayOfWeek.Saturday || result.Value.DayOfWeek == DayOfWeek.Sunday)
                    {
                        result = result.Value.AddDays(1);
                    }
                    Console.WriteLine($"[CALENDAR] Fallback used - adjusted to {result:yyyy-MM-dd (ddd)}");
                }

                return result;
            }
        }

        private string FindStrikesInOVML(string[] parts)
        {
            // Look for pattern like "11.8000C,12.1000C,11.5500P"
            return parts.FirstOrDefault(p => p.Contains("C") || p.Contains("P")) ?? "";
        }

        private string FindDirectionsInOVML(string[] parts)
        {
            // Look for pattern like "B,S,S"
            return parts.FirstOrDefault(p => p.Contains("B") || p.Contains("S")) ?? "";
        }

        private string FindNotionalsInOVML(string[] parts)
        {
            // Look for pattern like "N100M,125M,100M"
            return parts.FirstOrDefault(p => p.StartsWith("N") && p.Contains("M")) ?? "";
        }

        private string ExtractSpotFromOVML(string ovml)
        {
            var match = Regex.Match(ovml, @"SP(\d+\.?\d*)");
            return match.Success ? match.Groups[1].Value : "";
        }
    }
}