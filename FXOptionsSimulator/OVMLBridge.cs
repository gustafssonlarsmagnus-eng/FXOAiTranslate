using System;
using System.Collections.Generic;
using FXOptionsSimulator.FIX;
using System.Linq;
using System.Text.RegularExpressions;

namespace FXOptionsSimulator
{
    /// <summary>
    /// Converts OVML parser output to FIX TradeStructure
    /// </summary>
    public class OVMLBridge
    {
        /// <summary>
        /// Convert TradeParseResult (from your OVML parser) to TradeStructure (for FIX simulator)
        /// </summary>
        public static TradeStructure ConvertToTradeStructure(dynamic ovmlResult)
        {
            // Extract properties from your TradeParseResult
            string ovml = ovmlResult.OVML;
            string underlying = ovmlResult.Underlying;
            string expiry = ovmlResult.Expiry;
            int legCount = ovmlResult.LegCount;

            if (string.IsNullOrEmpty(ovml))
            {
                throw new ArgumentException("OVML string is empty");
            }

            // If underlying is null/empty, extract it from the OVML string
            if (string.IsNullOrEmpty(underlying))
            {
                // Extract currency pair from OVML (should be first token after "OVML")
                var parts = ovml.Replace("OVML", "").Trim().Split(' ');
                if (parts.Length > 0)
                {
                    underlying = parts[0];
                }
            }

            if (string.IsNullOrEmpty(underlying))
            {
                underlying = "EURUSD";
            }

            // Parse the OVML string
            var parsed = ParseOVML(ovml);

            // Get market data for spot reference if not in OVML
            double spotRef = parsed.SpotReference;
            if (spotRef == 0)
            {
                var marketData = GetMarketDataForPair(underlying);
                spotRef = marketData.SpotRate;
            }

            // Get forward reference from OVML (if specified)
            double fwdRef = parsed.ForwardReference;

            // Determine structure type from leg count and option types
            string structureType = DetermineStructureType(parsed.Legs);

            // Build TradeStructure
            var trade = new TradeStructure
            {
                Underlying = underlying,
                StructureType = structureType,
                PremiumCurrency = GetTermCurrency(underlying),
                SpotReference = spotRef,
                ForwardReference = fwdRef,
                Legs = new List<TradeStructure.OptionLeg>()
            };

            // Extract actual tenor from formatted string if present
            // Format: "31-Dec-25, Wed (1M)" -> extract "1M"
            string actualTenor = expiry;
            var tenorInParensMatch = Regex.Match(expiry, @"\(([^)]+)\)$");
            if (tenorInParensMatch.Success)
            {
                actualTenor = tenorInParensMatch.Groups[1].Value;
                Console.WriteLine($"[OVMLBridge] Extracted tenor '{actualTenor}' from formatted string '{expiry}'");
            }

            // Convert each parsed leg to TradeStructure.OptionLeg
            for (int i = 0; i < parsed.Legs.Count; i++)
            {
                var parsedLeg = parsed.Legs[i];
                var expiryDate = CalculateExpiryDate(actualTenor, underlying);

                // Calculate delivery date = expiry + spot lag business days
                var deliveryDate = CalculateDeliveryDate(expiryDate, underlying);

                trade.Legs.Add(new TradeStructure.OptionLeg
                {
                    Direction = parsedLeg.Direction,
                    OptionType = parsedLeg.OptionType,
                    Strike = parsedLeg.Strike,
                    Tenor = actualTenor,
                    ExpiryDate = expiryDate,
                    DeliveryDate = deliveryDate,
                    NotionalMM = parsedLeg.NotionalMM,
                    NotionalCurrency = GetBaseCurrency(underlying),  // Use base currency (EUR for EURUSD)
                    Cutoff = "NY",
                    Position = i == 0 ? "SAME" : "INVERSE",
                    LegID = $"SL{i}"
                });
            }

            return trade;
        }

        /// <summary>
        /// Parse OVML string into structured data
        /// </summary>
        private static ParsedOVML ParseOVML(string ovml)
        {
            var result = new ParsedOVML
            {
                Legs = new List<ParsedLeg>()
            };

            Console.WriteLine($"[OVMLBridge] Parsing OVML: {ovml}");

            // Remove "OVML" prefix and split by spaces
            var parts = ovml.Replace("OVML", "").Trim().Split(' ');

            string directions = "";
            string strikes = "";
            string notionals = "";
            bool isDeltaBased = false;
            string deltaSpec = "";
            string optionTypeFromDelta = "";

            // First pass: identify if this is delta-based and collect the option type
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("DS") || parts[i].StartsWith("DF"))
                {
                    isDeltaBased = true;
                    deltaSpec = parts[i];
                    // Next token should be C or P
                    if (i + 1 < parts.Length && (parts[i + 1] == "C" || parts[i + 1] == "P"))
                    {
                        optionTypeFromDelta = parts[i + 1];
                    }
                }
            }

