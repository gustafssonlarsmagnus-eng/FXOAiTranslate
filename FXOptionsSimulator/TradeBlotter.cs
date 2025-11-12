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
        public double NetPremium { get; set; }
        public string Status { get; set; } // PENDING, FILLED, REJECTED
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
