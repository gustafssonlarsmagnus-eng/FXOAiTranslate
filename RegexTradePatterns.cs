using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace FXOAiTranslator
{
    public static class RegexTradePatterns
    {
        public static readonly List<TradePattern> Patterns = new List<TradePattern>

        {

// COMPACT MULTI-LEG PATTERN: Multiple legs with direction codes (sb, sbb, etc.)
// Matches:
//   15mm USDNOK 10 usd call
//   23mm USDNOK 11.5 usd call
//   exp 15-Jun-26,
//   fwd ref 10.1045,
//   sb
// Direction codes: s=SELL, b=BUY (e.g., "sb" = SELL first leg, BUY second leg)
new TradePattern(
    "CompactMultiLeg_DirectionCodes",
    new Regex(
        @"(?<legs>(?:\d+(?:\.\d+)?mm?\s+[A-Z]{6}\s+[\d.]+\s+(?:usd|eur|nok|sek|gbp|aud|cad|chf|jpy|nzd)?\s*(?:call|put)\s*[\r\n]*)+)" +  // Multiple legs
        @"exp\s+(?<expiry>[\d\-A-Za-z]+),?\s*" +      // Expiry date
        @"(?:fwd\s+ref|sr|spot)\s+(?<fwdref>[\d.]+),?\s*" +  // Forward/spot reference
        @"(?<directions>[sb]+)\s*$",                  // Direction codes (sb, sbb, etc.)
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Multiline
    )
),

// DELTA PATTERN: Delta-based vanilla options
// Matches: "eurusd 1m 25delta call in 20mio" or "eurusd 25d put 10m" or "25 forward delta call 15mio"
// Generates: DS25 (spot delta) or DF25 (forward delta)
new TradePattern(
    "Vanilla_Delta",
    new Regex(
        @"(?<delta>\d+)\s*(?<deltaType>d|delta|fdelta|forward\s+delta|fd|f\s+delta)\s+" +  // 25delta or 25d or 25 forward delta
        @"(?<type>call|put)\s+" +                                                          // call or put
        @"(?:in\s+)?(?<notional>\d+)\s*(?:m|mio|million)",                                 // in 20 mio
        RegexOptions.IgnoreCase
    )
),

// DELTA PATTERN: Delta-based risk reversal
// Matches: "EURNOK 6m 25d r/r" or "eurusd 1m 25 delta rr" or "10 delta risk reversal"
// Generates: DS25 RR (spot delta) or DF25 RR (forward delta)
new TradePattern(
  "Delta_RiskReversal",
    new Regex(
        @"(?<delta>\d+)\s*(?<deltaType>d|delta|fdelta|forward\s+delta|fd|f\s+delta)?\s*" +  // 25d or 25 delta or 25 (optional delta suffix)
        @"(?<rrType>r/?r|rr|risk\s*reversal)",       // r/r, rr, or risk reversal
  RegexOptions.IgnoreCase
    )
),

// NEW PATTERN 1: Currency-led vanilla with expiry
// Matches: "EURUSD 1m 1.1600 call in 10 mio"
new TradePattern(
    "Vanilla_CurrencyLed",
    new Regex(
        @"[A-Z]{6}\s+" +                                      // EURUSD (ignore, extracted separately)
        @"[\w/\-\.]+\s+" +                                    // 1m (ignore, extracted separately)
        @"(?<strike>\d+(\.\d+)?)\s+" +                        // 1.1600
        @"(?<type>call|put)\s+" +                             // call or put
        @"(?:in\s+)?(?<notional>\d+)\s*(?:m|mio|million)",    // in 10 mio
        RegexOptions.IgnoreCase
    )
),

// NEW PATTERN: Currency-led vanilla WITHOUT explicit call/put
// Matches: "1m eurusd 1.17 in 15mio" or "eurusd 1m 1.1700 15mio"
// Option type will be inferred from strike vs spot
new TradePattern(
    "Vanilla_NoType",
    new Regex(
        @"(?:\d+[mMwWyYdD]\s+)?" +     // Optional tenor at start (1m, 2w, etc)
        @"(?:[A-Za-z]{6}\s+)?" +        // Optional EURUSD (case insensitive)
        @"(?:\d+[mMwWyYdD]\s+)?" +  // Optional tenor after pair
      @"(?:[A-Za-z]{6}\s+)?" +    // Optional EURUSD again (case insensitive)
        @"(?<strike>\d+(?:\.\d+)?)\s+" +      // Strike: 1.17 or 1.1700
        @"(?:in\s+)?(?<notional>\d+)\s*" +    // in 15 (captured notional)
        @"(?:m(?:io)?|mil|million)", // mio, m, mil, or million suffix
 RegexOptions.IgnoreCase
    )
),

       
            // ==================
            // 3-Leg Collar: Buy Call + Sell Call + Sell Put
            // MUST BE FIRST - Most specific pattern
            // Format: "i buy a 11.8000 call in 100 mio and sell a 12.1000 call in 125 mio and sell a 11.5500 put in 100 mio"
            // ==================
            new TradePattern(
                "Collar_BuyCallSellCallSellPut",
                new Regex(
                    @"(?:i\s+)?buy(?:\s+a)?\s+(?<strike1>\d+(\.\d+)?)\s*call\s+(?:in\s+)?(?<notional1>\d+)\s*mio.*?" +
                    @"(?:and\s+)?(?:i\s+)?sell(?:\s+a)?\s+(?<strike2>\d+(\.\d+)?)\s*call\s+(?:in\s+)?(?<notional2>\d+)\s*mio.*?" +
                    @"(?:and\s+)?(?:i\s+)?sell(?:\s+a)?\s+(?<strike3>\d+(\.\d+)?)\s*put\s+(?:in\s+)?(?<notional3>\d+)\s*mio",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ==================
            // 3-Leg Seagull: Buy Put + Sell Put + Sell Call
            // Format: "i buy a 11.8000 put in 100 mio and sell a 11.6000 put in 150 mio and sell a 11.9500 call in 50 mio"
            // ==================
            new TradePattern(
                "Seagull_BuyPutSellPutSellCall",
                new Regex(
                    @"(?:i\s+)?buy(?:\s+a)?\s+(?<strike1>\d+(\.\d+)?)\s*put\s+(?:in\s+)?(?<notional1>\d+)\s*mio.*?" +
                    @"(?:and\s+)?(?:i\s+)?sell(?:\s+a)?\s+(?<strike2>\d+(\.\d+)?)\s*put\s+(?:in\s+)?(?<notional2>\d+)\s*mio.*?" +
                    @"(?:and\s+)?(?:i\s+)?sell(?:\s+a)?\s+(?<strike3>\d+(\.\d+)?)\s*call\s+(?:in\s+)?(?<notional3>\d+)\s*mio",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Seagull (Put-led): Buy Put + Sell Put + Sell Call
            // ============================
            new TradePattern(
                "Seagull",
                new Regex(
                    @"buy\s+(?<notional1>\d+)\s*mio\s+(?<strike1>\d+(\.\d+)?)\s*put.*?sell\s+(?<notional2>\d+)\s*mio\s+(?<strike2>\d+(\.\d+)?)\s*put.*?sell\s+(?<notional3>\d+)\s*mio\s+(?<strike3>\d+(\.\d+)?)\s*call",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Seagull (Call-led): Buy Call + Sell Call + Sell Put
            // ============================
            new TradePattern(
                "Seagull_CallLed",
                new Regex(
                    @"buy\s+(?<notional1>\d+)\s*mio\s+(?<strike1>\d+(\.\d+)?)\s*call.*?sell\s+(?<notional2>\d+)\s*mio\s+(?<strike2>\d+(\.\d+)?)\s*call.*?sell\s+(?<notional3>\d+)\s*mio\s+(?<strike3>\d+(\.\d+)?)\s*put",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Put Spread with different notionals per leg
            //  Example: "i buy a 9.7500 put in 100 mio and sell a 9.5000 put in 150 mio"
            // ============================
            new TradePattern(
                "PutSpread_DifferentNotionals",
                new Regex(
                    @"(?<side1>buy|køpa|köpa)\s+(?:a\s+)?(?<strike1>\d+(\.\d+)?)\s+put\s+(?:in\s+)?(?<notional1>\d+)\s*(?:mio|m).*?(?:and\s+)?(?<side2>sell|sälja|sälj)\s+(?:a\s+)?(?<strike2>\d+(\.\d+)?)\s+put\s+(?:in\s+)?(?<notional2>\d+)\s*(?:mio|m)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Call Spread with different notionals per leg
            //  Example: "i buy a 11.0000 call in 100 mio and sell a 11.5000 call in 150 mio"
            // ============================
            new TradePattern(
                "CallSpread_DifferentNotionals",
                new Regex(
                    @"(?<side1>buy|køpa|köpa)\s+(?:a\s+)?(?<strike1>\d+(\.\d+)?)\s+call\s+(?:in\s+)?(?<notional1>\d+)\s*(?:mio|m).*?(?:and\s+)?(?<side2>sell|sälja|sälj)\s+(?:a\s+)?(?<strike2>\d+(\.\d+)?)\s+call\s+(?:in\s+)?(?<notional2>\d+)\s*(?:mio|m)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Put Spread - Multilingual
            // ============================
            new TradePattern(
                "PutSpread_Market",
                new Regex(
                    @"(?<side>buy|sell|køp|køpa|sälj|säljer|kjøp|kjøpe|selg|selger)\s+(?<notional>\d+)\s*(mio|m)\s+(put\s+spread|ps)\s+(?<strike1>\d+(\.\d+)?)\s*-\s*(?<strike2>\d+(\.\d+)?)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Call Spread - Multilingual
            // ============================
            new TradePattern(
                "CallSpread_Market",
                new Regex(
                    @"(?<side>buy|sell|køp|køpa|sälj|säljer|kjøp|kjøpe|selg|selger)\s+(?<notional>\d+)\s*(mio|m)\s+(call\s+spread|cs)\s+(?<pair>[A-Z]{6})?\s*(?<strike1>\d+(\.\d+)?)\s*-\s*(?<strike2>\d+(\.\d+)?)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Put Spread (Bloomberg shorthand: "PS")
            //  Example: "14Oct 11.60 11.40 PS just 10mio per"
            // ============================
            new TradePattern(
                "PutSpread_Short",
                new Regex(
                    @"(?<strike1>\d+(\.\d+)?)\s+(?<strike2>\d+(\.\d+)?)\s*PS\b.*?(?<notional>\d+)\s*(mio|m)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Call Spread (Bloomberg shorthand: "CS")
            //  Example: "14Oct 9.20 9.40 CS 25mio"
            // ============================
            new TradePattern(
                "CallSpread_Short",
                new Regex(
                    @"(?<strike1>\d+(\.\d+)?)\s+(?<strike2>\d+(\.\d+)?)\s*CS\b.*?(?<notional>\d+)\s*(mio|m)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Risk Reversal (Buy Put + Sell Call) — Multilingual, flexible word order
            // ============================
            new TradePattern(
                "RiskReversal_PutCall",
                new Regex(
                    @".*?(?<side1>buy|köp(?:er)?|sell|sälj(?:er)?)\s+(?:a|en|ett)?\s*" +
                    @"(?<strike1>\d+(\.\d+)?)\s*(?<type1>put)\s+(?:in|i)?\s*(?<notional1>\d+)\s*mio(?:\s*(USD|SEK))?.*?" +
                    @"(?:and|och)\s+.*?(?<side2>buy|köp(?:er)?|sell|sälj(?:er)?)\s+(?:a|en|ett)?\s*" +
                    @"(?<strike2>\d+(\.\d+)?)\s*(?<type2>call)\s+(?:in|i)?\s*(?<notional2>\d+)\s*mio(?:\s*(USD|SEK))?",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Risk Reversal (Buy Call + Sell Put) — Multilingual, flexible word order
            // ============================
            new TradePattern(
                "RiskReversal_CallPut",
                new Regex(
                    @".*?(?<side1>buy|köp(?:er)?|sell|sälj(?:er)?)\s+(?:a|en|ett)?\s*" +
                    @"(?<strike1>\d+(\.\d+)?)\s*(?<type1>call)\s+(?:in|i)?\s*(?<notional1>\d+)\s*mio(?:\s*(USD|SEK))?.*?" +
                    @"(?:and|och)\s+.*?(?<side2>buy|köp(?:er)?|sell|sälj(?:er)?)\s+(?:a|en|ett)?\s*" +
                    @"(?<strike2>\d+(\.\d+)?)\s*(?<type2>put)\s+(?:in|i)?\s*(?<notional2>\d+)\s*mio(?:\s*(USD|SEK))?",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Strangle (Buy/Buy)
            // ============================
            new TradePattern(
                "Strangle_Long",
                new Regex(
                    @"buy\s+(?<notional1>\d+)\s*mio\s+(?<strike1>\d+(\.\d+)?)\s*put.*?buy\s+(?<notional2>\d+)\s*mio\s+(?<strike2>\d+(\.\d+)?)\s*call",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Strangle (Sell/Sell)
            // ============================
            new TradePattern(
                "Strangle_Short",
                new Regex(
                    @"sell\s+(?<notional1>\d+)\s*mio\s+(?<strike1>\d+(\.\d+)?)\s*put.*?sell\s+(?<notional2>\d+)\s*mio\s+(?<strike2>\d+(\.\d+)?)\s*call",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Straddle (keyword) — supports buy/sell + 1 or 2 notionals
            // ============================
            new TradePattern(
                "Straddle",
                new Regex(
                    @"(?:(?<side>buy|sell|köp(?:er)?|sälj(?:er)?)\s+)?straddle\b.*?" +
                    @"(?<notional1>\d+)\s*mio\b(?:.*?(?:/|and|och|&|,)\s*(?<notional2>\d+)\s*mio\b)?",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Strangle (keyword) — supports buy/sell, one notional, explicit strikes
            // ============================
            new TradePattern(
                "Strangle_Keyword",
                new Regex(
                    @"(?:(?<side>buy|sell|köp(?:er)?|sälj(?:er)?)\s+)?strangle\b.*?" +
                    @"(?<notional>\d+)\s*mio\b.*?" +
                    @"(?<strike1>\d+(\.\d+)?)\s*put\b.*?(?:and|och)\s*(?<strike2>\d+(\.\d+)?)\s*call\b",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Vanilla (Explicit Buy/Sell) - More specific than Simple_Vanilla
            // ============================
            new TradePattern(
                "Vanilla",
                new Regex(
                    @"(?<side>buy|sell)\s+(?<notional>\d+)\s*mio\s+(?<strike>\d+(\.\d+)?)\s*(?<type>call|put)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Simple Vanilla (Strike + Notional) - MUST BE LAST - catches partial matches
            // ============================
           new TradePattern(
                "Simple_Vanilla",
                new Regex(
                    @"(?:i\s+)?(?<side>buy|sell)(?:\s+a)?\s+(?<strike>\d+(\.\d+)?)\s*(?<type>call|put)\s+(?:in\s+)?(?<notional>\d+)\s*mio",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            )
        };

        // Spot reference regex
        public static readonly Regex SpotRegex = new Regex(
            @"(?:spot\s*ref|sp\s*ref|spotref|s\.?\s*r\.?\s*ref|s\.?\s*r\.?|sr)\s*[:.]?\s*(?<spot>\d+[,.]?\d*)",
            RegexOptions.IgnoreCase
        );

        // Forward reference regex - NEW
        public static readonly Regex ForwardRefRegex = new Regex(
            @"(?:fwd\s*ref|forward\s*ref|fr\s*ref|f\.?\s*r\.?|fwdref|forwardref)\s*[:.]?\s*(?<fwdref>\d+[,.]?\d*)",
            RegexOptions.IgnoreCase
        );

        // === Helpers for OVML assembly ===
        public static string MapSide(string side)
        {
            side = side.ToLower();
            return (side.StartsWith("b") || side.StartsWith("köp")) ? "B" : "S";
        }

        public static string MapType(string type)
        {
            type = type.ToLower();
            return type.StartsWith("c") ? "C" : "P";
        }

        public static string BuildRiskReversalOVML(string ccyPair, Match match, string expiry, string spot)
        {
            var sides = string.Join(",", new[]
            {
                MapSide(match.Groups["side1"].Value),
                MapSide(match.Groups["side2"].Value)
            });

            var strikes = string.Join(",", new[]
            {
                match.Groups["strike1"].Value + MapType(match.Groups["type1"].Value),
                match.Groups["strike2"].Value + MapType(match.Groups["type2"].Value)
            });

            var notionals = string.Join(",", new[]
            {
                match.Groups["notional1"].Value + "M",
                match.Groups["notional2"].Value + "M"
            });

            string spotPart = string.IsNullOrEmpty(spot) ? "" : " SP" + spot;

            return $"OVML {ccyPair} 2L {sides} {strikes} {expiry} N{string.Join(",", notionals)}{spotPart}";
        }
    }

    public class TradePattern
    {
        public string Name { get; set; }
        public Regex Regex { get; set; }

        public TradePattern(string name, Regex regex)
        {
            Name = name;
            Regex = regex;

        }
    }
}