            foreach (var part in parts)
            {
                // Skip standalone C or P if it's part of delta spec
                if (isDeltaBased && (part == "C" || part == "P"))
                {
                    continue;
                }

                // Directions: B,S,S
                if (Regex.IsMatch(part, @"^[BS,]+$"))
                {
                    directions = part;
                }
                // Delta specification: DS25, DF50
                else if (part.StartsWith("DS") || part.StartsWith("DF"))
                {
                    strikes = part + optionTypeFromDelta; // Combine DS25 + C = DS25C
                }
                // Strikes: 9.6000P,9.1500P or 11.8000C,12.1000C
                else if (Regex.IsMatch(part, @"[\d.]+[CP]"))
                {
                    strikes = part;
                }
                // Notionals: N10M or N15M,10M (handle both single and multi-leg)
                else if (part.StartsWith("N") && part.Contains("M"))
                {
                    notionals = part;
                    Console.WriteLine($"[OVMLBridge] Found notionals token: {notionals}");
                }
                // Spot reference: SP9.3950
                else if (part.StartsWith("SP"))
                {
                    result.SpotReference = double.Parse(part.Substring(2));
                }
                // Forward reference: FW1.0950 or FR1.0950
                else if (part.StartsWith("FW") || part.StartsWith("FR"))
                {
                    result.ForwardReference = double.Parse(part.Substring(2));
                }
            }

            Console.WriteLine($"[OVMLBridge] Parsed - Directions: '{directions}', Strikes: '{strikes}', Notionals: '{notionals}'");

            // Parse legs
            var directionList = directions.Split(',');
            var strikeList = strikes.Split(',');

            // Parse notionals - handle format like "N15M" or "N15M,10M"
            // Remove leading N, then split by comma, then remove trailing M from each
            var notionalList = new List<double>();
            if (!string.IsNullOrEmpty(notionals))
            {
                var notionalStr = notionals.TrimStart('N'); // Remove leading N
                var notionalParts = notionalStr.Split(',');
                foreach (var np in notionalParts)
                {
                    var cleaned = np.Trim().TrimEnd('M', 'm');
                    if (double.TryParse(cleaned, out double val))
                    {
                        notionalList.Add(val);
                        Console.WriteLine($"[OVMLBridge] Parsed notional: {val} from '{np}'");
                    }
                }
            }

            for (int i = 0; i < strikeList.Length; i++)
            {
                var strikeStr = strikeList[i].Trim();
                char optionType = strikeStr.EndsWith("P") ? 'P' : 'C';
                double strike;

                // Handle delta specifications (DS25C, DF50P)
                if (strikeStr.StartsWith("DS") || strikeStr.StartsWith("DF"))
                {
                    // Extract delta value from DS25C or DF50P
                    string deltaValue = strikeStr.Substring(2).TrimEnd('C', 'P');
                    strike = double.Parse(deltaValue);
                    Console.WriteLine($"[OVMLBridge] Delta specification detected: {strikeStr} -> Delta value: {strike}");
                }
                else
                {
                    // Regular strike price
                    strike = double.Parse(strikeStr.TrimEnd('C', 'P'));
                }

                string direction = i < directionList.Length ? directionList[i].Trim() : "B";
                direction = direction == "B" ? "BUY" : "SELL";

                // Get notional from parsed list, default to 10 if not found
                double notional = i < notionalList.Count ? notionalList[i] : 10;
                Console.WriteLine($"[OVMLBridge] Leg {i}: Direction={direction}, Strike={strike}, Notional={notional}MM");

                result.Legs.Add(new ParsedLeg
                {
                    Direction = direction,
                    OptionType = optionType == 'C' ? "CALL" : "PUT",
                    Strike = strike,
                    NotionalMM = notional
                });
            }

