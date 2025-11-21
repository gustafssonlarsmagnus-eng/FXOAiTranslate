using System;
using System.Collections.Generic;
using System.Linq;

namespace FXOptionsSimulator
{
    /// <summary>
    /// Represents a single option leg from Trade Capture Report (AE)
    /// </summary>
    public class CapturedOptionLeg
    {
        public string Symbol { get; set; }              // Tag 600
        public string SecurityAltID { get; set; }       // Tag 605 (UTI)
        public string CFICode { get; set; }             // Tag 608 (e.g., HFTDVP)
        public string LegSecurityDesc { get; set; }     // Tag 654 (e.g., PutSpread-Leg-0-xxx)
        public string LegSide { get; set; }             // Tag 624 (1=Buy, 2=Sell)
        public double Quantity { get; set; }            // Tag 687 (notional MM)
        public string Currency { get; set; }            // Tag 556
        public string OptionType { get; set; }          // Tag 6215 (OD=European, etc)
        public DateTime MaturityDate { get; set; }      // Tag 611
        public DateTime DeliveryDate { get; set; }      // Tag 743
        public double StrikePrice { get; set; }         // Tag 612
        public double Delta { get; set; }               // Tag 6035
        public double SpotRate { get; set; }            // Tag 5235
        public double ForwardPoints { get; set; }       // Tag 5191
        public string PremiumCurrency { get; set; }     // Tag 9073
        public double Volatility { get; set; }          // Tag 5678
        public double Premium { get; set; }             // Tag 566 (GrossAmount)
        public DateTime PremiumSettlement { get; set; } // Tag 5020
        public double PremiumAmount { get; set; }       // Tag 6034
    }

    /// <summary>
    /// Represents a hedge leg from Trade Capture Report (AE)
    /// </summary>
    public class CapturedHedgeLeg
    {
        public string Currency { get; set; }            // Tag 9074
        public string SecurityAltID { get; set; }       // Tag 10605 (UTI)
        public string CFICode { get; set; }             // Tag 10608 (JFTXFC=NDF, JFTXFP=Fwd)
        public string Direction { get; set; }           // Tag 6666 (1=Buy, 2=Sell)
        public double Amount { get; set; }              // Tag 6036
        public double Rate { get; set; }                // Tag 9657
        public DateTime DeliveryDate { get; set; }      // Tag 9112
        public string Symbol { get; set; }              // Tag 6426
        public DateTime? FixingDate { get; set; }       // Tag 5790 (if NDF)
        public bool IsNDF => FixingDate.HasValue;
    }

    /// <summary>
    /// Represents a complete trade from Trade Capture Report (AE)
    /// </summary>
    public class CapturedTrade
    {
        public string ExecID { get; set; }              // Tag 17
        public string QuoteID { get; set; }             // Tag 117
        public string QuoteReqID { get; set; }          // Tag 131
        public string OrderID { get; set; }             // Tag 37
        public string ClOrdID { get; set; }             // Tag 11
        public DateTime TransactTime { get; set; }      // Tag 60
        public DateTime TradeDate { get; set; }         // Tag 75
        public string Symbol { get; set; }              // Tag 55
        public string Side { get; set; }                // Tag 54 (B=Buy, S=Sell)

        // Counterparty info from Tag 1: "LEI|Reuters|Region|LEI"
        public string CounterpartyLEI { get; set; }
        public string CounterpartyReuters { get; set; }
        public string CounterpartyName { get; set; }    // Populated from mapping table

        public List<CapturedOptionLeg> OptionLegs { get; set; }
        public List<CapturedHedgeLeg> HedgeLegs { get; set; }

        public CapturedTrade()
        {
            OptionLegs = new List<CapturedOptionLeg>();
            HedgeLegs = new List<CapturedHedgeLeg>();
        }
    }

    /// <summary>
    /// Parser for FIX 4.4 Trade Capture Report (35=AE) messages
    /// </summary>
    public class TradeCaptureParser
    {
        private static Dictionary<string, string> _leiMapping = new Dictionary<string, string>
        {
            // Placeholder mapping - to be replaced with real mapping table
            { "DGQCSV2PHVF7I2743539", "Nomura International PLC" },
            { "7LTWFZYICNSX8D621K86", "Deutsche Bank AG" },
            { "M312WZV08Y7LYUC71685", "Morgan Stanley" },
            { "GUNTJCA81C7IHNBGI392", "UBS AG" },
            // Add more as needed
        };

