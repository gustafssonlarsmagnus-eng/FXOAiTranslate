using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace FXOAiTranslator
{
    public static class RegexTradePatterns
    {
        public static readonly List<TradePattern> Patterns = new List<TradePattern>
        {
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
            //  Put Spread
            // ============================
            new TradePattern(
                "PutSpread_Market",
                new Regex(
                    @"buy\s+(?<notional>\d+)\s*mio\s+put\s+spread\s+(?<strike1>\d+(\.\d+)?)\s*-\s*(?<strike2>\d+(\.\d+)?)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
            //  Call Spread
            // ============================
            new TradePattern(
                "CallSpread_Market",
                new Regex(
                    @"buy\s+(?<notional>\d+)\s*mio\s+call\s+spread\s+(?<strike1>\d+(\.\d+)?)\s*-\s*(?<strike2>\d+(\.\d+)?)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline
                )
            ),

            // ============================
//  Risk Reversal (Buy Put + Sell Call)
// ============================
new TradePattern(
    "RiskReversal_PutCall",
    new Regex(
        @"(?:i\s+)?buy(?:\s+a)?\s+(?<strike1>\d+(\.\d+)?)\s*put\s+(?:in\s+)?(?<notional1>\d+)\s*mio.*?" +
        @"(?:and\s+)?(?:i\s+)?sell(?:\s+a)?\s+(?<strike2>\d+(\.\d+)?)\s*call\s+(?:in\s+)?(?<notional2>\d+)\s*mio",
        RegexOptions.IgnoreCase | RegexOptions.Singleline
    )
),

// ============================
//  Risk Reversal (Buy Call + Sell Put)
// ============================
new TradePattern(
    "RiskReversal_CallPut",
    new Regex(
        @"(?:i\s+)?buy(?:\s+a)?\s+(?<strike1>\d+(\.\d+)?)\s*call\s+(?:in\s+)?(?<notional1>\d+)\s*mio.*?" +
        @"(?:and\s+)?(?:i\s+)?sell(?:\s+a)?\s+(?<strike2>\d+(\.\d+)?)\s*put\s+(?:in\s+)?(?<notional2>\d+)\s*mio",
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
        public static readonly Regex SpotRegex =
            new Regex(@"(?:ref|spot|sp)\s*(?<spot>\d+(\.\d+)?)", RegexOptions.IgnoreCase);
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