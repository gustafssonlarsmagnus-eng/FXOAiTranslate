using System;
using System.Drawing;
using System.Windows.Forms;

namespace FXOptionsSimulator
{
    /// <summary>
    /// Container for one LP's quotes: Name/timestamp header + side-by-side BID and ASK tiles.
    /// Shows star indicator when LP provides best bid OR best ask.
    /// </summary>
    public class LPQuoteRow : Panel
    {
        // Fonts
        private readonly Font _headerFont = new Font("Segoe UI", 12F, FontStyle.Semibold);
        private readonly Font _timestampFont = new Font("Segoe UI", 10F, FontStyle.Regular);

        // Colors
        private readonly Color _headerBackground = ColorTranslator.FromHtml("#F8F9FA");
        private readonly Color _headerText = ColorTranslator.FromHtml("#212529");
        private readonly Color _timestampText = ColorTranslator.FromHtml("#6C757D");

        // UI Elements
        private Label _lblLPName;
        private Label _lblTimestamp;
        private QuoteTileControl _bidTile;
        private QuoteTileControl _askTile;
        private Panel _headerPanel;
        private Panel _tilesPanel;

        // State
        private string _lpName;
        private string _lpType; // "DIRECT" or "GFI"
        private DateTime _quoteTime = DateTime.MinValue;
        private bool _hasBestBid = false;
        private bool _hasBestAsk = false;

        public string LPName => _lpName;
        public QuoteTileControl BidTile => _bidTile;
        public QuoteTileControl AskTile => _askTile;

        public LPQuoteRow(string lpName, string lpType = "DIRECT")
        {
            _lpName = lpName;
            _lpType = lpType;
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.BackColor = Color.White;
            this.BorderStyle = BorderStyle.None;
            this.Padding = new Padding(0);
            this.Size = new Size(650, 180);
            this.Margin = new Padding(0, 0, 0, 8); // Space between rows

            // Header panel (LP name + timestamp)
            _headerPanel = new Panel
            {
                BackColor = _headerBackground,
                Dock = DockStyle.Top,
                Height = 32,
                Padding = new Padding(12, 6, 12, 6)
            };

            // LP name label (with star if best)
            _lblLPName = new Label
            {
                Text = $"{_lpName} ({_lpType})",
                Font = _headerFont,
                ForeColor = _headerText,
                AutoSize = true,
                Location = new Point(12, 6)
            };

            // Timestamp label
            _lblTimestamp = new Label
            {
                Text = "• --",
                Font = _timestampFont,
                ForeColor = _timestampText,
                AutoSize = true,
                Location = new Point(_lblLPName.Right + 8, 8)
            };

            _headerPanel.Controls.Add(_lblLPName);
            _headerPanel.Controls.Add(_lblTimestamp);

            // Tiles panel (side-by-side BID and ASK)
            _tilesPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(4)
            };

            // BID tile (left)
            _bidTile = new QuoteTileControl(true)
            {
                Location = new Point(4, 4),
                Size = new Size(318, 176)
            };

            // ASK tile (right)
            _askTile = new QuoteTileControl(false)
            {
                Location = new Point(326, 4),
                Size = new Size(318, 176)
            };

            _tilesPanel.Controls.Add(_bidTile);
            _tilesPanel.Controls.Add(_askTile);

            // Add panels to row
            this.Controls.Add(_tilesPanel);
            this.Controls.Add(_headerPanel);
        }

        /// <summary>
        /// Update header with timestamp and best indicator
        /// </summary>
        public void UpdateHeader(DateTime quoteTime, bool hasBestBid, bool hasBestAsk)
        {
            _quoteTime = quoteTime;
            _hasBestBid = hasBestBid;
            _hasBestAsk = hasBestAsk;

            // Calculate time ago
            TimeSpan elapsed = DateTime.Now - quoteTime;
            string timeAgo;
            if (elapsed.TotalSeconds < 1)
                timeAgo = "just now";
            else if (elapsed.TotalSeconds < 60)
                timeAgo = $"{elapsed.TotalSeconds:F1}s ago";
            else if (elapsed.TotalMinutes < 60)
                timeAgo = $"{elapsed.TotalMinutes:F0}m ago";
            else
                timeAgo = "stale";

            _lblTimestamp.Text = $"• {timeAgo}";

            // Add star if this LP has best bid OR best ask
            if (hasBestBid || hasBestAsk)
            {
                _lblLPName.Text = $"⭐ {_lpName} ({_lpType})";
            }
            else
            {
                _lblLPName.Text = $"{_lpName} ({_lpType})";
            }

            // Adjust timestamp position
            _lblTimestamp.Location = new Point(_lblLPName.Right + 8, 8);
        }

        /// <summary>
        /// Update both tiles with quote data
        /// </summary>
        public void UpdateQuotes(
            // BID data
            double bidVol, double bidPremium, string bidPremiumCcy, double bidPips,
            string bidHedge, string underlying, double notionalMM, bool isBestBid,
            // ASK data
            double askVol, double askPremium, string askPremiumCcy, double askPips,
            string askHedge, bool isBestAsk,
            // Common
            string displayMode, DateTime quoteTime)
        {
            // Update tiles
            _bidTile.UpdateQuote(bidVol, bidPremium, bidPremiumCcy, bidPips,
                                 bidHedge, underlying, notionalMM, isBestBid, displayMode, quoteTime);

            _askTile.UpdateQuote(askVol, askPremium, askPremiumCcy, askPips,
                                 askHedge, underlying, notionalMM, isBestAsk, displayMode, quoteTime);

            // Update header
            UpdateHeader(quoteTime, isBestBid, isBestAsk);
        }

        /// <summary>
        /// Clear both tiles (no quotes)
        /// </summary>
        public void ClearQuotes()
        {
            _bidTile.ClearQuote();
            _askTile.ClearQuote();
            _lblTimestamp.Text = "• --";
            _lblLPName.Text = $"{_lpName} ({_lpType})";
        }

        /// <summary>
        /// Wire up BID action clicked event
        /// </summary>
        public event EventHandler BidActionClicked
        {
            add { _bidTile.ActionClicked += value; }
            remove { _bidTile.ActionClicked -= value; }
        }

        /// <summary>
        /// Wire up ASK action clicked event
        /// </summary>
        public event EventHandler AskActionClicked
        {
            add { _askTile.ActionClicked += value; }
            remove { _askTile.ActionClicked -= value; }
        }
    }
}
