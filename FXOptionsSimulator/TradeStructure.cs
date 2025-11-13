using System;
using System.Collections.Generic;
using FXOptionsSimulator.FIX;  // ← ADD THIS LINE AT THE TOP

namespace FXOptionsSimulator
{
    /// <summary>
    /// Represents a trade structure that will be sent to GFI
    /// This is what your OVML parser will create
    /// </summary>
    public class TradeStructure
    {
        public string Underlying { get; set; }
        public string CurrencyPair { get; set; }  // ← ADD THIS LINE
        public string StructureType { get; set; } // "CallSpread", "PutSpread", "RiskReversal", "Seagull"
        public List<OptionLeg> Legs { get; set; } = new List<OptionLeg>();
        public string PremiumCurrency { get; set; }
        public string HedgeType { get; set; } = "SPOT"; // or "FORWARD"
        public double SpotReference { get; set; }

        public class OptionLeg
        {
            public string Direction { get; set; } // "BUY" or "SELL"
            public string OptionType { get; set; } // "CALL" or "PUT"
            public double Strike { get; set; }
            public string Tenor { get; set; } // "6M", "1Y"
            public DateTime ExpiryDate { get; set; }
            public DateTime DeliveryDate { get; set; }
            public double NotionalMM { get; set; } // In millions
            public string NotionalCurrency { get; set; }
            public string Cutoff { get; set; } = "NY"; // NY, TK, LON
            public string Position { get; set; } // "SAME" or "INVERSE"
            public string LegID { get; set; } // SL0, SL1, SL2
        }

        /// <summary>
        /// Example: EURUSD Call Spread - Buy 1M 6M 1.21C / Sell 2M 1Y 1.22C
        /// </summary>
        public static TradeStructure CreateCallSpread()
        {
            var marketData = MarketData.GetEURUSD();

            return new TradeStructure
            {
                Underlying = "EURUSD",
                StructureType = "CallSpread",
                PremiumCurrency = "USD",
                HedgeType = "SPOT",
                SpotReference = marketData.SpotRate,
                Legs = new List<OptionLeg>
                {
                    new OptionLeg
                    {
                        Direction = "BUY",
                        OptionType = "CALL",
                        Strike = 1.10,
                        Tenor = "6M",
                        ExpiryDate = DateTime.UtcNow.AddMonths(6),
                        DeliveryDate = DateTime.UtcNow.AddMonths(6).AddDays(2),
                        NotionalMM = 5, // 5 million USD
                        NotionalCurrency = "USD",
                        Cutoff = "NY",
                        Position = "SAME",
                        LegID = "SL0"
                    },
                    new OptionLeg
                    {
                        Direction = "SELL",
                        OptionType = "CALL",
                        Strike = 1.15,
                        Tenor = "6M",
                        ExpiryDate = DateTime.UtcNow.AddMonths(6),
                        DeliveryDate = DateTime.UtcNow.AddMonths(6).AddDays(2),
                        NotionalMM = 5, // 5 million USD
                        NotionalCurrency = "USD",
                        Cutoff = "NY",
                        Position = "INVERSE",
                        LegID = "SL1"
                    }
                }
            };
        }

        /// <summary>
        /// Example: USDSEK Put Spread
        /// </summary>
        public static TradeStructure CreatePutSpread()
        {
            var marketData = MarketData.GetUSDSEK();

            return new TradeStructure
            {
                Underlying = "USDSEK",
                StructureType = "PutSpread",
                PremiumCurrency = "SEK",
                SpotReference = marketData.SpotRate,
                Legs = new List<OptionLeg>
                {
                    new OptionLeg
                    {
                        Direction = "BUY",
                        OptionType = "PUT",
                        Strike = 9.60,
                        Tenor = "3M",
                        ExpiryDate = DateTime.UtcNow.AddMonths(3),
                        DeliveryDate = DateTime.UtcNow.AddMonths(3).AddDays(2),
                        NotionalMM = 10,
                        NotionalCurrency = "SEK",
                        Cutoff = "NY",
                        Position = "SAME",
                        LegID = "SL0"
                    },
                    new OptionLeg
                    {
                        Direction = "SELL",
                        OptionType = "PUT",
                        Strike = 9.15,
                        Tenor = "3M",
                        ExpiryDate = DateTime.UtcNow.AddMonths(3),
                        DeliveryDate = DateTime.UtcNow.AddMonths(3).AddDays(2),
                        NotionalMM = 10,
                        NotionalCurrency = "SEK",
                        Cutoff = "NY",
                        Position = "INVERSE",
                        LegID = "SL1"
                    }
                }
            };
        }

