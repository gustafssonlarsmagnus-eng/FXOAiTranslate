using System;
using System.Drawing;
using System.Windows.Forms;

namespace FXOptionsSimulator
{
    /// <summary>
    /// Individual quote tile for BID or ASK side.
    /// Shows Vol, Premium, Pips with large bold font for preferred metric.
    /// Background color changes based on best price (darker green shade).
    /// </summary>
    public class QuoteTileControl : Panel
    {
        // Visual hierarchy fonts
        private readonly Font _preferredFont = new Font("Segoe UI", 24F, FontStyle.Bold);
        private readonly Font _normalFont = new Font("Segoe UI", 11F, FontStyle.Regular);
        private readonly Font _smallFont = new Font("Segoe UI", 10F, FontStyle.Regular);

        // Color scheme (modern, not Bloomberg black/orange)
        private readonly Color _bestPriceBackground = ColorTranslator.FromHtml("#D4EDDA"); // Soft mint green
        private readonly Color _goodPriceBackground = ColorTranslator.FromHtml("#F8F9FA"); // Light gray
        private readonly Color _normalBackground = Color.White;
        private readonly Color _staleBackground = ColorTranslator.FromHtml("#FFF8E1"); // Light yellow
        private readonly Color _textColor = ColorTranslator.FromHtml("#212529"); // Dark gray

        // UI Elements
        private Label _lblSide;           // "BID" or "ASK"
        private Label _lblVol;            // "Vol: 4.863%"
        private Label _lblPremium;        // "64,550 EUR" (24pt BOLD when preferred)
        private Label _lblPips;           // "Receive 64.550 Pips"
        private Label _lblHedge;          // "Hedge: Buy 10M EUR/SEK"
        private Button _btnAction;        // "HIT BID" or "LIFT OFFER"
        private Label _lblBestIndicator;  // "⭐ BEST BID" shown when this is best

        // State
        private bool _isBid;
        private bool _isBest;
        private string _displayMode = "Premium"; // "Vol", "Premium", or "Pips"
        private DateTime _quoteTime = DateTime.MinValue;

        public QuoteTileControl(bool isBid)
        {
            _isBid = isBid;
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.BackColor = _normalBackground;
            this.BorderStyle = BorderStyle.None;
            this.Padding = new Padding(12);
            this.Size = new Size(300, 140);

            // Best indicator (hidden by default)
            _lblBestIndicator = new Label
            {
                Text = _isBid ? "⭐ BEST BID" : "⭐ BEST ASK",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = ColorTranslator.FromHtml("#28A745"), // Green
                AutoSize = true,
                Location = new Point(12, 8),
                Visible = false
            };

            // Side label ("BID" or "ASK")
            _lblSide = new Label
            {
                Text = _isBid ? "BID" : "ASK",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = _textColor,
                AutoSize = true,
                Location = new Point(12, 8)
            };

            // Vol label
            _lblVol = new Label
            {
                Text = "Vol: --",
                Font = _normalFont,
                ForeColor = _textColor,
                AutoSize = false,
                Size = new Size(270, 24),
                Location = new Point(12, 32)
            };

            // Premium label (24pt BOLD when preferred)
            _lblPremium = new Label
            {
                Text = "--- EUR",
                Font = _preferredFont,
                ForeColor = _textColor,
                AutoSize = false,
                Size = new Size(270, 36),
                Location = new Point(12, 56)
            };

            // Pips label
            _lblPips = new Label
            {
                Text = _isBid ? "Receive --- Pips" : "Pay --- Pips",
                Font = _normalFont,
                ForeColor = _textColor,
                AutoSize = false,
                Size = new Size(270, 20),
                Location = new Point(12, 92)
            };

            // Hedge label
            _lblHedge = new Label
            {
                Text = "Hedge: ---",
                Font = _smallFont,
                ForeColor = ColorTranslator.FromHtml("#6C757D"), // Gray
                AutoSize = false,
                Size = new Size(270, 18),
                Location = new Point(12, 112)
            };

            // Action button
            _btnAction = new Button
            {
                Text = _isBid ? "HIT BID" : "LIFT OFFER",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = _isBid ? ColorTranslator.FromHtml("#28A745") : ColorTranslator.FromHtml("#007BFF"),
                FlatStyle = FlatStyle.Flat,
                Size = new Size(270, 32),
                Location = new Point(12, 136),
                Cursor = Cursors.Hand
            };
            _btnAction.FlatAppearance.BorderSize = 0;

            // Add controls
            this.Controls.Add(_lblBestIndicator);
            this.Controls.Add(_lblSide);
            this.Controls.Add(_lblVol);
            this.Controls.Add(_lblPremium);
            this.Controls.Add(_lblPips);
            this.Controls.Add(_lblHedge);
            this.Controls.Add(_btnAction);
        }

