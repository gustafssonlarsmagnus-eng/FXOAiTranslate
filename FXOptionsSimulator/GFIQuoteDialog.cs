using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FXOptionsSimulator;
using FXOptionsSimulator.FIX;

namespace FXOAiTranslator
{
    public partial class GFIQuoteDialog : Form
    {
        // WinAPI for preventing flicker during grid updates
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int wMsg, bool wParam, int lParam);
        private const int WM_SETREDRAW = 0x000B;

        private GFIFIXSessionManager _fixSession;  // Changed from FIXSimulator
        private TradeStructure _trade;
        private string _groupId;
        private System.Windows.Forms.Timer _quoteTimer;
        private System.Windows.Forms.Timer _countdownTimer;  // NEW: For quote expiry countdown
        private DataGridView dgvQuotes;
        private DataGridView dgvLegs;
        private DataGridView dgvBlotter;  // NEW: Trade blotter grid
        private Button btnRequestQuotes;
        private Button btnExecute;
        private Button btnCancel;
        private Button btnBuy;
        private Label lblTradeSummary;
        private GroupBox gbLPs;
        private CheckBox chkSOCGEN;
        private CheckBox chkCIBC;
        private CheckBox chkMS;
        private CheckBox chkHSBC;
        private CheckBox chkNATWEST;
        private CheckBox chkSCBL;
        private CheckBox chkNOMURA;
        private CheckBox chkBAML;
        private CheckBox chkBNP;
        private CheckBox chkDeut;  // Testing only
        private int _selectedLegCount;
        private bool _showVolatility = true;  // NEW: Toggle between Vol (true) and Premium (false)
        private bool _spotHedge = true;  // NEW: Spot hedge toggle (default ON)
        private string _cutoff = "NY";  // NEW: Cutoff toggle (default NY)
        private HashSet<int> _passedTests = new HashSet<int>();  // Track which tests passed
        private List<CheckBox> _testCaseCheckboxes = new List<CheckBox>();  // All test case checkboxes
        private int _currentTestId = -1;  // Currently active test case
        private string _currentGroupId = null;  // Current test group ID for tracking

        public GFIQuoteDialog(dynamic ovmlResult)
        {
            InitializeComponent();
            InitializeCustomComponents();

            _trade = OVMLBridge.ConvertToTradeStructure(ovmlResult);
            _fixSession = GlobalFIXSession.Instance;  // Changed

            Console.WriteLine($"\n=== TRADE STRUCTURE DEBUG ===");
            Console.WriteLine($"StructureType: {_trade.StructureType}");
            Console.WriteLine($"Underlying: {_trade.Underlying}");
            Console.WriteLine($"Leg Count: {_trade.Legs.Count}");

            for (int i = 0; i < _trade.Legs.Count; i++)
            {
                var leg = _trade.Legs[i];
                Console.WriteLine($"\nLeg {i}:");
                Console.WriteLine($"  Direction: {leg.Direction}");
                Console.WriteLine($"  OptionType: {leg.OptionType}");
                Console.WriteLine($"  Strike: {leg.Strike}");
                Console.WriteLine($"  NotionalMM: {leg.NotionalMM}");
                Console.WriteLine($"  Tenor: {leg.Tenor}");
            }
            Console.WriteLine($"=========================\n");

            lblTradeSummary.Text = $"{_trade.StructureType}: {_trade.Underlying} - {_trade.Legs.Count} legs";
            PopulateLegGrid();

            // Subscribe to quote events
            _fixSession.Application.OnQuoteReceived += OnQuoteReceivedFromFIX;
        }

