using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FXOptionsSimulator
{
    /// <summary>
    /// Leg pricing information from Quote Response (NoMQEntries)
    /// </summary>
    public class LegPricingInfo
    {
        public string LegStrategyID { get; set; }      // Tag 7940
        public string Volatility { get; set; }          // Tag 5678
        public string MQSize { get; set; }              // Tag 5359
        public string LegPremPrice { get; set; }        // Tag 5844
        public string LegSpotRate { get; set; }         // Tag 5235
        public string LegSymbol { get; set; }           // Tag 600 (usually same as main symbol)
    }

    /// <summary>
    /// Simple FIX message representation for simulator
    /// </summary>
    public class FIXMessage
    {
        public Dictionary<string, string> Fields { get; set; }
        public DateTime Timestamp { get; set; }
        public List<LegPricingInfo> LegPricing { get; set; }

        public FIXMessage(string msgType)
        {
            Fields = new Dictionary<string, string>
            {
                ["35"] = msgType
            };
            Timestamp = DateTime.UtcNow;
            LegPricing = new List<LegPricingInfo>();
        }

        public FIXMessage Set(string tag, string value)
        {
            Fields[tag] = value;
            return this;
        }

        public string Get(string tag, string defaultValue = null)
        {
            return Fields.TryGetValue(tag, out var value) ? value : defaultValue;
        }

        public string MsgType => Get("35");

        public string ToFixString()
        {
            // Sort by tag number for proper FIX ordering
            // Skip non-numeric tags (like leg1_xxx which are just for demo)
            var ordered = Fields
                .Where(kvp => int.TryParse(kvp.Key, out _)) // Only include numeric tags
                .OrderBy(kvp => int.Parse(kvp.Key))
                .Select(kvp => $"{kvp.Key}={kvp.Value}");

            return string.Join("|", ordered);
        }

        public override string ToString()
        {
            return $"FIX({MsgType}): {ToFixString()}";
        }
    }

    /// <summary>
    /// FIX message types
    /// </summary>
    public static class MsgTypes
    {
        public const string QuoteRequest = "R";
        public const string Quote = "S";
        public const string QuoteStatusReport = "AI";
        public const string QuoteCancel = "Z";
        public const string NewOrderMultileg = "AB";
        public const string ExecutionReport = "8";
        public const string BusinessMessageReject = "j";
    }

    /// <summary>
    /// Common FIX tags
    /// </summary>
    public static class Tags
    {
        // Header
        public const int MsgType = 35;
        public const int SenderCompID = 49;
        public const int TargetCompID = 56;
        public const int OnBehalfOfCompID = 115;
        public const int DeliverToCompID = 128;
        public const int SendingTime = 52;

        // Identification
        public const int QuoteReqID = 131;
        public const int QuoteID = 117;
        public const int ClOrdID = 11;
        public const int OrderID = 37;
        public const int ExecID = 17;

        // Product
        public const int Symbol = 55;
        public const int Structure = 9126;
        public const int NoLegs = 555;
        public const int LegSymbol = 600;
        public const int LegStrikePrice = 612;
        public const int LegQty = 687;
        public const int LegCurrency = 556;
        public const int LegStrategyInd = 6714;
        public const int LegStrategyID = 7940;

        // Dates
        public const int Tenor = 6215;
        public const int LegMaturityDate = 611;
        public const int DeliveryDate = 743;
        public const int PremiumDelivery = 5020;
        public const int Cutoff = 9125;

        // Pricing
        public const int Side = 54;
        public const int Volatility = 5678;
        public const int LegPremPrice = 5844;
        public const int MQSize = 5359;
        public const int LegSpotRate = 5235;
        public const int LegForwardPoints = 5191;
        public const int DepoRate = 9115;
        public const int DepoRateCcy = 9073;
        public const int LegDelta = 6035;
        public const int MQStrikePrice = 6354;

        // Status
        public const int QuoteStatus = 297;
        public const int OrdStatus = 39;
        public const int ExecType = 150;
        public const int Text = 58;
        public const int BusinessRejectReason = 380;
        public const int RefMsgType = 372;

        // Custom Fenics
        public const int NoBanksReqFenics = 8051;
        public const int BankRequestedCompID = 8053;
        public const int RegulationVenueType = 8505;
        public const int PremiumCcy = 5830;
        public const int HedgeTradeType = 9016;
        public const int FXOptionStyle = 9019;
        public const int Position = 6351;
        public const int PriceIndicator = 9904;
    }
    public static class TagStrings
    {
        public const string MsgType = "35";
        public const string SenderCompID = "49";
        public const string TargetCompID = "56";
        public const string OnBehalfOfCompID = "115";
        public const string DeliverToCompID = "128";
        public const string QuoteReqID = "131";
        public const string QuoteID = "117";
        public const string ClOrdID = "11";
        public const string OrderID = "37";
        public const string Symbol = "55";
        public const string Structure = "9126";
        public const string NoLegs = "555";
        public const string Side = "54";
        public const string Volatility = "5678";
        public const string MQSize = "5359";
        public const string QuoteStatus = "297";
        public const string OrdStatus = "39";
        public const string ExecType = "150";
        public const string NoBanksReqFenics = "8051";
        public const string PremiumCcy = "5830";
        public const string HedgeTradeType = "9016";
        public const string SendingTime = "52";
    }

    /// <summary>
    /// FIX field values
    /// </summary>
    public static class Values
    {
        public static class Side
        {
            public const string Buy = "1";
            public const string Sell = "2";
        }

        public static class OrdStatus
        {
            public const string New = "0";
            public const string PartiallyFilled = "1";
            public const string Filled = "2";
            public const string Canceled = "4";
            public const string Rejected = "8";
        }

        public static class Structure
        {
            public const string Call = "1";
            public const string Put = "2";
            public const string CallSpread = "8";
            public const string PutSpread = "9";
            public const string RiskReversal = "5";
        }
    }
}