        /// <summary>
        /// Parse a FIX 4.4 Trade Capture Report (AE) message
        /// </summary>
        public static CapturedTrade ParseAE(string fixMessage)
        {
            // Parse FIX message into tag=value dictionary
            var fields = ParseFIXString(fixMessage);

            if (fields.Get("35") != "AE")
            {
                throw new Exception($"Expected message type AE, got {fields.Get("35")}");
            }

            var trade = new CapturedTrade
            {
                ExecID = fields.Get("17"),
                QuoteID = fields.Get("117"),
                QuoteReqID = fields.Get("131"),
                OrderID = fields.Get("37"),
                ClOrdID = fields.Get("11"),
                Symbol = fields.Get("55"),
                Side = fields.Get("54")
            };

            // Parse TransactTime (tag 60)
            if (DateTime.TryParseExact(fields.Get("60"), "yyyyMMdd-HH:mm:ss.ffffff",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime transactTime))
            {
                trade.TransactTime = transactTime;
            }

            // Parse TradeDate (tag 75)
            if (DateTime.TryParseExact(fields.Get("75"), "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime tradeDate))
            {
                trade.TradeDate = tradeDate;
            }

            // Parse counterparty from Tag 1: "LEI|Reuters|Region|LEI"
            var cptyInfo = fields.Get("1");
            if (!string.IsNullOrEmpty(cptyInfo))
            {
                var parts = cptyInfo.Split('|');
                if (parts.Length >= 2)
                {
                    trade.CounterpartyLEI = parts[0];
                    trade.CounterpartyReuters = parts[1];

                    // Lookup name from mapping
                    if (_leiMapping.TryGetValue(trade.CounterpartyLEI, out string name))
                    {
                        trade.CounterpartyName = name;
                    }
                    else
                    {
                        trade.CounterpartyName = trade.CounterpartyLEI; // Fallback to LEI
                    }
                }
            }

            // Parse option legs (tag 555 repeating group)
            int numLegs = int.Parse(fields.Get("555") ?? "0");
            trade.OptionLegs = ParseOptionLegs(fields, numLegs);

            // Parse hedge legs (tag 7464 repeating group)
            int numHedges = int.Parse(fields.Get("7464") ?? "0");
            if (numHedges > 0)
            {
                trade.HedgeLegs = ParseHedgeLegs(fields, numHedges);
            }

            return trade;
        }

        /// <summary>
        /// Parse FIX string format (pipe-delimited) into dictionary
        /// </summary>
        private static FIXMessage ParseFIXString(string fixString)
        {
            var msg = new FIXMessage("AE");

            var parts = fixString.Split('|');
            foreach (var part in parts)
            {
                var tagValue = part.Split(new[] { '=' }, 2);
                if (tagValue.Length == 2)
                {
                    msg.Set(tagValue[0], tagValue[1]);
                }
            }

            return msg;
        }

        /// <summary>
        /// Parse option legs from tag 555 repeating group
        /// Note: This is a simplified parser - real implementation needs to handle
        /// repeating group boundaries properly
        /// </summary>
        private static List<CapturedOptionLeg> ParseOptionLegs(FIXMessage fields, int numLegs)
        {
            var legs = new List<CapturedOptionLeg>();

            // TODO: Implement proper repeating group parsing once conformance pack arrives
            // For now, this is a placeholder structure

            // The repeating group starts after tag 555
            // Each leg contains fields like 600, 605, 606, 608, 624, 687, 556, 611, 743, 612, etc.
            // We need to identify the boundaries of each leg

            Console.WriteLine($"[TradeCaptureParser] TODO: Parse {numLegs} option legs");

            return legs;
        }

        /// <summary>
        /// Parse hedge legs from tag 7464 repeating group
        /// </summary>
        private static List<CapturedHedgeLeg> ParseHedgeLegs(FIXMessage fields, int numHedges)
        {
            var hedges = new List<CapturedHedgeLeg>();

            // TODO: Implement proper repeating group parsing once conformance pack arrives

            Console.WriteLine($"[TradeCaptureParser] TODO: Parse {numHedges} hedge legs");

            return hedges;
        }

        /// <summary>
        /// Update LEI mapping table
        /// </summary>
        public static void UpdateLEIMapping(Dictionary<string, string> mapping)
        {
            _leiMapping = mapping;
        }

        /// <summary>
        /// Format trade for display
        /// </summary>
        public static string FormatTrade(CapturedTrade trade)
        {
            return $"Trade: {trade.Symbol} {trade.Side} with {trade.CounterpartyName}\n" +
                   $"  ExecID: {trade.ExecID}\n" +
                   $"  QuoteID: {trade.QuoteID}\n" +
                   $"  TransactTime: {trade.TransactTime:yyyy-MM-dd HH:mm:ss}\n" +
                   $"  Option Legs: {trade.OptionLegs.Count}\n" +
                   $"  Hedge Legs: {trade.HedgeLegs.Count}";
        }
    }
}
