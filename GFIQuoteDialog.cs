using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FXOptionsSimulator;

namespace FXOAiTranslator
{
    public partial class GFIQuoteDialog : Form
    {
        private FIXSimulator _simulator;
        private TradeStructure _trade;
        private string _groupId;
        private System.Windows.Forms.Timer _quoteTimer;
        private DataGridView dgvQuotes;
        private DataGridView dgvLegs;
        private Button btnRequestQuotes;
        private Button btnExecute;
        private Button btnCancel;
        private Label lblTradeSummary;
        private GroupBox gbLPs;
        private CheckBox chkMS;
        private CheckBox chkUBS;
        private CheckBox chkNatwest;
        private int _selectedLegCount;

        public GFIQuoteDialog(dynamic ovmlResult)
        {
            InitializeComponent();
            InitializeCustomComponents();

            _trade = OVMLBridge.ConvertToTradeStructure(ovmlResult);
            _simulator = new FIXSimulator();

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
        }

        private void InitializeCustomComponents()
        {
            this.Text = "GFI Fenics - Request Quotes";
            this.Size = new Size(1000, 650);
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

            gbLPs = new GroupBox
            {
                Text = "Select Liquidity Providers",
                Location = new Point(20, 220),
                Size = new Size(940, 60)
            };
            this.Controls.Add(gbLPs);

            chkMS = new CheckBox
            {
                Text = "Morgan Stanley",
                Location = new Point(20, 25),
                Size = new Size(150, 25),
                Checked = true
            };
            gbLPs.Controls.Add(chkMS);

            chkUBS = new CheckBox
            {
                Text = "UBS",
                Location = new Point(200, 25),
                Size = new Size(150, 25),
                Checked = true
            };
            gbLPs.Controls.Add(chkUBS);

            chkNatwest = new CheckBox
            {
                Text = "NatWest Markets",
                Location = new Point(380, 25),
                Size = new Size(150, 25),
                Checked = true
            };
            gbLPs.Controls.Add(chkNatwest);

            // Quotes grid - will be dynamically configured
            dgvQuotes = new DataGridView
            {
                Location = new Point(20, 300),
                Size = new Size(940, 250),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                RowHeadersVisible = false
            };

            this.Controls.Add(dgvQuotes);

            btnRequestQuotes = new Button
            {
                Text = "Request Quotes",
                Location = new Point(20, 570),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            btnRequestQuotes.Click += BtnRequestQuotes_Click;
            this.Controls.Add(btnRequestQuotes);

            btnExecute = new Button
            {
                Text = "Execute (Sell)",
                Location = new Point(190, 570),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Enabled = false
            };
            btnExecute.Click += BtnExecute_Click;
            this.Controls.Add(btnExecute);

            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(810, 570),
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

            // Fixed columns
            dgvQuotes.Columns.Add("LP", "LP");
            dgvQuotes.Columns["LP"].Width = 80;

            dgvQuotes.Columns.Add("NetPremBid", "Net Prem (Bid)");
            dgvQuotes.Columns["NetPremBid"].DefaultCellStyle.Format = "N2";
            dgvQuotes.Columns["NetPremBid"].Width = 100;

            dgvQuotes.Columns.Add("NetPremOffer", "Net Prem (Offer)");
            dgvQuotes.Columns["NetPremOffer"].DefaultCellStyle.Format = "N2";
            dgvQuotes.Columns["NetPremOffer"].Width = 110;

            // Dynamic columns for each leg
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
            if (chkMS.Checked) lps.Add("MS");
            if (chkUBS.Checked) lps.Add("UBS");
            if (chkNatwest.Checked) lps.Add("NATWEST");

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

            // Setup quote grid with dynamic columns
            SetupQuoteGrid(selectedLegCount);

            UpdateTradeFromGrid();

            (_groupId, var requests) = _simulator.SendQuoteRequest(_trade.Underlying, lps);

            _quoteTimer = new System.Windows.Forms.Timer();
            _quoteTimer.Interval = 2000;
            _quoteTimer.Tick += QuoteTimer_Tick;
            _quoteTimer.Start();

            btnRequestQuotes.Enabled = false;
            btnExecute.Enabled = true;

            _simulator.StreamQuotes(_groupId, numUpdates: 1, delayMs: 0);
            UpdateQuoteDisplay();
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
            _simulator.StreamQuotes(_groupId, numUpdates: 1, delayMs: 0);
            UpdateQuoteDisplay();
        }

        private void UpdateQuoteDisplay()
        {
            dgvQuotes.Rows.Clear();
            var streams = _simulator.GetActiveStreams(_groupId);

            foreach (var stream in streams)
            {
                var rowData = new List<object>();
                rowData.Add(stream.LP);

                // Calculate net premiums
                double? netPremBid = CalculateNetPremium(stream.BidQuote);
                double? netPremOffer = CalculateNetPremium(stream.OfferQuote);

                rowData.Add(netPremBid?.ToString("N2") ?? "-");
                rowData.Add(netPremOffer?.ToString("N2") ?? "-");

                // Add individual leg vols
                for (int i = 1; i <= _selectedLegCount; i++)
                {
                    double? bidVol = GetLegVol(stream.BidQuote, i);
                    double? offerVol = GetLegVol(stream.OfferQuote, i);

                    rowData.Add(bidVol?.ToString("N2") ?? "-");
                    rowData.Add(offerVol?.ToString("N2") ?? "-");
                }

                rowData.Add(stream.LastUpdate.ToString("HH:mm:ss"));

                var rowIndex = dgvQuotes.Rows.Add(rowData.ToArray());

                // Highlight best net premium
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
            var streams = _simulator.GetActiveStreams(_groupId);

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

        private void BtnExecute_Click(object sender, EventArgs e)
        {
            _quoteTimer?.Stop();

            var (bestBid, _) = _simulator.GetBestPrices(_groupId);

            if (bestBid == null)
            {
                MessageBox.Show("No valid quotes available", "Cannot Execute",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _quoteTimer?.Start();
                return;
            }

            bool filled = _simulator.ExecuteTrade(bestBid, "SELL");

            if (filled)
            {
                var netPrem = CalculateNetPremium(bestBid);
                MessageBox.Show(
                    $"Trade FILLED!\n\nLP: {bestBid.Get(TagStrings.OnBehalfOfCompID)}\nNet Premium: {netPrem?.ToString("N2") ?? "N/A"} pips",
                    "Execution Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                var result = MessageBox.Show(
                    "Trade REJECTED by LP (last-look)\n\nTry again?",
                    "Execution Failed",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (result == DialogResult.Yes)
                {
                    _quoteTimer?.Start();
                }
                else
                {
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _quoteTimer?.Stop();
            _quoteTimer?.Dispose();
            base.OnFormClosing(e);
        }
    }
}