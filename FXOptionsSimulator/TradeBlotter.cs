using System;
using System.Collections.Generic;
using System.Linq;

namespace FXOptionsSimulator
{
    public class TradeBlotterEntry
    {
        // Core Identification
        public DateTime TradeTime { get; set; }
        public string ClOrdID { get; set; }
        public string ExecID { get; set; }
        public DateTime? ExecTimestamp { get; set; }  // EXEC TS

        // Status & Parties
        public string Status { get; set; } // PENDING, FILLED, REJECTED, CONFIRMED (STP Status)
        public string AffirmedBy { get; set; }  // Who confirmed the trade
        public string MyBroker { get; set; }
        public string MyTrader { get; set; }
        public string MyCenter { get; set; }  // NY/TKY/CNH
        public string LP { get; set; }  // Counterparty
        public string CounterpartyCenter { get; set; }  // Ctpy Center
        public string Venue { get; set; }

        // Trade Details
        public string Underlying { get; set; }  // CCY Pair
        public string Side { get; set; }  // Buy/Sell
        public string StructureType { get; set; }  // Strategy (numeric)
        public string StrategyName { get; set; }  // Strategy (readable: Vanilla, RR, etc.)
        public int LegCount { get; set; }
        public double? Strike { get; set; }  // Strike price
        public double? Notional { get; set; }  // Notional amount
        public double NotionalMM { get; set; }  // Size (M) - Notional in millions
        public string NotionalCcy { get; set; }  // Notional currency
        public double? Delta { get; set; }  // Delta
        public double? Volatility { get; set; }  // Vol
        public string OptionType { get; set; }  // CALL, PUT, or structure description

        // Dates
        public DateTime? ExpiryDate { get; set; }  // Expiry
        public string ExpDate { get; set; }  // Expiry date (yyyyMMdd format)
        public string SettlementDate { get; set; }  // Delivery (yyyyMMdd format)
        public DateTime? ValueDate { get; set; }  // Value date

        // Pricing
        public double NetPremium { get; set; }  // Price
        public string PremiumCcy { get; set; }  // Premium currency
        public string PremiumDate { get; set; }  // Premium payment date
        public double? FillPrice { get; set; }
        public string Cut { get; set; }  // Cut (NY/TKY/CNH)
        public double? SpotReference { get; set; }  // Spot
        public double? Swap { get; set; }  // Swap points
        public double? Depo { get; set; }  // Deposit rate

        // Hedge Information
        public string HedgeSide { get; set; }  // Hedge B/S
        public double? HedgeAmount { get; set; }  // Hedge Amt
        public double? HedgeRate { get; set; }  // Hedge Rate
        public string HedgeDeliveryDate { get; set; }  // Hedge Del Date

        // Reject Handling
        public string RejectReason { get; set; }
    }

    public class TradeBlotter
    {
        private static TradeBlotter _instance;
        private readonly List<TradeBlotterEntry> _trades;
        private readonly object _lock = new object();

        public static TradeBlotter Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new TradeBlotter();
                return _instance;
            }
        }

        public event Action<TradeBlotterEntry> OnTradeAdded;
        public event Action<TradeBlotterEntry> OnTradeUpdated;

        private TradeBlotter()
        {
            _trades = new List<TradeBlotterEntry>();
        }

        public void AddTrade(TradeBlotterEntry trade)
        {
            lock (_lock)
            {
                _trades.Add(trade);
                Console.WriteLine($"[Blotter] Trade added: {trade.ClOrdID} - {trade.Side} {trade.Underlying} - {trade.Status}");
            }
            OnTradeAdded?.Invoke(trade);
        }

        public void UpdateTradeStatus(string clOrdID, string status, string execID = null,
            double? fillPrice = null, string rejectReason = null, string premiumDate = null)
        {
            lock (_lock)
            {
                var trade = _trades.FirstOrDefault(t => t.ClOrdID == clOrdID);
                if (trade != null)
                {
                    trade.Status = status;
                    trade.ExecID = execID;
                    trade.FillPrice = fillPrice;
                    trade.RejectReason = rejectReason;

                    // Update premium date if provided
                    if (!string.IsNullOrEmpty(premiumDate))
                    {
                        trade.PremiumDate = premiumDate;
                        Console.WriteLine($"[Blotter] Premium date updated: {premiumDate}");
                    }

                    Console.WriteLine($"[Blotter] Trade updated: {clOrdID} - Status: {status}");
                    OnTradeUpdated?.Invoke(trade);
                }
                else
                {
                    Console.WriteLine($"[Blotter] WARNING: Trade {clOrdID} not found");
                }
            }
        }

        public void UpdateTradeWithSTPConfirmation(string clOrdID, string counterpartyName, string premiumDate = null)
        {
            lock (_lock)
            {
                var trade = _trades.FirstOrDefault(t => t.ClOrdID == clOrdID);
                if (trade != null)
                {
                    trade.Status = "CONFIRMED";
                    trade.LP = counterpartyName;  // Update LP to final counterparty

                    // Update premium date if provided
                    if (!string.IsNullOrEmpty(premiumDate))
                    {
                        trade.PremiumDate = premiumDate;
                        Console.WriteLine($"[Blotter] Premium date updated: {premiumDate}");
                    }

                    Console.WriteLine($"[Blotter] STP Confirmation: {clOrdID} - Counterparty: {counterpartyName}");
                    OnTradeUpdated?.Invoke(trade);
                }
                else
                {
                    Console.WriteLine($"[Blotter] WARNING: Trade {clOrdID} not found for STP confirmation");
                }
            }
        }

        public List<TradeBlotterEntry> GetAllTrades()
        {
            lock (_lock)
            {
                return new List<TradeBlotterEntry>(_trades);
            }
        }

        public List<TradeBlotterEntry> GetTodaysTrades()
        {
            lock (_lock)
            {
                var today = DateTime.Today;
                return _trades.Where(t => t.TradeTime.Date == today).ToList();
            }
        }

        public TradeBlotterEntry GetTrade(string clOrdID)
        {
            lock (_lock)
            {
                return _trades.FirstOrDefault(t => t.ClOrdID == clOrdID);
            }
        }
    }
}
