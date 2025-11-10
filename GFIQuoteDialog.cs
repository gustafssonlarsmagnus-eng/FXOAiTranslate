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
        private DataGridView dgvQuotes;
        private DataGridView dgvLegs;
        private Button btnRequestQuotes;
        private Button btnExecute;
        private Button btnCancel;
        private Button btnBuy;    
        private Button btnViewBlotter;
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
            this.Size = new Size(1000, 700);  // Increased height for more LPs
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

            // Quotes Grid - adjusted position
            dgvQuotes = new DataGridView
            {
                Location = new Point(20, 330),  // Moved down
                Size = new Size(940, 270),      // Adjusted size
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                RowHeadersVisible = false
            };

            this.Controls.Add(dgvQuotes);

            // Buttons - adjusted position
            btnRequestQuotes = new Button
            {
                Text = "Request Quotes",
                Location = new Point(20, 620),  // Moved down
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            btnRequestQuotes.Click += BtnRequestQuotes_Click;
            this.Controls.Add(btnRequestQuotes);

            btnExecute = new Button
            {
                Text = "Sell (Hit Bid)",
                Location = new Point(190, 620),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Enabled = false
            };
            btnExecute.Click += (s, e) => BtnExecute_Click("SELL");
            this.Controls.Add(btnExecute);

            btnBuy = new Button
            {
                Text = "Buy (Lift Offer)",
                Location = new Point(360, 620),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Enabled = false
            };
            btnBuy.Click += (s, e) => BtnExecute_Click("BUY");
            this.Controls.Add(btnBuy);

            btnViewBlotter = new Button
            {
                Text = "View Blotter",
                Location = new Point(530, 620),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Regular)
            };
            btnViewBlotter.Click += (s, e) =>
            {
                var blotter = new TradeBlotterForm();
                blotter.Show();
            };
            this.Controls.Add(btnViewBlotter);

            btnBuy = new Button
            {
                Text = "Buy (Lift Offer)",
                Location = new Point(360, 620),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Enabled = false
            };
            btnBuy.Click += (s, e) => BtnExecute_Click("BUY");
            this.Controls.Add(btnBuy);

            btnViewBlotter = new Button
            {
                Text = "View Blotter",
                Location = new Point(530, 620),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Regular)
            };
            btnViewBlotter.Click += (s, e) =>
            {
                var blotter = new TradeBlotterForm();
                blotter.Show();
            };
            this.Controls.Add(btnViewBlotter);

            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(810, 620),  // Moved down
                Size = new Size(150, 35),
                DialogResult = DialogResult.Cancel
            };
            this.Controls.Add(btnCancel);
            this.CancelButton = btnCancel;
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
            foreach (var lp in lps)
            {
                try
                {
                    string quoteReqID = _fixSession.SendQuoteRequest(_trade, lp, _groupId);
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
                // Also enable the buy button (find it by iterating controls or store as field)
            }
        }

        private double? CalculateNetPremium(FIXMessage quote)
        {
            if (quote == null) return null;

            double netPrem = 0;
            for (int i = 1; i <= _selectedLegCount; i++)
            {
                var premStr = quote.Get($"leg{i}_5844");
                if (!string.IsNullOrEmpty(premStr) && double.TryParse(premStr, out double prem))
                {
                    netPrem += prem;
                }
            }

            return netPrem;
        }

        private double? GetLegVol(FIXMessage quote, int legNum)
        {
            if (quote == null) return null;

            var volStr = quote.Get($"leg{legNum}_5678");
            if (!string.IsNullOrEmpty(volStr) && double.TryParse(volStr, out double vol))
            {
                return vol;
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

                string clOrdID = _fixSession.SendExecution(selectedQuote, side, _trade);

                var netPrem = CalculateNetPremium(selectedQuote);
                MessageBox.Show(
                    $"Trade SENT!\n\n" +
                    $"Order ID: {clOrdID}\n" +
                    $"LP: {lpName}\n" +
                    $"Side: {side}\n" +
                    $"Net Premium: {netPrem?.ToString("N2") ?? "N/A"} pips\n\n" +
                    $"Waiting for execution report...\n\n" +
                    $"Check the Trade Blotter for updates.",
                    "Order Sent",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Execution error:\n\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                _quoteTimer?.Start();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _quoteTimer?.Stop();
            _quoteTimer?.Dispose();

            // Unsubscribe from events
            _fixSession.Application.OnQuoteReceived -= OnQuoteReceivedFromFIX;

            base.OnFormClosing(e);
        }
    }
}