            return result;
        }

        /// <summary>
        /// Determine structure type from legs
        /// </summary>
        private static string DetermineStructureType(List<ParsedLeg> legs)
        {
            if (legs.Count == 1)
                return "Vanilla";

            if (legs.Count == 2)
            {
                bool bothCalls = legs.All(l => l.OptionType == "CALL");
                bool bothPuts = legs.All(l => l.OptionType == "PUT");

                if (bothCalls) return "CallSpread";
                if (bothPuts) return "PutSpread";

                return "RiskReversal";
            }

            if (legs.Count == 3)
            {
                int puts = legs.Count(l => l.OptionType == "PUT");
                int calls = legs.Count(l => l.OptionType == "CALL");

                if (puts == 2 && calls == 1) return "Seagull";
                if (calls == 2 && puts == 1) return "Collar";
            }

            return "CustomSpread";
        }

        /// <summary>
        /// Calculate expiry date from tenor or date string
        /// </summary>
        private static DateTime CalculateExpiryDate(string expiry, string currencyPair = "EURUSD")
        {
            // Try tenor format (3M, 6M, 1Y)
            var tenorMatch = Regex.Match(expiry, @"^(\d+)([MYWDW])$", RegexOptions.IgnoreCase);
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

                    Console.WriteLine($"[CALENDAR-OVML] Tenor {expiry} for {currencyPair}: {expiryDate:yyyy-MM-dd (ddd)} (business day adjusted)");
                    return expiryDate;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CALENDAR-OVML] FxCalendarService failed for {expiry}/{currencyPair}: {ex.Message}, using fallback");

                    // Fallback to simple calculation with weekend adjustment
                    int amount = int.Parse(tenorMatch.Groups[1].Value);
                    char period = char.ToUpper(tenorMatch.Groups[2].Value[0]);

                    var date = period switch
                    {
                        'D' => DateTime.UtcNow.AddDays(amount),
                        'W' => DateTime.UtcNow.AddDays(amount * 7),
                        'M' => DateTime.UtcNow.AddMonths(amount),
                        'Y' => DateTime.UtcNow.AddYears(amount),
                        _ => DateTime.UtcNow.AddMonths(3)
                    };

                    // Simple weekend adjustment
                    while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                    {
                        date = date.AddDays(1);
                    }

                    Console.WriteLine($"[CALENDAR-OVML] Fallback used - adjusted to {date:yyyy-MM-dd (ddd)}");
                    return date;
                }
            }

            // Try date format (MM/dd/yy or ddMMMyy)
            if (DateTime.TryParse(expiry, out DateTime result))
            {
                // Check if it's a weekend and adjust if needed
                if (result.DayOfWeek == DayOfWeek.Saturday || result.DayOfWeek == DayOfWeek.Sunday)
                {
                    Console.WriteLine($"[CALENDAR-OVML] WARNING: Parsed date {result:yyyy-MM-dd} is a weekend!");
                    while (result.DayOfWeek == DayOfWeek.Saturday || result.DayOfWeek == DayOfWeek.Sunday)
                    {
                        result = result.AddDays(1);
                    }
                    Console.WriteLine($"[CALENDAR-OVML] Adjusted to next business day: {result:yyyy-MM-dd (ddd)}");
                }
                return result;
            }

            // Default: 3 months with business day adjustment
            try
            {
                var rules = new FxDateRules();
                var defaultResult = FxDateService.ComputeDates(
                    DateTime.UtcNow,
                    currencyPair,
                    "3M",
                    premiumCcy: currencyPair.Length >= 6 ? currencyPair.Substring(3, 3) : "USD",
                    rules
                );
                return defaultResult.expiryDate;
            }
            catch
            {
                return DateTime.UtcNow.AddMonths(3);
            }
        }

        /// <summary>
        /// Calculate delivery date from expiry date by adding spot lag business days
        /// </summary>
        private static DateTime CalculateDeliveryDate(DateTime expiryDate, string currencyPair = "EURUSD")
        {
            // Determine spot lag for the currency pair
            int spotLag = GetSpotLag(currencyPair);

            // Add spot lag business days to expiry date
            DateTime deliveryDate = expiryDate;
            int businessDaysAdded = 0;

            while (businessDaysAdded < spotLag)
            {
                deliveryDate = deliveryDate.AddDays(1);

                // Skip weekends
                if (deliveryDate.DayOfWeek == DayOfWeek.Saturday || deliveryDate.DayOfWeek == DayOfWeek.Sunday)
                    continue;

                // Skip January 1st (global holiday)
                if (deliveryDate.Month == 1 && deliveryDate.Day == 1)
                    continue;

                businessDaysAdded++;
            }

            Console.WriteLine($"[OVMLBridge] Delivery date for {currencyPair}: Expiry={expiryDate:yyyy-MM-dd (ddd)} + {spotLag} BD = {deliveryDate:yyyy-MM-dd (ddd)}");
            return deliveryDate;
        }

        /// <summary>
        /// Get spot lag for currency pair (T+1 or T+2)
        /// </summary>
        private static int GetSpotLag(string currencyPair)
        {
            // T+1 pairs
            if (currencyPair == "USDCAD" || currencyPair == "USDTRY" ||
                currencyPair == "USDPHP" || currencyPair == "USDRUB")
            {
                return 1;
            }

            // Most pairs are T+2
            return 2;
        }

        /// <summary>
        /// Get term currency (second currency in pair)
        /// </summary>
        private static string GetBaseCurrency(string underlying)
        {
            if (underlying.Length >= 6)
                return underlying.Substring(0, 3);

            return "EUR"; // Default
        }

        private static string GetTermCurrency(string underlying)
        {
            if (underlying.Length >= 6)
                return underlying.Substring(3, 3);

            return "USD"; // Default
        }

        /// <summary>
        /// Get market data for a currency pair
        /// </summary>
        private static MarketData GetMarketDataForPair(string pair)
        {
            return pair switch
            {
                "EURUSD" => MarketData.GetEURUSD(),
                "USDSEK" => MarketData.GetUSDSEK(),
                _ => new MarketData
                {
                    Symbol = pair,
                    SpotRate = 1.0,
                    ForwardPoints = new Dictionary<string, double>(),
                    ImpliedVols = new Dictionary<string, double>()
                }
            };
        }

        // Helper classes for parsing
        private class ParsedOVML
        {
            public List<ParsedLeg> Legs { get; set; }
            public double SpotReference { get; set; }
            public double ForwardReference { get; set; }
        }

        private class ParsedLeg
        {
            public string Direction { get; set; }
            public string OptionType { get; set; }
            public double Strike { get; set; }
            public double NotionalMM { get; set; }
        }
    }
}