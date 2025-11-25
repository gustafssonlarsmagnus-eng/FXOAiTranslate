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
        private System.Windows.Forms.Timer _countdownTimer;
        private DataGridView dgvQuotes;
        private DataGridView dgvLegs;
        private DataGridView dgvBlotter;  // NEW: Trade blotter grid
        private Button btnRequestQuotes;
        private Button btnExecute;
        private Button btnCancel;
        private Button btnBuy;
        private Label lblTradeSummary;
        private GroupBox gbLPs;
        private CheckBox chkMS;
        private CheckBox chkUBS;
        private CheckBox chkNatwest;
        private CheckBox chkGoldman;
        private CheckBox chkBarclays;
        private CheckBox chkHSBC;
        private CheckBox chkBNP;
        private CheckBox chkCIBC;
        private CheckBox chkDeut;
        private CheckBox chkDBS;
        private int _selectedLegCount;
        private ToggleSwitch togglePremiumCurrency;  // Toggle for premium currency display

        public GFIQuoteDialog(dynamic ovmlResult)
        {
            InitializeComponent();
            InitializeCustomComponents();

            _trade = OVMLBridge.ConvertToTradeStructure(ovmlResult);
            _fixSession = GlobalFIXSession.Instance;  // Changed

            lblTradeSummary.Text = $"{_trade.StructureType}: {_trade.Underlying} - {_trade.Legs.Count} legs";
            PopulateLegGrid();

            // Initialize currency conversion checkbox (needs _trade to be set first)
            InitializeCurrencyConversionCheckbox();

            // Subscribe to quote events
            _fixSession.Application.OnQuoteReceived += OnQuoteReceivedFromFIX;

            // Subscribe to STP Trade Capture Reports
            try
            {
                var stpSession = GlobalSTPSession.Instance;
                stpSession.Application.OnTradeCaptureReceived += OnTradeCaptureReceived;
                Console.WriteLine("[GFIQuoteDialog] ✓ Subscribed to STP Trade Capture Reports");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GFIQuoteDialog] ⚠ Failed to connect to STP session: {ex.Message}");
                Console.WriteLine($"[GFIQuoteDialog] Trades will not receive final confirmations");
            }
        }

        private void InitializeCustomComponents()
        {
            this.Text = "GFI Fenics - Request Quotes";
            this.Size = new Size(1000, 850);  // Increased height for blotter
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            lblTradeSummary = new Label
            {
                Location = new Point(20, 20),
                Size = new Size(940, 30),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Text = "Loading trade..."
            };
            this.Controls.Add(lblTradeSummary);

            var lblLegs = new Label
            {
                Text = "Select Legs & Edit Notionals:",
                Location = new Point(20, 60),
                Size = new Size(200, 20),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            this.Controls.Add(lblLegs);

            dgvLegs = new DataGridView
            {
                Location = new Point(20, 85),
                Size = new Size(940, 120),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,  // Allow cell editing
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                RowHeadersVisible = false,
                EditMode = DataGridViewEditMode.EditOnEnter  // Enable editing
            };

            var chkCol = new DataGridViewCheckBoxColumn
            {
                Name = "Include",
                HeaderText = "Include",
                Width = 60,
                TrueValue = true,
                FalseValue = false
            };
            dgvLegs.Columns.Add(chkCol);

            dgvLegs.Columns.Add("Leg", "Leg");
            dgvLegs.Columns["Leg"].Width = 50;
            dgvLegs.Columns["Leg"].ReadOnly = true;  // Not editable

            dgvLegs.Columns.Add("Direction", "Direction");
            dgvLegs.Columns["Direction"].Width = 70;
            dgvLegs.Columns["Direction"].ReadOnly = true;  // Not editable

            dgvLegs.Columns.Add("Type", "Type");
            dgvLegs.Columns["Type"].Width = 50;
            dgvLegs.Columns["Type"].ReadOnly = true;  // Not editable

            // Editable columns
            var strikeCol = new DataGridViewTextBoxColumn
            {
                Name = "Strike",
                HeaderText = "Strike",
                Width = 80,
                ReadOnly = false  // EDITABLE
            };
            dgvLegs.Columns.Add(strikeCol);

            var tenorCol = new DataGridViewTextBoxColumn
            {
                Name = "Tenor",
                HeaderText = "Tenor",
                Width = 60,
                ReadOnly = false  // EDITABLE
            };
            dgvLegs.Columns.Add(tenorCol);

            var expiryCol = new DataGridViewTextBoxColumn
            {
                Name = "ExpiryDate",
                HeaderText = "Expiry Date",
                Width = 100,
                ReadOnly = false  // EDITABLE
            };
            dgvLegs.Columns.Add(expiryCol);

            var notionalCol = new DataGridViewTextBoxColumn
            {
                Name = "NotionalMM",
                HeaderText = "Notional (MM)",
                Width = 90,
                ReadOnly = false  // EDITABLE
            };
            dgvLegs.Columns.Add(notionalCol);

            // Add cell formatting to highlight editable cells
            dgvLegs.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0) return;

                // Highlight editable columns with light yellow background
                var colName = dgvLegs.Columns[e.ColumnIndex].Name;
                if (colName == "Strike" || colName == "Tenor" || colName == "ExpiryDate" || colName == "NotionalMM")
                {
                    e.CellStyle.BackColor = Color.LightYellow;
                }
            };

            // Add event handler for tenor changes - auto-calculate expiry date
            dgvLegs.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

                var colName = dgvLegs.Columns[e.ColumnIndex].Name;
                if (colName == "Tenor")
                {
                    // Recalculate expiry date when tenor changes
                    var tenorStr = dgvLegs.Rows[e.RowIndex].Cells["Tenor"].Value?.ToString();
                    if (!string.IsNullOrEmpty(tenorStr))
                    {
                        try
                        {
                            // Use FxDateService to calculate proper expiry date
                            var pair = _trade.Underlying;
                            var premiumCcy = _trade.PremiumCurrency;
                            var policy = GlobalDatePolicy.Policy;

                            var rules = new FxDateRules
                            {
                                Ccy1 = pair.Substring(0, 3),
                                Ccy2 = pair.Substring(3, 3),
                                SpotLag = policy.SpotLagForPair(pair),
                                ExpiryConvention = policy.ExpiryConvention,
                                ExpiryEOM = policy.ExpiryEOM,
                                PremiumSettleDays = policy.PremiumSettleDays,
                                PremiumCalMode = policy.PremiumCalendarMode,
                                PremiumConvention = policy.PremiumConvention
                            };

                            var nowUtc = DateTime.UtcNow;
                            var (_, _, expiryDate, deliveryDate, _) =
                                FxDateService.ComputeDates(nowUtc, pair, tenorStr.Trim().ToUpperInvariant(), premiumCcy, rules);

                            // Update the expiry date cell
                            dgvLegs.Rows[e.RowIndex].Cells["ExpiryDate"].Value = expiryDate.ToString("dd MMM yyyy");

                            // Also update the underlying trade object
                            if (e.RowIndex < _trade.Legs.Count)
                            {
                                _trade.Legs[e.RowIndex].Tenor = tenorStr.Trim().ToUpperInvariant();
                                _trade.Legs[e.RowIndex].ExpiryDate = expiryDate;
                                _trade.Legs[e.RowIndex].DeliveryDate = deliveryDate;
                            }
                        }
                        catch (Exception ex)
                        {
                            // Silently handle expiry recalculation errors
                        }
                    }
                }
            };

            // Enable edit notifications
            dgvLegs.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (dgvLegs.IsCurrentCellDirty)
                {
                    dgvLegs.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };

            this.Controls.Add(dgvLegs);

            // LP Selection GroupBox - EXPANDED
            gbLPs = new GroupBox
            {
                Text = "Select Liquidity Providers",
                Location = new Point(20, 220),
                Size = new Size(940, 90)  // Increased height for 2 rows
            };
            this.Controls.Add(gbLPs);

            // Row 1 - Major Banks
            chkMS = new CheckBox
            {
                Text = "Morgan Stanley",
                Location = new Point(20, 25),
                Size = new Size(150, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkMS);

            chkGoldman = new CheckBox
            {
                Text = "Goldman Sachs",
                Location = new Point(190, 25),
                Size = new Size(150, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkGoldman);

            chkBarclays = new CheckBox
            {
                Text = "Barclays",
                Location = new Point(360, 25),
                Size = new Size(150, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkBarclays);

            chkHSBC = new CheckBox
            {
                Text = "HSBC",
                Location = new Point(530, 25),
                Size = new Size(150, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkHSBC);

            chkBNP = new CheckBox
            {
                Text = "BNP Paribas",
                Location = new Point(700, 25),
                Size = new Size(150, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkBNP);

            // Row 2 - Additional Banks
            chkUBS = new CheckBox
            {
                Text = "UBS",
                Location = new Point(20, 55),
                Size = new Size(150, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkUBS);

            chkNatwest = new CheckBox
            {
                Text = "NatWest Markets",
                Location = new Point(190, 55),
                Size = new Size(150, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkNatwest);

            chkCIBC = new CheckBox
            {
                Text = "CIBC",
                Location = new Point(360, 55),
                Size = new Size(150, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkCIBC);

            chkDeut = new CheckBox
            {
                Text = "Deutsche Bank",
                Location = new Point(530, 55),
                Size = new Size(150, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkDeut);

            chkDBS = new CheckBox
            {
                Text = "DBS Bank",
                Location = new Point(700, 55),
                Size = new Size(150, 25),
                Checked = false
            };
            gbLPs.Controls.Add(chkDBS);

            // Quotes Grid - reduced size to make room for blotter
            dgvQuotes = new DataGridView
            {
                Location = new Point(20, 340),  // Moved down 10px for checkbox
                Size = new Size(940, 190),      // Reduced by 10px
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

            // Trade Blotter Grid
            var lblBlotter = new Label
            {
                Text = "Trade Blotter:",
                Location = new Point(20, 540),  // Adjusted for checkbox
                Size = new Size(200, 20),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            this.Controls.Add(lblBlotter);

            dgvBlotter = new DataGridView
            {
                Location = new Point(20, 565),  // Adjusted for checkbox
                Size = new Size(940, 135),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                RowHeadersVisible = false
            };

            // Blotter columns
            dgvBlotter.Columns.Add("Time", "Time");
            dgvBlotter.Columns["Time"].Width = 35;
            dgvBlotter.Columns.Add("ClOrdID", "Order ID");
            dgvBlotter.Columns["ClOrdID"].Width = 150;
            dgvBlotter.Columns.Add("LP", "LP");
            dgvBlotter.Columns["LP"].Width = 80;
            dgvBlotter.Columns.Add("Side", "Side");
            dgvBlotter.Columns["Side"].Width = 60;
            dgvBlotter.Columns.Add("Symbol", "Symbol");
            dgvBlotter.Columns["Symbol"].Width = 80;
            dgvBlotter.Columns.Add("Structure", "Structure");
            dgvBlotter.Columns["Structure"].Width = 100;
            dgvBlotter.Columns.Add("Premium", "Premium");
            dgvBlotter.Columns["Premium"].Width = 80;
            dgvBlotter.Columns.Add("Delta", "Delta");
            dgvBlotter.Columns["Delta"].Width = 70;
            dgvBlotter.Columns.Add("Volatility", "Vol");
            dgvBlotter.Columns["Volatility"].Width = 70;
            dgvBlotter.Columns.Add("Status", "Status");
            dgvBlotter.Columns["Status"].Width = 100;

            this.Controls.Add(dgvBlotter);

            // Buttons - moved down for blotter
            btnRequestQuotes = new Button
            {
                Text = "Request Quotes",
                Location = new Point(20, 715),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            btnRequestQuotes.Click += BtnRequestQuotes_Click;
            this.Controls.Add(btnRequestQuotes);

            btnExecute = new Button
            {
                Text = "Sell (Hit Bid)",
                Location = new Point(190, 715),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Enabled = false
            };
            btnExecute.Click += (s, e) => BtnExecute_Click("SELL");
            this.Controls.Add(btnExecute);

            btnBuy = new Button
            {
                Text = "Buy (Lift Offer)",
                Location = new Point(360, 715),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Enabled = false
            };
            btnBuy.Click += (s, e) => BtnExecute_Click("BUY");
            this.Controls.Add(btnBuy);

            btnCancel = new Button
            {
                Text = "Close",
                Location = new Point(530, 715),
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

        private void InitializeCurrencyConversionCheckbox()
        {
            // Premium currency toggle - dynamically set text based on pair
            string baseCcy = _trade.Underlying.Substring(0, 3);  // e.g., "EUR" from "EURUSD"
            string termCcy = _trade.Underlying.Substring(3, 3);  // e.g., "USD" from "EURUSD"

            togglePremiumCurrency = new ToggleSwitch
            {
                Location = new Point(20, 315),    // Left-aligned with other controls
                Size = new Size(120, 24),         // Compact size (2/3 of previous)
                LeftText = termCcy,               // Off state shows term currency (USD for EURUSD)
                RightText = baseCcy,              // On state shows base currency (EUR for EURUSD)
                Checked = false                   // Default: show in term currency (unchecked)
            };
            togglePremiumCurrency.CheckedChanged += (s, e) => UpdateQuoteDisplay();  // Refresh display when toggled
            this.Controls.Add(togglePremiumCurrency);
        }

        private void PopulateLegGrid()
        {
            dgvLegs.Rows.Clear();

            for (int i = 0; i < _trade.Legs.Count; i++)
            {
                var leg = _trade.Legs[i];
                dgvLegs.Rows.Add(
                    true,                                  // Include checkbox
                    $"Leg {i + 1}",                        // Leg number
                    leg.Direction,                         // BUY/SELL
                    leg.OptionType,                        // CALL/PUT
                    leg.Strike.ToString("F4"),             // Strike (EDITABLE)
                    leg.Tenor,                             // Tenor (EDITABLE)
                    leg.ExpiryDate.ToString("dd MMM yyyy"), // Expiry Date (EDITABLE)
                    leg.NotionalMM.ToString("F1")          // Notional (EDITABLE)
                );
            }
        }

        private void SetupQuoteGrid(int legCount)
        {
            dgvQuotes.Columns.Clear();
            _selectedLegCount = legCount;

            dgvQuotes.Columns.Add("LP", "LP");
            dgvQuotes.Columns["LP"].Width = 80;

            // Will be updated dynamically based on currency display mode
            dgvQuotes.Columns.Add("NetPremBid", "Rec");
            dgvQuotes.Columns["NetPremBid"].DefaultCellStyle.Format = "N0"; // Raw integer values
            dgvQuotes.Columns["NetPremBid"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvQuotes.Columns["NetPremBid"].Width = 100;

            dgvQuotes.Columns.Add("NetPremOffer", "Pay");
            dgvQuotes.Columns["NetPremOffer"].DefaultCellStyle.Format = "N0"; // Raw integer values
            dgvQuotes.Columns["NetPremOffer"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvQuotes.Columns["NetPremOffer"].Width = 110;

            // Update headers with currency
            UpdatePremiumColumnHeaders();

            for (int i = 1; i <= legCount; i++)
            {
                dgvQuotes.Columns.Add($"Leg{i}BidVol", $"L{i} Bid Vol");
                dgvQuotes.Columns[$"Leg{i}BidVol"].DefaultCellStyle.Format = "N2";
                dgvQuotes.Columns[$"Leg{i}BidVol"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dgvQuotes.Columns[$"Leg{i}BidVol"].Width = 80;

                dgvQuotes.Columns.Add($"Leg{i}OfferVol", $"L{i} Offer Vol");
                dgvQuotes.Columns[$"Leg{i}OfferVol"].DefaultCellStyle.Format = "N2";
                dgvQuotes.Columns[$"Leg{i}OfferVol"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dgvQuotes.Columns[$"Leg{i}OfferVol"].Width = 90;
            }

            dgvQuotes.Columns.Add("TTL", "Expires In");
            dgvQuotes.Columns["TTL"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvQuotes.Columns["TTL"].Width = 90;
            dgvQuotes.Columns.Add("ValidUntilTime", "ValidUntilTime");  // Hidden column to store expiry time
            dgvQuotes.Columns["ValidUntilTime"].Visible = false;
        }

        private void UpdatePremiumColumnHeaders()
        {
            // Check if columns exist
            if (!dgvQuotes.Columns.Contains("NetPremBid") || !dgvQuotes.Columns.Contains("NetPremOffer"))
                return;

            // Get base and term currencies from the pair
            string baseCcy = _trade.Underlying.Substring(0, 3);  // EUR or USD
            string termCcy = _trade.Underlying.Substring(3, 3);  // USD or SEK

            // Determine currency to display
            string displayCcy = "";
            if (togglePremiumCurrency != null && togglePremiumCurrency.Checked)
            {
                // When checked, showing in base currency (converted)
                displayCcy = baseCcy;
            }
            else
            {
                // When unchecked, showing in term currency (what LPs typically send)
                displayCcy = termCcy;
            }

            // Lock column widths to prevent resizing when header text changes
            dgvQuotes.Columns["NetPremBid"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            dgvQuotes.Columns["NetPremOffer"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;

            // Update column headers
            dgvQuotes.Columns["NetPremBid"].HeaderText = $"Rec {displayCcy}";
            dgvQuotes.Columns["NetPremOffer"].HeaderText = $"Pay {displayCcy}";
        }

        private void BtnRequestQuotes_Click(object sender, EventArgs e)
        {
            var lps = new List<string>();

            // Check all LP checkboxes
            if (chkMS.Checked) lps.Add("MS");
            if (chkGoldman.Checked) lps.Add("GOLDMAN");
            if (chkBarclays.Checked) lps.Add("BARCLAYS");
            if (chkHSBC.Checked) lps.Add("HSBC");
            if (chkBNP.Checked) lps.Add("BNP");
            if (chkUBS.Checked) lps.Add("UBS");
            if (chkNatwest.Checked) lps.Add("NATWEST");
            if (chkCIBC.Checked) lps.Add("CIBC");
            if (chkDeut.Checked) lps.Add("DEUT");
            if (chkDBS.Checked) lps.Add("DBS");

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

            // Generate group ID
            _groupId = $"3-REQ{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            // Send quote request to each LP
            // Note: hedge parameter defaults to false - GFI sends both BID and OFFER quotes regardless
            foreach (var lp in lps)
            {
                try
                {
                    string quoteReqID = _fixSession.SendQuoteRequest(_trade, lp, _groupId);
                }
                catch (Exception ex)
                {
                    // Silently handle errors
                }
            }
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

                    // Read Strike
                    var strikeStr = dgvLegs.Rows[i].Cells["Strike"].Value?.ToString();
                    if (double.TryParse(strikeStr, out double strike))
                    {
                        originalLeg.Strike = strike;
                    }

                    // Read Tenor
                    var tenor = dgvLegs.Rows[i].Cells["Tenor"].Value?.ToString();
                    if (!string.IsNullOrEmpty(tenor))
                    {
                        originalLeg.Tenor = tenor.Trim().ToUpperInvariant();
                    }

                    // Read Expiry Date
                    var expiryStr = dgvLegs.Rows[i].Cells["ExpiryDate"].Value?.ToString();
                    if (DateTime.TryParse(expiryStr, out DateTime expiryDate))
                    {
                        originalLeg.ExpiryDate = expiryDate;
                        // Also update delivery date (T+2 from expiry)
                        originalLeg.DeliveryDate = expiryDate.AddDays(2);
                    }

                    // Read Notional
                    var notionalStr = dgvLegs.Rows[i].Cells["NotionalMM"].Value?.ToString();
                    if (double.TryParse(notionalStr, out double notionalMM))
                    {
                        originalLeg.NotionalMM = notionalMM;
                    }

                    selectedLegs.Add(originalLeg);
                }
            }

            _trade.Legs = selectedLegs;
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

            UpdateQuoteDisplay();
        }

        private void UpdateQuoteDisplay()
        {
            // Update column headers based on currency display mode
            UpdatePremiumColumnHeaders();

            // Suspend drawing to prevent flicker
            SendMessage(dgvQuotes.Handle, WM_SETREDRAW, false, 0);

            try
            {
                var streams = _fixSession.Application.GetActiveStreams(_groupId);

                // Update existing rows or add new ones (don't clear and rebuild)
                foreach (var stream in streams)
                {
                    // Find existing row for this LP
                    int rowIndex = -1;
                    for (int i = 0; i < dgvQuotes.Rows.Count; i++)
                    {
                        if (dgvQuotes.Rows[i].Cells["LP"].Value?.ToString() == stream.LP)
                        {
                            rowIndex = i;
                            break;
                        }
                    }

                    double? netPremBid = CalculateNetPremium(stream.BidQuote);
                    double? netPremOffer = CalculateNetPremium(stream.OfferQuote);

                    // If row doesn't exist, create it
                    if (rowIndex == -1)
                    {
                        var rowData = new List<object>();
                        rowData.Add(stream.LP);
                        rowData.Add(netPremBid?.ToString("N0") ?? "-");
                        rowData.Add(netPremOffer?.ToString("N0") ?? "-");

                        for (int i = 1; i <= _selectedLegCount; i++)
                        {
                            double? bidVol = GetLegVol(stream.BidQuote, i);
                            double? offerVol = GetLegVol(stream.OfferQuote, i);
                            rowData.Add(bidVol?.ToString("N2") ?? "-");
                            rowData.Add(offerVol?.ToString("N2") ?? "-");
                        }

                        string validUntilStr = stream.OfferQuote?.Get("62") ?? stream.BidQuote?.Get("62");
                        rowData.Add(""); // TTL - handled by timer
                        rowData.Add(validUntilStr ?? "");

                        rowIndex = dgvQuotes.Rows.Add(rowData.ToArray());
                    }
                    else
                    {
                        // Update existing row (don't touch TTL column - let timer handle it)
                        dgvQuotes.Rows[rowIndex].Cells["NetPremBid"].Value = netPremBid?.ToString("N0") ?? "-";
                        dgvQuotes.Rows[rowIndex].Cells["NetPremOffer"].Value = netPremOffer?.ToString("N0") ?? "-";

                        for (int i = 1; i <= _selectedLegCount; i++)
                        {
                            double? bidVol = GetLegVol(stream.BidQuote, i);
                            double? offerVol = GetLegVol(stream.OfferQuote, i);
                            dgvQuotes.Rows[rowIndex].Cells[$"Leg{i}BidVol"].Value = bidVol?.ToString("N2") ?? "-";
                            dgvQuotes.Rows[rowIndex].Cells[$"Leg{i}OfferVol"].Value = offerVol?.ToString("N2") ?? "-";
                        }

                        // Update ValidUntilTime only (TTL is handled by timer)
                        string validUntilStr = stream.OfferQuote?.Get("62") ?? stream.BidQuote?.Get("62");
                        if (!string.IsNullOrEmpty(validUntilStr))
                        {
                            dgvQuotes.Rows[rowIndex].Cells["ValidUntilTime"].Value = validUntilStr;
                        }
                    }

                    // Reset cell styles
                    dgvQuotes.Rows[rowIndex].Cells["NetPremBid"].Style.BackColor = Color.White;
                    dgvQuotes.Rows[rowIndex].Cells["NetPremBid"].Style.Font = dgvQuotes.Font;
                    dgvQuotes.Rows[rowIndex].Cells["NetPremOffer"].Style.BackColor = Color.White;
                    dgvQuotes.Rows[rowIndex].Cells["NetPremOffer"].Style.Font = dgvQuotes.Font;
                }

                // Apply best price highlighting
                var (bestBid, bestOffer) = GetBestPremiums();
                for (int i = 0; i < dgvQuotes.Rows.Count; i++)
                {
                    var bidStr = dgvQuotes.Rows[i].Cells["NetPremBid"].Value?.ToString();
                    var offerStr = dgvQuotes.Rows[i].Cells["NetPremOffer"].Value?.ToString();

                    if (bestBid.HasValue && double.TryParse(bidStr?.Replace(",", ""), out double bidVal) &&
                        Math.Abs(bidVal - bestBid.Value) < 0.01)
                    {
                        dgvQuotes.Rows[i].Cells["NetPremBid"].Style.BackColor = Color.LightGreen;
                        dgvQuotes.Rows[i].Cells["NetPremBid"].Style.Font = new Font(dgvQuotes.Font, FontStyle.Bold);
                    }

                    if (bestOffer.HasValue && double.TryParse(offerStr?.Replace(",", ""), out double offerVal) &&
                        Math.Abs(offerVal - bestOffer.Value) < 0.01)
                    {
                        dgvQuotes.Rows[i].Cells["NetPremOffer"].Style.BackColor = Color.LightGreen;
                        dgvQuotes.Rows[i].Cells["NetPremOffer"].Style.Font = new Font(dgvQuotes.Font, FontStyle.Bold);
                    }
                }

                // Enable execute buttons if we have quotes
                if (streams.Any(s => s.BidQuote != null || s.OfferQuote != null))
                {
                    btnExecute.Enabled = true;
                    btnBuy.Enabled = true;
                }

                // Clear selection so best price highlighting is visible
                dgvQuotes.ClearSelection();

                // Start countdown timer
                if (!_countdownTimer.Enabled)
                    _countdownTimer.Start();
            }
            finally
            {
                // Resume drawing and refresh once
                SendMessage(dgvQuotes.Handle, WM_SETREDRAW, true, 0);
                dgvQuotes.Refresh();
            }
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            // Suspend drawing completely using WM_SETREDRAW
            SendMessage(dgvQuotes.Handle, WM_SETREDRAW, false, 0);

            try
            {
                // Update TTL for each row
                for (int i = 0; i < dgvQuotes.Rows.Count; i++)
                {
                    var validUntilStr = dgvQuotes.Rows[i].Cells["ValidUntilTime"].Value?.ToString();
                    if (string.IsNullOrEmpty(validUntilStr))
                    {
                        dgvQuotes.Rows[i].Cells["TTL"].Value = "-";
                        continue;
                    }

                    // Parse ValidUntilTime as UTC (FIX timestamps are in UTC)
                    if (DateTime.TryParseExact(validUntilStr, "yyyyMMdd-HH:mm:ss", null,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out DateTime validUntilUtc))
                    {
                        // Compare with UTC time
                        TimeSpan remaining = validUntilUtc - DateTime.UtcNow;

                        if (remaining.TotalSeconds > 0)
                        {
                            dgvQuotes.Rows[i].Cells["TTL"].Value = $"{(int)remaining.TotalMinutes}:{remaining.Seconds:D2}";
                        }
                        else
                        {
                            dgvQuotes.Rows[i].Cells["TTL"].Value = "EXPIRED";
                        }
                    }
                    else
                    {
                        dgvQuotes.Rows[i].Cells["TTL"].Value = "-";
                    }
                }
            }
            finally
            {
                // Resume drawing and refresh once
                SendMessage(dgvQuotes.Handle, WM_SETREDRAW, true, 0);
                dgvQuotes.Refresh();
            }
        }


        private void DgvQuotes_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            // Format TTL column colors
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

            // Get premium currency from tag 9073 (inbound quote) or tag 5830 (fallback)
            string premiumCcy = quote.Get("9073") ?? quote.Get("5830") ?? "";

            double? rawPremium = null;

            // PREFER Tag 6436 (Premium) if available - display raw value
            string tag6436 = quote.Get("6436");
            if (!string.IsNullOrEmpty(tag6436) && double.TryParse(tag6436, out double premium6436))
            {
                rawPremium = premium6436;

                // DIAGNOSTIC: Log tag 6436 value with currency
                Console.WriteLine($"[PREMIUM-DEBUG] {lpName} {side}: Tag 6436 = {premium6436} {premiumCcy}");
            }
            // FALLBACK: Use LegPricing structure - display raw values
            else if (quote.LegPricing != null && quote.LegPricing.Count > 0)
            {
                double netPrem = 0;
                Console.WriteLine($"[PREMIUM-DEBUG] {lpName} {side}: No tag 6436, using LegPricing (count={quote.LegPricing.Count})");
                foreach (var leg in quote.LegPricing)
                {
                    if (!string.IsNullOrEmpty(leg.LegPremPrice) && double.TryParse(leg.LegPremPrice, out double prem))
                    {
                        Console.WriteLine($"[PREMIUM-DEBUG] {lpName} {side}: Tag 5844 (LegPremPrice) = {prem}");
                        netPrem += prem;
                    }
                }
                rawPremium = netPrem;
                Console.WriteLine($"[PREMIUM-DEBUG] {lpName} {side}: Total from LegPricing = {netPrem}");
            }
            // Fallback to old field structure for backwards compatibility
            else
            {
                double netPremOld = 0;
                for (int i = 1; i <= _selectedLegCount; i++)
                {
                    var premStr = quote.Get($"leg{i}_5844");
                    if (!string.IsNullOrEmpty(premStr) && double.TryParse(premStr, out double prem))
                    {
                        netPremOld += prem;
                    }
                }
                rawPremium = netPremOld;
            }

            // DIAGNOSTIC: Log volatility for comparison
            if (quote.LegPricing != null && quote.LegPricing.Count > 0)
            {
                var vol = quote.LegPricing[0].Volatility;
                Console.WriteLine($"[PREMIUM-DEBUG] {lpName} {side}: Volatility = {vol}, Premium = {rawPremium}");
            }

            if (!rawPremium.HasValue)
                return null;

            // Currency conversion if toggle is checked
            // When checked: always show in BASE currency (convert from TERM if needed)
            if (togglePremiumCurrency != null && togglePremiumCurrency.Checked && !string.IsNullOrEmpty(spotRef))
            {
                // Get base and term currencies from the pair
                string pair = _trade.Underlying;  // e.g., "EURUSD" or "USDSEK"
                string baseCcy = pair.Substring(0, 3);  // EUR or USD
                string termCcy = pair.Substring(3, 3);  // USD or SEK

                if (double.TryParse(spotRef, out double spot))
                {
                    string premCcy = premiumCcy.ToUpperInvariant();

                    // If premium is in term currency, convert to base currency
                    if (premCcy == termCcy.ToUpperInvariant())
                    {
                        // Formula: Term Premium / Spot = Base Premium
                        // Example: USD/SEK with premium in SEK: SEK 1000 / 10.5 = USD 95.24
                        // Example: EUR/USD with premium in USD: USD 1000 / 1.15 = EUR 869.57
                        double convertedPremium = rawPremium.Value / spot;
                        Console.WriteLine($"[PREMIUM-DEBUG] {lpName} {side}: Converting {rawPremium.Value} {termCcy} / {spot} = {convertedPremium} {baseCcy}");
                        return convertedPremium;
                    }
                    // If premium is already in base currency, no conversion needed
                    else if (premCcy == baseCcy.ToUpperInvariant())
                    {
                        Console.WriteLine($"[PREMIUM-DEBUG] {lpName} {side}: Already in base currency {baseCcy}, no conversion needed");
                        return rawPremium.Value;
                    }
                }
            }

            // Return raw premium (original currency or conversion not possible)
            return rawPremium.Value;
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
                // Re-fetch streams to check current quote state
                // NOTE: No delay - execute as fast as possible to minimize window for LP to update quote
                var refreshedStreams = _fixSession.Application.GetActiveStreams(_groupId);
                var refreshedStream = refreshedStreams.FirstOrDefault(s => s.LP == lpName);

                if (refreshedStream == null)
                {
                    MessageBox.Show(
                        $"Quote from {lpName} is no longer available.\n\nPlease request fresh quotes.",
                        "Quote No Longer Available",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    _quoteTimer?.Start();
                    return;
                }

                // Check if the specific side (bid/offer) was canceled
                FIXMessage refreshedQuote = side == "SELL" ? refreshedStream.BidQuote : refreshedStream.OfferQuote;

                if (refreshedQuote == null)
                {
                    MessageBox.Show(
                        $"Quote from {lpName} was just canceled.\n\nPlease request fresh quotes.",
                        "Quote Canceled",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    _quoteTimer?.Start();
                    return;
                }

                // Check if the QuoteID changed (quote was replaced)
                string originalQuoteID = selectedQuote.Get(Tags.QuoteID.ToString());
                string currentQuoteID = refreshedQuote.Get(Tags.QuoteID.ToString());

                if (originalQuoteID != currentQuoteID)
                {
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

                // Check ValidUntilTime (tag 62) - quote expiration
                string validUntilStr = refreshedQuote.Get("62");

                if (!string.IsNullOrEmpty(validUntilStr))
                {
                    if (DateTime.TryParseExact(validUntilStr, "yyyyMMdd-HH:mm:ss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out DateTime validUntil))
                    {
                        var timeRemaining = validUntil - DateTime.UtcNow;

                        if (timeRemaining.TotalSeconds <= 0)
                        {
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
                            var result = MessageBox.Show(
                                $"WARNING: Quote expires in {timeRemaining.TotalSeconds:F1}s!\n\nThere may not be enough time to execute.\n\nProceed anyway?",
                                "Quote Expiring Soon",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning
                            );

                            if (result == DialogResult.No)
                            {
                                _quoteTimer?.Start();
                                return;
                            }
                        }
                    }
                }

                // Use the refreshed quote for execution to ensure we have the latest data
                selectedQuote = refreshedQuote;
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

            // Format delta in millions for readability
            string deltaDisplay = "-";
            if (entry.Delta.HasValue)
            {
                double deltaInMillions = entry.Delta.Value / 1_000_000.0;
                deltaDisplay = deltaInMillions.ToString("N2") + "M";
            }

            // Format volatility
            string volDisplay = entry.Volatility.HasValue ? entry.Volatility.Value.ToString("N2") : "-";

            dgvBlotter.Rows.Add(
                entry.TradeTime.ToString("HH:mm:ss"),
                entry.ClOrdID,
                entry.LP,
                entry.Side,
                entry.Underlying,
                entry.StructureType,
                entry.NetPremium.ToString("N0"), // Raw integer value
                deltaDisplay,
                volDisplay,
                entry.Status
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

            // Check if columns exist (grid might be recreating)
            if (!dgvBlotter.Columns.Contains("ClOrdID") ||
                !dgvBlotter.Columns.Contains("Status") ||
                !dgvBlotter.Columns.Contains("Premium"))
                return;

            // Find the row with matching ClOrdID and update it
            foreach (DataGridViewRow row in dgvBlotter.Rows)
            {
                if (row.Cells["ClOrdID"].Value?.ToString() == entry.ClOrdID)
                {
                    row.Cells["Status"].Value = entry.Status;
                    row.Cells["Premium"].Value = entry.NetPremium.ToString("N0"); // Raw integer value
                    ColorCodeBlotterRow(row, entry.Status);
                    break;
                }
            }
        }

        /// <summary>
        /// Handle Trade Capture Report (35=AE) from STP session
        /// This is the final confirmation with full trade economics
        /// </summary>
        private void OnTradeCaptureReceived(CapturedTrade trade)
        {
            if (dgvBlotter.InvokeRequired)
            {
                dgvBlotter.Invoke(new Action(() => OnTradeCaptureReceived(trade)));
                return;
            }

            Console.WriteLine($"\n[GFIQuoteDialog] Trade Capture Report received:");
            Console.WriteLine($"  Symbol: {trade.Symbol}");
            Console.WriteLine($"  Side: {trade.Side}");
            Console.WriteLine($"  Counterparty: {trade.CounterpartyName} (LEI: {trade.CounterpartyLEI})");
            Console.WriteLine($"  ExecID: {trade.ExecID}");
            Console.WriteLine($"  QuoteID: {trade.QuoteID}");
            Console.WriteLine($"  ClOrdID: {trade.ClOrdID}");
            Console.WriteLine($"  Option Legs: {trade.OptionLegs.Count}");
            Console.WriteLine($"  Hedge Legs: {trade.HedgeLegs.Count}");

            // Check if columns exist
            if (!dgvBlotter.Columns.Contains("ClOrdID") ||
                !dgvBlotter.Columns.Contains("Status") ||
                !dgvBlotter.Columns.Contains("LP"))
                return;

            // Find the row with matching ClOrdID and update with final trade details
            foreach (DataGridViewRow row in dgvBlotter.Rows)
            {
                if (row.Cells["ClOrdID"].Value?.ToString() == trade.ClOrdID)
                {
                    // Update with confirmed counterparty name
                    row.Cells["LP"].Value = trade.CounterpartyName;

                    // Update status to confirmed
                    row.Cells["Status"].Value = "CONFIRMED";

                    // Color code as confirmed (light blue)
                    row.DefaultCellStyle.BackColor = Color.LightBlue;

                    Console.WriteLine($"[GFIQuoteDialog] ✓ Updated blotter row with final confirmation");
                    break;
                }
            }
        }

        private void ColorCodeBlotterRow(DataGridViewRow row, string status)
        {
            switch (status?.ToUpper())
            {
                case "FILLED":
                    row.DefaultCellStyle.BackColor = Color.LightGreen;
                    break;
                case "REJECTED":
                    row.DefaultCellStyle.BackColor = Color.LightCoral;
                    break;
                case "PENDING":
                    row.DefaultCellStyle.BackColor = Color.LightYellow;
                    break;
                default:
                    row.DefaultCellStyle.BackColor = Color.White;
                    break;
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

            // Unsubscribe from events
            _fixSession.Application.OnQuoteReceived -= OnQuoteReceivedFromFIX;

            base.OnFormClosing(e);
        }
    }
}