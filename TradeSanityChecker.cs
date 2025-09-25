using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FXOAiTranslator
{
    public class TradeSanityChecker
    {
        private readonly OpenAIService _openAIService;
        private readonly BloombergService _bloombergService;
        private readonly Action<string> _debugCallback;

        public TradeSanityChecker(OpenAIService openAIService, BloombergService bloombergService, Action<string> debugCallback = null)
        {
            _openAIService = openAIService;   // kept for signature compatibility
            _bloombergService = bloombergService; // not used in this slim version
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

            // 3. Option type (C/P)
            if (!Regex.IsMatch(aiResult.OVML, @"\b[CP]\b"))
                result.CriticalErrors.Add("Missing option type (C or P)");
            else
                result.PassedChecks.Add("Option type present");

            // 4. Direction (B/S)
            if (!Regex.IsMatch(aiResult.OVML, @"\b[BS]\b"))
                result.CriticalErrors.Add("Missing direction (B or S)");
            else
                result.PassedChecks.Add("Direction present");

            // 5. Notional format (NxxM)
            if (!Regex.IsMatch(aiResult.OVML, @"N\d+M"))
                result.Warnings.Add("No notional in standard format (NxxM)");
            else
                result.PassedChecks.Add("Notional format OK");

            // 6. Strike(s)
            if (!Regex.IsMatch(aiResult.OVML, @"\d+(\.\d+)?[CP]?"))
                result.Warnings.Add("No strike detected");
            else
                result.PassedChecks.Add("Strike(s) detected");

            // 7. Spot reference (optional but useful)
            if (aiResult.OVML.Contains("SP"))
                result.PassedChecks.Add("Spot reference present");
            else
                result.Warnings.Add("No spot reference (SP) in OVML");

            // === Result decision ===
            if (result.CriticalErrors.Count > 0)
                return WrapResult(false, "Critical errors: " + string.Join("; ", result.CriticalErrors), result);
            // Highlight results in console

            // Passed checks in green
            if (result.PassedChecks.Any())
            {
                Console.ForegroundColor = ConsoleColor.Green;
                foreach (var check in result.PassedChecks)
                {
                    Console.WriteLine($"[SANITY OK] ✅ {check}");
                }
                Console.ResetColor();
            }

            // Warnings in yellow
            if (result.Warnings.Any())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                foreach (var warning in result.Warnings)
                {
                    Console.WriteLine($"[SANITY WARNING] ⚠ {warning}");
                }
                Console.ResetColor();
            }

            // Critical errors in red
            if (result.CriticalErrors.Any())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                foreach (var error in result.CriticalErrors)
                {
                    Console.WriteLine($"[SANITY ERROR] ❌ {error}");
                }
                Console.ResetColor();
            }


            return WrapResult(true, "Basic structure looks OK", result);
        }
        public Task<SanityCheckResult> ValidateTradeAsync(string originalInput, TradeParseResult aiResult)
        {
            return Task.FromResult(ValidateTrade(originalInput, aiResult));
        }

        private SanityCheckResult WrapResult(bool isValid, string reason, RuleBasedCheckResult checks)
        {
            LogDebug($"Sanity check: {(isValid ? "VALID" : "INVALID")} - {reason}");
            return new SanityCheckResult
            {
                IsValid = isValid,
                ValidationMethod = "Rule-Based Structural",
                Reason = reason,
                Confidence = isValid ? 0.9 : 0.1,
                RuleBasedChecks = checks
            };
        }

        private void LogDebug(string msg) => _debugCallback?.Invoke($"[SanityChecker] {msg}");

        public void Dispose()
        {
            // Nothing to dispose in this slim version
        }
    }

    // Support classes
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
