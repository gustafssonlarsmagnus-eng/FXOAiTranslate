using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FXOptionsSimulator;
using FXOptionsSimulator.FIX;

namespace FXOAiTranslator
{
    public partial class GFIQuoteDialog : Form
    {
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
        private CheckBox chkHedge;  // Hedge checkbox
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
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false
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
            dgvLegs.Columns.Add("Direction", "Direction");
            dgvLegs.Columns.Add("Type", "Type");
            dgvLegs.Columns.Add("Strike", "Strike");

            var notionalCol = new DataGridViewTextBoxColumn
            {
                Name = "NotionalMM",
                HeaderText = "Notional (MM)",
                Width = 100
            };
            dgvLegs.Columns.Add(notionalCol);

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
                Location = new Point(20, 330),
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

            this.Controls.Add(dgvQuotes);

            // Trade Blotter Grid - NEW
            var lblBlotter = new Label
            {
                Text = "Trade Blotter:",
                Location = new Point(20, 540),
                Size = new Size(200, 20),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            this.Controls.Add(lblBlotter);

            dgvBlotter = new DataGridView
            {
                Location = new Point(20, 565),
                Size = new Size(940, 150),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,  // Allow multiple row selection
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false
            };
            dgvBlotter.KeyDown += DgvBlotter_KeyDown;

            // Blotter columns
            dgvBlotter.Columns.Add("Time", "Time");
            dgvBlotter.Columns["Time"].Width = 70;
            dgvBlotter.Columns.Add("ClOrdID", "Order ID");
            dgvBlotter.Columns["ClOrdID"].Width = 200;
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
            dgvBlotter.Columns.Add("Vol", "Vol");
            dgvBlotter.Columns["Vol"].Width = 60;
            dgvBlotter.Columns.Add("Delta", "Delta");
            dgvBlotter.Columns["Delta"].Width = 90;  // Wider for notional amounts
            dgvBlotter.Columns.Add("Spot", "Spot");
            dgvBlotter.Columns["Spot"].Width = 70;
            dgvBlotter.Columns.Add("Status", "Status");
            dgvBlotter.Columns["Status"].Width = 100;

            this.Controls.Add(dgvBlotter);

            // Hedge checkbox
            chkHedge = new CheckBox
            {
                Text = "Hedge",
                Location = new Point(720, 735),
                Size = new Size(100, 25),
                Checked = true,  // Default ON
                Font = new Font("Segoe UI", 10, FontStyle.Regular)
            };
            this.Controls.Add(chkHedge);

            // Buttons - moved down for blotter
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

            for (int i = 0; i < _trade.Legs.Count; i++)
            {
                var leg = _trade.Legs[i];
                dgvLegs.Rows.Add(
                    true,
                    $"Leg {i + 1}",
                    leg.Direction,
                    leg.OptionType,
                    leg.Strike.ToString("F4"),
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

            Console.WriteLine($"\n[Quote Request] Sending {selectedLegCount} legs:");
            for (int i = 0; i < _trade.Legs.Count; i++)
            {
                var leg = _trade.Legs[i];
                Console.WriteLine($"  Leg {i}: {leg.Direction} {leg.NotionalMM}MM {leg.OptionType} @ {leg.Strike}");
            }
            Console.WriteLine();

            // Send quote request to each LP
            bool hedge = chkHedge.Checked;
            Console.WriteLine($"[Quote Request] Hedge: {(hedge ? "ON" : "OFF")} (9016={(hedge ? "1" : "0")})");

            foreach (var lp in lps)
            {
                try
                {
                    string quoteReqID = _fixSession.SendQuoteRequest(_trade, lp, _groupId, hedge);
                    Console.WriteLine($"[Quote Request] Sent to {lp}: {quoteReqID}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Quote Request] Error sending to {lp}: {ex.Message}");
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

            Console.WriteLine($"\n[DISPLAY] UpdateQuoteDisplay called for groupId={_groupId}, found {streams.Count} streams");

            foreach (var stream in streams)
            {
                Console.WriteLine($"[DISPLAY] LP={stream.LP}: BidQuote={(stream.BidQuote != null ? "EXISTS" : "NULL")}, OfferQuote={(stream.OfferQuote != null ? "EXISTS" : "NULL")}");

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

                    Console.WriteLine($"[DISPLAY]   Leg {i}: BidVol={bidVol?.ToString("N2") ?? "NULL"}, OfferVol={offerVol?.ToString("N2") ?? "NULL"}");

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

            // Start countdown timer
            if (!_countdownTimer.Enabled)
                _countdownTimer.Start();
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            if (dgvQuotes.InvokeRequired)
            {
                dgvQuotes.Invoke(new Action(() => CountdownTimer_Tick(sender, e)));
                return;
            }

            var nowUtc = DateTime.UtcNow;

            // Suspend painting to reduce flicker
            dgvQuotes.SuspendLayout();

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
                            Color newBackColor;
                            Color newForeColor;

                            if (remainingTime.TotalSeconds <= 0)
                            {
                                newValue = "EXPIRED";
                                newBackColor = Color.LightGray;
                                newForeColor = Color.DarkRed;
                            }
                            else
                            {
                                // Format as MM:SS
                                int minutes = (int)remainingTime.TotalMinutes;
                                int seconds = remainingTime.Seconds;
                                newValue = $"{minutes}:{seconds:D2}";

                                // Color code based on time remaining
                                if (remainingTime.TotalSeconds > 60)
                                {
                                    newBackColor = Color.LightGreen;
                                    newForeColor = Color.Black;
                                }
                                else if (remainingTime.TotalSeconds > 30)
                                {
                                    newBackColor = Color.Yellow;
                                    newForeColor = Color.Black;
                                }
                                else
                                {
                                    newBackColor = Color.LightCoral;
                                    newForeColor = Color.White;
                                }
                            }

                            // Only update if value changed (reduces redraws)
                            if (ttlCell.Value?.ToString() != newValue)
                            {
                                ttlCell.Value = newValue;
                            }

                            // Only update colors if they changed
                            if (ttlCell.Style.BackColor != newBackColor)
                            {
                                ttlCell.Style.BackColor = newBackColor;
                            }
                            if (ttlCell.Style.ForeColor != newForeColor)
                            {
                                ttlCell.Style.ForeColor = newForeColor;
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
                dgvQuotes.ResumeLayout();
            }
        }

        private double? CalculateNetPremium(FIXMessage quote)
        {
            if (quote == null) return null;

            // Get LP name to check for LP-specific premium units
            string lpName = quote.Get(Tags.OnBehalfOfCompID.ToString()) ?? "";

            // Use new LegPricing structure
            if (quote.LegPricing != null && quote.LegPricing.Count > 0)
            {
                double netPrem = 0;
                foreach (var leg in quote.LegPricing)
                {
                    if (!string.IsNullOrEmpty(leg.LegPremPrice) && double.TryParse(leg.LegPremPrice, out double prem))
                    {
                        // HSBC sends premiums in percentage, others in basis points
                        // Convert HSBC from % to bps by multiplying by 100
                        if (lpName == "HSBC")
                        {
                            prem *= 100.0;
                        }
                        netPrem += prem;
                    }
                }

                // GFI sends premiums already from client perspective:
                // - BID quote: Positive premium (client receives when selling)
                // - OFFER quote: Negative premium (client pays when buying)
                // No negation needed
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

            // No negation needed - GFI sends client perspective
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

                if (bid.HasValue && (!bestBid.HasValue || bid.Value > bestBid.Value))
                    bestBid = bid.Value;

                if (offer.HasValue && (!bestOffer.HasValue || offer.Value < bestOffer.Value))
                    bestOffer = offer.Value;
            }

            return (bestBid, bestOffer);
        }

        private void BtnExecute_Click(string side)
        {
            Console.WriteLine($"\n========== EXECUTION ATTEMPT ==========");
            Console.WriteLine($"User Action: {side}");
            Console.WriteLine($"GroupID: {_groupId}");
            _quoteTimer?.Stop();

            // Get best bid quote
            var streams = _fixSession.Application.GetActiveStreams(_groupId);
            Console.WriteLine($"Available Streams: {streams.Count}");
            FIXMessage bestBidQuote = null;
            double bestBidPremium = double.MinValue;

            foreach (var stream in streams)
            {
                Console.WriteLine($"  Stream: {stream.LP}");
                Console.WriteLine($"    BidQuote: {(stream.BidQuote != null ? "AVAILABLE (QuoteID=" + stream.BidQuote.Get("117") + ")" : "NULL")}");
                Console.WriteLine($"    OfferQuote: {(stream.OfferQuote != null ? "AVAILABLE (QuoteID=" + stream.OfferQuote.Get("117") + ")" : "NULL")}");

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

            Console.WriteLine($"\nBest BidQuote: {(bestBidQuote != null ? "FOUND (Premium=" + bestBidPremium + ")" : "NOT FOUND")}");

            if (bestBidQuote == null)
            {
                Console.WriteLine($"[ERROR] No bid quotes available - cannot execute {side}");
                Console.WriteLine($"=======================================\n");
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
                    // SELL uses BID quote (Side=1) - client sells TO the LP's bid
                    selectedQuote = bestBidQuote;
                    selectedPremium = bestBidPremium;
                    lpName = bestBidQuote.Get(Tags.OnBehalfOfCompID.ToString());
                    Console.WriteLine($"[EXECUTION] Action={side} → Using BidQuote, QuoteID={selectedQuote.Get(Tags.QuoteID.ToString())}");
                }
                else // BUY
                {
                    // BUY uses OFFER quote (Side=2) - client buys FROM the LP's offer
                    Console.WriteLine($"[EXECUTION] Searching for OFFER quotes...");
                    FIXMessage bestOfferQuote = null;
                    double bestOfferPremium = double.MaxValue;

                    foreach (var stream in streams)
                    {
                        if (stream.OfferQuote != null)
                        {
                            var prem = CalculateNetPremium(stream.OfferQuote);
                            Console.WriteLine($"  {stream.LP}: OfferQuote Premium={prem?.ToString("N2") ?? "NULL"}");
                            if (prem.HasValue && prem.Value < bestOfferPremium)
                            {
                                bestOfferPremium = prem.Value;
                                bestOfferQuote = stream.OfferQuote;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"  {stream.LP}: OfferQuote is NULL");
                        }
                    }

                    Console.WriteLine($"\nBest OfferQuote: {(bestOfferQuote != null ? "FOUND (Premium=" + bestOfferPremium + ")" : "NOT FOUND")}");

                    if (bestOfferQuote == null)
                    {
                        Console.WriteLine($"[ERROR] No offer quotes available - cannot execute BUY");
                        Console.WriteLine($"=======================================\n");
                        MessageBox.Show("No valid offer quotes available", "Cannot Execute",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        _quoteTimer?.Start();
                        return;
                    }

                    selectedQuote = bestOfferQuote;
                    selectedPremium = bestOfferPremium;
                    lpName = bestOfferQuote.Get(Tags.OnBehalfOfCompID.ToString());
                    Console.WriteLine($"[EXECUTION] Action={side} → Using OfferQuote, QuoteID={selectedQuote.Get(Tags.QuoteID.ToString())}");
                }

                // ===== QUOTE FRESHNESS VALIDATION =====
                Console.WriteLine($"\n[VALIDATION] Starting quote freshness check for {lpName}");
                Console.WriteLine($"[VALIDATION] Original QuoteID: {selectedQuote.Get(Tags.QuoteID.ToString())}");

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
                // SELL uses BidQuote (Side=1), BUY uses OfferQuote (Side=2)
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

                // NOTE: QuoteID prefix (B_ vs O_) is NOT reliable for quote type
                // The only reliable indicator is the FIX Side field (tag 54) that GFI sends
                // We trust our quote storage based on the Side field

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
                // ===== END QUOTE FRESHNESS VALIDATION =====

                // Final execution summary
                Console.WriteLine($"\n========== SENDING EXECUTION ==========");
                Console.WriteLine($"User Action: {side}");
                Console.WriteLine($"LP: {lpName}");
                Console.WriteLine($"QuoteID: {selectedQuote.Get(Tags.QuoteID.ToString())}");
                Console.WriteLine($"Quote Side: {selectedQuote.Get("54")} ({(selectedQuote.Get("54") == "1" ? "BID" : "OFFER")})");
                Console.WriteLine($"Premium: {selectedPremium:N2}");
                Console.WriteLine($"Symbol: {_trade.Underlying}");
                Console.WriteLine($"Structure: {_trade.StructureType}");
                Console.WriteLine($"=======================================\n");

                string clOrdID = _fixSession.SendExecution(selectedQuote, side, _trade);

                Console.WriteLine($"[EXECUTION] Order sent with ClOrdID: {clOrdID}");
                Console.WriteLine($"=======================================\n");

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

            dgvBlotter.Rows.Add(
                entry.TradeTime.ToString("HH:mm:ss"),
                entry.ClOrdID,
                entry.LP,
                entry.Side,
                entry.Underlying,
                entry.StructureType,
                entry.NetPremium.ToString("N2"),
                entry.Volatility.ToString("F2"),
                entry.Delta.ToString("N0"),  // Format as integer with thousand separators
                entry.SpotRate.ToString("F4"),
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
                    row.Cells["Vol"].Value = entry.Volatility.ToString("F2");
                    row.Cells["Delta"].Value = entry.Delta.ToString("N0");  // Format as integer with thousand separators
                    row.Cells["Spot"].Value = entry.SpotRate.ToString("F4");
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

        private void DgvBlotter_KeyDown(object sender, KeyEventArgs e)
        {
            // Copy selected rows to clipboard with Ctrl+C
            if (e.Control && e.KeyCode == Keys.C)
            {
                if (dgvBlotter.SelectedRows.Count == 0)
                    return;

                var sb = new System.Text.StringBuilder();

                // Add header row
                var headers = new List<string>();
                foreach (DataGridViewColumn col in dgvBlotter.Columns)
                {
                    headers.Add(col.HeaderText);
                }
                sb.AppendLine(string.Join("\t", headers));

                // Add selected rows
                foreach (DataGridViewRow row in dgvBlotter.SelectedRows)
                {
                    var cells = new List<string>();
                    foreach (DataGridViewCell cell in row.Cells)
                    {
                        cells.Add(cell.Value?.ToString() ?? "");
                    }
                    sb.AppendLine(string.Join("\t", cells));
                }

                Clipboard.SetText(sb.ToString());
                e.Handled = true;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _quoteTimer?.Stop();
            _quoteTimer?.Dispose();

            _countdownTimer?.Stop();
            _countdownTimer?.Dispose();

            // Unsubscribe from events
            _fixSession.Application.OnQuoteReceived -= OnQuoteReceivedFromFIX;

            TradeBlotter.Instance.OnTradeAdded -= OnTradeAddedToBlotter;
            TradeBlotter.Instance.OnTradeUpdated -= OnTradeUpdatedInBlotter;

            base.OnFormClosing(e);
        }
    }
}