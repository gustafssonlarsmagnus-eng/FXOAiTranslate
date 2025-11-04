using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FXOAiTranslator
{
    public class TradeSanityChecker
    {
        private readonly OpenAIService _openAIService;
        private readonly BloombergService _bloombergService;
        private readonly Action<string> _debugCallback;

        public TradeSanityChecker(OpenAIService openAIService, BloombergService bloombergService, Action<string> debugCallback = null)
        {
            _openAIService = openAIService;
            _bloombergService = bloombergService;
            _debugCallback = debugCallback;
        }

        public SanityCheckResult ValidateTrade(string originalInput, TradeParseResult aiResult)
        {
            var result = new RuleBasedCheckResult();

            if (string.IsNullOrWhiteSpace(aiResult.OVML))
            {
                result.CriticalErrors.Add("OVML string is empty");
                return WrapResult(false, "Empty OVML", result);
            }

            var ovmlParts = aiResult.OVML.Split(' ');

            // 1. OVML prefix
            if (!aiResult.OVML.StartsWith("OVML"))
                result.CriticalErrors.Add("OVML does not start with 'OVML'");
            else
                result.PassedChecks.Add("Starts with OVML");

            // 2. Currency pair (6 uppercase letters)
            var currencyRegex = new Regex(@"\b[A-Z]{6}\b");
            if (!currencyRegex.IsMatch(aiResult.OVML))
                result.CriticalErrors.Add("No valid 6-letter currency pair found");
            else
                result.PassedChecks.Add("Currency pair format OK");

            // 3. Detect if multi-leg
            var legCountMatch = Regex.Match(aiResult.OVML, @"\b(\d+)L\b");
            bool isMultiLeg = legCountMatch.Success;
            int expectedLegs = isMultiLeg ? int.Parse(legCountMatch.Groups[1].Value) : 1;

            if (isMultiLeg)
            {
                result.PassedChecks.Add($"Multi-leg trade detected: {expectedLegs} legs");

                // Validate multi-leg structure
                ValidateMultiLeg(aiResult.OVML, expectedLegs, result);
            }
            else
            {
                // Single leg validation
                ValidateSingleLeg(aiResult.OVML, result);
            }

            // 4. Spot reference (optional but good)
            if (aiResult.OVML.Contains("SP"))
                result.PassedChecks.Add("Spot reference present");
            else
                result.Warnings.Add("No spot reference (SP) in OVML");

            // 5. Check if expiry is in the past
            var expiryStr = ExtractExpiryFromOVML(aiResult.OVML);
            if (!string.IsNullOrEmpty(expiryStr))
            {
                // Try to parse the expiry date
                DateTime expiryDate;
                bool parsed = false;

                // Try multiple date formats
                string[] formats = { "MM/dd/yy", "dd/MM/yy", "ddMMMyy", "ddMMyy" };
                foreach (var format in formats)
                {
                    if (DateTime.TryParseExact(expiryStr, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out expiryDate))
                    {
                        parsed = true;
                        if (expiryDate < DateTime.Now.Date)
                        {
                            result.CriticalErrors.Add($"Expiry date {expiryStr} is in the past (parsed as {expiryDate:MMM dd, yyyy})");
                        }
                        else
                        {
                            result.PassedChecks.Add($"Expiry date is in the future ({expiryDate:MMM dd, yyyy})");
                        }
                        break;
                    }
                }

                if (!parsed)
                {
                    result.Warnings.Add($"Could not parse expiry date format: {expiryStr}");
                }
            }

    

            // Display results
            DisplayResults(result);

            // Decision logic
            if (result.CriticalErrors.Count > 0)
                return WrapResult(false, "Critical errors: " + string.Join("; ", result.CriticalErrors), result);

            // If only warnings, still valid but lower confidence
            double confidence = result.Warnings.Count == 0 ? 0.9 : 0.75;
            return WrapResult(true, "Structure validated successfully", result, confidence);
        }

        private void ValidateSingleLeg(string ovml, RuleBasedCheckResult result)
        {
            // Check for duplicate expiry dates
            var expiryMatches = Regex.Matches(ovml, @"\d{2}/\d{2}/\d{2}|\d{1,2}[A-Z]{3}\d{2}?|\b\d+[DWMY]\b");
            if (expiryMatches.Count > 1)
            {
                result.CriticalErrors.Add($"Multiple expiry dates found ({expiryMatches.Count}): {string.Join(", ", expiryMatches.Cast<Match>().Select(m => m.Value))}");
            }
            else if (expiryMatches.Count == 1)
            {
                result.PassedChecks.Add("Single expiry date");
            }
            else
            {
                result.CriticalErrors.Add("No expiry date found");
            }

            // Option type (C/P) - must be attached to strike like 0.9500C
            if (!Regex.IsMatch(ovml, @"\d+\.\d+[CP]"))
                result.CriticalErrors.Add("Missing option type (C or P)");
            else
                result.PassedChecks.Add("Option type present");

            // Direction (B/S)
            if (!Regex.IsMatch(ovml, @"\b[BS]\b"))
                result.CriticalErrors.Add("Missing direction (B or S)");
            else
                result.PassedChecks.Add("Direction present");

            // Notional - must actually exist, not just format check
            if (!Regex.IsMatch(ovml, @"N\d+M\b"))
                result.CriticalErrors.Add("Missing notional (NxxM not found)");
            else
                result.PassedChecks.Add("Notional format OK");

            // Strike
            if (!Regex.IsMatch(ovml, @"\d+(\.\d+)?"))
                result.Warnings.Add("No strike detected");
            else
                result.PassedChecks.Add("Strike detected");
        }

        private void ValidateMultiLeg(string ovml, int expectedLegs, RuleBasedCheckResult result)
        {
            // Extract strikes (e.g., "9.6000P,9.1500P")
            var strikesMatch = Regex.Match(ovml, @"(\d+\.\d+[CP],?)+");
            if (!strikesMatch.Success)
            {
                result.CriticalErrors.Add("No strikes found in multi-leg trade");
                return;
            }

            var strikes = strikesMatch.Value.Split(',');
            if (strikes.Length != expectedLegs)
            {
                result.CriticalErrors.Add($"Strike count mismatch: expected {expectedLegs}, found {strikes.Length}");
            }
            else
            {
                result.PassedChecks.Add($"Strike count matches leg count ({expectedLegs})");
            }

            // Extract directions (e.g., "B,S")
            var directionsMatch = Regex.Match(ovml, @"\b([BS],?)+\b");
            if (!directionsMatch.Success)
            {
                result.CriticalErrors.Add("No directions found in multi-leg trade");
                return;
            }

            var directions = directionsMatch.Value.Split(',');
            if (directions.Length != expectedLegs)
            {
                result.CriticalErrors.Add($"Direction count mismatch: expected {expectedLegs}, found {directions.Length}");
            }
            else
            {
                result.PassedChecks.Add($"Direction count matches leg count ({expectedLegs})");
            }

            // Extract notionals (e.g., "N5M,25M")
            var notionalsMatch = Regex.Match(ovml, @"N(\d+M,?)+");
            if (!notionalsMatch.Success)
            {
                result.Warnings.Add("No notionals found in multi-leg trade");
            }
            else
            {
                var notionalString = notionalsMatch.Value.Substring(1); // Remove leading N
                var notionals = notionalString.Split(',');

                if (notionals.Length != expectedLegs)
                {
                    result.Warnings.Add($"Notional count mismatch: expected {expectedLegs}, found {notionals.Length}");
                }
                else
                {
                    result.PassedChecks.Add($"Notional count matches leg count ({expectedLegs})");
                }
            }

            // Validate spread logic for 2-leg trades
            if (expectedLegs == 2 && strikes.Length == 2 && directions.Length == 2)
            {
                ValidateSpreadLogic(strikes, directions, result);
            }
        }

        private void ValidateSpreadLogic(string[] strikes, string[] directions, RuleBasedCheckResult result)
        {
            // Extract strike values and types
            var strike1Match = Regex.Match(strikes[0], @"(\d+\.\d+)([CP])");
            var strike2Match = Regex.Match(strikes[1], @"(\d+\.\d+)([CP])");

            if (!strike1Match.Success || !strike2Match.Success)
                return;

            double strike1Value = double.Parse(strike1Match.Groups[1].Value);
            double strike2Value = double.Parse(strike2Match.Groups[1].Value);
            char type1 = strike1Match.Groups[2].Value[0];
            char type2 = strike2Match.Groups[2].Value[0];

            // Check if mixed types (could be straddle/strangle)
            if (type1 != type2)
            {
                // Check if it's a straddle (same strike, different types)
                if (Math.Abs(strike1Value - strike2Value) < 0.0001)
                {
                    // Straddle: same strike, buy/buy or sell/sell
                    bool isBuyBoth = directions[0] == "B" && directions[1] == "B";
                    bool isSellBoth = directions[0] == "S" && directions[1] == "S";

                    if (isBuyBoth)
                    {
                        result.PassedChecks.Add($"Long straddle structure detected (same strike {strike1Value}, mixed types)");
                    }
                    else if (isSellBoth)
                    {
                        result.PassedChecks.Add($"Short straddle structure detected (same strike {strike1Value}, mixed types)");
                    }
                    else
                    {
                        result.Warnings.Add($"Unusual straddle directions: {directions[0]},{directions[1]}");
                    }
                    return; // Valid straddle/strangle, exit
                }

                // Different strikes and different types = strangle
                result.PassedChecks.Add($"Strangle structure detected (different strikes, mixed types)");
                return;
            }

            // Same type - validate as spread...
            // Check spread direction logic
            bool isBuyFirst = directions[0] == "B";
            bool isSellSecond = directions[1] == "S";

            if (type1 == 'P') // Put spread
            {
                // Put spread: Buy higher strike, Sell lower strike
                if (strike1Value > strike2Value && isBuyFirst && isSellSecond)
                {
                    result.PassedChecks.Add("Put spread structure correct (Buy high, Sell low)");
                }
                else if (strike1Value < strike2Value)
                {
                    result.Warnings.Add($"Put spread: Higher strike should be first (found {strike1Value} then {strike2Value})");
                }
                else if (!isBuyFirst || !isSellSecond)
                {
                    result.Warnings.Add($"Put spread directions unusual: {directions[0]},{directions[1]}");
                }
            }
            else // Call spread
            {
                // Call spread: Buy lower strike, Sell higher strike
                if (strike1Value < strike2Value && isBuyFirst && isSellSecond)
                {
                    result.PassedChecks.Add("Call spread structure correct (Buy low, Sell high)");
                }
                else if (strike1Value > strike2Value)
                {
                    result.Warnings.Add($"Call spread: Lower strike should be first (found {strike1Value} then {strike2Value})");
                }
                else if (!isBuyFirst || !isSellSecond)
                {
                    result.Warnings.Add($"Call spread directions unusual: {directions[0]},{directions[1]}");
                }
            }
        }

        private void DisplayResults(RuleBasedCheckResult result)
        {
            if (result.PassedChecks.Any())
            {
                Console.ForegroundColor = ConsoleColor.Green;
                foreach (var check in result.PassedChecks)
                {
                    Console.WriteLine($"[SANITY OK] ✓ {check}");
                }
                Console.ResetColor();
            }

            if (result.Warnings.Any())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                foreach (var warning in result.Warnings)
                {
                    Console.WriteLine($"[SANITY WARNING] ⚠ {warning}");
                }
                Console.ResetColor();
            }

            if (result.CriticalErrors.Any())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                foreach (var error in result.CriticalErrors)
                {
                    Console.WriteLine($"[SANITY ERROR] ✗ {error}");
                }
                Console.ResetColor();
            }
        }

        public Task<SanityCheckResult> ValidateTradeAsync(string originalInput, TradeParseResult aiResult)
        {
            return Task.FromResult(ValidateTrade(originalInput, aiResult));
        }

        private SanityCheckResult WrapResult(bool isValid, string reason, RuleBasedCheckResult checks, double confidence = 0.9)
        {
            if (!isValid)
                confidence = 0.1;

            LogDebug($"Sanity check: {(isValid ? "VALID" : "INVALID")} - {reason}");
            return new SanityCheckResult
            {
                IsValid = isValid,
                ValidationMethod = "Enhanced Multi-Leg Validation",
                Reason = reason,
                Confidence = confidence,
                RuleBasedChecks = checks
            };
        }

        private void LogDebug(string msg) => _debugCallback?.Invoke($"[SanityChecker] {msg}");

        public void Dispose()
        {
            // Nothing to dispose
        }

        private string ExtractExpiryFromOVML(string ovml)
        {
            var match = Regex.Match(ovml, @"\b(\d{1,2}/\d{1,2}/\d{2}|\d{2}[A-Z]{3}\d{2}|\d+[DWMY])\b");
            return match.Success ? match.Value : "";
        }

    }

    public class SanityCheckResult
    {
        public bool IsValid { get; set; }
        public string ValidationMethod { get; set; }
        public string Reason { get; set; }
        public double Confidence { get; set; }
        public RuleBasedCheckResult RuleBasedChecks { get; set; }
    }

    public class RuleBasedCheckResult
    {
        public List<string> CriticalErrors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> PassedChecks { get; set; } = new();
    }
}