        /// <summary>
        /// Update tile with quote data
        /// </summary>
        public void UpdateQuote(double vol, double premium, string premiumCcy, double pips,
                                string hedgeDirection, string underlying, double notionalMM,
                                bool isBest, string displayMode, DateTime quoteTime)
        {
            _isBest = isBest;
            _displayMode = displayMode;
            _quoteTime = quoteTime;

            // Update values
            _lblVol.Text = $"Vol: {vol:F3}%";
            _lblPremium.Text = $"{premium:N0} {premiumCcy}";
            _lblPips.Text = _isBid ? $"Receive {pips:F3} Pips" : $"Pay {pips:F3} Pips";
            _lblHedge.Text = $"Hedge: {hedgeDirection} {notionalMM:F1}M {underlying}";

            // Update visual hierarchy based on display mode
            UpdateFontSizes(displayMode);

            // Update background color
            UpdateBackgroundColor();

            // Show/hide best indicator
            if (isBest)
            {
                _lblBestIndicator.Visible = true;
                _lblSide.Visible = false;
            }
            else
            {
                _lblBestIndicator.Visible = false;
                _lblSide.Visible = true;
            }
        }

        /// <summary>
        /// Clear tile (no quote received)
        /// </summary>
        public void ClearQuote()
        {
            _lblVol.Text = "Vol: --";
            _lblPremium.Text = "--- EUR";
            _lblPips.Text = _isBid ? "Receive --- Pips" : "Pay --- Pips";
            _lblHedge.Text = "Hedge: ---";
            this.BackColor = _normalBackground;
            _lblBestIndicator.Visible = false;
            _lblSide.Visible = true;
            _btnAction.Enabled = false;
        }

        /// <summary>
        /// Adjust font sizes based on display mode (Vol/Premium/Pips)
        /// </summary>
        private void UpdateFontSizes(string mode)
        {
            // Reset all to normal
            _lblVol.Font = _normalFont;
            _lblPremium.Font = _normalFont;
            _lblPips.Font = _normalFont;

            // Make preferred metric 24pt BOLD
            switch (mode)
            {
                case "Vol":
                    _lblVol.Font = _preferredFont;
                    break;
                case "Premium":
                    _lblPremium.Font = _preferredFont;
                    break;
                case "Pips":
                    _lblPips.Font = _preferredFont;
                    break;
            }
        }

        /// <summary>
        /// Update background color based on best/good/stale status
        /// </summary>
        private void UpdateBackgroundColor()
        {
            // Check if quote is stale (>5 seconds old)
            bool isStale = (DateTime.Now - _quoteTime).TotalSeconds > 5;

            if (_isBest)
            {
                this.BackColor = _bestPriceBackground; // Darker green shade
            }
            else if (isStale)
            {
                this.BackColor = _staleBackground; // Light yellow
            }
            else
            {
                // TODO: Check if quote is "good" (within 5% of best)
                // For now, use normal background
                this.BackColor = _normalBackground;
            }
        }

        /// <summary>
        /// Wire up action button click event
        /// </summary>
        public event EventHandler ActionClicked
        {
            add { _btnAction.Click += value; }
            remove { _btnAction.Click -= value; }
        }

        /// <summary>
        /// Enable/disable action button
        /// </summary>
        public bool ActionEnabled
        {
            get { return _btnAction.Enabled; }
            set { _btnAction.Enabled = value; }
        }
    }
}
