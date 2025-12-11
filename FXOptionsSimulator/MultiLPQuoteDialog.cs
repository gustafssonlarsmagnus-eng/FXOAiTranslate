using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FXOptionsSimulator
{
    /// <summary>
    /// Modern multi-LP quote request dialog.
    /// Left panel: Trade staging (legs, parameters, market data).
    /// Right panel: LP selection + side-by-side Bid|Offer pricing tiles.
    /// </summary>
    public partial class MultiLPQuoteDialog : Form
    {
        // State
        private TradeStructure _trade;
        private string _displayMode = "Premium"; // "Vol", "Premium", or "Pips"
        private Dictionary<string, LPQuoteRow> _lpRows = new Dictionary<string, LPQuoteRow>();
        private List<string> _selectedLPs = new List<string>();
        private Dictionary<string, QuoteData> _quotes = new Dictionary<string, QuoteData>();

        // Quote data structure
        private class QuoteData
        {
            public string LPName { get; set; }
            public double BidVol { get; set; }
            public double BidPremium { get; set; }
            public double BidPips { get; set; }
            public double AskVol { get; set; }
            public double AskPremium { get; set; }
            public double AskPips { get; set; }
            public DateTime QuoteTime { get; set; }
        }

        // UI Components - Left Panel (Trade Staging)
        private Panel _leftPanel;
        private Label _lblTradeTitle;
        private DataGridView _dgvLegs;
        private GroupBox _gbMarketData;
        private Label _lblSpot;
        private Label _lblForward;
        private Label _lblDepo1;
        private Label _lblDepo2;
        private GroupBox _gbParameters;
        private ComboBox _cmbPremiumType;
        private ComboBox _cmbHedgeType;
        private Button _btnToggleDisplay;
        private TextBox _txtNotional;
        private Label _lblNotionalCcy;

        // UI Components - Right Panel (LP Selection + Pricing)
        private Panel _rightPanel;
        private GroupBox _gbLPSelection;
        private CheckBox _chkGFI;
        private Panel _pnlDirectLPs;
        private Button _btnRequestQuotes;
        private Panel _pnlBestPrices;  // NEW: Top section showing aggregated best bid/offer
        private Panel _pnlBestBid;
        private Panel _pnlBestOffer;
        private Label _lblBestBidValue;
        private Label _lblBestOfferValue;
        private Label _lblBestBidDetails;
        private Label _lblBestOfferDetails;
        private Label _lblBestBidSource;
        private Label _lblBestOfferSource;
        private Panel _pnlPricingTiles;
        private FlowLayoutPanel _flowPricingTiles;

        // LP configurations
        private readonly Dictionary<string, string> _directLPs = new Dictionary<string, string>
        {
            { "SEB Stockholm", "DIRECT" },
            { "Nordea Stockholm", "DIRECT" },
            { "Danske Copenhagen", "DIRECT" },
            { "Handelsbanken Stockholm", "DIRECT" }
        };

        public MultiLPQuoteDialog(TradeStructure trade)
        {
            _trade = trade;
            InitializeUI();
            PopulateTradeDetails();
        }

        private void InitializeUI()
        {
            // Form properties
            this.Text = "Multi-LP Quote Request";
            this.Size = new Size(1400, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColorTranslator.FromHtml("#FFFFFF");
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(1200, 700);

            // Main split container
            var splitMain = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 600,
                IsSplitterFixed = false,
                BorderStyle = BorderStyle.None
            };

            // Left panel (Trade Staging)
            _leftPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ColorTranslator.FromHtml("#F8F9FA"),
                Padding = new Padding(16)
            };

            InitializeLeftPanel();
            splitMain.Panel1.Controls.Add(_leftPanel);

            // Right panel (LP Selection + Pricing)
            _rightPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(16)
            };

            InitializeRightPanel();
            splitMain.Panel2.Controls.Add(_rightPanel);

            this.Controls.Add(splitMain);
        }

        private void InitializeLeftPanel()
        {
            int yPos = 0;

            // Trade title
            _lblTradeTitle = new Label
            {
                Text = $"{_trade.Underlying} {GetStrategyName(_trade.StructureType)}",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = ColorTranslator.FromHtml("#212529"),
                AutoSize = true,
                Location = new Point(0, yPos)
            };
            _leftPanel.Controls.Add(_lblTradeTitle);
            yPos += 40;

            // Legs grid
            _dgvLegs = new DataGridView
            {
                Location = new Point(0, yPos),
                Size = new Size(560, 180),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                BorderStyle = BorderStyle.None,
                BackgroundColor = Color.White,
                GridColor = ColorTranslator.FromHtml("#DEE2E6"),
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = ColorTranslator.FromHtml("#E9ECEF"),
                    ForeColor = ColorTranslator.FromHtml("#495057"),
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    Alignment = DataGridViewContentAlignment.MiddleLeft,
                    Padding = new Padding(8)
                },
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.White,
                    ForeColor = ColorTranslator.FromHtml("#212529"),
                    Font = new Font("Segoe UI", 10F),
                    SelectionBackColor = ColorTranslator.FromHtml("#E3F2FD"),
                    SelectionForeColor = ColorTranslator.FromHtml("#1976D2"),
                    Padding = new Padding(8)
                }
            };

            // Define columns
            _dgvLegs.Columns.Add("Leg", "Leg");
            _dgvLegs.Columns.Add("Direction", "Dir");
            _dgvLegs.Columns.Add("Style", "Style");
            _dgvLegs.Columns.Add("Strike", "Strike");
            _dgvLegs.Columns.Add("Expiry", "Expiry");
            _dgvLegs.Columns.Add("Notional", "Notional");

            _dgvLegs.Columns["Leg"].Width = 60;
            _dgvLegs.Columns["Direction"].Width = 60;
            _dgvLegs.Columns["Style"].Width = 70;
            _dgvLegs.Columns["Strike"].Width = 90;
            _dgvLegs.Columns["Expiry"].Width = 140;
            _dgvLegs.Columns["Notional"].Width = 120;

            _leftPanel.Controls.Add(_dgvLegs);
            yPos += 190;

            // Market data group
            _gbMarketData = new GroupBox
            {
                Text = "Market Data",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Location = new Point(0, yPos),
                Size = new Size(560, 120),
                ForeColor = ColorTranslator.FromHtml("#495057")
            };

            _lblSpot = CreateDataLabel("Spot:", "Loading...", 20);
            _lblForward = CreateDataLabel("Forward:", "Loading...", 45);
            _lblDepo1 = CreateDataLabel($"{GetCcy1()} Depo:", "Loading...", 70);
            _lblDepo2 = CreateDataLabel($"{GetCcy2()} Depo:", "Loading...", 95);

            _gbMarketData.Controls.Add(_lblSpot);
            _gbMarketData.Controls.Add(_lblForward);
            _gbMarketData.Controls.Add(_lblDepo1);
            _gbMarketData.Controls.Add(_lblDepo2);

            _leftPanel.Controls.Add(_gbMarketData);
            yPos += 130;

            // Parameters group
            _gbParameters = new GroupBox
            {
                Text = "Parameters",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Location = new Point(0, yPos),
                Size = new Size(560, 160),
                ForeColor = ColorTranslator.FromHtml("#495057")
            };

            // Premium Type
            var lblPremType = new Label
            {
                Text = "Premium Type:",
                Font = new Font("Segoe UI", 10F),
                Location = new Point(16, 30),
                AutoSize = true
            };

            _cmbPremiumType = new ComboBox
            {
                Location = new Point(140, 28),
                Size = new Size(120, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10F)
            };
            _cmbPremiumType.Items.AddRange(new object[] { "Spot", "Forward" });
            _cmbPremiumType.SelectedIndex = 0;

            // Hedge Type
            var lblHedgeType = new Label
            {
                Text = "Delta Hedge:",
                Font = new Font("Segoe UI", 10F),
                Location = new Point(280, 30),
                AutoSize = true
            };

            _cmbHedgeType = new ComboBox
            {
                Location = new Point(380, 28),
                Size = new Size(120, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10F)
            };
            _cmbHedgeType.Items.AddRange(new object[] { "Live", "Spot", "Forward" });
            _cmbHedgeType.SelectedIndex = 0;

            // Display toggle (Vol/Premium/Pips)
            var lblDisplay = new Label
            {
                Text = "Display:",
                Font = new Font("Segoe UI", 10F),
                Location = new Point(16, 70),
                AutoSize = true
            };

            _btnToggleDisplay = new Button
            {
                Text = "Premium",
                Location = new Point(140, 66),
                Size = new Size(120, 30),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = ColorTranslator.FromHtml("#007BFF"),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnToggleDisplay.FlatAppearance.BorderSize = 0;
            _btnToggleDisplay.Click += BtnToggleDisplay_Click;

            // Notional
            var lblNotional = new Label
            {
                Text = "Notional:",
                Font = new Font("Segoe UI", 10F),
                Location = new Point(16, 110),
                AutoSize = true
            };

            _txtNotional = new TextBox
            {
                Text = "10.0",
                Location = new Point(140, 108),
                Size = new Size(80, 25),
                Font = new Font("Segoe UI", 10F)
            };

            _lblNotionalCcy = new Label
            {
                Text = $"M {GetCcy1()}",
                Font = new Font("Segoe UI", 10F),
                Location = new Point(225, 110),
                AutoSize = true
            };

            _gbParameters.Controls.Add(lblPremType);
            _gbParameters.Controls.Add(_cmbPremiumType);
            _gbParameters.Controls.Add(lblHedgeType);
            _gbParameters.Controls.Add(_cmbHedgeType);
            _gbParameters.Controls.Add(lblDisplay);
            _gbParameters.Controls.Add(_btnToggleDisplay);
            _gbParameters.Controls.Add(lblNotional);
            _gbParameters.Controls.Add(_txtNotional);
            _gbParameters.Controls.Add(_lblNotionalCcy);

            _leftPanel.Controls.Add(_gbParameters);
        }

        private void InitializeRightPanel()
        {
            int yPos = 0;

            // Title
            var lblTitle = new Label
            {
                Text = "Liquidity Providers",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = ColorTranslator.FromHtml("#212529"),
                AutoSize = true,
                Location = new Point(0, yPos)
            };
            _rightPanel.Controls.Add(lblTitle);
            yPos += 40;

            // LP Selection group
            _gbLPSelection = new GroupBox
            {
                Text = "Select LPs",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Location = new Point(0, yPos),
                Size = new Size(700, 140),
                ForeColor = ColorTranslator.FromHtml("#495057")
            };

            // GFI Aggregator (expandable)
            _chkGFI = new CheckBox
            {
                Text = "☐ GFI Aggregator (6 LPs)",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(16, 25),
                Size = new Size(250, 24),
                Checked = false
            };
            _chkGFI.CheckedChanged += ChkLP_CheckedChanged;
            _gbLPSelection.Controls.Add(_chkGFI);

            // Direct LPs
            _pnlDirectLPs = new Panel
            {
                Location = new Point(16, 55),
                Size = new Size(680, 70),
                AutoScroll = false
            };

            int xPos = 0;
            foreach (var lp in _directLPs.Keys)
            {
                var chkLP = new CheckBox
                {
                    Text = lp,
                    Font = new Font("Segoe UI", 10F),
                    Location = new Point(xPos, 0),
                    Size = new Size(160, 24),
                    Checked = true,
                    Tag = lp
                };
                chkLP.CheckedChanged += ChkLP_CheckedChanged;
                _pnlDirectLPs.Controls.Add(chkLP);
                xPos += 170;

                // Start new row after 4 checkboxes
                if (xPos > 680)
                {
                    xPos = 0;
                }
            }

            _gbLPSelection.Controls.Add(_pnlDirectLPs);

            _rightPanel.Controls.Add(_gbLPSelection);
            yPos += 150;

            // Request Quotes button
            _btnRequestQuotes = new Button
            {
                Text = "REQUEST QUOTES",
                Location = new Point(0, yPos),
                Size = new Size(700, 45),
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = ColorTranslator.FromHtml("#28A745"),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            _btnRequestQuotes.FlatAppearance.BorderSize = 0;
            _btnRequestQuotes.Click += BtnRequestQuotes_Click;
            _rightPanel.Controls.Add(_btnRequestQuotes);
            yPos += 55;

            // Best Prices section (Aggregated best bid and best offer)
            InitializeBestPricesSection(ref yPos);

            // Pricing tiles panel (scrollable)
            _pnlPricingTiles = new Panel
            {
                Location = new Point(0, yPos),
                Size = new Size(720, 400),
                AutoScroll = true,
                BorderStyle = BorderStyle.None,
                BackColor = ColorTranslator.FromHtml("#F8F9FA")
            };

            _flowPricingTiles = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8)
            };

            _pnlPricingTiles.Controls.Add(_flowPricingTiles);
            _rightPanel.Controls.Add(_pnlPricingTiles);

            // Initialize pricing tiles (empty initially)
            UpdatePricingTiles();
        }

        private void InitializeBestPricesSection(ref int yPos)
        {
            // Panel container for best prices
            _pnlBestPrices = new Panel
            {
                Location = new Point(0, yPos),
                Size = new Size(700, 160),
                BackColor = ColorTranslator.FromHtml("#F8F9FA"),
                BorderStyle = BorderStyle.None,
                Padding = new Padding(8)
            };

            // Section title
            var lblBestPricesTitle = new Label
            {
                Text = "BEST PRICES",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = ColorTranslator.FromHtml("#495057"),
                Location = new Point(8, 8),
                AutoSize = true
            };
            _pnlBestPrices.Controls.Add(lblBestPricesTitle);

            // BEST BID tile (left)
            _pnlBestBid = new Panel
            {
                Location = new Point(8, 35),
                Size = new Size(338, 115),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblBestBidTitle = new Label
            {
                Text = "BEST BID - YOU RECEIVE",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = ColorTranslator.FromHtml("#28A745"), // Green
                Location = new Point(12, 8),
                AutoSize = true
            };

            _lblBestBidValue = new Label
            {
                Text = "---",
                Font = new Font("Segoe UI", 36F, FontStyle.Bold), // Very large!
                ForeColor = ColorTranslator.FromHtml("#212529"),
                Location = new Point(12, 35),
                Size = new Size(310, 50),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _lblBestBidDetails = new Label
            {
                Text = "Vol: -- | Pips: --",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = ColorTranslator.FromHtml("#6C757D"),
                Location = new Point(12, 87),
                AutoSize = true
            };

            _lblBestBidSource = new Label
            {
                Text = "Source: --",
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = ColorTranslator.FromHtml("#6C757D"),
                Location = new Point(12, 100),
                AutoSize = true
            };

            _pnlBestBid.Controls.Add(lblBestBidTitle);
            _pnlBestBid.Controls.Add(_lblBestBidValue);
            _pnlBestBid.Controls.Add(_lblBestBidDetails);
            _pnlBestBid.Controls.Add(_lblBestBidSource);

            // BEST OFFER tile (right)
            _pnlBestOffer = new Panel
            {
                Location = new Point(354, 35),
                Size = new Size(338, 115),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblBestOfferTitle = new Label
            {
                Text = "BEST OFFER - YOU PAY",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = ColorTranslator.FromHtml("#DC3545"), // Red
                Location = new Point(12, 8),
                AutoSize = true
            };

            _lblBestOfferValue = new Label
            {
                Text = "---",
                Font = new Font("Segoe UI", 36F, FontStyle.Bold), // Very large!
                ForeColor = ColorTranslator.FromHtml("#212529"),
                Location = new Point(12, 35),
                Size = new Size(310, 50),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _lblBestOfferDetails = new Label
            {
                Text = "Vol: -- | Pips: --",
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = ColorTranslator.FromHtml("#6C757D"),
                Location = new Point(12, 87),
                AutoSize = true
            };

            _lblBestOfferSource = new Label
            {
                Text = "Source: --",
                Font = new Font("Segoe UI", 8F, FontStyle.Italic),
                ForeColor = ColorTranslator.FromHtml("#6C757D"),
                Location = new Point(12, 100),
                AutoSize = true
            };

            _pnlBestOffer.Controls.Add(lblBestOfferTitle);
            _pnlBestOffer.Controls.Add(_lblBestOfferValue);
            _pnlBestOffer.Controls.Add(_lblBestOfferDetails);
            _pnlBestOffer.Controls.Add(_lblBestOfferSource);

            _pnlBestPrices.Controls.Add(_pnlBestBid);
            _pnlBestPrices.Controls.Add(_pnlBestOffer);

            _rightPanel.Controls.Add(_pnlBestPrices);
            yPos += 170; // Space for best prices section
        }

        private Label CreateDataLabel(string label, string value, int yPos)
        {
            var lbl = new Label
            {
                Text = $"{label} {value}",
                Font = new Font("Segoe UI", 10F),
                ForeColor = ColorTranslator.FromHtml("#212529"),
                Location = new Point(16, yPos),
                AutoSize = true
            };
            return lbl;
        }

        private void PopulateTradeDetails()
        {
            // Populate legs grid
            _dgvLegs.Rows.Clear();
            for (int i = 0; i < _trade.Legs.Count; i++)
            {
                var leg = _trade.Legs[i];
                _dgvLegs.Rows.Add(
                    $"Leg {i + 1}",
                    leg.Direction == "BUY" ? "B" : "S",
                    leg.OptionType,
                    leg.Strike.ToString("F4"),
                    _trade.Expiry.ToString("dd-MMM-yy"),
                    $"{leg.NotionalMM:F1}M {GetCcy1()}"
                );
            }

            // TODO: Fetch market data
            _lblSpot.Text = "Spot: Loading...";
            _lblForward.Text = "Forward: Loading...";
            _lblDepo1.Text = $"{GetCcy1()} Depo: Loading...";
            _lblDepo2.Text = $"{GetCcy2()} Depo: Loading...";
        }

        private void ChkLP_CheckedChanged(object sender, EventArgs e)
        {
            UpdateSelectedLPs();
            UpdatePricingTiles();
        }

        private void UpdateSelectedLPs()
        {
            _selectedLPs.Clear();

            if (_chkGFI.Checked)
            {
                _selectedLPs.Add("GFI Aggregator");
            }

            foreach (Control ctrl in _pnlDirectLPs.Controls)
            {
                if (ctrl is CheckBox chk && chk.Checked)
                {
                    _selectedLPs.Add(chk.Tag.ToString());
                }
            }
        }

        private void UpdatePricingTiles()
        {
            _flowPricingTiles.Controls.Clear();
            _lpRows.Clear();

            foreach (var lpName in _selectedLPs)
            {
                string lpType = lpName == "GFI Aggregator" ? "GFI" : "DIRECT";
                var row = new LPQuoteRow(lpName, lpType);

                // Wire up action events
                row.BidActionClicked += (s, e) => OnBidAction(lpName);
                row.AskActionClicked += (s, e) => OnAskAction(lpName);

                _lpRows[lpName] = row;
                _flowPricingTiles.Controls.Add(row);

                // Show empty tiles initially
                row.ClearQuotes();
            }
        }

        private void BtnToggleDisplay_Click(object sender, EventArgs e)
        {
            // Cycle through display modes
            _displayMode = _displayMode switch
            {
                "Vol" => "Premium",
                "Premium" => "Pips",
                "Pips" => "Vol",
                _ => "Premium"
            };

            _btnToggleDisplay.Text = _displayMode;

            // Update all tiles
            RefreshPricingTiles();
        }

        private void BtnRequestQuotes_Click(object sender, EventArgs e)
        {
            // TODO: Send quote requests to all selected LPs
            MessageBox.Show($"Requesting quotes from {_selectedLPs.Count} LPs:\n{string.Join(", ", _selectedLPs)}",
                            "Quote Request", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Simulate quote updates after delay
            var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (s, e2) =>
            {
                SimulateQuoteUpdate();
                timer.Stop();
            };
            timer.Start();
        }

        private void SimulateQuoteUpdate()
        {
            // Simulate quotes for testing
            Random rnd = new Random();

            foreach (var lpName in _selectedLPs)
            {
                if (_lpRows.ContainsKey(lpName))
                {
                    double bidVol = 4.5 + rnd.NextDouble() * 0.5;
                    double askVol = bidVol + 0.3 + rnd.NextDouble() * 0.2;

                    double bidPremium = 60000 + rnd.Next(5000);
                    double askPremium = bidPremium + 3000 + rnd.Next(2000);

                    double bidPips = bidPremium / 1000;
                    double askPips = askPremium / 1000;

                    // Store quote data
                    _quotes[lpName] = new QuoteData
                    {
                        LPName = lpName,
                        BidVol = bidVol,
                        BidPremium = bidPremium,
                        BidPips = bidPips,
                        AskVol = askVol,
                        AskPremium = askPremium,
                        AskPips = askPips,
                        QuoteTime = DateTime.Now
                    };

                    // Update LP row (will be marked as best/not best in DetermineBestPrices)
                    _lpRows[lpName].UpdateQuotes(
                        bidVol, bidPremium, GetCcy1(), bidPips, $"Buy {_txtNotional.Text}M", _trade.Underlying, double.Parse(_txtNotional.Text), false,
                        askVol, askPremium, GetCcy1(), askPips, $"Sell {_txtNotional.Text}M", false,
                        _displayMode, DateTime.Now
                    );
                }
            }

            // Determine best bid/ask and update displays
            DetermineBestPrices();
        }

        private void DetermineBestPrices()
        {
            if (_quotes.Count == 0)
            {
                // No quotes yet - clear best prices
                _lblBestBidValue.Text = "---";
                _lblBestOfferValue.Text = "---";
                _lblBestBidDetails.Text = "Vol: -- | Pips: --";
                _lblBestOfferDetails.Text = "Vol: -- | Pips: --";
                _lblBestBidSource.Text = "Source: --";
                _lblBestOfferSource.Text = "Source: --";
                return;
            }

            // Find best bid (highest premium = you receive more)
            var bestBid = _quotes.Values.OrderByDescending(q => q.BidPremium).First();

            // Find best offer (lowest premium = you pay less)
            var bestOffer = _quotes.Values.OrderBy(q => q.AskPremium).First();

            // Update top best price tiles
            UpdateBestPriceTiles(bestBid, bestOffer);

            // Update individual LP rows with best indicators
            foreach (var lpName in _quotes.Keys)
            {
                if (_lpRows.ContainsKey(lpName))
                {
                    var quote = _quotes[lpName];
                    bool isBestBid = quote.LPName == bestBid.LPName;
                    bool isBestOffer = quote.LPName == bestOffer.LPName;

                    _lpRows[lpName].UpdateQuotes(
                        quote.BidVol, quote.BidPremium, GetCcy1(), quote.BidPips,
                        $"Buy {_txtNotional.Text}M", _trade.Underlying, double.Parse(_txtNotional.Text), isBestBid,
                        quote.AskVol, quote.AskPremium, GetCcy1(), quote.AskPips,
                        $"Sell {_txtNotional.Text}M", isBestOffer,
                        _displayMode, quote.QuoteTime
                    );
                }
            }
        }

        private void UpdateBestPriceTiles(QuoteData bestBid, QuoteData bestOffer)
        {
            string ccy = GetCcy1();

            // Update BEST BID tile
            switch (_displayMode)
            {
                case "Vol":
                    _lblBestBidValue.Text = $"{bestBid.BidVol:F3}%";
                    _lblBestBidDetails.Text = $"Premium: {bestBid.BidPremium:N0} {ccy} | Pips: {bestBid.BidPips:F3}";
                    break;
                case "Premium":
                    _lblBestBidValue.Text = $"{bestBid.BidPremium:N0}";
                    _lblBestBidDetails.Text = $"Vol: {bestBid.BidVol:F3}% | Pips: {bestBid.BidPips:F3}";
                    break;
                case "Pips":
                    _lblBestBidValue.Text = $"{bestBid.BidPips:F3}";
                    _lblBestBidDetails.Text = $"Vol: {bestBid.BidVol:F3}% | Premium: {bestBid.BidPremium:N0} {ccy}";
                    break;
            }
            _lblBestBidSource.Text = $"Source: {bestBid.LPName}";

            // Update BEST OFFER tile
            switch (_displayMode)
            {
                case "Vol":
                    _lblBestOfferValue.Text = $"{bestOffer.AskVol:F3}%";
                    _lblBestOfferDetails.Text = $"Premium: {bestOffer.AskPremium:N0} {ccy} | Pips: {bestOffer.AskPips:F3}";
                    break;
                case "Premium":
                    _lblBestOfferValue.Text = $"{bestOffer.AskPremium:N0}";
                    _lblBestOfferDetails.Text = $"Vol: {bestOffer.AskVol:F3}% | Pips: {bestOffer.AskPips:F3}";
                    break;
                case "Pips":
                    _lblBestOfferValue.Text = $"{bestOffer.AskPips:F3}";
                    _lblBestOfferDetails.Text = $"Vol: {bestOffer.AskVol:F3}% | Premium: {bestOffer.AskPremium:N0} {ccy}";
                    break;
            }
            _lblBestOfferSource.Text = $"Source: {bestOffer.LPName}";

            // Add currency label for Premium display mode
            if (_displayMode == "Premium")
            {
                _lblBestBidValue.Text += $" {ccy}";
                _lblBestOfferValue.Text += $" {ccy}";
            }
        }

        private void RefreshPricingTiles()
        {
            // Re-render all tiles with new display mode
            if (_quotes.Count > 0)
            {
                DetermineBestPrices(); // This will update both best tiles and individual rows
            }
        }

        private void OnBidAction(string lpName)
        {
            MessageBox.Show($"HIT BID from {lpName}", "Execute", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnAskAction(string lpName)
        {
            MessageBox.Show($"LIFT OFFER from {lpName}", "Execute", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private string GetStrategyName(string structureType)
        {
            return structureType switch
            {
                "1" => "Vanilla",
                "5" => "Risk Reversal",
                "8" => "Call Spread",
                "9" => "Put Spread",
                "10" => "Seagull",
                _ => "Custom"
            };
        }

        private string GetCcy1()
        {
            return _trade?.Underlying?.Length >= 3 ? _trade.Underlying.Substring(0, 3) : "EUR";
        }

        private string GetCcy2()
        {
            return _trade?.Underlying?.Length >= 6 ? _trade.Underlying.Substring(3, 3) : "USD";
        }
    }
}
