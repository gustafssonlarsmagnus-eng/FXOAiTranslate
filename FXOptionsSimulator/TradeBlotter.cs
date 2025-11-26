using System;
using System.Collections.Generic;
using System.Linq;

namespace FXOptionsSimulator
{
    public class TradeBlotterEntry
    {
        public DateTime TradeTime { get; set; }
        public string ClOrdID { get; set; }
        public string LP { get; set; }
        public string Side { get; set; }
        public string Underlying { get; set; }
        public string StructureType { get; set; }
        public int LegCount { get; set; }
        public double? Strike { get; set; }  // Strike price (first leg for multi-leg)
        public double? Notional { get; set; }  // Notional amount
        public string NotionalCcy { get; set; }  // Notional currency
        public string ExpDate { get; set; }  // Expiry date (yyyyMMdd format)
        public string SettlementDate { get; set; }  // Settlement/delivery date (yyyyMMdd format)
        public string Cut { get; set; }  // Cutoff (NY/TK/LON)
        public double NetPremium { get; set; }
        public string PremiumCcy { get; set; }  // Premium currency
        public string PremiumDate { get; set; }  // Premium payment date (yyyyMMdd format)
        public double? Delta { get; set; }  // Option delta from quote
        public double? Volatility { get; set; }  // Executed volatility (average for multi-leg)
        public string Status { get; set; } // PENDING, FILLED, REJECTED, CONFIRMED
        public string RejectReason { get; set; }
        public string ExecID { get; set; }
        public double? FillPrice { get; set; }
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
            double? fillPrice = null, string rejectReason = null)
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

                    Console.WriteLine($"[Blotter] Trade updated: {clOrdID} - Status: {status}");
                    OnTradeUpdated?.Invoke(trade);
                }
                else
                {
                    Console.WriteLine($"[Blotter] WARNING: Trade {clOrdID} not found");
                }
            }
        }

        public void UpdateTradeWithSTPConfirmation(string clOrdID, string counterpartyName)
        {
            lock (_lock)
            {
                var trade = _trades.FirstOrDefault(t => t.ClOrdID == clOrdID);
                if (trade != null)
                {
                    trade.Status = "CONFIRMED";
                    trade.LP = counterpartyName;  // Update LP to final counterparty

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
