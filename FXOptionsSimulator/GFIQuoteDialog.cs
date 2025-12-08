using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
        private string _hedgeType = "Spot";  // NEW: Hedge type: "Live", "Spot", or "Forward"
        private string _premiumType = "Spot";  // NEW: Premium delivery: "Spot" or "Forward"
        private string _cutoff = "NY";  // NEW: Cutoff toggle (default NY)
        private string _premiumCurrency = null;  // NEW: Premium currency toggle (EUR/USD, etc.)
        private string _notionalCurrency = null;  // NEW: Notional currency (EUR/NOK, etc.)
        private HashSet<int> _passedTests = new HashSet<int>();  // Track which tests passed
        private List<CheckBox> _testCaseCheckboxes = new List<CheckBox>();  // All test case checkboxes
        private int _currentTestId = -1;  // Currently active test case
        private string _currentGroupId = null;  // Current test group ID for tracking

        // UI elements stored as fields for dynamic updates
        private Button _btnCcy1;
        private Button _btnCcy2;
        private Label _lblCcyToggle;
        private Button _btnToggleCut;
        private Button _btnToggleDisplay;  // Vol/Premium toggle button
        private Button _btnTogglePremType;  // Premium Type toggle (Spot/Forward)
        private ComboBox _cmbHedgeType;  // Delta Hedge dropdown (Live/Spot/Forward)
        private TextBox _txtNotionalAmount;  // Ccy1 amount (e.g., EUR)
        private TextBox _txtNotionalCcy2;     // Ccy2 amount (e.g., USD)
        private Label _lblNotionalCcy1;
        private Label _lblNotionalCcy2;

        public GFIQuoteDialog(dynamic ovmlResult)
        {
            InitializeComponent();
            
            // IMPORTANT: Convert trade structure BEFORE InitializeCustomComponents
            // so that currency-dependent UI elements can use the underlying pair
          _trade = OVMLBridge.ConvertToTradeStructure(ovmlResult);
            
         InitializeCustomComponents();
            
  _fixSession = GlobalFIXSession.Instance;

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
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                RowHeadersVisible = false
            };
            // Set subtle selection color
            dgvLegs.DefaultCellStyle.SelectionBackColor = Color.LightSteelBlue;
            dgvLegs.DefaultCellStyle.SelectionForeColor = Color.Black;
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

            // Extract currencies from pair for column headers
            string ccy1 = _trade?.Underlying?.Length >= 6 ? _trade.Underlying.Substring(0, 3) : "EUR";
            string ccy2 = _trade?.Underlying?.Length >= 6 ? _trade.Underlying.Substring(3, 3) : "USD";

            var notionalCol1 = new DataGridViewTextBoxColumn
            {
                Name = "NotionalCcy1",
                HeaderText = $"Notional ({ccy1})",
                Width = 100,
                ReadOnly = false
            };
            dgvLegs.Columns.Add(notionalCol1);

            var notionalCol2 = new DataGridViewTextBoxColumn
            {
                Name = "NotionalCcy2",
                HeaderText = $"Notional ({ccy2})",
                Width = 100,
                ReadOnly = false
            };
            dgvLegs.Columns.Add(notionalCol2);

            // Add event handler for cell editing
            dgvLegs.CellEndEdit += DgvLegs_CellEndEdit;

            // LP Selection GroupBox
            gbLPs = new GroupBox
            {
                Text = "Select Liquidity Providers",
                Location = new Point(20, 210),
                Size = new Size(940, 90)
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
            _btnToggleDisplay = new Button
            {
                Text = "Show: Volatility",
                Location = new Point(20, 305),  // Increased from 293 to 305 for more spacing
                Size = new Size(130, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.LightBlue,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnToggleDisplay.FlatAppearance.BorderColor = Color.DodgerBlue;
            _btnToggleDisplay.Click += (s, e) =>
            {
                _showVolatility = !_showVolatility;
                _btnToggleDisplay.Text = _showVolatility ? "Show: Volatility" : "Show: Premium";
                _btnToggleDisplay.BackColor = _showVolatility ? Color.LightBlue : Color.LightGreen;

                // Rebuild quote grid with new columns
                if (_selectedLegCount > 0)
                {
                    SetupQuoteGrid(_selectedLegCount);
                    UpdateQuoteDisplay(); // Refresh data
                }
            };
            this.Controls.Add(_btnToggleDisplay);

            // Toggle button for Premium Type (Spot/Forward)
            _btnTogglePremType = new Button
            {
                Text = "Prem: Spot",
                Location = new Point(160, 305),
                Size = new Size(110, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.LightGreen,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnTogglePremType.FlatAppearance.BorderColor = Color.Green;
            _btnTogglePremType.Click += (s, e) =>
            {
                // Toggle between Spot and Forward
                _premiumType = _premiumType == "Spot" ? "Forward" : "Spot";
                UpdatePremiumTypeButton();

                // Recalculate premium dates if trade exists
                if (_trade != null)
                {
                    PopulateLegGrid();
                }
            };
            this.Controls.Add(_btnTogglePremType);

            // Dropdown for Delta Hedge (Live/Spot/Forward)
            _cmbHedgeType = new ComboBox
            {
                Location = new Point(280, 305),
                Size = new Size(120, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.White
            };
            _cmbHedgeType.Items.AddRange(new object[] { "Live", "Spot", "Forward" });
            _cmbHedgeType.SelectedIndex = 1; // Default to "Spot"
            _cmbHedgeType.SelectedIndexChanged += (s, e) =>
            {
                // Update hedge type when dropdown selection changes
                _hedgeType = _cmbHedgeType.SelectedItem.ToString();
                UpdateHedgeTypeDropdown();
                Console.WriteLine($"[HEDGE TYPE] Changed to: {_hedgeType}");
            };
            this.Controls.Add(_cmbHedgeType);

            // Toggle button for Cutoff - stored as class field for dynamic updates
            _btnToggleCut = new Button
            {
                Text = $"Cut: {_cutoff}",
                Location = new Point(410, 305),
                Size = new Size(100, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                BackColor = Color.LightSkyBlue,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnToggleCut.FlatAppearance.BorderColor = Color.DodgerBlue;
            _btnToggleCut.Click += BtnToggleCut_Click;
            this.Controls.Add(_btnToggleCut);

            // Currency Toggle Buttons - created initially, updated dynamically via UpdateCurrencyButtons()
            _btnCcy1 = new Button
            {
                Location = new Point(520, 305),
                Size = new Size(55, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnCcy1.FlatAppearance.BorderColor = Color.Gray;
            _btnCcy1.Click += BtnCcy1_Click;
            this.Controls.Add(_btnCcy1);

            _btnCcy2 = new Button
            {
                Location = new Point(580, 305),
                Size = new Size(55, 25),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnCcy2.FlatAppearance.BorderColor = Color.Gray;
            _btnCcy2.Click += BtnCcy2_Click;
            this.Controls.Add(_btnCcy2);

            // Label for currency toggle
            _lblCcyToggle = new Label
            {
                Text = "Premium Ccy:",
                Location = new Point(390, 285),
                Size = new Size(115, 15),
                Font = new Font("Segoe UI", 8, FontStyle.Regular)
            };
            this.Controls.Add(_lblCcyToggle);

            // Initialize toggle buttons with current trade data
            UpdateCurrencyButtons();
            UpdateCutoffButton();

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
                "1: NY EURUSD 1M Buy Call @ 1.16 (10M EUR) PREM [Prem:Spot, Hedge:Spot]",
                "2: TKY USDJPY 10Dec26 Buy Put @ 155 (10M USD) PREM [Prem:Spot, Hedge:Spot]",
                "3: TKY USDJPY 08Nov26 Sell Call @ 156.00 (10M USD) PREM [Prem:Spot, Hedge:Spot]",
                "4: NY EURSEK 1M Sell Put @ 7.16 (10M SEK) PREM [Prem:Spot, Hedge:Spot]",
                "5: NY USDNOK 08Nov26 Buy Call @ 11.9 (10M USD) PREM [Prem:Spot, Hedge:Spot]",
                "6: NY EURNOK 6M Buy Put @ 7.807 (10M NOK) PREM [Prem:Spot, Hedge:Spot]",
                "7: TKY AUDUSD 9M Sell Call @ 0.69 (10M AUD) PREM Fwd [Prem:Fwd, Hedge:Fwd]",
                "8: NY AUDUSD 1Y Sell Put @ 0.65 (10M USD) PREM Fwd [Prem:Fwd, Hedge:Fwd]",
                "9: TKY USDJPY 08Mar26 Buy Call @ 147 (10M USD) PREM [Prem:Spot, Hedge:Spot]",
                "10: NY EURUSD 3M Buy Put @ 1.15 (10M USD) PREM [Prem:Spot, Hedge:Spot]",
                "11: TKY EURUSD 08Nov26 Sell Call @ 1.16 (10M EUR) PREM Fwd [Prem:Fwd, Hedge:Fwd]",
                "12: NY USDJPY 3M Sell Put @ 155 (10M JPY) PREM Fwd [Prem:Fwd, Hedge:Fwd]",
                "13: NY EURSEK 07Nov26 Buy Call @ 11.1 (10M EUR) PREM +Spot [Prem:Spot, Hedge:Spot]",
                "14: TKY GBPUSD 3M Buy Call @ 1.32 (10M GBP) PREM +Fwd [Prem:Spot, Hedge:Fwd]",
                "15: NY EURCHF 6M Buy Put @ 0.93 (10M EUR) PREM +Fwd [Prem:Spot, Hedge:Fwd]",

                // Structure tests (25-26 only, removed 16-24)
                "25: TKY USDJPY VOL 1M Buy Call @ 155 (10M JPY) VOL [Volatility Mode]",
                "26: CNH USDCNH 4M Sell Put @ 7.10 (10M USD) PREM [Prem:Spot, Hedge:Spot]"
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
                RowHeadersVisible = false,
                RowTemplate = { Height = 38 }  // Taller rows for two-line premium display
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

            // STP Blotter columns (only fields we have data for)
            // Identification & Status
            dgvBlotter.Columns.Add("TradeID", "Trade ID");
            dgvBlotter.Columns["TradeID"].Width = 200;
            dgvBlotter.Columns.Add("ExecTS", "EXEC TS");
            dgvBlotter.Columns["ExecTS"].Width = 80;
            dgvBlotter.Columns.Add("STPStatus", "STP Status");
            dgvBlotter.Columns["STPStatus"].Width = 95;

            // Party & Trade Info
            dgvBlotter.Columns.Add("MyCenter", "My Center");
            dgvBlotter.Columns["MyCenter"].Width = 75;
            dgvBlotter.Columns.Add("CCYPair", "CCY Pair");
            dgvBlotter.Columns["CCYPair"].Width = 80;
            dgvBlotter.Columns.Add("BuySell", "Buy/Sell");
            dgvBlotter.Columns["BuySell"].Width = 70;

            // Trade Details
            dgvBlotter.Columns.Add("Vol", "Vol");
            dgvBlotter.Columns["Vol"].Width = 70;
            dgvBlotter.Columns.Add("SizeM", "Size (M)");
            dgvBlotter.Columns["SizeM"].Width = 75;
            dgvBlotter.Columns.Add("Strike", "Strike");
            dgvBlotter.Columns["Strike"].Width = 85;
            dgvBlotter.Columns.Add("Delta", "Delta");
            dgvBlotter.Columns["Delta"].Width = 75;
            dgvBlotter.Columns.Add("Strategy", "Strategy");
            dgvBlotter.Columns["Strategy"].Width = 95;

            // Dates
            dgvBlotter.Columns.Add("Expiry", "Expiry");
            dgvBlotter.Columns["Expiry"].Width = 90;
            dgvBlotter.Columns.Add("Delivery", "Delivery");
            dgvBlotter.Columns["Delivery"].Width = 90;

            // Pricing
            dgvBlotter.Columns.Add("Price", "Price");
            dgvBlotter.Columns["Price"].Width = 85;
            dgvBlotter.Columns.Add("Counterparty", "Counterparty");
            dgvBlotter.Columns["Counterparty"].Width = 100;
            dgvBlotter.Columns.Add("Cut", "Cut");
            dgvBlotter.Columns["Cut"].Width = 55;
            dgvBlotter.Columns.Add("Spot", "Spot");
            dgvBlotter.Columns["Spot"].Width = 80;
            dgvBlotter.Columns.Add("Swap", "Swap");
            dgvBlotter.Columns["Swap"].Width = 70;
            dgvBlotter.Columns.Add("Depo", "Depo");
            dgvBlotter.Columns["Depo"].Width = 70;

            dgvBlotter.Columns.Add("TradeTime", "Trade Time");
            dgvBlotter.Columns["TradeTime"].Width = 85;

            this.Controls.Add(dgvBlotter);

            // Buttons - moved down for blotter with proper spacing
            btnRequestQuotes = new Button
            {
                Text = "Request Quotes",
                Location = new Point(20, 730),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            btnRequestQuotes.Click += BtnRequestQuotes_Click;
            this.Controls.Add(btnRequestQuotes);

            btnExecute = new Button
            {
                Text = "Sell (Hit Bid)",
                Location = new Point(190, 730),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Enabled = false
            };
            btnExecute.Click += (s, e) => BtnExecute_Click("SELL");
            this.Controls.Add(btnExecute);

            btnBuy = new Button
            {
                Text = "Buy (Lift Offer)",
                Location = new Point(360, 730),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Enabled = false
            };
            btnBuy.Click += (s, e) => BtnExecute_Click("BUY");
            this.Controls.Add(btnBuy);

            btnCancel = new Button
            {
                Text = "Close",
                Location = new Point(530, 730),
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

            // Update notional column headers based on current trade's currency pair
            if (_trade != null && _trade.Underlying?.Length >= 6)
            {
                string ccy1 = _trade.Underlying.Substring(0, 3);
                string ccy2 = _trade.Underlying.Substring(3, 3);

                if (dgvLegs.Columns["NotionalCcy1"] != null)
                {
                    dgvLegs.Columns["NotionalCcy1"].HeaderText = $"Notional ({ccy1})";
                }
                if (dgvLegs.Columns["NotionalCcy2"] != null)
                {
                    dgvLegs.Columns["NotionalCcy2"].HeaderText = $"Notional ({ccy2})";
                }

                Console.WriteLine($"[PopulateLegGrid] Updated column headers: {ccy1} / {ccy2}");
            }

            // Calculate premium date based on premium type
            // Spot Premium: T+2 from today (spot date)
            // Forward Premium: T+2 from expiry (delivery date)
            DateTime? spotPremiumDate = null;
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
                spotPremiumDate = spotDt;

                Console.WriteLine($"[PopulateLegGrid] Premium Type: {_premiumType}");
                Console.WriteLine($"[PopulateLegGrid] Spot Premium Date (T+2 from today): {spotDt:dd-MMM-yy}");
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
                string premiumDateText = "-";

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

                    // Premium Date depends on premium type
                    if (_premiumType == "Forward" && leg.DeliveryDate != DateTime.MinValue)
                    {
                        // Forward premium: paid at delivery date
                        premiumDateText = leg.DeliveryDate.ToString("dd-MMM-yy", enUS);
                        Console.WriteLine($"[PopulateLegGrid] Leg {i+1} Forward Premium Date: {premiumDateText}");
                    }
                    else if (spotPremiumDate.HasValue)
                    {
                        // Spot premium: paid at spot date (T+2 from today)
                        premiumDateText = spotPremiumDate.Value.ToString("dd-MMM-yy", enUS);
                        Console.WriteLine($"[PopulateLegGrid] Leg {i+1} Spot Premium Date: {premiumDateText}");
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

                // Calculate both notional amounts (Ccy1 and Ccy2)
                string ccy1 = _trade.Underlying.Substring(0, 3);
                string notionalCcy = leg.NotionalCurrency ?? ccy1;
                double notionalInMM = leg.NotionalMM;
                double strike = leg.Strike;

                double notionalCcy1, notionalCcy2;
                if (notionalCcy == ccy1)
                {
                    // Notional is in Ccy1, calculate Ccy2
                    notionalCcy1 = notionalInMM * 1000000;
                    notionalCcy2 = notionalCcy1 * strike;
                }
                else
                {
                    // Notional is in Ccy2, calculate Ccy1
                    notionalCcy2 = notionalInMM * 1000000;
                    notionalCcy1 = notionalCcy2 / strike;
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
                    notionalCcy1.ToString("N0"),
                    notionalCcy2.ToString("N0")
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
  // Format: "Rec USD 37,130\n37 pips" (two lines, centered like GFI)
     dgvQuotes.Columns["NetPremBid"].Width = 140;
      dgvQuotes.Columns["NetPremBid"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
     dgvQuotes.Columns["NetPremBid"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

  dgvQuotes.Columns.Add("NetPremOffer", "Net Prem (Offer)");
       // Format: "Pay USD 38,620\n39 pips" (two lines, centered like GFI)
    dgvQuotes.Columns["NetPremOffer"].Width = 150;
dgvQuotes.Columns["NetPremOffer"].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
     dgvQuotes.Columns["NetPremOffer"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

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
                    // Premium mode - format handled manually with "PTS CCY" suffix
                    dgvQuotes.Columns.Add($"Leg{i}BidPrem", $"L{i} Bid Prem");
                    dgvQuotes.Columns[$"Leg{i}BidPrem"].Width = 120;  // Increased for "PTS USD"

                    dgvQuotes.Columns.Add($"Leg{i}OfferPrem", $"L{i} Offer Prem");
                    dgvQuotes.Columns[$"Leg{i}OfferPrem"].Width = 130;  // Increased for "PTS USD"
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
            Console.WriteLine($"║ Hedge Type: {_hedgeType}");
            Console.WriteLine($"║ Premium Type: {_premiumType}");
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
                    string quoteReqID = _fixSession.SendQuoteRequest(_trade, lp, _groupId, _hedgeType, _premiumType);
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
            Console.WriteLine($"\n┌─────────────────────────────────────────────────┐");
            Console.WriteLine($"│ QUOTE REQUEST SUMMARY");
            Console.WriteLine($"├─────────────────────────────────────────────────┤");
            Console.WriteLine($"│ Successful: {successfulLPs.Count}/{lps.Count}  {string.Join(", ", successfulLPs)}");
            if (failedLPs.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"│ Failed: {failedLPs.Count}      {string.Join(", ", failedLPs)}");
            }
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"└─────────────────────────────────────────────────┘\n");
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
                    // Use NotionalCcy1 column (full amount) and convert to millions
                    var notionalStr = dgvLegs.Rows[i].Cells["NotionalCcy1"].Value?.ToString()?.Replace(",", "");

                    if (double.TryParse(notionalStr, out double notionalAmount))
                    {
                        // Convert from full amount to millions (e.g., 10,000,000 -> 10)
                        originalLeg.NotionalMM = notionalAmount / 1_000_000.0;
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

                // Log detailed leg information for debugging
                Console.WriteLine($"    NotionalMM: {leg.NotionalMM}");
                Console.WriteLine($"    Direction: {leg.Direction}");
                Console.WriteLine($"    OptionType: {leg.OptionType}");
                Console.WriteLine($"    Strike: {leg.Strike}");
                Console.WriteLine($"    Tenor: {leg.Tenor}");
                Console.WriteLine($"    ExpiryDate: {leg.ExpiryDate:dd-MM-yyyy HH:mm:ss}");
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
     var streams = _fixSession.Application.GetActiveStreams(_groupId);

     foreach (var stream in streams)
    {
       var rowData = new List<object>();
   rowData.Add(stream.LP);

       double? netPremBid = CalculateNetPremium(stream.BidQuote);
 double? netPremOffer = CalculateNetPremium(stream.OfferQuote);

         // Extract premium currency from the quote itself (Tag 5830)
      // This is the authoritative source - GFI tells us what currency the premium is in
         string quotePremCcyBid = stream.BidQuote?.Get("5830");
              string quotePremCcyOffer = stream.OfferQuote?.Get("5830");
         string quotePremCcy = quotePremCcyBid ?? quotePremCcyOffer;

       // Get spot rate for currency conversion (if needed)
     double spotRate = _trade?.SpotReference ?? 1.0;

       // Extract currencies from pair
string ccy1 = _trade?.Underlying?.Length >= 6 ? _trade.Underlying.Substring(0, 3) : "EUR";
    string ccy2 = _trade?.Underlying?.Length >= 6 ? _trade.Underlying.Substring(3, 3) : "USD";

    // Determine base premium currency:
 // 1. Use Tag 5830 from quote if available (most reliable)
         // 2. If USD is in the pair, premium is typically in USD
  // 3. Otherwise, premium is typically in the quote currency (ccy2)
             string basePremCcy;
       if (!string.IsNullOrEmpty(quotePremCcy))
    {
       // GFI told us explicitly what currency the premium is in
     basePremCcy = quotePremCcy;
    }
    else if (ccy1 == "USD" || ccy2 == "USD")
   {
     // If USD is in the pair, premium is in USD
         basePremCcy = "USD";
      }
   else
         {
          // For crosses without USD (e.g., EURSEK, EURJPY, GBPJPY)
        // Premium is typically in the quote currency (ccy2)
           basePremCcy = ccy2;
      }

  // Only convert if user wants a DIFFERENT currency than what we received
          double conversionFactor = 1.0;
      if (_premiumCurrency != basePremCcy && spotRate > 0)
       {
// Need to convert between currencies
    if (_premiumCurrency == ccy1 && basePremCcy == ccy2)
{
 // Convert from ccy2 to ccy1 (e.g., SEK to EUR): divide by spot
  conversionFactor = 1.0 / spotRate;
  }
       else if (_premiumCurrency == ccy2 && basePremCcy == ccy1)
  {
         // Convert from ccy1 to ccy2 (e.g., EUR to SEK): multiply by spot
conversionFactor = spotRate;
       }
       }

        double? displayBid = netPremBid.HasValue ? netPremBid.Value * conversionFactor : (double?)null;
    double? displayOffer = netPremOffer.HasValue ? netPremOffer.Value * conversionFactor : (double?)null;

        // Calculate pips from Tag 6436 (USD amount) / 1000 for consistency across all LPs
        // Tag 5844 is LP-specific and not reliable for comparison
        double? bidPips = displayBid.HasValue ? displayBid.Value / 1000.0 : (double?)null;
      double? offerPips = displayOffer.HasValue ? displayOffer.Value / 1000.0 : (double?)null;

        // Format like GFI: "Rec USD 37,130\n37 pips" centered
      string FormatPremiumCell(double? usdAmount, double? pips, string ccy)
        {
      if (!usdAmount.HasValue) return "-";
   
    string direction = usdAmount.Value >= 0 ? "Rec" : "Pay";
     string absAmount = Math.Abs(usdAmount.Value).ToString("N0");
       string pipsStr = pips.HasValue ? $"{Math.Abs(pips.Value):N0} pips" : "";
       
        return $"{direction} {ccy} {absAmount}\n{pipsStr}";
        }

     rowData.Add(FormatPremiumCell(displayBid, bidPips, _premiumCurrency));
        rowData.Add(FormatPremiumCell(displayOffer, offerPips, _premiumCurrency));

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
                        // Premium mode - show with "PTS CCY" label
                        double? bidPrem = GetLegPremium(stream.BidQuote, i);
                        double? offerPrem = GetLegPremium(stream.OfferQuote, i);

                        rowData.Add(bidPrem.HasValue ? $"{bidPrem.Value:N2} PTS {_premiumCurrency}" : "-");
                        rowData.Add(offerPrem.HasValue ? $"{offerPrem.Value:N2} PTS {_premiumCurrency}" : "-");
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

        // Highlighting logic based on display mode
     if (_showVolatility)
       {
    // VOLATILITY MODE - Only highlight best volatilities (NOT Net Premium)
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
       // PREMIUM MODE - Only highlight best Net Premium (based on Tag 6436)
       var (bestBid, bestOffer) = GetBestPremiums();

    if (bestBid.HasValue && netPremBid.HasValue && Math.Abs(netPremBid.Value - bestBid.Value) < 1.0)
         {
          dgvQuotes.Rows[rowIndex].Cells["NetPremBid"].Style.BackColor = Color.LightGreen;
          dgvQuotes.Rows[rowIndex].Cells["NetPremBid"].Style.Font = new Font(dgvQuotes.Font, FontStyle.Bold);
       }

      if (bestOffer.HasValue && netPremOffer.HasValue && Math.Abs(netPremOffer.Value - bestOffer.Value) < 1.0)
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

          // Get total notional from trade legs (in millions)
 double totalNotionalMM = _trade?.Legs?.Sum(l => l.NotionalMM) ?? 1.0;
        
            // Get currencies for logging
            string ccy1 = _trade?.Underlying?.Length >= 6 ? _trade.Underlying.Substring(0, 3) : "USD";
    string ccy2 = _trade?.Underlying?.Length >= 6 ? _trade.Underlying.Substring(3, 3) : "JPY";
string premCcy = _trade?.PremiumCurrency ?? ccy1; // For USD-based pairs, premium is typically in USD

 // PREFER Tag 6436 (Premium) if available
    // GFI sends premium as INTEGER in the quote currency (e.g., 33600 = $33,600 USD)
       string tag6436 = quote.Get("6436");
     if (!string.IsNullOrEmpty(tag6436) && double.TryParse(tag6436, 
      System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, 
  out double premium6436))
  {
           // Tag 6436 IS the actual premium amount in the premium currency
     // Note: Premium can be positive (you receive) or negative (you pay)
   Console.WriteLine($"[PREMIUM-DEBUG] {lpName} {side}:");
           Console.WriteLine($"  Tag6436 raw value: '{tag6436}'");
       Console.WriteLine($"  Parsed as: {premium6436:N2} {premCcy}");
   Console.WriteLine($"  Notional: {totalNotionalMM}MM");
    Console.WriteLine($"  Spot: {spotRef ?? "N/A"}");

       return premium6436;
   }

       // FALLBACK: Use LegPricing structure (sum of leg premiums)
  if (quote.LegPricing != null && quote.LegPricing.Count > 0)
      {
       double netPremPoints = 0;
       var legDetails = new List<string>();
      
      foreach (var leg in quote.LegPricing)
     {
   if (!string.IsNullOrEmpty(leg.LegPremPrice) && 
              double.TryParse(leg.LegPremPrice, 
       System.Globalization.NumberStyles.Any,
 System.Globalization.CultureInfo.InvariantCulture,
       out double prem))
    {
       netPremPoints += prem;
     legDetails.Add($"Leg{quote.LegPricing.IndexOf(leg)+1}={prem:F2}");
      }
     }

  Console.WriteLine($"[PREMIUM-DEBUG] {lpName} {side} (from LegPricing):");
     Console.WriteLine($"  Leg premiums: [{string.Join(", ", legDetails)}]");
     Console.WriteLine($"  Net points: {netPremPoints:F2}");
Console.WriteLine($"  Notional: {totalNotionalMM}MM");
         
   // LegPremPrice (tag 5844) is typically in PIPS per notional unit
       // For display, we may need to scale by notional
    // But first, let's see what the raw values are
       return netPremPoints;
       }

       // Fallback to old field structure for backwards compatibility
            double netPremOld = 0;
      for (int i = 1; i <= _selectedLegCount; i++)
 {
       var premStr = quote.Get($"leg{i}_5844");
       if (!string.IsNullOrEmpty(premStr) && 
         double.TryParse(premStr, 
     System.Globalization.NumberStyles.Any,
         System.Globalization.CultureInfo.InvariantCulture,
       out double prem))
{
         netPremOld += prem;
   }
  }

     Console.WriteLine($"[PREMIUM-DEBUG] {lpName} {side} (old format): {netPremOld:F2}");
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

        #region Dynamic UI Update Methods

        /// <summary>
/// Updates the currency toggle buttons based on the current trade's underlying pair.
        /// Called when the dialog loads or when a different test case is selected.
        /// </summary>
 private void UpdateCurrencyButtons()
        {
    if (_btnCcy1 == null || _btnCcy2 == null) return;

       // Extract currencies from pair (e.g., EURUSD -> EUR, USD)
        string ccy1 = _trade?.Underlying?.Length >= 6 ? _trade.Underlying.Substring(0, 3) : "USD";
  string ccy2 = _trade?.Underlying?.Length >= 6 ? _trade.Underlying.Substring(3, 3) : "JPY";

       // Update button text
            _btnCcy1.Text = ccy1;
        _btnCcy2.Text = ccy2;

    // Determine default premium currency:
            // - For USD-base pairs (USDJPY, USDCHF, etc.), default to USD (ccy1)
       // - For other pairs (EURUSD, GBPUSD, etc.), default to USD if present, otherwise ccy1
            if (ccy1 == "USD")
            {
                _premiumCurrency = ccy1;  // USD is base currency, use it
  }
       else if (ccy2 == "USD")
   {
     _premiumCurrency = ccy2;  // USD is quote currency, use it
            }
      else
         {
         _premiumCurrency = ccy1;  // No USD in pair, default to base currency
            }

            // Update button styles based on selection
       bool ccy1Selected = (_premiumCurrency == ccy1);
       _btnCcy1.BackColor = ccy1Selected ? Color.MediumPurple : Color.White;
            _btnCcy1.ForeColor = ccy1Selected ? Color.White : Color.Black;
        _btnCcy2.BackColor = ccy1Selected ? Color.White : Color.MediumPurple;
    _btnCcy2.ForeColor = ccy1Selected ? Color.Black : Color.White;

            Console.WriteLine($"[CCY] Updated currency buttons: {ccy1}/{ccy2}, default selection: {_premiumCurrency}");
        }

    private void BtnCcy1_Click(object sender, EventArgs e)
        {
     string ccy1 = _trade?.Underlying?.Length >= 6 ? _trade.Underlying.Substring(0, 3) : "USD";
          _premiumCurrency = ccy1;
       _btnCcy1.BackColor = Color.MediumPurple;
      _btnCcy1.ForeColor = Color.White;
     _btnCcy2.BackColor = Color.White;
            _btnCcy2.ForeColor = Color.Black;
UpdateQuoteDisplay(); // Refresh to show premiums in new currency
        }

     private void BtnCcy2_Click(object sender, EventArgs e)
  {
  string ccy2 = _trade?.Underlying?.Length >= 6 ? _trade.Underlying.Substring(3, 3) : "JPY";
            _premiumCurrency = ccy2;
  _btnCcy2.BackColor = Color.MediumPurple;
   _btnCcy2.ForeColor = Color.White;
       _btnCcy1.BackColor = Color.White;
            _btnCcy1.ForeColor = Color.Black;
  UpdateQuoteDisplay(); // Refresh to show premiums in new currency
        }
        /// <summary>
        /// Handles cell editing in dgvLegs - links notional columns and handles strike changes
        /// </summary>
        private void DgvLegs_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= dgvLegs.Rows.Count) return;

            var row = dgvLegs.Rows[e.RowIndex];
            string columnName = dgvLegs.Columns[e.ColumnIndex].Name;

            string ccy1 = _trade?.Underlying?.Length >= 6 ? _trade.Underlying.Substring(0, 3) : "EUR";
            string ccy2 = _trade?.Underlying?.Length >= 6 ? _trade.Underlying.Substring(3, 3) : "USD";

            // Handle Expiry column changes - calculate all dates
            if (columnName == "Expiry")
            {
                string expiryInput = row.Cells["Expiry"].Value?.ToString()?.Trim() ?? "";

                if (string.IsNullOrEmpty(expiryInput))
                {
                    Console.WriteLine("[EXPIRY EDIT] Empty expiry value");
                    return;
                }

                // Extract just the tenor if cell contains "3M (09-Jan-26)" format
                // Use regex to extract first word before parenthesis
                var tenorMatch = System.Text.RegularExpressions.Regex.Match(expiryInput, @"^(\S+)");
                if (tenorMatch.Success)
                {
                    expiryInput = tenorMatch.Groups[1].Value;
                }

                Console.WriteLine($"[EXPIRY EDIT] Parsed tenor: '{expiryInput}'");

                try
                {
                    // Determine premium currency (base currency of pair)
                    string premiumCcy = ccy1;

                    // Validate tenor format before calling ComputeDates
                    string tenorUpper = expiryInput.ToUpper();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(tenorUpper, @"^\d+[DWMY]$"))
                    {
                        throw new ArgumentException($"Invalid tenor format: '{expiryInput}'. Use format like: 3M, 6M, 1Y, 7D, 2W");
                    }

                    string underlying = _trade?.Underlying ?? "EURUSD";
                    Console.WriteLine($"[EXPIRY CALC] Input values: Underlying={underlying}, Tenor={tenorUpper}, PremiumCcy={premiumCcy}");

                    // Use FxDateService to calculate all dates
                    var rules = new FxDateRules();
                    var (tradeDate, spotDate, expiryDate, deliveryDate, premiumDate) =
                        FxDateService.ComputeDates(
                            DateTime.UtcNow,
                            underlying,
                            tenorUpper,
                            premiumCcy,
                            rules
                        );

                    // Update grid cells
                    row.Cells["Expiry"].Value = $"{tenorUpper} ({expiryDate:dd-MMM-yy})";
                    row.Cells["SettlementDate"].Value = deliveryDate.ToString("dd-MMM-yy");
                    row.Cells["PremiumDate"].Value = premiumDate.ToString("dd-MMM-yy");

                    Console.WriteLine($"[EXPIRY CALC] {tenorUpper} → Expiry: {expiryDate:dd-MMM-yy}, Settlement: {deliveryDate:dd-MMM-yy}, Premium: {premiumDate:dd-MMM-yy}");

                    // Update trade structure
                    if (_trade != null && e.RowIndex < _trade.Legs.Count)
                    {
                        _trade.Legs[e.RowIndex].Tenor = tenorUpper;
                        _trade.Legs[e.RowIndex].ExpiryDate = expiryDate;
                        _trade.Legs[e.RowIndex].DeliveryDate = deliveryDate;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EXPIRY EDIT] Error: {ex.Message}");
                    Console.WriteLine($"[EXPIRY EDIT] Stack trace: {ex.StackTrace}");
                    MessageBox.Show($"Could not calculate dates for '{expiryInput}'.\n\nPlease use format like: 3M, 6M, 1Y, 7D, 2W\n\nError: {ex.Message}",
                                    "Invalid Expiry", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            // Handle Strike column changes - recalculate notionals
            if (columnName == "Strike")
            {
                if (!double.TryParse(row.Cells["Strike"].Value?.ToString(), out double newStrike) || newStrike <= 0)
                {
                    Console.WriteLine("[STRIKE EDIT] Invalid strike value");
                    return;
                }

                // Get current notional in Ccy1 (keep this constant, recalculate Ccy2)
                if (double.TryParse(row.Cells["NotionalCcy1"].Value?.ToString(), out double ccy1Amount))
                {
                    double ccy2Amount = ccy1Amount * newStrike;
                    row.Cells["NotionalCcy2"].Value = ccy2Amount.ToString("N0");
                    Console.WriteLine($"[STRIKE CHANGE] {newStrike:F4} → {ccy2} recalculated: {ccy2Amount:N0}");

                    // Update trade structure
                    if (_trade.Legs.Count > e.RowIndex)
                    {
                        _trade.Legs[e.RowIndex].Strike = newStrike;
                    }
                }
                return;
            }

            // Handle notional column edits
            if (columnName != "NotionalCcy1" && columnName != "NotionalCcy2") return;

            // Get strike from Strike column
            if (!double.TryParse(row.Cells["Strike"].Value?.ToString(), out double strike) || strike <= 0)
            {
                Console.WriteLine("[NOTIONAL EDIT] Invalid strike value");
                return;
            }

            if (columnName == "NotionalCcy1")
            {
                // User edited Ccy1, calculate Ccy2
                string rawValue = row.Cells["NotionalCcy1"].Value?.ToString() ?? "";
                double ccy1Amount = ParseNotionalAmount(rawValue);

                if (ccy1Amount > 0)
                {
                    double ccy2Amount = ccy1Amount * strike;
                    row.Cells["NotionalCcy1"].Value = ccy1Amount.ToString("N0");
                    row.Cells["NotionalCcy2"].Value = ccy2Amount.ToString("N0");

                    Console.WriteLine($"[NOTIONAL] {ccy1} {ccy1Amount:N0} → {ccy2} {ccy2Amount:N0} (strike {strike})");

                    // Update trade structure
                    UpdateTradeNotionals(e.RowIndex, ccy1Amount, ccy1);
                }
            }
            else if (columnName == "NotionalCcy2")
            {
                // User edited Ccy2, calculate Ccy1
                string rawValue = row.Cells["NotionalCcy2"].Value?.ToString() ?? "";
                double ccy2Amount = ParseNotionalAmount(rawValue);

                if (ccy2Amount > 0)
                {
                    double ccy1Amount = ccy2Amount / strike;
                    row.Cells["NotionalCcy1"].Value = ccy1Amount.ToString("N0");
                    row.Cells["NotionalCcy2"].Value = ccy2Amount.ToString("N0");

                    Console.WriteLine($"[NOTIONAL] {ccy2} {ccy2Amount:N0} → {ccy1} {ccy1Amount:N0} (strike {strike})");

                    // Update trade structure
                    UpdateTradeNotionals(e.RowIndex, ccy2Amount, ccy2);
                }
            }
        }

        /// <summary>
        /// Parses notional amount with support for M (millions) and K (thousands)
        /// Examples: "15m" → 15000000, "250k" → 250000, "10000000" → 10000000
        /// </summary>
        private double ParseNotionalAmount(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return 0;

            input = input.Trim().Replace(",", "").Replace(" ", "");

            // Check for M or K suffix
            if (input.EndsWith("m", StringComparison.OrdinalIgnoreCase))
            {
                string numPart = input.Substring(0, input.Length - 1);
                if (double.TryParse(numPart, out double value))
                {
                    return value * 1000000;
                }
            }
            else if (input.EndsWith("k", StringComparison.OrdinalIgnoreCase))
            {
                string numPart = input.Substring(0, input.Length - 1);
                if (double.TryParse(numPart, out double value))
                {
                    return value * 1000;
                }
            }
            else if (double.TryParse(input, out double value))
            {
                return value;
            }

            return 0;
        }

        /// <summary>
        /// Updates trade structure when notional is edited in grid
        /// </summary>
        private void UpdateTradeNotionals(int rowIndex, double amount, string currency)
        {
            if (_trade == null || rowIndex >= _trade.Legs.Count) return;

            double notionalMM = amount / 1000000.0;
            _trade.Legs[rowIndex].NotionalMM = notionalMM;
            _trade.Legs[rowIndex].NotionalCurrency = currency;

            Console.WriteLine($"[NOTIONAL UPDATE] Leg {rowIndex + 1}: {notionalMM:F2}M {currency}");
        }

        /// <summary>
    /// Click handler for cutoff toggle button - cycles through NY -> TKY -> CNH -> NY
        /// </summary>
     private void BtnToggleCut_Click(object sender, EventArgs e)
        {
            // Cycle through cutoffs: NY -> TKY -> CNH -> NY
            _cutoff = _cutoff switch
    {
       "NY" => "TKY",
            "TKY" => "CNH",
    "CNH" => "NY",
           _ => "NY"
 };
      UpdateCutoffButton();
    }

        /// <summary>
        /// Updates the cutoff toggle button based on the current _cutoff value.
     /// Called when the dialog loads or when a different test case is selected.
   /// </summary>
  private void UpdateCutoffButton()
        {
     if (_btnToggleCut == null) return;

    _btnToggleCut.Text = $"Cut: {_cutoff}";

            // Color coding by cutoff
     (_btnToggleCut.BackColor, _btnToggleCut.FlatAppearance.BorderColor) = _cutoff switch
            {
     "NY" => (Color.LightSkyBlue, Color.DodgerBlue),
        "TKY" => (Color.LightSalmon, Color.OrangeRed),
           "CNH" => (Color.LightGreen, Color.Green),
        _ => (Color.LightSkyBlue, Color.DodgerBlue)
 };

            Console.WriteLine($"[CUT] Updated cutoff button: {_cutoff}");
        }

        private void UpdateQuoteTypeButton()
        {
            if (_btnToggleDisplay == null) return;

            // Get quote type from trade structure (default to PREM if not set)
            string quoteType = _trade?.QuoteType ?? "PREM";

            // Update button state based on quote type
            if (quoteType == "VOL")
            {
                _showVolatility = true;
                _btnToggleDisplay.Text = "Show: Volatility";
                _btnToggleDisplay.BackColor = Color.LightBlue;
                _btnToggleDisplay.FlatAppearance.BorderColor = Color.DodgerBlue;
            }
            else // PREM
            {
                _showVolatility = false;
                _btnToggleDisplay.Text = "Show: Premium";
                _btnToggleDisplay.BackColor = Color.LightGreen;
                _btnToggleDisplay.FlatAppearance.BorderColor = Color.Green;
            }

            Console.WriteLine($"[QUOTE TYPE] Updated button to: {quoteType} (_showVolatility={_showVolatility})");
        }

        private void UpdatePremiumTypeButton()
        {
            if (_btnTogglePremType == null) return;

            _btnTogglePremType.Text = $"Prem: {_premiumType}";

            // Color coding: Spot = Green, Forward = Blue
            if (_premiumType == "Forward")
            {
                _btnTogglePremType.BackColor = Color.LightBlue;
                _btnTogglePremType.FlatAppearance.BorderColor = Color.DodgerBlue;
            }
            else // Spot
            {
                _btnTogglePremType.BackColor = Color.LightGreen;
                _btnTogglePremType.FlatAppearance.BorderColor = Color.Green;
            }

            Console.WriteLine($"[PREMIUM TYPE] Updated to: {_premiumType}");
        }

        private void UpdateHedgeTypeDropdown()
        {
            if (_cmbHedgeType == null) return;

            // Update dropdown selection based on hedge type
            string displayValue = _hedgeType;
            if (_cmbHedgeType.Items.Contains(displayValue))
            {
                _cmbHedgeType.SelectedItem = displayValue;
            }

            Console.WriteLine($"[HEDGE TYPE] Updated dropdown to: {_hedgeType}");
        }

        #endregion

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

            // Insert at top (index 0) instead of adding to bottom
            dgvBlotter.Rows.Insert(0,
                // Identification & Status
                entry.ClOrdID,                                      // Trade ID
                entry.ExecTimestamp?.ToString("HH:mm:ss") ?? entry.TradeTime.ToString("HH:mm:ss"),  // EXEC TS
                entry.Status,                                       // STP Status

                // Party & Trade Info
                entry.MyCenter ?? entry.Cut ?? "-",                // My Center
                entry.Underlying,                                   // CCY Pair
                entry.Side,                                         // Buy/Sell

                // Trade Details
                FormatValue(entry.Volatility, "N4"),               // Vol
                entry.NotionalMM > 0 ? entry.NotionalMM.ToString("N1") : "-",  // Size (M)
                FormatValue(entry.Strike, "N4"),                   // Strike
                deltaDisplay,                                       // Delta
                strategyName,                                       // Strategy

                // Dates
                FormatDate(entry.ExpDate),                         // Expiry
                FormatDate(entry.SettlementDate),                  // Delivery

                // Pricing
                entry.NetPremium.ToString("N2"),                   // Price
                entry.LP ?? "-",                                   // // Counterparty
                entry.Cut ?? "-",                                  // Cut
                FormatValue(entry.SpotReference, "N4"),            // Spot
                FormatValue(entry.Swap, "N4"),                     // Swap
                FormatValue(entry.Depo, "N4"),                     // Depo

                entry.TradeTime.ToString("HH:mm:ss")               // Trade Time
            );

            // Color coding logic - now reference first row (index 0)
            ColorCodeBlotterRow(dgvBlotter.Rows[0], entry.Status);
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
                    row.DefaultCellStyle.BackColor = Color.LightGreen;  // Green for confirmed (STP success)
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

        // Update toggle buttons for new trade (ParseTestCase already set _cutoff, _hedgeType, _premiumType)
UpdateCurrencyButtons();
  UpdateCutoffButton();
                UpdateQuoteTypeButton();  // Update Vol/Premium button based on test case
                UpdatePremiumTypeButton();  // Update Premium Type button (Spot/Forward)
                UpdateHedgeTypeDropdown();  // Update Hedge Type dropdown (Live/Spot/Forward)

     // Generate group ID and store current test info
    string groupId = $"TEST-{testId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                _currentTestId = testId;
                _currentGroupId = groupId;

                // Send quote requests
                foreach (var lp in lps)
                {
                    try
                    {
                        string quoteReqID = _fixSession.SendQuoteRequest(trade, lp, groupId, _hedgeType, _premiumType);
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
            // "1: NY EURUSD 1M Buy Call @ 1.16 (10M EUR) PREM"
            // "2: TKY USDJPY 10Dec26 Buy Put @ 155 (10M USD)"
            // "13: NY EURSEK 07Nov26 Buy Call + Spot Hedge"
            // "25: TKY USDJPY VOL 1M Buy Call @ 155 (10M JPY) VOL"
            var parts = testDescription.Split(':');
            if (parts.Length < 2)
                return null;

            // Extract test ID
            int testId = int.Parse(parts[0].Trim());

            // Detect quote type, premium type, and hedge type from end of description
            string quoteType = "PREM";  // Default to premium
            string hedgeType = "Spot";  // Default to spot hedge
            string premiumType = "Spot";  // Default to spot premium

            string descriptionUpper = testDescription.ToUpper();

            // Parse suffixes: "PREM", "PREM Fwd", "PREM +Spot", "PREM +Fwd", "VOL"
            if (descriptionUpper.EndsWith("VOL"))
            {
                quoteType = "VOL";
                Console.WriteLine($"[TEST PARSE] Detected VOL mode for test {testId}");
            }
            else if (descriptionUpper.Contains("PREM FWD"))
            {
                quoteType = "PREM";
                premiumType = "Forward";
                hedgeType = "Forward";
                Console.WriteLine($"[TEST PARSE] Detected PREM Fwd mode for test {testId} (Premium: Forward, Hedge: Forward)");
            }
            else if (descriptionUpper.Contains("PREM +FWD"))
            {
                quoteType = "PREM";
                premiumType = "Spot";
                hedgeType = "Forward";
                Console.WriteLine($"[TEST PARSE] Detected PREM +Fwd mode for test {testId} (Premium: Spot, Hedge: Forward)");
            }
            else if (descriptionUpper.Contains("PREM +SPOT"))
            {
                quoteType = "PREM";
                premiumType = "Spot";
                hedgeType = "Spot";
                Console.WriteLine($"[TEST PARSE] Detected PREM +Spot mode for test {testId} (Premium: Spot, Hedge: Spot)");
            }
            else if (descriptionUpper.EndsWith("PREM"))
            {
                quoteType = "PREM";
                Console.WriteLine($"[TEST PARSE] Detected PREM mode for test {testId}");
            }

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

            // Update hedge and premium type toggles
            _hedgeType = hedgeType;
            _premiumType = premiumType;

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
                StructureType = "1",  // Default to vanilla, will adjust for structures
                QuoteType = quoteType,  // PREM or VOL based on test description
                HedgeType = hedgeType.ToUpper()  // SPOT or FORWARD
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

            // Extract notional amount and currency from test description
            // Format: "(10M EUR)" or "(50M USD)"
            double notionalMM = 10;  // Default
            string notionalCcy = ccy1;  // Default to base currency

            var notionalMatch = Regex.Match(testDescription, @"\((\d+(?:\.\d+)?)M?\s+([A-Z]{3})\)");
            if (notionalMatch.Success)
            {
                double amount = double.Parse(notionalMatch.Groups[1].Value);
                string currency = notionalMatch.Groups[2].Value;

                notionalMM = amount;
                notionalCcy = currency;

                Console.WriteLine($"[TEST PARSE] Extracted notional: {notionalMM}M {notionalCcy}");
            }

            // Helper to create a complete leg
            TradeStructure.OptionLeg CreateLeg(string legDirection, string legOptionType, int legIndex)
            {
                return new TradeStructure.OptionLeg
                {
                    Direction = legDirection,
                    OptionType = legOptionType.ToUpper(),
                    Strike = strike,  // Use official test protocol strike
                    NotionalMM = notionalMM,
                    Tenor = tenor,
                    ExpiryDate = expiry,
                    DeliveryDate = delivery,
                    NotionalCurrency = notionalCcy,  // Use extracted currency
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
            Console.WriteLine($"[AUTO-MARK] Trade status changed: {trade.Status}, CurrentTestID: {_currentTestId}");

            // Auto-mark test as successful when trade reaches CONFIRMED status
            if (trade.Status == "CONFIRMED" && _currentTestId > 0)
            {
                Console.WriteLine($"[AUTO-MARK] Looking for checkbox with TestID={_currentTestId}");

                // Find the checkbox for the current test
                var checkbox = _testCaseCheckboxes.FirstOrDefault(chk => (int)chk.Tag == _currentTestId);
                if (checkbox != null)
                {
                    Console.WriteLine($"[AUTO-MARK] Checkbox found. Already passed: {_passedTests.Contains(_currentTestId)}");

                    if (!_passedTests.Contains(_currentTestId))
                    {
                        // Mark as passed
                        if (checkbox.InvokeRequired)
                        {
                            checkbox.Invoke(new Action(() =>
                            {
                                _passedTests.Add(_currentTestId);
                                checkbox.BackColor = Color.LightGreen;
                                Console.WriteLine($"[TEST CASE {_currentTestId}] ✓✓✓ Auto-marked as SUCCESSFUL (trade confirmed) - GREEN CHECKBOX");
                            }));
                        }
                        else
                        {
                            _passedTests.Add(_currentTestId);
                            checkbox.BackColor = Color.LightGreen;
                            Console.WriteLine($"[TEST CASE {_currentTestId}] ✓✓✓ Auto-marked as SUCCESSFUL (trade confirmed) - GREEN CHECKBOX");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("[AUTO-MARK] ✗ Checkbox NOT found for TestID=" + _currentTestId);
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