        private void InitializeCustomComponents()
        {
            this.Text = "GFI Fenics - Request Quotes";
            this.Size = new Size(1350, 840);  // Increased width for test case panel
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            lblTradeSummary = new Label
            {
                Location = new Point(20, 20),
                Size = new Size(940, 25),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Text = "Loading trade..."
            };
            this.Controls.Add(lblTradeSummary);

            var lblLegs = new Label
            {
                Text = "Select Legs & Edit Notionals:",
                Location = new Point(20, 50),
                Size = new Size(200, 20),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            this.Controls.Add(lblLegs);

            // GroupBox for legs grid
            var gbLegs = new GroupBox
            {
                Text = "",
                Location = new Point(20, 65),
                Size = new Size(940, 135)
            };
            this.Controls.Add(gbLegs);

            dgvLegs = new DataGridView
            {
                Location = new Point(10, 15),
                Size = new Size(920, 105),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                RowHeadersVisible = false
            };
            gbLegs.Controls.Add(dgvLegs);

            var chkCol = new DataGridViewCheckBoxColumn
            {
                Name = "Include",
                HeaderText = "Include",
                Width = 35,
                TrueValue = true,
                FalseValue = false
            };
            dgvLegs.Columns.Add(chkCol);
            dgvLegs.Columns.Add("Leg", "Leg");
            dgvLegs.Columns["Leg"].Width = 35;
            dgvLegs.Columns.Add("Direction", "Direction");
            dgvLegs.Columns["Direction"].Width = 35;
            dgvLegs.Columns.Add("Type", "Type");
            dgvLegs.Columns["Type"].Width = 35;
            dgvLegs.Columns.Add("Strike", "Strike");
            dgvLegs.Columns["Strike"].Width = 60;
            dgvLegs.Columns.Add("Expiry", "Expiry");
            dgvLegs.Columns["Expiry"].Width = 150;
            dgvLegs.Columns.Add("SettlementDate", "Settlement Date");
            dgvLegs.Columns["SettlementDate"].Width = 90;
            dgvLegs.Columns.Add("PremiumDate", "Premium Date");
            dgvLegs.Columns["PremiumDate"].Width = 90;

            var notionalCol = new DataGridViewTextBoxColumn
            {
                Name = "NotionalMM",
                HeaderText = "Notional (MM)",
                Width = 70
            };
            dgvLegs.Columns.Add(notionalCol);

            // LP Selection GroupBox - EXPANDED
            gbLPs = new GroupBox
            {
                Text = "Select Liquidity Providers",
                Location = new Point(20, 210),
                Size = new Size(940, 90)  // Increased height for 2 rows
            };
            this.Controls.Add(gbLPs);

            // Row 1 - Configured LPs (matching FenicsConfig)
            chkSOCGEN = new CheckBox
            {
                Text = "Societe Generale",
                Location = new Point(20, 25),
                Size = new Size(160, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkSOCGEN);

            chkCIBC = new CheckBox
            {
                Text = "CIBC",
                Location = new Point(190, 25),
                Size = new Size(130, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkCIBC);

            chkMS = new CheckBox
            {
                Text = "Morgan Stanley",
                Location = new Point(330, 25),
                Size = new Size(150, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkMS);

            chkHSBC = new CheckBox
            {
                Text = "HSBC",
                Location = new Point(490, 25),
                Size = new Size(120, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkHSBC);

            chkNATWEST = new CheckBox
            {
                Text = "NatWest Markets",
                Location = new Point(620, 25),
                Size = new Size(150, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkNATWEST);

            // Row 2 - Additional Configured LPs
            chkSCBL = new CheckBox
            {
                Text = "Standard Chartered",
                Location = new Point(20, 55),
                Size = new Size(160, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkSCBL);

            chkNOMURA = new CheckBox
            {
                Text = "Nomura",
                Location = new Point(190, 55),
                Size = new Size(130, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkNOMURA);

            chkBAML = new CheckBox
            {
                Text = "Bank of America",
                Location = new Point(330, 55),
                Size = new Size(150, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkBAML);

            chkBNP = new CheckBox
            {
                Text = "BNP Paribas",
                Location = new Point(490, 55),
                Size = new Size(120, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkBNP);

            chkDeut = new CheckBox
            {
                Text = "Deutsche Bank (Test)",
                Location = new Point(620, 55),
                Size = new Size(150, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkDeut);

            // Toggle button for Vol/Premium pricing - NEW
            var btnToggleDisplay = new Button
            {
                Text = "Show: Volatility",
                Location = new Point(20, 305),  // Increased from 293 to 305 for more spacing
                Size = new Size(130, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.LightBlue,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnToggleDisplay.FlatAppearance.BorderColor = Color.DodgerBlue;
            btnToggleDisplay.Click += (s, e) =>
            {
                _showVolatility = !_showVolatility;
                btnToggleDisplay.Text = _showVolatility ? "Show: Volatility" : "Show: Premium";
                btnToggleDisplay.BackColor = _showVolatility ? Color.LightBlue : Color.LightGreen;

                // Rebuild quote grid with new columns
                if (_selectedLegCount > 0)
                {
                    SetupQuoteGrid(_selectedLegCount);
                    UpdateQuoteDisplay(); // Refresh data
                }
            };
            this.Controls.Add(btnToggleDisplay);

            // Toggle button for Spot Hedge - NEW
            var btnToggleHedge = new Button
            {
                Text = "Hedge: ON",
                Location = new Point(160, 305),
                Size = new Size(110, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.LightGreen,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnToggleHedge.FlatAppearance.BorderColor = Color.Green;
            btnToggleHedge.Click += (s, e) =>
            {
                _spotHedge = !_spotHedge;
                btnToggleHedge.Text = _spotHedge ? "Hedge: ON" : "Hedge: OFF";
                btnToggleHedge.BackColor = _spotHedge ? Color.LightGreen : Color.LightCoral;
                btnToggleHedge.FlatAppearance.BorderColor = _spotHedge ? Color.Green : Color.Red;
            };
            this.Controls.Add(btnToggleHedge);

            // Toggle button for Cutoff - NEW
            var btnToggleCut = new Button
            {
                Text = "Cut: NY",
                Location = new Point(280, 305),
                Size = new Size(100, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.LightSkyBlue,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnToggleCut.FlatAppearance.BorderColor = Color.DodgerBlue;
            btnToggleCut.Click += (s, e) =>
            {
                _cutoff = (_cutoff == "NY") ? "TKY" : "NY";
                btnToggleCut.Text = $"Cut: {_cutoff}";
                btnToggleCut.BackColor = (_cutoff == "NY") ? Color.LightSkyBlue : Color.LightSalmon;
            };
            this.Controls.Add(btnToggleCut);

            // Test Case Checklist Panel - NEW
            var gbTestCases = new GroupBox
            {
                Text = "GFI Test Protocol",
                Location = new Point(980, 20),
                Size = new Size(340, 780),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            this.Controls.Add(gbTestCases);

            var pnlTestCases = new Panel
            {
                Location = new Point(10, 25),
                Size = new Size(320, 745),
                AutoScroll = true
            };
            gbTestCases.Controls.Add(pnlTestCases);

            // Add all test cases from official GFI FX Options Test Protocol with full details
            var testCases = new[]
            {
                // Format: "ID: Cut Pair Tenor Dir Type @ Strike (Notional) Mode"
                // Vanilla tests (1-15)
                "1: NY EURUSD 1M Buy Call @ 1.16 (10M EUR) PREM",
                "2: TKY USDJPY 10Dec26 Buy Put @ 155 (10M USD) PREM",
                "3: TKY USDJPY 08Nov26 Sell Call @ 7.15 (10M USD) PREM",
                "4: NY EURSEK 1M Sell Put @ 7.16 (10M SEK) PREM",
                "5: NY USDNOK 08Nov26 Buy Call @ 11.9 (10M USD) PREM",
                "6: NY EURNOK 6M Buy Put @ 7.807 (10M NOK) PREM",
                "7: TKY AUDUSD 9M Sell Call @ 0.69 (10M AUD) PREM Fwd",
                "8: NY AUDUSD 1Y Sell Put @ 0.65 (10M USD) PREM Fwd",
                "9: TKY USDJPY 08Mar26 Buy Call @ 147 (10M USD) PREM",
                "10: NY EURUSD 3M Buy Put @ 1.15 (10M USD) PREM",
                "11: TKY EURUSD 08Nov26 Sell Call @ 1.16 (10M EUR) PREM Fwd",
                "12: NY USDJPY 3M Sell Put @ 155 (10M JPY) PREM Fwd",
                "13: NY EURSEK 07Nov26 Buy Call @ 11.1 (10M EUR) PREM +Spot",
                "14: TKY GBPUSD 3M Buy Call @ 1.32 (10M GBP) PREM +Fwd",
                "15: NY EURCHF 6M Buy Put @ 0.93 (10M EUR) PREM +Fwd",

                // Structure tests (16-26)
                "16: NY GBPUSD 1M Call Spread @ 1.30 (10M GBP) PREM",
                "17: NY GBPUSD 2M Put Spread @ 1.30 (10M GBP) PREM",
                "18: NY USDJPY 3M RR (S Put, B Call) @ 150 (10M USD)",
                "19: NY USDJPY 6M RR (B Put, S Call) @ 150 (10M USD)",
                "20: NY EURSEK 1M Straddle @ 11.3 (10M SEK) PREM",
                "21: NY EURSEK 2M Strangle @ 11.3 (10M SEK) PREM",
                "22: NY USDSEK 3M Seagull (B Put, S Put, S Call) @ 10.3",
                "23: NY USDSEK 6M Seagull (B Call, S Call, S Put) @ 10.3",
                "24: NY EURNOK 1M Collar (B Call, S Call, S Put) @ 11.5",
                "25: TKY USDJPY VOL 1M Buy Call @ 155 (10M JPY) VOL",
                "26: CNH USDCNH 4M Sell Put @ 7.10 (10M USD) PREM"
            };

            int yPos = 5;
            int testIndex = 0;
            foreach (var testCase in testCases)
            {
                var chk = new CheckBox
                {
                    Text = testCase,
                    Location = new Point(5, yPos),
                    Size = new Size(295, 20),
                    Font = new Font("Segoe UI", 8, FontStyle.Regular),
                    Tag = testIndex + 1  // Store test ID in Tag
                };

                // Auto-send quote request when checked
                chk.CheckedChanged += TestCase_CheckedChanged;

                // Right-click to mark as passed
                chk.MouseClick += (s, e) =>
                {
                    if (e.Button == MouseButtons.Right)
                    {
                        var checkbox = (CheckBox)s;
                        int testId = (int)checkbox.Tag;

                        if (_passedTests.Contains(testId))
                        {
                            _passedTests.Remove(testId);
                            checkbox.BackColor = Color.Transparent;
                        }
                        else
                        {
                            _passedTests.Add(testId);
                            checkbox.BackColor = Color.LightGreen;
                        }
                    }
                };

                _testCaseCheckboxes.Add(chk);  // Store reference for single-selection logic
                pnlTestCases.Controls.Add(chk);
                yPos += 25;
                testIndex++;
            }

            // Subscribe to trade blotter events for auto-marking successful tests
            TradeBlotter.Instance.OnTradeUpdated += OnTradeStatusChanged;

            // Quotes Grid - reduced size to make room for blotter
            dgvQuotes = new DataGridView
            {
                Location = new Point(20, 335),  // Increased from 320 to 335 to match button spacing
                Size = new Size(940, 200),      // Reduced from 270
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                RowHeadersVisible = false
            };

            // Enable double buffering to reduce flicker (via reflection since DoubleBuffered is protected)
            typeof(DataGridView).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty,
                null, dgvQuotes, new object[] { true });

            // Use CellFormatting event for dynamic styling (reduces flicker)
            dgvQuotes.CellFormatting += DgvQuotes_CellFormatting;

            this.Controls.Add(dgvQuotes);

            // Trade Blotter Grid - NEW
            var lblBlotter = new Label
            {
                Text = "Trade Blotter:",
                Location = new Point(20, 550),  // Increased from 530 for more spacing
                Size = new Size(200, 20),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            this.Controls.Add(lblBlotter);

            dgvBlotter = new DataGridView
            {
                Location = new Point(20, 575),  // Increased from 555 for more spacing
                Size = new Size(940, 135),      // Fixed size, horizontal scroll enabled
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,  // Fixed widths for scrolling
                ScrollBars = ScrollBars.Both,  // Enable horizontal scrolling
                RowHeadersVisible = false
            };

            // GFI-style comprehensive blotter columns
            // Identification & Status
            dgvBlotter.Columns.Add("AffirmedBy", "Affirmed By");
            dgvBlotter.Columns["AffirmedBy"].Width = 80;
            dgvBlotter.Columns.Add("TradeID", "Trade ID");
            dgvBlotter.Columns["TradeID"].Width = 180;
            dgvBlotter.Columns.Add("ExecTS", "EXEC TS");
            dgvBlotter.Columns["ExecTS"].Width = 80;
            dgvBlotter.Columns.Add("STPStatus", "STP Status");
            dgvBlotter.Columns["STPStatus"].Width = 90;

            // Party Information
            dgvBlotter.Columns.Add("MyBroker", "My Broker");
            dgvBlotter.Columns["MyBroker"].Width = 80;
            dgvBlotter.Columns.Add("MyTrader", "My Trader");
            dgvBlotter.Columns["MyTrader"].Width = 80;
            dgvBlotter.Columns.Add("MyCenter", "My Center");
            dgvBlotter.Columns["MyCenter"].Width = 70;
            dgvBlotter.Columns.Add("CCYPair", "CCY Pair");
            dgvBlotter.Columns["CCYPair"].Width = 80;
            dgvBlotter.Columns.Add("BuySell", "Buy/Sell");
            dgvBlotter.Columns["BuySell"].Width = 70;

            // Trade Details
            dgvBlotter.Columns.Add("Vol", "Vol");
            dgvBlotter.Columns["Vol"].Width = 60;
            dgvBlotter.Columns.Add("SizeM", "Size (M)");
            dgvBlotter.Columns["SizeM"].Width = 70;
            dgvBlotter.Columns.Add("Strike", "Strike");
            dgvBlotter.Columns["Strike"].Width = 70;
            dgvBlotter.Columns.Add("Delta", "Delta");
            dgvBlotter.Columns["Delta"].Width = 70;
            dgvBlotter.Columns.Add("Strategy", "Strategy");
            dgvBlotter.Columns["Strategy"].Width = 90;
            dgvBlotter.Columns.Add("Venue", "Venue");
            dgvBlotter.Columns["Venue"].Width = 70;

            // Dates
            dgvBlotter.Columns.Add("Expiry", "Expiry");
            dgvBlotter.Columns["Expiry"].Width = 80;
            dgvBlotter.Columns.Add("Delivery", "Delivery");
            dgvBlotter.Columns["Delivery"].Width = 80;

            // Pricing
            dgvBlotter.Columns.Add("Price", "Price");
            dgvBlotter.Columns["Price"].Width = 80;
            dgvBlotter.Columns.Add("Counterparty", "Counterparty");
            dgvBlotter.Columns["Counterparty"].Width = 100;
            dgvBlotter.Columns.Add("CtpyCenter", "Ctpy Center");
            dgvBlotter.Columns["CtpyCenter"].Width = 80;
            dgvBlotter.Columns.Add("Cut", "Cut");
            dgvBlotter.Columns["Cut"].Width = 50;
            dgvBlotter.Columns.Add("Spot", "Spot");
            dgvBlotter.Columns["Spot"].Width = 70;
            dgvBlotter.Columns.Add("Swap", "Swap");
            dgvBlotter.Columns["Swap"].Width = 60;
            dgvBlotter.Columns.Add("Depo", "Depo");
            dgvBlotter.Columns["Depo"].Width = 60;
            dgvBlotter.Columns.Add("Value", "Value");
            dgvBlotter.Columns["Value"].Width = 80;

            // Hedge Information
            dgvBlotter.Columns.Add("HedgeBS", "Hedge B/S");
            dgvBlotter.Columns["HedgeBS"].Width = 80;
            dgvBlotter.Columns.Add("HedgeAmt", "Hedge Amt");
            dgvBlotter.Columns["HedgeAmt"].Width = 80;
            dgvBlotter.Columns.Add("HedgeRate", "Hedge Rate");
            dgvBlotter.Columns["HedgeRate"].Width = 80;
            dgvBlotter.Columns.Add("HedgeDelDate", "Hedge Del Date");
            dgvBlotter.Columns["HedgeDelDate"].Width = 100;

            dgvBlotter.Columns.Add("TradeTime", "Trade Time");
            dgvBlotter.Columns["TradeTime"].Width = 80;

            this.Controls.Add(dgvBlotter);

            // Buttons - moved down for blotter
            btnRequestQuotes = new Button
            {
                Text = "Request Quotes",
                Location = new Point(20, 705),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            btnRequestQuotes.Click += BtnRequestQuotes_Click;
            this.Controls.Add(btnRequestQuotes);

            btnExecute = new Button
            {
                Text = "Sell (Hit Bid)",
                Location = new Point(190, 705),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Enabled = false
            };
            btnExecute.Click += (s, e) => BtnExecute_Click("SELL");
            this.Controls.Add(btnExecute);

            btnBuy = new Button
            {
                Text = "Buy (Lift Offer)",
                Location = new Point(360, 705),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Enabled = false
            };
            btnBuy.Click += (s, e) => BtnExecute_Click("BUY");
            this.Controls.Add(btnBuy);

            btnCancel = new Button
            {
                Text = "Close",
                Location = new Point(530, 705),
                Size = new Size(150, 35),
                DialogResult = DialogResult.Cancel
            };
            this.Controls.Add(btnCancel);
            this.CancelButton = btnCancel;

            // Subscribe to blotter events to update grid
            TradeBlotter.Instance.OnTradeAdded += OnTradeAddedToBlotter;
            TradeBlotter.Instance.OnTradeUpdated += OnTradeUpdatedInBlotter;

            // Countdown timer for quote expiry
            _countdownTimer = new System.Windows.Forms.Timer();
            _countdownTimer.Interval = 1000; // Update every second
            _countdownTimer.Tick += CountdownTimer_Tick;
        }

        private void PopulateLegGrid()
        {
            dgvLegs.Rows.Clear();

            // Calculate premium date ONCE for all legs (same for entire trade)
            // Premium Date = Trade Date (TODAY) + Spot Lag (T+2 for EURUSD)
            string premiumDateText = "-";
            try
            {
                var P = GlobalDatePolicy.Policy;
                string pair = _trade.Underlying;
                string premiumCcy = _trade.PremiumCurrency;

                var rules = new FxDateRules
                {
                    Ccy1 = pair.Substring(0, 3),
                    Ccy2 = pair.Substring(3, 3),
                    SpotLag = P.SpotLagForPair(pair),
                    ExpiryConvention = P.ExpiryConvention,
                    ExpiryEOM = P.ExpiryEOM,
                    PremiumSettleDays = P.PremiumSettleDays,
                    PremiumCalMode = P.PremiumCalendarMode,
                    PremiumConvention = P.PremiumConvention
                };

                var nowUtc = DateTime.UtcNow;
                // ComputeDates with "0D" tenor: spotDate = TODAY + spot lag
                var (_, spotDt, _, _, _) = FxDateService.ComputeDates(nowUtc, pair, "0D", premiumCcy, rules);

                var enUS = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                premiumDateText = spotDt.ToString("dd-MMM-yy", enUS);

                Console.WriteLine($"[PopulateLegGrid] Premium Date (spot from today): {premiumDateText}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PopulateLegGrid] Premium date calculation error: {ex.Message}");
            }

            for (int i = 0; i < _trade.Legs.Count; i++)
            {
                var leg = _trade.Legs[i];
                string expiryText;
                string settlementDateText = "-";

                if (leg.ExpiryDate != DateTime.MinValue)
                {
                    // Format: "30-Dec-25, Tue (1M)" in English - force en-US culture
                    var enUS = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                    string dateStr = leg.ExpiryDate.ToString("dd-MMM-yy, ddd", enUS);
                    expiryText = !string.IsNullOrEmpty(leg.Tenor)
                        ? $"{dateStr} ({leg.Tenor})"
                        : dateStr;

                    // Settlement Date = leg.DeliveryDate (already calculated correctly)
                    if (leg.DeliveryDate != DateTime.MinValue)
                    {
                        settlementDateText = leg.DeliveryDate.ToString("dd-MMM-yy", enUS);
                    }
                    else
                    {
                        Console.WriteLine($"[PopulateLegGrid] WARNING: Leg {i+1} has DeliveryDate = MinValue!");
                    }

                    Console.WriteLine($"[PopulateLegGrid] Leg {i+1}:");
                    Console.WriteLine($"  ExpiryDate: {leg.ExpiryDate.ToString("dd-MMM-yy")}");
                    Console.WriteLine($"  DeliveryDate: {(leg.DeliveryDate != DateTime.MinValue ? leg.DeliveryDate.ToString("dd-MMM-yy") : "NOT SET")}");
                    Console.WriteLine($"  Settlement Date (display): {settlementDateText}");
                    Console.WriteLine($"  Premium Date (display): {premiumDateText}");
                }
                else
                {
                    // Fallback to just tenor if no date calculated
                    expiryText = !string.IsNullOrEmpty(leg.Tenor) ? $"({leg.Tenor})" : "N/A";
                }

                dgvLegs.Rows.Add(
                    true,
                    $"Leg {i + 1}",
                    leg.Direction,
                    leg.OptionType,
                    leg.Strike.ToString("F4"),
                    expiryText,
                    settlementDateText,
                    premiumDateText,
                    leg.NotionalMM.ToString("F1")
                );
            }
        }

        private void SetupQuoteGrid(int legCount)
        {
            dgvQuotes.Columns.Clear();
            _selectedLegCount = legCount;

            dgvQuotes.Columns.Add("LP", "LP");
            dgvQuotes.Columns["LP"].Width = 80;

            dgvQuotes.Columns.Add("NetPremBid", "Net Prem (Bid)");
            dgvQuotes.Columns["NetPremBid"].DefaultCellStyle.Format = "N2";
            dgvQuotes.Columns["NetPremBid"].Width = 100;

            dgvQuotes.Columns.Add("NetPremOffer", "Net Prem (Offer)");
            dgvQuotes.Columns["NetPremOffer"].DefaultCellStyle.Format = "N2";
            dgvQuotes.Columns["NetPremOffer"].Width = 110;

            // Add leg columns based on display mode (Vol or Premium)
            for (int i = 1; i <= legCount; i++)
            {
                if (_showVolatility)
                {
                    // Volatility mode
                    dgvQuotes.Columns.Add($"Leg{i}BidVol", $"L{i} Bid Vol");
                    dgvQuotes.Columns[$"Leg{i}BidVol"].DefaultCellStyle.Format = "N2";
                    dgvQuotes.Columns[$"Leg{i}BidVol"].Width = 80;

                    dgvQuotes.Columns.Add($"Leg{i}OfferVol", $"L{i} Offer Vol");
                    dgvQuotes.Columns[$"Leg{i}OfferVol"].DefaultCellStyle.Format = "N2";
                    dgvQuotes.Columns[$"Leg{i}OfferVol"].Width = 90;
                }
                else
                {
                    // Premium mode
                    dgvQuotes.Columns.Add($"Leg{i}BidPrem", $"L{i} Bid Prem");
                    dgvQuotes.Columns[$"Leg{i}BidPrem"].DefaultCellStyle.Format = "N2";
                    dgvQuotes.Columns[$"Leg{i}BidPrem"].Width = 90;

                    dgvQuotes.Columns.Add($"Leg{i}OfferPrem", $"L{i} Offer Prem");
                    dgvQuotes.Columns[$"Leg{i}OfferPrem"].DefaultCellStyle.Format = "N2";
                    dgvQuotes.Columns[$"Leg{i}OfferPrem"].Width = 100;
                }
            }

            dgvQuotes.Columns.Add("LastUpdate", "Last Update");
            dgvQuotes.Columns["LastUpdate"].Width = 80;

            dgvQuotes.Columns.Add("TTL", "Expires In");
            dgvQuotes.Columns["TTL"].Width = 90;
            dgvQuotes.Columns.Add("ValidUntilTime", "ValidUntilTime");  // Hidden column to store expiry time
            dgvQuotes.Columns["ValidUntilTime"].Visible = false;
        }

        private void BtnRequestQuotes_Click(object sender, EventArgs e)
        {
            var lps = new List<string>();

            // Check all LP checkboxes (matching FenicsConfig)
            if (chkSOCGEN.Checked) lps.Add("SOCGEN");
            if (chkCIBC.Checked) lps.Add("CIBC");
            if (chkMS.Checked) lps.Add("MS");
            if (chkHSBC.Checked) lps.Add("HSBC");
            if (chkNATWEST.Checked) lps.Add("NATWEST");
            if (chkSCBL.Checked) lps.Add("SCBL");
            if (chkNOMURA.Checked) lps.Add("NOMURA");
            if (chkBAML.Checked) lps.Add("BAML");
            if (chkBNP.Checked) lps.Add("BNP");
            if (chkDeut.Checked) lps.Add("DEUT");  // Testing only

            if (lps.Count == 0)
            {
                MessageBox.Show("Please select at least one LP", "No LPs Selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool anyLegSelected = false;
            int selectedLegCount = 0;
            for (int i = 0; i < dgvLegs.Rows.Count; i++)
            {
                if ((bool)dgvLegs.Rows[i].Cells["Include"].Value)
                {
                    anyLegSelected = true;
                    selectedLegCount++;
                }
            }

            if (!anyLegSelected)
            {
                MessageBox.Show("Please select at least one leg", "No Legs Selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetupQuoteGrid(selectedLegCount);
            UpdateTradeFromGrid();

            // Update cutoff for all legs based on toggle
            foreach (var leg in _trade.Legs)
            {
                leg.Cutoff = _cutoff;
            }

            // Generate group ID
            _groupId = $"3-REQ{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            // ====== ENHANCED DEBUG OUTPUT ======
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("\n╔════════════════════════════════════════════════════════════════");
            Console.WriteLine("║ 📤 REQUESTING QUOTES FROM LPs");
            Console.WriteLine("╠════════════════════════════════════════════════════════════════");
            Console.WriteLine($"║ Group ID: {_groupId}");
            Console.WriteLine($"║ Underlying: {_trade.Underlying}");
            Console.WriteLine($"║ Structure: {_trade.StructureType}");
            Console.WriteLine($"║ Cutoff: {_cutoff}");
            Console.WriteLine($"║ Spot Hedge: {(_spotHedge ? "ON" : "OFF")}");
            Console.WriteLine($"║ Selected LPs ({lps.Count}): {string.Join(", ", lps)}");
            Console.WriteLine("╠════════════════════════════════════════════════════════════════");
            Console.WriteLine($"║ LEGS ({selectedLegCount}):");
            for (int i = 0; i < _trade.Legs.Count; i++)
            {
                var leg = _trade.Legs[i];
                Console.WriteLine($"║   Leg {i + 1}: {leg.Direction} {leg.NotionalMM}MM {leg.OptionType} @ {leg.Strike} exp:{leg.Tenor}");
            }
            Console.WriteLine("╚════════════════════════════════════════════════════════════════");
            Console.ResetColor();

            // Track which LPs successfully received requests
            var successfulLPs = new List<string>();
            var failedLPs = new List<string>();

            // Send quote request to each LP
            foreach (var lp in lps)
            {
                try
                {
                    string quoteReqID = _fixSession.SendQuoteRequest(_trade, lp, _groupId, _spotHedge);
                    successfulLPs.Add(lp);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✓ Quote request sent to {lp,-15} | QuoteReqID: {quoteReqID}");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    failedLPs.Add(lp);

                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"✗ Failed to send to {lp,-15} | Error: {ex.Message}");
                    Console.ResetColor();
                }
            }

            // Summary
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n┌─────────────────────────────────────────────┐");
            Console.WriteLine($"│ QUOTE REQUEST SUMMARY");
            Console.WriteLine($"├─────────────────────────────────────────────┤");
            Console.WriteLine($"│ Successful: {successfulLPs.Count}/{lps.Count}  {string.Join(", ", successfulLPs)}");
            if (failedLPs.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"│ Failed: {failedLPs.Count}      {string.Join(", ", failedLPs)}");
            }
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"└─────────────────────────────────────────────┘\n");
            Console.ResetColor();

            // Start watching for responses
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"⏳ Waiting for quotes from {successfulLPs.Count} LPs...\n");
            Console.ResetColor();
        }

        private void UpdateTradeFromGrid()
        {
            var selectedLegs = new List<TradeStructure.OptionLeg>();

            for (int i = 0; i < dgvLegs.Rows.Count; i++)
            {
                bool include = (bool)dgvLegs.Rows[i].Cells["Include"].Value;

                if (include)
                {
                    var originalLeg = _trade.Legs[i];
                    var notionalStr = dgvLegs.Rows[i].Cells["NotionalMM"].Value?.ToString();

                    if (double.TryParse(notionalStr, out double notionalMM))
                    {
                        originalLeg.NotionalMM = notionalMM;
                    }

                    selectedLegs.Add(originalLeg);
                }
            }

            _trade.Legs = selectedLegs;

            Console.WriteLine($"\n[Quote Request] Sending {selectedLegs.Count} legs:");
            for (int i = 0; i < selectedLegs.Count; i++)
            {
                var leg = selectedLegs[i];
                Console.WriteLine($"  Leg {i}: {leg.Direction} {leg.NotionalMM}MM {leg.OptionType} @ {leg.Strike}");
            }
        }

        private void QuoteTimer_Tick(object sender, EventArgs e)
        {
            UpdateQuoteDisplay();
        }

        private void OnQuoteReceivedFromFIX(string quoteReqID, FIXMessage quote)
        {
            // Marshal to UI thread
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => OnQuoteReceivedFromFIX(quoteReqID, quote)));
                return;
            }

            Console.WriteLine($"[UI] Quote received: {quoteReqID}");
            UpdateQuoteDisplay();
        }

        private void UpdateQuoteDisplay()
        {
            dgvQuotes.Rows.Clear();
            var streams = _fixSession.Application.GetActiveStreams(_groupId);  // Changed

            foreach (var stream in streams)
            {
                var rowData = new List<object>();
                rowData.Add(stream.LP);

                double? netPremBid = CalculateNetPremium(stream.BidQuote);
                double? netPremOffer = CalculateNetPremium(stream.OfferQuote);

                rowData.Add(netPremBid?.ToString("N2") ?? "-");
                rowData.Add(netPremOffer?.ToString("N2") ?? "-");

                // Add leg data based on display mode
                for (int i = 1; i <= _selectedLegCount; i++)
                {
                    if (_showVolatility)
                    {
                        // Volatility mode
                        double? bidVol = GetLegVol(stream.BidQuote, i);
                        double? offerVol = GetLegVol(stream.OfferQuote, i);

                        rowData.Add(bidVol?.ToString("N2") ?? "-");
                        rowData.Add(offerVol?.ToString("N2") ?? "-");
                    }
                    else
                    {
                        // Premium mode
                        double? bidPrem = GetLegPremium(stream.BidQuote, i);
                        double? offerPrem = GetLegPremium(stream.OfferQuote, i);

                        rowData.Add(bidPrem?.ToString("N2") ?? "-");
                        rowData.Add(offerPrem?.ToString("N2") ?? "-");
                    }
                }

                rowData.Add(stream.LastUpdate.ToString("HH:mm:ss"));

                // Extract ValidUntilTime (tag 62) from the quote
                string validUntilStr = stream.OfferQuote?.Get("62") ?? stream.BidQuote?.Get("62");

                // DEBUG: Log what we got for tag 62
                Console.WriteLine($"[COUNTDOWN DEBUG] LP={stream.LP}, ValidUntilTime (tag 62)='{validUntilStr}'");

                rowData.Add(""); // TTL - will be calculated by timer
                rowData.Add(validUntilStr ?? ""); // Hidden ValidUntilTime column

                var rowIndex = dgvQuotes.Rows.Add(rowData.ToArray());

                // Apply highlighting based on toggle mode
                if (_showVolatility)
                {
                    // Volatility mode - highlight best volatilities for leg 1
                    var (bestBidVol, bestOfferVol) = GetBestVolatilities();
        
                    double? bidVol = GetLegVol(stream.BidQuote, 1);
                    double? offerVol = GetLegVol(stream.OfferQuote, 1);

                    if (bestBidVol.HasValue && bidVol.HasValue && Math.Abs(bidVol.Value - bestBidVol.Value) < 0.01)
                    {
                        string colName = "Leg1BidVol";
                        if (dgvQuotes.Columns.Contains(colName))
                        {
                            dgvQuotes.Rows[rowIndex].Cells[colName].Style.BackColor = Color.LightGreen;
                            dgvQuotes.Rows[rowIndex].Cells[colName].Style.Font = new Font(dgvQuotes.Font, FontStyle.Bold);
                        }
                    }

                    if (bestOfferVol.HasValue && offerVol.HasValue && Math.Abs(offerVol.Value - bestOfferVol.Value) < 0.01)
                    {
                        string colName = "Leg1OfferVol";
                        if (dgvQuotes.Columns.Contains(colName))
                        {
                            dgvQuotes.Rows[rowIndex].Cells[colName].Style.BackColor = Color.LightGreen;
                            dgvQuotes.Rows[rowIndex].Cells[colName].Style.Font = new Font(dgvQuotes.Font, FontStyle.Bold);
                        }
                    }
                }
                else
                {
                    // Premium mode - highlight best net premiums
                    var (bestBid, bestOffer) = GetBestPremiums();

                    if (bestBid.HasValue && netPremBid.HasValue && Math.Abs(netPremBid.Value - bestBid.Value) < 0.01)
                    {
                        dgvQuotes.Rows[rowIndex].Cells["NetPremBid"].Style.BackColor = Color.LightGreen;
                        dgvQuotes.Rows[rowIndex].Cells["NetPremBid"].Style.Font = new Font(dgvQuotes.Font, FontStyle.Bold);
                    }

                    if (bestOffer.HasValue && netPremOffer.HasValue && Math.Abs(netPremOffer.Value - bestOffer.Value) < 0.01)
                    {
                        dgvQuotes.Rows[rowIndex].Cells["NetPremOffer"].Style.BackColor = Color.LightGreen;
                        dgvQuotes.Rows[rowIndex].Cells["NetPremOffer"].Style.Font = new Font(dgvQuotes.Font, FontStyle.Bold);
                    }
                }
            }
            // Enable execute buttons if we have quotes
            if (streams.Any(s => s.BidQuote != null || s.OfferQuote != null))
            {
                btnExecute.Enabled = true;
                btnBuy.Enabled = true;
            }

            // Clear selection so best price highlighting is visible (not covered by blue selection)
            dgvQuotes.ClearSelection();

            // Start countdown timer - only if grid is properly initialized
            if (!_countdownTimer.Enabled && dgvQuotes != null && dgvQuotes.Columns.Contains("TTL"))
                _countdownTimer.Start();
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            if (dgvQuotes.InvokeRequired)
            {
                dgvQuotes.Invoke(new Action(() => CountdownTimer_Tick(sender, e)));
                return;
            }

            // Safety checks to prevent NullReferenceException
            if (dgvQuotes == null || dgvQuotes.Columns == null || dgvQuotes.Rows == null)
            {
                return;
            }

            // Check if required columns exist
            if (!dgvQuotes.Columns.Contains("TTL") || !dgvQuotes.Columns.Contains("ValidUntilTime"))
            {
                return;
            }

            var nowUtc = DateTime.UtcNow;

            // Completely suspend painting using WinAPI (more aggressive than SuspendLayout)
            SendMessage(dgvQuotes.Handle, WM_SETREDRAW, false, 0);

            try
            {
                foreach (DataGridViewRow row in dgvQuotes.Rows)
                {
                    try
                    {
                        string validUntilStr = row.Cells["ValidUntilTime"].Value?.ToString();
                        if (string.IsNullOrEmpty(validUntilStr))
                        {
                            if (row.Cells["TTL"].Value?.ToString() != "-")
                                row.Cells["TTL"].Value = "-";
                            continue;
                        }

                        // Parse ValidUntilTime: format is "YYYYMMDD-HH:mm:ss" (already in UTC)
                        if (DateTime.TryParseExact(validUntilStr, "yyyyMMdd-HH:mm:ss",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                            out DateTime validUntil))
                        {
                            var remainingTime = validUntil - nowUtc;
                            var ttlCell = row.Cells["TTL"];
                            string newValue;

                            if (remainingTime.TotalSeconds <= 0)
                            {
                                newValue = "EXPIRED";
                            }
                            else
                            {
                                // Format as MM:SS
                                int minutes = (int)remainingTime.TotalMinutes;
                                int seconds = remainingTime.Seconds;
                                newValue = $"{minutes}:{seconds:D2}";
                            }

                            // Only update if value changed (reduces redraws)
                            if (ttlCell.Value?.ToString() != newValue)
                            {
                                ttlCell.Value = newValue;
                            }
                        }
                        else
                        {
                            if (row.Cells["TTL"].Value?.ToString() != "-")
                                row.Cells["TTL"].Value = "-";
                        }
                    }
                    catch
                    {
                        if (row.Cells["TTL"].Value?.ToString() != "-")
                            row.Cells["TTL"].Value = "-";
                    }
                }
            }
            finally
            {
                // Resume painting and refresh only the TTL column
                SendMessage(dgvQuotes.Handle, WM_SETREDRAW, true, 0);
                // Additional safety check before invalidating
                if (dgvQuotes.Columns.Contains("TTL"))
                {
                    dgvQuotes.Invalidate(dgvQuotes.GetColumnDisplayRectangle(dgvQuotes.Columns["TTL"].Index, false));
                }
            }
        }

        private void DgvQuotes_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // Only format TTL column
            if (dgvQuotes.Columns[e.ColumnIndex].Name != "TTL")
                return;

            if (e.Value == null)
                return;

            string ttlValue = e.Value.ToString();

            // Apply colors based on TTL value
            if (ttlValue == "EXPIRED")
            {
                e.CellStyle.BackColor = Color.LightGray;
                e.CellStyle.ForeColor = Color.DarkRed;
            }
            else if (ttlValue == "-")
            {
                e.CellStyle.BackColor = Color.White;
                e.CellStyle.ForeColor = Color.Black;
            }
            else
            {
                // Parse time to determine color
                string[] parts = ttlValue.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out int minutes) && int.TryParse(parts[1], out int seconds))
                {
                    int totalSeconds = minutes * 60 + seconds;

                    if (totalSeconds > 60)
                    {
                        e.CellStyle.BackColor = Color.LightGreen;
                        e.CellStyle.ForeColor = Color.Black;
                    }
                    else if (totalSeconds > 30)
                    {
                        e.CellStyle.BackColor = Color.Yellow;
                        e.CellStyle.ForeColor = Color.Black;
                    }
                    else
                    {
                        e.CellStyle.BackColor = Color.LightCoral;
                        e.CellStyle.ForeColor = Color.White;
                    }
                }
            }
        }

        private double? CalculateNetPremium(FIXMessage quote)
        {
            if (quote == null) return null;

            // Get LP name to check for LP-specific premium units
            string lpName = quote.Get(Tags.OnBehalfOfCompID.ToString()) ?? "";
            string side = quote.Get("54") == "1" ? "BID" : "OFFER";

            // Extract spot reference from first leg (tag 5235)
            string spotRef = quote.LegPricing != null && quote.LegPricing.Count > 0
                ? quote.LegPricing[0].LegSpotRate
                : null;

            // PREFER Tag 6436 (Premium) if available - it's in consistent units across all LPs
            // Tag 6436 appears to be in hundredths of basis points (divide by 1000 for basis points)
            string tag6436 = quote.Get("6436");
            if (!string.IsNullOrEmpty(tag6436) && double.TryParse(tag6436, out double premium6436))
            {
                double basisPoints = premium6436 / 1000.0;
                Console.WriteLine($"[PREMIUM] {lpName} {side}: Tag6436={tag6436} -> Display={basisPoints:F2}, Spot={spotRef ?? "N/A"}");
                return basisPoints;
            }

            // FALLBACK: Use LegPricing structure (less reliable due to PriceIndicator differences)
            if (quote.LegPricing != null && quote.LegPricing.Count > 0)
            {
                double netPrem = 0;
                foreach (var leg in quote.LegPricing)
                {
                    if (!string.IsNullOrEmpty(leg.LegPremPrice) && double.TryParse(leg.LegPremPrice, out double prem))
                    {
                        // HSBC sends premiums in percentage, others in basis points
                        // Convert HSBC from % to bps by multiplying with 100
                        if (lpName == "HSBC")
                        {
                            prem *= 100.0;
                        }
                        netPrem += prem;
                    }
                }
                return netPrem;
            }

            // Fallback to old field structure for backwards compatibility
            double netPremOld = 0;
            for (int i = 1; i <= _selectedLegCount; i++)
            {
                var premStr = quote.Get($"leg{i}_5844");
                if (!string.IsNullOrEmpty(premStr) && double.TryParse(premStr, out double prem))
                {
                    // HSBC sends premiums in percentage, others in basis points
                    if (lpName == "HSBC")
                    {
                        prem *= 100.0;
                    }
                    netPremOld += prem;
                }
            }

            return netPremOld;
        }

        private double? GetLegVol(FIXMessage quote, int legNum)
        {
            if (quote == null) return null;

            // Use new LegPricing structure (legNum is 1-indexed, array is 0-indexed)
            if (quote.LegPricing != null && quote.LegPricing.Count >= legNum)
            {
                var leg = quote.LegPricing[legNum - 1];
                if (!string.IsNullOrEmpty(leg.Volatility) && double.TryParse(leg.Volatility, out double vol))
                {
                    return vol;
                }
            }

            // Fallback to old field structure
            var volStr = quote.Get($"leg{legNum}_5678");
            if (!string.IsNullOrEmpty(volStr) && double.TryParse(volStr, out double volOld))
            {
                return volOld;
            }

            return null;
        }

        private double? GetLegPremium(FIXMessage quote, int legNum)
        {
            if (quote == null) return null;

            // Use new LegPricing structure (legNum is 1-indexed, array is 0-indexed)
            if (quote.LegPricing != null && quote.LegPricing.Count >= legNum)
            {
                var leg = quote.LegPricing[legNum - 1];
                if (!string.IsNullOrEmpty(leg.LegPremPrice) && double.TryParse(leg.LegPremPrice, out double prem))
                {
                    return prem;
                }
            }

            // Fallback to old field structure
            var premStr = quote.Get($"leg{legNum}_5844");
            if (!string.IsNullOrEmpty(premStr) && double.TryParse(premStr, out double premOld))
            {
                return premOld;
            }

            return null;
        }

        private (double? bestBid, double? bestOffer) GetBestPremiums()
        {
            var streams = _fixSession.Application.GetActiveStreams(_groupId);  // Changed

            double? bestBid = null;
            double? bestOffer = null;

            foreach (var stream in streams)
            {
                var bid = CalculateNetPremium(stream.BidQuote);
                var offer = CalculateNetPremium(stream.OfferQuote);

                // Best BID = highest (client sells at best price)
                if (bid.HasValue && (!bestBid.HasValue || bid.Value > bestBid.Value))
                    bestBid = bid.Value;

                // Best OFFER = least negative / closest to zero (client buys at lowest price)
                if (offer.HasValue && (!bestOffer.HasValue || offer.Value > bestOffer.Value))
                    bestOffer = offer.Value;
            }

            return (bestBid, bestOffer);
        }

        private (double? bestBid, double? bestOffer) GetBestVolatilities()
        {
            var streams = _fixSession.Application.GetActiveStreams(_groupId);

            double? bestBid = null;
            double? bestOffer = null;

            foreach (var stream in streams)
            {
              // Get volatility for leg 1
  var bid = GetLegVol(stream.BidQuote, 1);
        var offer = GetLegVol(stream.OfferQuote, 1);

      // Best BID VOL = highest (better for client to sell)
     if (bid.HasValue && (!bestBid.HasValue || bid.Value > bestBid.Value))
   bestBid = bid.Value;

      // Best OFFER VOL = lowest (better for client to buy)
        if (offer.HasValue && (!bestOffer.HasValue || offer.Value < bestOffer.Value))
    bestOffer = offer.Value;
  }

  return (bestBid, bestOffer);
  }

        private void BtnExecute_Click(string side)
        {
            _quoteTimer?.Stop();

            // Get best bid quote
            var streams = _fixSession.Application.GetActiveStreams(_groupId);
            FIXMessage bestBidQuote = null;
            double bestBidPremium = double.MinValue;

            foreach (var stream in streams)
            {
                if (stream.BidQuote != null)
                {
                    var prem = CalculateNetPremium(stream.BidQuote);
                    if (prem.HasValue && prem.Value > bestBidPremium)
                    {
                        bestBidPremium = prem.Value;
                        bestBidQuote = stream.BidQuote;
                    }
                }
            }

            if (bestBidQuote == null)
            {
                MessageBox.Show("No valid quotes available", "Cannot Execute",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _quoteTimer?.Start();
                return;
            }

            // Execute the trade
            try
            {
                FIXMessage selectedQuote;
                double selectedPremium;
                string lpName;

                if (side == "SELL")
                {
                    // Hit the bid - find best bid
                    selectedQuote = bestBidQuote;
                    selectedPremium = bestBidPremium;
                    lpName = bestBidQuote.Get(Tags.OnBehalfOfCompID.ToString());
                }
                else // BUY
                {
                    // Lift the offer - find best offer
                    FIXMessage bestOfferQuote = null;
                    double bestOfferPremium = double.MaxValue;

                    foreach (var stream in streams)
                    {
                        if (stream.OfferQuote != null)
                        {
                            var prem = CalculateNetPremium(stream.OfferQuote);
                            if (prem.HasValue && prem.Value < bestOfferPremium)
                            {
                                bestOfferPremium = prem.Value;
                                bestOfferQuote = stream.OfferQuote;
                            }
                        }
                    }

                    if (bestOfferQuote == null)
                    {
                        MessageBox.Show("No valid offer quotes available", "Cannot Execute",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        _quoteTimer?.Start();
                        return;
                    }

                    selectedQuote = bestOfferQuote;
                    selectedPremium = bestOfferPremium;
                    lpName = bestOfferQuote.Get(Tags.OnBehalfOfCompID.ToString());
                }

                // ===== QUOTE FRESHNESS VALIDATION =====
                Console.WriteLine($"\n[VALIDATION] Starting quote freshness check for {lpName}");
                Console.WriteLine($"[VALIDATION] Original QuoteID: {selectedQuote.Get(Tags.QuoteID.ToString())}");
                Console.WriteLine($"[VALIDATION] Selected Quote Side (tag 54): {selectedQuote.Get("54")} ({(selectedQuote.Get("54") == "1" ? "BID" : selectedQuote.Get("54") == "2" ? "OFFER" : "UNKNOWN")})");
                Console.WriteLine($"[VALIDATION] User Action: {side}");

                // Re-fetch streams to check current quote state
                // NOTE: No delay - execute as fast as possible to minimize window for LP to update quote
                Console.WriteLine($"[VALIDATION] Re-fetching streams for GroupID: {_groupId}");
                var refreshedStreams = _fixSession.Application.GetActiveStreams(_groupId);
                var refreshedStream = refreshedStreams.FirstOrDefault(s => s.LP == lpName);

                if (refreshedStream == null)
                {
                    Console.WriteLine($"[VALIDATION] ✗ Stream for {lpName} NOT FOUND - quote no longer available!");
                    MessageBox.Show(
                        $"Quote from {lpName} is no longer available.\n\nPlease request fresh quotes.",
                        "Quote No Longer Available",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    _quoteTimer?.Start();
                    return;
                }
                Console.WriteLine($"[VALIDATION] ✓ Stream for {lpName} found");

                // Check if the specific side (bid/offer) was canceled
                FIXMessage refreshedQuote = side == "SELL" ? refreshedStream.BidQuote : refreshedStream.OfferQuote;

                if (refreshedQuote == null)
                {
                    Console.WriteLine($"[VALIDATION] ✗ Quote for side={side} from {lpName} is NULL - was canceled!");
                    MessageBox.Show(
                        $"Quote from {lpName} was just canceled.\n\nPlease request fresh quotes.",
                        "Quote Canceled",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    _quoteTimer?.Start();
                    return;
                }
                Console.WriteLine($"[VALIDATION] ✓ Quote for side={side} exists");

                // Check if the QuoteID changed (quote was replaced)
                string originalQuoteID = selectedQuote.Get(Tags.QuoteID.ToString());
                string currentQuoteID = refreshedQuote.Get(Tags.QuoteID.ToString());

                if (originalQuoteID != currentQuoteID)
                {
                    Console.WriteLine($"[VALIDATION] ✗ QuoteID CHANGED! Old={originalQuoteID}, New={currentQuoteID}");
                    MessageBox.Show(
                        $"Quote from {lpName} was updated.\n\nOld QuoteID: {originalQuoteID}\nNew QuoteID: {currentQuoteID}\n\nPlease review the updated price.",
                        "Quote Updated",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );

                    // Auto-refresh display with new quote
                    UpdateQuoteDisplay();
                    _quoteTimer?.Start();
                    return;
                }

                Console.WriteLine($"[VALIDATION] ✓ QuoteID unchanged: {currentQuoteID}");

                // Check ValidUntilTime (tag 62) - quote expiration
                string validUntilStr = refreshedQuote.Get("62");
                Console.WriteLine($"[VALIDATION] ValidUntilTime (tag 62): {validUntilStr}");

                if (!string.IsNullOrEmpty(validUntilStr))
                {
                    if (DateTime.TryParseExact(validUntilStr, "yyyyMMdd-HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out DateTime validUntil))
                    {
                        var timeRemaining = validUntil - DateTime.UtcNow;
                        Console.WriteLine($"[VALIDATION] Time remaining: {timeRemaining.TotalSeconds:F1}s");

                        if (timeRemaining.TotalSeconds <= 0)
                        {
                            Console.WriteLine($"[VALIDATION] ✗ Quote EXPIRED! ValidUntil={validUntil:HH:mm:ss}, Now={DateTime.UtcNow:HH:mm:ss}");
                            MessageBox.Show(
                                $"Quote from {lpName} has expired.\n\nExpired: {-timeRemaining.TotalSeconds:F1}s ago\n\nPlease request fresh quotes.",
                                "Quote Expired",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning
                            );
                            _quoteTimer?.Start();
                            return;
                        }

                        if (timeRemaining.TotalSeconds < 2)
                        {
                            Console.WriteLine($"[VALIDATION] ⚠ Quote expiring very soon ({timeRemaining.TotalSeconds:F1}s)");
                            var result = MessageBox.Show(
                                $"WARNING: Quote expires in {timeRemaining.TotalSeconds:F1}s!\n\nThere may not be enough time to execute.\n\nProceed anyway?",
                                "Quote Expiring Soon",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning
                            );

                            if (result == DialogResult.No)
                            {
                                Console.WriteLine($"[VALIDATION] User declined to execute expiring quote");
                                _quoteTimer?.Start();
                                return;
                            }
                        }

                        Console.WriteLine($"[VALIDATION] ✓ Quote is valid (expires in {timeRemaining.TotalSeconds:F1}s)");
                    }
                }

                Console.WriteLine($"[VALIDATION] ✓ All checks passed - proceeding with execution\n");

                // Use the refreshed quote for execution to ensure we have the latest data
                selectedQuote = refreshedQuote;
                Console.WriteLine($"[VALIDATION] FINAL Quote Side before execution: {selectedQuote.Get("54")} ({(selectedQuote.Get("54") == "1" ? "BID" : selectedQuote.Get("54") == "2" ? "OFFER" : "UNKNOWN")})");
                Console.WriteLine($"[VALIDATION] FINAL QuoteID before execution: {selectedQuote.Get(Tags.QuoteID.ToString())}");
                // ===== END QUOTE FRESHNESS VALIDATION =====

                string clOrdID = _fixSession.SendExecution(selectedQuote, side, _trade);

                // Order sent - user will see updates in the trade blotter below
                // Don't close dialog - let user see blotter updates
                _quoteTimer?.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Execution error:\n\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _quoteTimer?.Start();
            }
        }

        private void OnTradeAddedToBlotter(TradeBlotterEntry entry)
        {
            if (dgvBlotter.InvokeRequired)
            {
                dgvBlotter.Invoke(new Action(() => OnTradeAddedToBlotter(entry)));
                return;
            }

            // Helper to format nullable values
            string FormatValue(double? value, string format = "N2") => value?.ToString(format) ?? "-";
            string FormatDate(string date) => string.IsNullOrEmpty(date) ? "-" :
                (DateTime.TryParseExact(date, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var dt)
                    ? dt.ToString("dd-MMM-yy") : date);

            // Format delta in millions
            string deltaDisplay = entry.Delta.HasValue ? (entry.Delta.Value / 1_000_000.0).ToString("N2") + "M" : "-";

            // Get strategy name from type
            string strategyName = entry.StrategyName ?? (entry.StructureType == "1" ? "Vanilla" :
                entry.StructureType == "5" ? "RR" :
                entry.StructureType == "8" ? "Call Spread" :
                entry.StructureType == "9" ? "Put Spread" :
                entry.StructureType == "10" ? "Seagull" : entry.StructureType);

            dgvBlotter.Rows.Add(
                // Identification & Status
                entry.AffirmedBy ?? "-",                           // Affirmed By
                entry.ClOrdID,                                      // Trade ID
                entry.ExecTimestamp?.ToString("HH:mm:ss") ?? entry.TradeTime.ToString("HH:mm:ss"),  // EXEC TS
                entry.Status,                                       // STP Status

                // Party Information
                entry.MyBroker ?? "-",                             // My Broker
                entry.MyTrader ?? "-",                             // My Trader
                entry.MyCenter ?? entry.Cut ?? "-",                // My Center
                entry.Underlying,                                   // CCY Pair
                entry.Side,                                         // Buy/Sell

                // Trade Details
                FormatValue(entry.Volatility, "N4"),               // Vol
                entry.NotionalMM > 0 ? entry.NotionalMM.ToString("N1") : "-",  // Size (M)
                FormatValue(entry.Strike, "N4"),                   // Strike
                deltaDisplay,                                       // Delta
                strategyName,                                       // Strategy
                entry.Venue ?? "-",                                // Venue

                // Dates
                FormatDate(entry.ExpDate),                         // Expiry
                FormatDate(entry.SettlementDate),                  // Delivery

                // Pricing
                entry.NetPremium.ToString("N2"),                   // Price
                entry.LP ?? "-",                                   // Counterparty
                entry.CounterpartyCenter ?? "-",                   // Ctpy Center
                entry.Cut ?? "-",                                  // Cut
                FormatValue(entry.SpotReference, "N4"),            // Spot
                FormatValue(entry.Swap, "N4"),                     // Swap
                FormatValue(entry.Depo, "N4"),                     // Depo
                entry.ValueDate?.ToString("dd-MMM-yy") ?? "-",     // Value

                // Hedge Information
                entry.HedgeSide ?? "-",                            // Hedge B/S
                FormatValue(entry.HedgeAmount, "N2"),              // Hedge Amt
                FormatValue(entry.HedgeRate, "N4"),                // Hedge Rate
                entry.HedgeDeliveryDate ?? "-",                    // Hedge Del Date

                entry.TradeTime.ToString("HH:mm:ss")               // Trade Time
            );

            // Color code by status
            var row = dgvBlotter.Rows[dgvBlotter.Rows.Count - 1];
            ColorCodeBlotterRow(row, entry.Status);
        }

        private void OnTradeUpdatedInBlotter(TradeBlotterEntry entry)
        {
            if (dgvBlotter.InvokeRequired)
            {
                dgvBlotter.Invoke(new Action(() => OnTradeUpdatedInBlotter(entry)));
                return;
            }

            // Find the row with matching Trade ID and update key fields
            foreach (DataGridViewRow row in dgvBlotter.Rows)
            {
                if (row.Cells["TradeID"].Value?.ToString() == entry.ClOrdID)
                {
                    // Update key fields that may change
                    row.Cells["STPStatus"].Value = entry.Status;
                    row.Cells["Price"].Value = entry.NetPremium.ToString("N2");
                    row.Cells["Counterparty"].Value = entry.LP ?? "-";
                    row.Cells["AffirmedBy"].Value = entry.AffirmedBy ?? "-";

                    ColorCodeBlotterRow(row, entry.Status);
                    break;
                }
            }
        }

        private void ColorCodeBlotterRow(DataGridViewRow row, string status)
        {
            switch (status?.ToUpper())
            {
                case "CONFIRMED":
                    row.DefaultCellStyle.BackColor = Color.LightBlue;  // Bright blue for confirmed
                    row.DefaultCellStyle.ForeColor = Color.Black;
                    break;
                case "FILLED":
                    row.DefaultCellStyle.BackColor = Color.LightGreen;
                    row.DefaultCellStyle.ForeColor = Color.Black;
                    break;
                case "REJECTED":
                    row.DefaultCellStyle.BackColor = Color.LightCoral;
                    row.DefaultCellStyle.ForeColor = Color.Black;
                    break;
                case "PENDING":
                    row.DefaultCellStyle.BackColor = Color.LightYellow;
                    row.DefaultCellStyle.ForeColor = Color.Black;
                    break;
                default:
                    row.DefaultCellStyle.BackColor = Color.White;
                    row.DefaultCellStyle.ForeColor = Color.Black;
                    break;
            }
        }

        private void TestCase_CheckedChanged(object sender, EventArgs e)
        {
            var checkbox = (CheckBox)sender;

            // Only process when checking (not unchecking)
            if (!checkbox.Checked)
                return;

            int testId = (int)checkbox.Tag;
            string testDescription = checkbox.Text;

            // SINGLE SELECTION: Uncheck all other test cases
            foreach (var otherCheckbox in _testCaseCheckboxes)
            {
                if (otherCheckbox != checkbox && otherCheckbox.Checked)
                {
                    otherCheckbox.Checked = false;
                }
            }

            Console.WriteLine($"\n[TEST CASE {testId}] Initiating automatic quote request...");

            try
            {
                // Parse test case and create trade structure
                var trade = ParseTestCase(testDescription);
                if (trade == null)
                {
                    MessageBox.Show($"Failed to parse test case: {testDescription}", "Parse Error");
                    checkbox.Checked = false;
                    return;
                }

                // Get selected LPs
                var lps = GetSelectedLPs();
                if (lps.Count == 0)
                {
                    MessageBox.Show("Please select at least one LP before checking test cases", "No LPs Selected");
                    checkbox.Checked = false;
                    return;
                }

                // Update the dialog's trade and refresh UI
                _trade = trade;
                lblTradeSummary.Text = $"{_trade.StructureType}: {_trade.Underlying} - {_trade.Legs.Count} legs";
                PopulateLegGrid();
                SetupQuoteGrid(_trade.Legs.Count);

                // Generate group ID and store current test info
                string groupId = $"TEST-{testId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                _currentTestId = testId;
                _currentGroupId = groupId;

                // Send quote requests
                foreach (var lp in lps)
                {
                    try
                    {
                        string quoteReqID = _fixSession.SendQuoteRequest(trade, lp, groupId, _spotHedge);
                        Console.WriteLine($"[TEST CASE {testId}] ✓ Quote sent to {lp} | QuoteReqID: {quoteReqID}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TEST CASE {testId}] ✗ Failed to send to {lp}: {ex.Message}");
                    }
                }

                Console.WriteLine($"[TEST CASE {testId}] Quote requests completed");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing test case: {ex.Message}", "Error");
                checkbox.Checked = false;
            }
        }

        private TradeStructure ParseTestCase(string testDescription)
        {
            // Extract test ID and parameters from description
            // Format examples:
            // "1: NY EURUSD 1M Buy Call"
            // "2: TKY USDJPY 10Dec26 Buy Put"
            // "13: NY EURSEK 07Nov26 Buy Call + Spot Hedge"
            var parts = testDescription.Split(':');
            if (parts.Length < 2)
                return null;

            // Extract test ID
            int testId = int.Parse(parts[0].Trim());

            var details = parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (details.Length < 5)
                return null;

            string cutoff = details[0];        // NY, TKY, or CNH
            string pair = details[1];          // EURUSD

            // Check if "VOL" keyword is present (test 25)
            bool isVolMode = details.Length > 2 && details[2].Equals("VOL", StringComparison.OrdinalIgnoreCase);
            int offset = isVolMode ? 1 : 0;  // Skip "VOL" if present

            string tenorOrDate = details[2 + offset];   // 1M or 10Dec26 (odd date)
            string direction = details[3 + offset];     // Buy or Sell
            string optionType = details[4 + offset];    // Call or Put (or structure)

            // Check for hedge flag
            bool hasHedge = testDescription.Contains("Hedge");

            // Remaining text for structures
            string optionInfo = string.Join(" ", details.Skip(4 + offset));

            // Update cutoff toggle
            _cutoff = cutoff;

            // Extract currency components
            string ccy1 = pair.Substring(0, 3);  // Base currency (EUR)
            string ccy2 = pair.Substring(3, 3);  // Quote currency (USD)

            // Get official strike from test protocol
            double strike = GetTestProtocolStrike(testId, pair);

            // Normalize direction to uppercase
            direction = direction.ToUpper();

            // Create trade structure with default values
            var trade = new TradeStructure
            {
                Underlying = pair,
                PremiumCurrency = ccy2,  // Premium in quote currency
                StructureType = "1"  // Default to vanilla, will adjust for structures
            };

            // Calculate expiry and delivery dates from tenor or odd date
            DateTime expiry;
            string tenor;

            if (tenorOrDate.Contains("Dec") || tenorOrDate.Contains("Nov") || tenorOrDate.Contains("Mar"))
            {
                // Odd date format: "10Dec26" or "08Nov26"
                expiry = ParseOddDate(tenorOrDate);
                tenor = tenorOrDate;  // Use the date string as tenor
            }
            else
            {
                // Standard tenor: "1M", "3M", "6M", "9M", "1Y"
                expiry = CalculateExpiryFromTenor(tenorOrDate);
                tenor = tenorOrDate;
            }

            DateTime delivery = expiry.AddDays(2);  // Standard T+2 delivery

            // Helper to create a complete leg
            TradeStructure.OptionLeg CreateLeg(string legDirection, string legOptionType, int legIndex)
            {
                return new TradeStructure.OptionLeg
                {
                    Direction = legDirection,
                    OptionType = legOptionType.ToUpper(),
                    Strike = strike,  // Use official test protocol strike
                    NotionalMM = 10,
                    Tenor = tenor,
                    ExpiryDate = expiry,
                    DeliveryDate = delivery,
                    NotionalCurrency = ccy1,  // Notional in base currency
                    Cutoff = cutoff,
                    Position = "SAME",
                    LegID = $"SL{legIndex}"
                };
            }

            // Build legs based on option type
            if (optionInfo.Contains("Call Spread"))
            {
                trade.StructureType = "8";
                trade.Legs = new List<TradeStructure.OptionLeg>
                {
                    CreateLeg("BUY", "CALL", 0),
                    CreateLeg("SELL", "CALL", 1)
                };
            }
            else if (optionInfo.Contains("Put Spread"))
            {
                trade.StructureType = "9";
                trade.Legs = new List<TradeStructure.OptionLeg>
                {
                    CreateLeg("BUY", "PUT", 0),
                    CreateLeg("SELL", "PUT", 1)
                };
            }
            else if (optionInfo.Contains("RR"))
            {
                trade.StructureType = "5";
                // Check direction from description
                if (optionInfo.Contains("S Put, B Call"))
                {
                    trade.Legs = new List<TradeStructure.OptionLeg>
                    {
                        CreateLeg("SELL", "PUT", 0),
                        CreateLeg("BUY", "CALL", 1)
                    };
                }
                else
                {
                    trade.Legs = new List<TradeStructure.OptionLeg>
                    {
                        CreateLeg("BUY", "PUT", 0),
                        CreateLeg("SELL", "CALL", 1)
                    };
                }
            }
            else if (optionInfo.Contains("Straddle"))
            {
                trade.StructureType = "6";
                trade.Legs = new List<TradeStructure.OptionLeg>
                {
                    CreateLeg("BUY", "CALL", 0),
                    CreateLeg("BUY", "PUT", 1)
                };
            }
            else if (optionInfo.Contains("Strangle"))
            {
                trade.StructureType = "7";
                trade.Legs = new List<TradeStructure.OptionLeg>
                {
                    CreateLeg("BUY", "PUT", 0),
                    CreateLeg("BUY", "CALL", 1)
                };
            }
            else if (optionInfo.Contains("Seagull"))
            {
                trade.StructureType = "10";
                // Parse direction from description
                if (optionInfo.Contains("B Put, S Put, S Call"))
                {
                    trade.Legs = new List<TradeStructure.OptionLeg>
                    {
                        CreateLeg("BUY", "PUT", 0),
                        CreateLeg("SELL", "PUT", 1),
                        CreateLeg("SELL", "CALL", 2)
                    };
                }
                else
                {
                    trade.Legs = new List<TradeStructure.OptionLeg>
                    {
                        CreateLeg("BUY", "CALL", 0),
                        CreateLeg("SELL", "CALL", 1),
                        CreateLeg("SELL", "PUT", 2)
                    };
                }
            }
            else if (optionInfo.Contains("Collar"))
            {
                trade.StructureType = "11";
                trade.Legs = new List<TradeStructure.OptionLeg>
                {
                    CreateLeg("BUY", "CALL", 0),
                    CreateLeg("SELL", "CALL", 1),
                    CreateLeg("SELL", "PUT", 2)
                };
            }
            else if (optionType.Contains("Call", StringComparison.OrdinalIgnoreCase))
            {
                // Vanilla Call - use direction from test case
                trade.Legs = new List<TradeStructure.OptionLeg>
                {
                    CreateLeg(direction, "CALL", 0)
                };
            }
            else if (optionType.Contains("Put", StringComparison.OrdinalIgnoreCase))
            {
                // Vanilla Put - use direction from test case
                trade.Legs = new List<TradeStructure.OptionLeg>
                {
                    CreateLeg(direction, "PUT", 0)
                };
            }
            else
            {
                return null;  // Unknown option type
            }

            Console.WriteLine($"[PARSED] {pair} {tenor} {optionInfo} -> {trade.Legs.Count} legs, Structure: {trade.StructureType}");

            return trade;
        }

        private DateTime CalculateExpiryFromTenor(string tenor)
        {
            DateTime today = DateTime.Today;

            if (tenor.EndsWith("M"))
            {
                int months = int.Parse(tenor.TrimEnd('M'));
                return today.AddMonths(months);
            }
            else if (tenor.EndsWith("W"))
            {
                int weeks = int.Parse(tenor.TrimEnd('W'));
                return today.AddDays(weeks * 7);
            }
            else if (tenor.EndsWith("Y"))
            {
                int years = int.Parse(tenor.TrimEnd('Y'));
                return today.AddYears(years);
            }
            else if (tenor == "ON")
            {
                return today.AddDays(1);  // Overnight
            }

            return today.AddMonths(1);  // Default 1 month
        }

        private DateTime ParseOddDate(string oddDate)
        {
            // Parse odd date format: "10Dec26" or "08Nov26" or "08Mar26"
            // Extract day, month, year
            try
            {
                string dayStr = oddDate.Substring(0, 2);  // "10"
                string monthStr = oddDate.Substring(2, 3);  // "Dec"
                string yearStr = oddDate.Substring(5, 2);  // "26"

                int day = int.Parse(dayStr);
                int year = 2000 + int.Parse(yearStr);  // "26" -> 2026

                // Map month names
                int month = monthStr.ToUpper() switch
                {
                    "JAN" => 1, "FEB" => 2, "MAR" => 3,
                    "APR" => 4, "MAY" => 5, "JUN" => 6,
                    "JUL" => 7, "AUG" => 8, "SEP" => 9,
                    "OCT" => 10, "NOV" => 11, "DEC" => 12,
                    _ => 1
                };

                return new DateTime(year, month, day);
            }
            catch
            {
                // Fallback to 1 month from today
                return DateTime.Today.AddMonths(1);
            }
        }

        private double GetTestProtocolStrike(int testId, string pair)
        {
            // Official strikes from GFI FX Options Test Protocol
            return testId switch
            {
                1 => 1.16,      // EURUSD
                2 => 155.0,     // USDJPY
                3 => 7.15,      // USDJPY (different strike)
                4 => 7.16,      // EURSEK
                5 => 11.9,      // USDNOK
                6 => 7.8070,    // EURNOK
                7 => 0.69,      // AUDUSD
                8 => 0.65,      // AUDUSD (different strike)
                9 => 147.0,     // USDJPY
                10 => 1.15,     // EURUSD (different strike)
                11 => 1.16,     // EURUSD
                12 => 155.0,    // USDJPY
                13 => 11.1,     // EURSEK
                14 => 1.32,     // GBPUSD
                15 => 0.93,     // EURCHF
                25 => 155.0,    // USDJPY (VOL test)
                26 => 7.10,     // USDCNH

                // Structure tests (16-24) - use reasonable defaults
                _ => GetDefaultStrike(pair)
            };
        }

        private double GetDefaultStrike(string pair)
        {
            // Reasonable default strikes for pairs not in protocol
            return pair switch
            {
                "EURUSD" => 1.10,
                "USDJPY" => 150.0,
                "GBPUSD" => 1.30,
                "AUDUSD" => 0.65,
                "USDCAD" => 1.35,
                "USDCHF" => 0.90,
                "NZDUSD" => 0.60,
                "EURGBP" => 0.85,
                "EURJPY" => 160.0,
                "GBPJPY" => 190.0,
                "EURCHF" => 0.95,
                "AUDJPY" => 95.0,
                "EURAUD" => 1.65,
                "EURNOK" => 11.5,
                "EURSEK" => 11.3,
                "USDNOK" => 10.5,
                "USDSEK" => 10.3,
                "USDCNH" => 7.20,
                _ => 1.0
            };
        }

        private List<string> GetSelectedLPs()
        {
            var lps = new List<string>();

            if (chkSOCGEN.Checked) lps.Add("SOCGEN");
            if (chkCIBC.Checked) lps.Add("CIBC");
            if (chkMS.Checked) lps.Add("MS");
            if (chkHSBC.Checked) lps.Add("HSBC");
            if (chkNATWEST.Checked) lps.Add("NATWEST");
            if (chkSCBL.Checked) lps.Add("SCBL");
            if (chkNOMURA.Checked) lps.Add("NOMURA");
            if (chkBAML.Checked) lps.Add("BAML");
            if (chkBNP.Checked) lps.Add("BNP");
            if (chkDeut.Checked) lps.Add("DEUT");

            return lps;
        }

        private void OnTradeStatusChanged(TradeBlotterEntry trade)
        {
            // Auto-mark test as successful when trade reaches CONFIRMED status
            if (trade.Status == "CONFIRMED" && _currentTestId > 0)
            {
                // Find the checkbox for the current test
                var checkbox = _testCaseCheckboxes.FirstOrDefault(chk => (int)chk.Tag == _currentTestId);
                if (checkbox != null && !_passedTests.Contains(_currentTestId))
                {
                    // Mark as passed
                    if (checkbox.InvokeRequired)
                    {
                        checkbox.Invoke(new Action(() =>
                        {
                            _passedTests.Add(_currentTestId);
                            checkbox.BackColor = Color.LightGreen;
                            Console.WriteLine($"[TEST CASE {_currentTestId}] ✓ Auto-marked as SUCCESSFUL (trade confirmed)");
                        }));
                    }
                    else
                    {
                        _passedTests.Add(_currentTestId);
                        checkbox.BackColor = Color.LightGreen;
                        Console.WriteLine($"[TEST CASE {_currentTestId}] ✓ Auto-marked as SUCCESSFUL (trade confirmed)");
                    }
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _quoteTimer?.Stop();
            _quoteTimer?.Dispose();

            _countdownTimer?.Stop();
            _countdownTimer?.Dispose();

            // Unsubscribe from blotter events
            TradeBlotter.Instance.OnTradeAdded -= OnTradeAddedToBlotter;
            TradeBlotter.Instance.OnTradeUpdated -= OnTradeUpdatedInBlotter;
            TradeBlotter.Instance.OnTradeUpdated -= OnTradeStatusChanged;  // Unsubscribe from auto-marking

            // Unsubscribe from events
            _fixSession.Application.OnQuoteReceived -= OnQuoteReceivedFromFIX;

            base.OnFormClosing(e);
        }
    }
}