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
            this.Size = new Size(1000, 840);  // Adjusted height
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

            // Quotes Grid - reduced size to make room for blotter
            dgvQuotes = new DataGridView
            {
                Location = new Point(20, 320),
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
                Location = new Point(20, 530),
                Size = new Size(200, 20),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            this.Controls.Add(lblBlotter);

            dgvBlotter = new DataGridView
            {
                Location = new Point(20, 555),
                Size = new Size(940, 135),      // Reduced slightly
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
            dgvBlotter.Columns.Add("Status", "Status");
            dgvBlotter.Columns["Status"].Width = 100;

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

            for (int i = 1; i <= legCount; i++)
            {
                dgvQuotes.Columns.Add($"Leg{i}BidVol", $"L{i} Bid Vol");
                dgvQuotes.Columns[$"Leg{i}BidVol"].DefaultCellStyle.Format = "N2";
                dgvQuotes.Columns[$"Leg{i}BidVol"].Width = 80;

                dgvQuotes.Columns.Add($"Leg{i}OfferVol", $"L{i} Offer Vol");
                dgvQuotes.Columns[$"Leg{i}OfferVol"].DefaultCellStyle.Format = "N2";
                dgvQuotes.Columns[$"Leg{i}OfferVol"].Width = 90;
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
                    string quoteReqID = _fixSession.SendQuoteRequest(_trade, lp, _groupId);
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

                for (int i = 1; i <= _selectedLegCount; i++)
                {
                    double? bidVol = GetLegVol(stream.BidQuote, i);
                    double? offerVol = GetLegVol(stream.OfferQuote, i);

                    rowData.Add(bidVol?.ToString("N2") ?? "-");
                    rowData.Add(offerVol?.ToString("N2") ?? "-");
                }

                rowData.Add(stream.LastUpdate.ToString("HH:mm:ss"));

                // Extract ValidUntilTime (tag 62) from the quote
                string validUntilStr = stream.OfferQuote?.Get("62") ?? stream.BidQuote?.Get("62");

                // DEBUG: Log what we got for tag 62
                Console.WriteLine($"[COUNTDOWN DEBUG] LP={stream.LP}, ValidUntilTime (tag 62)='{validUntilStr}'");

                rowData.Add(""); // TTL - will be calculated by timer
                rowData.Add(validUntilStr ?? ""); // Hidden ValidUntilTime column

                var rowIndex = dgvQuotes.Rows.Add(rowData.ToArray());

                var (bestBid, bestOffer) = GetBestPremiums();

                if (bestBid.HasValue && netPremBid.HasValue && Math.Abs(netPremBid.Value - bestBid.Value) < 0.01)
                {
                    dgvQuotes.Rows[rowIndex].Cells["NetPremBid"].Style.BackColor = Color.LightGreen;
                    dgvQuotes.Rows[rowIndex].Cells["NetPremBid"].Style.Font =
                        new Font(dgvQuotes.Font, FontStyle.Bold);
                }

                if (bestOffer.HasValue && netPremOffer.HasValue && Math.Abs(netPremOffer.Value - bestOffer.Value) < 0.01)
                {
                    dgvQuotes.Rows[rowIndex].Cells["NetPremOffer"].Style.BackColor = Color.LightGreen;
                    dgvQuotes.Rows[rowIndex].Cells["NetPremOffer"].Style.Font =
                        new Font(dgvQuotes.Font, FontStyle.Bold);
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

            // Format delta in millions for readability
            string deltaDisplay = "-";
            if (entry.Delta.HasValue)
            {
                double deltaInMillions = entry.Delta.Value / 1_000_000.0;
                deltaDisplay = deltaInMillions.ToString("N2") + "M";
            }

            dgvBlotter.Rows.Add(
                entry.TradeTime.ToString("HH:mm:ss"),
                entry.ClOrdID,
                entry.LP,
                entry.Side,
                entry.Underlying,
                entry.StructureType,
                entry.NetPremium.ToString("N2"),
                deltaDisplay,
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

            // Find the row with matching ClOrdID and update it
            foreach (DataGridViewRow row in dgvBlotter.Rows)
            {
                if (row.Cells["ClOrdID"].Value?.ToString() == entry.ClOrdID)
                {
                    row.Cells["Status"].Value = entry.Status;
                    row.Cells["Premium"].Value = entry.NetPremium.ToString("N2");
                    ColorCodeBlotterRow(row, entry.Status);
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