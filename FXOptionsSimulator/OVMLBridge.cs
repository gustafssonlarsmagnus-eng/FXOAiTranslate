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

            // Determine structure type from leg count and option types
            string structureType = DetermineStructureType(parsed.Legs);

            // Build TradeStructure
            var trade = new TradeStructure
            {
                Underlying = underlying,
                StructureType = structureType,
                PremiumCurrency = GetTermCurrency(underlying),
                SpotReference = spotRef,
                Legs = new List<TradeStructure.OptionLeg>()
            };

            // Convert each parsed leg to TradeStructure.OptionLeg
            for (int i = 0; i < parsed.Legs.Count; i++)
            {
                var parsedLeg = parsed.Legs[i];
                var expiryDate = CalculateExpiryDate(expiry, underlying);

                trade.Legs.Add(new TradeStructure.OptionLeg
                {
                    Direction = parsedLeg.Direction,
                    OptionType = parsedLeg.OptionType,
                    Strike = parsedLeg.Strike,
                    Tenor = expiry,
                    ExpiryDate = expiryDate,
                    DeliveryDate = expiryDate.AddDays(2),
                    NotionalMM = parsedLeg.NotionalMM,
                    NotionalCurrency = GetTermCurrency(underlying),
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

            // Remove "OVML" prefix and split by spaces
            var parts = ovml.Replace("OVML", "").Trim().Split(' ');

            string directions = "";
            string strikes = "";
            string notionals = "";

            foreach (var part in parts)
            {
                // Directions: B,S,S
                if (Regex.IsMatch(part, @"^[BS,]+$"))
                {
                    directions = part;
                }
                // Strikes: 9.6000P,9.1500P or 11.8000C,12.1000C
                else if (Regex.IsMatch(part, @"[\d.]+[CP]"))
                {
                    strikes = part;
                }
                // Notionals: N10M,50M
                else if (part.StartsWith("N") && part.Contains("M"))
                {
                    notionals = part;
                }
                // Spot reference: SP9.3950
                else if (part.StartsWith("SP"))
                {
                    result.SpotReference = double.Parse(part.Substring(2));
                }
            }

            // Parse legs
            var directionList = directions.Split(',');
            var strikeList = strikes.Split(',');
            var notionalList = notionals.Replace("N", "").Replace("M", "").Split(',');

            for (int i = 0; i < strikeList.Length; i++)
            {
                var strikeStr = strikeList[i].Trim();
                char optionType = strikeStr.EndsWith("P") ? 'P' : 'C';
                double strike = double.Parse(strikeStr.TrimEnd('C', 'P'));

                string direction = i < directionList.Length ? directionList[i].Trim() : "B";
                direction = direction == "B" ? "BUY" : "SELL";

                double notional = i < notionalList.Length ? double.Parse(notionalList[i].Trim()) : 10;

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
                        expiry.ToUpper(),
                        currencyPair
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
        /// Get term currency (second currency in pair)
        /// </summary>
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