        /// <summary>
        /// Example: Risk Reversal (Sell Put / Buy Call)
        /// </summary>
        public static TradeStructure CreateRiskReversal()
        {
            var marketData = MarketData.GetEURUSD();

            return new TradeStructure
            {
                Underlying = "EURUSD",
                StructureType = "RiskReversal",
                PremiumCurrency = "USD",
                SpotReference = marketData.SpotRate,
                Legs = new List<OptionLeg>
                {
                    new OptionLeg
                    {
                        Direction = "SELL",
                        OptionType = "PUT",
                        Strike = 1.05,
                        Tenor = "6M",
                        ExpiryDate = DateTime.UtcNow.AddMonths(6),
                        DeliveryDate = DateTime.UtcNow.AddMonths(6).AddDays(2),
                        NotionalMM = 10,
                        NotionalCurrency = "USD",
                        Position = "SAME", // First leg defines direction
                        LegID = "SL0"
                    },
                    new OptionLeg
                    {
                        Direction = "BUY",
                        OptionType = "CALL",
                        Strike = 1.12,
                        Tenor = "6M",
                        ExpiryDate = DateTime.UtcNow.AddMonths(6),
                        DeliveryDate = DateTime.UtcNow.AddMonths(6).AddDays(2),
                        NotionalMM = 10,
                        NotionalCurrency = "USD",
                        Position = "INVERSE", // Opposite of first leg
                        LegID = "SL1"
                    }
                }
            };
        }

        /// <summary>
        /// Example: Seagull (3-leg structure for zero-cost)
        /// Buy Put / Sell Put / Sell Call
        /// </summary>
        public static TradeStructure CreateSeagull()
        {
            var marketData = MarketData.GetUSDSEK();

            return new TradeStructure
            {
                Underlying = "USDSEK",
                StructureType = "Seagull",
                PremiumCurrency = "SEK",
                SpotReference = marketData.SpotRate,
                Legs = new List<OptionLeg>
                {
                    // Buy protective put
                    new OptionLeg
                    {
                        Direction = "BUY",
                        OptionType = "PUT",
                        Strike = 9.25,
                        Tenor = "6M",
                        ExpiryDate = DateTime.UtcNow.AddMonths(6),
                        DeliveryDate = DateTime.UtcNow.AddMonths(6).AddDays(2),
                        NotionalMM = 40,
                        NotionalCurrency = "SEK",
                        Position = "SAME",
                        LegID = "SL0"
                    },
                    // Sell lower put (finance it)
                    new OptionLeg
                    {
                        Direction = "SELL",
                        OptionType = "PUT",
                        Strike = 8.95,
                        Tenor = "6M",
                        ExpiryDate = DateTime.UtcNow.AddMonths(6),
                        DeliveryDate = DateTime.UtcNow.AddMonths(6).AddDays(2),
                        NotionalMM = 40,
                        NotionalCurrency = "SEK",
                        Position = "INVERSE",
                        LegID = "SL1"
                    },
                    // Sell call (make it zero-cost)
                    new OptionLeg
                    {
                        Direction = "SELL",
                        OptionType = "CALL",
                        Strike = 9.52, // This strike makes it zero-cost
                        Tenor = "6M",
                        ExpiryDate = DateTime.UtcNow.AddMonths(6),
                        DeliveryDate = DateTime.UtcNow.AddMonths(6).AddDays(2),
                        NotionalMM = 40,
                        NotionalCurrency = "SEK",
                        Position = "INVERSE",
                        LegID = "SL2"
                    }
                }
            };
        }

        public void PrintSummary()
        {
            Console.WriteLine($"\n=== TRADE STRUCTURE: {StructureType} ===");
            Console.WriteLine($"Underlying:   {Underlying}");
            Console.WriteLine($"Spot Ref:     {SpotReference:F4}");
            Console.WriteLine($"Premium Ccy:  {PremiumCurrency}");
            Console.WriteLine($"Hedge Type:   {HedgeType}");
            Console.WriteLine($"\nLegs:");

            foreach (var leg in Legs)
            {
                var direction = leg.Direction == "BUY" ? "Buy" : "Sell";
                Console.WriteLine($"  {leg.LegID}: {direction} {leg.NotionalMM}M {leg.OptionType} @ {leg.Strike:F4} ({leg.Tenor})");
            }
        }
    }
}