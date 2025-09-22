using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
            _openAIService = openAIService;
            _bloombergService = bloombergService;
            _debugCallback = debugCallback;
        }

        public async Task<SanityCheckResult> ValidateTradeAsync(string originalInput, TradeParseResult aiResult)
        {
            try
            {
                LogDebug("Starting sanity check validation...");

                // Perform basic rule-based checks first (fast)
                var ruleBasedResult = PerformRuleBasedChecks(originalInput, aiResult);

                // If rule-based checks fail badly, skip AI validation
                if (ruleBasedResult.CriticalErrors.Count > 2)
                {
                    LogDebug("Too many critical errors, skipping AI validation");
                    return new SanityCheckResult
                    {
                        IsValid = false,
                        ValidationMethod = "Rule-Based Only",
                        Reason = "Critical errors: " + string.Join("; ", ruleBasedResult.CriticalErrors),
                        Confidence = 0.9,
                        RuleBasedChecks = ruleBasedResult
                    };
                }

                // Perform AI validation for nuanced checks
                var aiValidation = await PerformAIValidationAsync(originalInput, aiResult, ruleBasedResult);

                // Combine results
                var finalResult = CombineValidationResults(ruleBasedResult, aiValidation);
                LogDebug($"Sanity check completed: {(finalResult.IsValid ? "VALID" : "INVALID")} - {finalResult.Reason}");

                return finalResult;
            }
            catch (Exception ex)
            {
                LogDebug($"Sanity check failed with error: {ex.Message}");
                return new SanityCheckResult
                {
                    IsValid = true, // Default to valid on error to not block trades
                    ValidationMethod = "Error",
                    Reason = $"Validation error: {ex.Message}",
                    Confidence = 0.1
                };
            }
        }

        private RuleBasedCheckResult PerformRuleBasedChecks(string originalInput, TradeParseResult aiResult)
        {
            var result = new RuleBasedCheckResult();

            // 1. Currency pair validation
            if (!string.IsNullOrEmpty(aiResult.Underlying))
            {
                var inputUpper = originalInput.ToUpper();
                if (!inputUpper.Contains(aiResult.Underlying.ToUpper()))
                {
                    result.CriticalErrors.Add($"Currency mismatch: {aiResult.Underlying} not found in input");
                }
                else
                {
                    result.PassedChecks.Add("Currency pair matches input");
                }
            }

            // 2. Expiry date validation
            if (!string.IsNullOrEmpty(aiResult.Expiry))
            {
                if (TryParseExpiry(aiResult.Expiry, out var expiryDate))
                {
                    if (expiryDate <= DateTime.Today)
                    {
                        result.CriticalErrors.Add($"Expiry date {aiResult.Expiry} is in the past");
                    }
                    else if (expiryDate > DateTime.Today.AddYears(10))
                    {
                        result.Warnings.Add($"Expiry date {aiResult.Expiry} is very far in future (>10 years)");
                    }
                    else
                    {
                        result.PassedChecks.Add("Expiry date is reasonable");
                    }
                }
                else
                {
                    result.Warnings.Add($"Could not parse expiry date: {aiResult.Expiry}");
                }
            }

            // 3. OVML format validation
            if (!string.IsNullOrEmpty(aiResult.OVML))
            {
                if (!aiResult.OVML.StartsWith("OVML"))
                {
                    result.CriticalErrors.Add("OVML command doesn't start with 'OVML'");
                }
                else
                {
                    result.PassedChecks.Add("OVML format starts correctly");
                }

                // Check for reasonable strike levels if we have Bloomberg connection
                if (_bloombergService?.IsConnected == true)
                {
                    ValidateStrikeLevels(aiResult, result);
                }
            }

            // 4. Leg count validation
            if (aiResult.LegCount > 0)
            {
                var inputLower = originalInput.ToLower();

                if (aiResult.LegCount == 1)
                {
                    // Should be vanilla option
                    if (inputLower.Contains("spread") || inputLower.Contains("collar") ||
                        inputLower.Contains("seagull") || inputLower.Contains("strangle"))
                    {
                        result.Warnings.Add("Input suggests multi-leg strategy but result shows single leg");
                    }
                }
                else if (aiResult.LegCount > 1)
                {
                    // Should indicate strategy type
                    if (!inputLower.Contains("spread") && !inputLower.Contains("collar") &&
                        !inputLower.Contains("seagull") && !inputLower.Contains("strangle") &&
                        !inputLower.Contains("straddle") && !inputLower.Contains("risk") &&
                        !inputLower.Contains("buy") && !inputLower.Contains("sell"))
                    {
                        result.Warnings.Add("Multi-leg result but no strategy keywords in input");
                    }
                }

                result.PassedChecks.Add($"Leg count ({aiResult.LegCount}) seems reasonable");
            }

            return result;
        }

        private void ValidateStrikeLevels(TradeParseResult aiResult, RuleBasedCheckResult result)
        {
            try
            {
                // This is a placeholder - you'd implement actual Bloomberg spot lookup
                // For now, just validate OVML contains reasonable strike format
                var strikePattern = new Regex(@"(\d+\.\d{4})");
                var strikes = strikePattern.Matches(aiResult.OVML);

                if (strikes.Count > 0)
                {
                    result.PassedChecks.Add($"Found {strikes.Count} properly formatted strikes");

                    // Basic sanity check - strikes should be reasonable numbers for FX
                    foreach (Match strike in strikes)
                    {
                        if (double.TryParse(strike.Value, out var strikeValue))
                        {
                            if (strikeValue < 0.01 || strikeValue > 1000)
                            {
                                result.Warnings.Add($"Strike {strikeValue} seems unusual for FX options");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Could not validate strike levels: {ex.Message}");
            }
        }

        private async Task<AIValidationResult> PerformAIValidationAsync(string originalInput, TradeParseResult aiResult, RuleBasedCheckResult ruleChecks)
        {
            var prompt = CreateValidationPrompt(originalInput, aiResult, ruleChecks);

            LogDebug("Sending AI validation request...");
            var response = await _openAIService.GetChatCompletion(prompt, "gpt-4o-mini");

            if (response?.choices?.Length > 0)
            {
                var aiResponse = response.choices[0].message.content;
                LogDebug($"AI validation response: {aiResponse}");
                return ParseAIResponse(aiResponse);
            }
            else
            {
                throw new Exception("Invalid response from OpenAI service");
            }
        }

        private string GetSystemPrompt()
        {
            return @"You are an expert FX options trading validator. Your job is to review trade translations for accuracy and flag any concerns.

Focus on:
1. Semantic accuracy - does the translation match the intent?
2. Trading logic - do the strikes, directions, and structures make sense?
3. Bloomberg OVML syntax - is the format correct?
4. Risk assessment - any obvious red flags?

Be concise but thorough. Flag both obvious errors and subtle concerns.";
        }

        private string CreateValidationPrompt(string originalInput, TradeParseResult aiResult, RuleBasedCheckResult ruleChecks)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("SANITY CHECK: Review this FX options trade translation.");
            prompt.AppendLine();
            prompt.AppendLine($"ORIGINAL INPUT: {originalInput}");
            prompt.AppendLine($"AI TRANSLATION:");
            prompt.AppendLine($"- OVML: {aiResult.OVML}");
            if (!string.IsNullOrEmpty(aiResult.AdditionalInfo))
            {
                prompt.AppendLine($"- Additional Info: {aiResult.AdditionalInfo}");
            }
            prompt.AppendLine($"- Underlying: {aiResult.Underlying}");
            prompt.AppendLine($"- Expiry: {aiResult.Expiry}");
            prompt.AppendLine($"- Legs: {aiResult.LegCount}");
            prompt.AppendLine();

            if (ruleChecks.PassedChecks.Any())
            {
                prompt.AppendLine("RULE-BASED CHECKS PASSED:");
                foreach (var check in ruleChecks.PassedChecks)
                {
                    prompt.AppendLine($"✓ {check}");
                }
                prompt.AppendLine();
            }

            if (ruleChecks.Warnings.Any() || ruleChecks.CriticalErrors.Any())
            {
                prompt.AppendLine("RULE-BASED CONCERNS:");
                foreach (var error in ruleChecks.CriticalErrors)
                {
                    prompt.AppendLine($"⚠ CRITICAL: {error}");
                }
                foreach (var warning in ruleChecks.Warnings)
                {
                    prompt.AppendLine($"⚠ WARNING: {warning}");
                }
                prompt.AppendLine();
            }

            prompt.AppendLine("Please assess:");
            prompt.AppendLine("1. Does the translation accurately reflect the original request?");
            prompt.AppendLine("2. Are the option types (call/put) and directions logical?");
            prompt.AppendLine("3. Do the strike levels and structure make trading sense?");
            prompt.AppendLine("4. Is the OVML syntax correct for Bloomberg?");
            prompt.AppendLine();
            prompt.AppendLine("Respond with: VALID or INVALID: [brief reason]");

            return prompt.ToString();
        }

        private AIValidationResult ParseAIResponse(string response)
        {
            var isValid = response.ToUpper().Contains("VALID") && !response.ToUpper().Contains("INVALID");

            // Extract reasoning
            var reason = response;
            if (response.Contains(":"))
            {
                reason = response.Substring(response.IndexOf(":") + 1).Trim();
            }

            // Calculate confidence based on response certainty
            var confidence = 0.7; // Default
            if (response.ToUpper().Contains("CLEARLY") || response.ToUpper().Contains("OBVIOUSLY"))
                confidence = 0.9;
            else if (response.ToUpper().Contains("SEEMS") || response.ToUpper().Contains("APPEARS"))
                confidence = 0.6;

            return new AIValidationResult
            {
                IsValid = isValid,
                Reason = reason,
                Confidence = confidence,
                RawResponse = response
            };
        }

        private SanityCheckResult CombineValidationResults(RuleBasedCheckResult ruleChecks, AIValidationResult aiValidation)
        {
            // Critical errors always make it invalid
            if (ruleChecks.CriticalErrors.Any())
            {
                return new SanityCheckResult
                {
                    IsValid = false,
                    ValidationMethod = "Rule-Based + AI",
                    Reason = "Critical errors: " + string.Join("; ", ruleChecks.CriticalErrors),
                    Confidence = 0.9,
                    RuleBasedChecks = ruleChecks,
                    AIValidation = aiValidation
                };
            }

            // If AI says invalid, trust it (but lower confidence if only warnings from rules)
            if (!aiValidation.IsValid)
            {
                var confidence = ruleChecks.Warnings.Any() ? aiValidation.Confidence : aiValidation.Confidence * 0.8;
                return new SanityCheckResult
                {
                    IsValid = false,
                    ValidationMethod = "AI Validation",
                    Reason = aiValidation.Reason,
                    Confidence = confidence,
                    RuleBasedChecks = ruleChecks,
                    AIValidation = aiValidation
                };
            }

            // Both validations passed - combine warnings
            var warnings = ruleChecks.Warnings.ToList();
            var allReasons = new List<string>();

            if (warnings.Any())
            {
                allReasons.Add("Warnings: " + string.Join("; ", warnings));
            }

            allReasons.Add(aiValidation.Reason);

            return new SanityCheckResult
            {
                IsValid = true,
                ValidationMethod = "Rule-Based + AI",
                Reason = string.Join(" | ", allReasons),
                Confidence = warnings.Any() ? aiValidation.Confidence * 0.9 : aiValidation.Confidence,
                RuleBasedChecks = ruleChecks,
                AIValidation = aiValidation
            };
        }

        private bool TryParseExpiry(string expiryString, out DateTime expiryDate)
        {
            expiryDate = default;

            // Try common Bloomberg date formats
            var formats = new[]
            {
                "ddMMMyy", "dd/MM/yy", "MM/dd/yy", "yyyy-MM-dd",
                "ddMMMyyy", "dd MMM yyyy", "MMM dd yyyy"
            };

            foreach (var format in formats)
            {
                if (DateTime.TryParseExact(expiryString, format, null, System.Globalization.DateTimeStyles.None, out expiryDate))
                {
                    return true;
                }
            }

            return DateTime.TryParse(expiryString, out expiryDate);
        }

        private void LogDebug(string message)
        {
            _debugCallback?.Invoke($"[SanityChecker] {message}");
        }

        public void Dispose()
        {
            // OpenAIService disposal is handled elsewhere
        }
    }

    // Supporting classes
    public class SanityCheckResult
    {
        public bool IsValid { get; set; }
        public string ValidationMethod { get; set; }
        public string Reason { get; set; }
        public double Confidence { get; set; }
        public RuleBasedCheckResult RuleBasedChecks { get; set; }
        public AIValidationResult AIValidation { get; set; }
    }

    public class RuleBasedCheckResult
    {
        public List<string> CriticalErrors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public List<string> PassedChecks { get; set; } = new List<string>();
    }

    public class AIValidationResult
    {
        public bool IsValid { get; set; }
        public string Reason { get; set; }
        public double Confidence { get; set; }
        public string RawResponse { get; set; }
    }
}