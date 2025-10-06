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
        private Button btnRequestQuotes;
        private Button btnExecute;
        private Button btnCancel;
        private Label lblTradeSummary;
        private GroupBox gbLPs;
        private CheckBox chkMS;
        private CheckBox chkUBS;
        private CheckBox chkNatwest;

        public GFIQuoteDialog(dynamic ovmlResult)
        {
            InitializeComponent();
            InitializeCustomComponents();

            // Convert OVML to trade structure
            _trade = OVMLBridge.ConvertToTradeStructure(ovmlResult);
            _simulator = new FIXSimulator();

            // Show trade summary
            lblTradeSummary.Text = $"{_trade.StructureType}: {_trade.Underlying} - {_trade.Legs.Count} legs";
        }

        private void InitializeCustomComponents()
        {
            this.Text = "GFI Fenics - Request Quotes";
            this.Size = new Size(800, 500);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // Trade Summary Label
            lblTradeSummary = new Label
            {
                Location = new Point(20, 20),
                Size = new Size(740, 30),
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Text = "Loading trade..."
            };
            this.Controls.Add(lblTradeSummary);

            // LP Selection GroupBox
            gbLPs = new GroupBox
            {
                Text = "Select Liquidity Providers",
                Location = new Point(20, 60),
                Size = new Size(740, 60)
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

            // Quotes DataGridView
            dgvQuotes = new DataGridView
            {
                Location = new Point(20, 140),
                Size = new Size(740, 250),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            dgvQuotes.Columns.Add("LP", "LP");
            dgvQuotes.Columns.Add("BidVol", "Bid Vol");
            dgvQuotes.Columns.Add("OfferVol", "Offer Vol");
            dgvQuotes.Columns.Add("Spread", "Spread");
            dgvQuotes.Columns.Add("LastUpdate", "Last Update");

            dgvQuotes.Columns["BidVol"].DefaultCellStyle.Format = "N2";
            dgvQuotes.Columns["OfferVol"].DefaultCellStyle.Format = "N2";
            dgvQuotes.Columns["Spread"].DefaultCellStyle.Format = "N2";

            this.Controls.Add(dgvQuotes);

            // Buttons
            btnRequestQuotes = new Button
            {
                Text = "Request Quotes",
                Location = new Point(20, 410),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            btnRequestQuotes.Click += BtnRequestQuotes_Click;
            this.Controls.Add(btnRequestQuotes);

            btnExecute = new Button
            {
                Text = "Execute (Sell)",
                Location = new Point(190, 410),
                Size = new Size(150, 35),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Enabled = false
            };
            btnExecute.Click += BtnExecute_Click;
            this.Controls.Add(btnExecute);

            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(610, 410),
                Size = new Size(150, 35),
                DialogResult = DialogResult.Cancel
            };
            this.Controls.Add(btnCancel);
            this.CancelButton = btnCancel;
        }

        private void BtnRequestQuotes_Click(object sender, EventArgs e)
        {
            // Get selected LPs
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

            // Send quote requests
            (_groupId, var requests) = _simulator.SendQuoteRequest(_trade.Underlying, lps);

            // Start streaming quotes
            _quoteTimer = new System.Windows.Forms.Timer();
            _quoteTimer.Interval = 2000; // Update every 2 seconds
            _quoteTimer.Tick += QuoteTimer_Tick;
            _quoteTimer.Start();

            btnRequestQuotes.Enabled = false;
            btnExecute.Enabled = true;

            // Initial quote stream
            _simulator.StreamQuotes(_groupId, numUpdates: 1, delayMs: 0);
            UpdateQuoteDisplay();
        }

        private void QuoteTimer_Tick(object sender, EventArgs e)
        {
            // Stream one update
            _simulator.StreamQuotes(_groupId, numUpdates: 1, delayMs: 0);
            UpdateQuoteDisplay();
        }

        private void UpdateQuoteDisplay()
        {
            dgvQuotes.Rows.Clear();

            var streams = _simulator.GetActiveStreams(_groupId);

            foreach (var stream in streams)
            {
                double? bidVol = null;
                double? offerVol = null;

                if (stream.BidQuote != null)
                {
                    var bidVolStr = stream.BidQuote.Get("leg1_5678");
                    if (!string.IsNullOrEmpty(bidVolStr))
                        bidVol = double.Parse(bidVolStr);
                }

                if (stream.OfferQuote != null)
                {
                    var offerVolStr = stream.OfferQuote.Get("leg1_5678");
                    if (!string.IsNullOrEmpty(offerVolStr))
                        offerVol = double.Parse(offerVolStr);
                }

                double? spread = null;
                if (bidVol.HasValue && offerVol.HasValue)
                    spread = offerVol.Value - bidVol.Value;

                var rowIndex = dgvQuotes.Rows.Add(
                    stream.LP,
                    bidVol?.ToString("N2") ?? "-",
                    offerVol?.ToString("N2") ?? "-",
                    spread?.ToString("N2") ?? "-",
                    stream.LastUpdate.ToString("HH:mm:ss")
                );

                // Highlight best prices
                var (bestBid, bestOffer) = _simulator.GetBestPrices(_groupId);

                if (bestBid != null && stream.BidQuote == bestBid)
                {
                    dgvQuotes.Rows[rowIndex].Cells["BidVol"].Style.BackColor = Color.LightGreen;
                    dgvQuotes.Rows[rowIndex].Cells["BidVol"].Style.Font =
                        new Font(dgvQuotes.Font, FontStyle.Bold);
                }

                if (bestOffer != null && stream.OfferQuote == bestOffer)
                {
                    dgvQuotes.Rows[rowIndex].Cells["OfferVol"].Style.BackColor = Color.LightGreen;
                    dgvQuotes.Rows[rowIndex].Cells["OfferVol"].Style.Font =
                        new Font(dgvQuotes.Font, FontStyle.Bold);
                }
            }
        }

        private void BtnExecute_Click(object sender, EventArgs e)
        {
            _quoteTimer?.Stop();

            // Execute on best bid (we're selling)
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
                MessageBox.Show(
                    $"Trade FILLED!\n\nLP: {bestBid.Get(Tags.OnBehalfOfCompID)}\nVol: {bestBid.Get(Tags.Volatility)}",
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