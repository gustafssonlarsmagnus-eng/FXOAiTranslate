using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FXOptionsSimulator;

namespace FXOAiTranslator
{
    public class TradeBlotterForm : Form
    {
        private DataGridView dgvBlotter;
        private Button btnRefresh;
        private Label lblTitle;

        public TradeBlotterForm()
        {
            InitializeComponent();
            SetupBlotter();
            RefreshBlotter();

            // Subscribe to blotter updates
            TradeBlotter.Instance.OnTradeAdded += OnTradeAddedOrUpdated;
            TradeBlotter.Instance.OnTradeUpdated += OnTradeAddedOrUpdated;
        }

        private void InitializeComponent()
        {
            this.Text = "Trade Blotter";
            this.Size = new Size(1200, 600);
            this.StartPosition = FormStartPosition.CenterScreen;

            lblTitle = new Label
            {
                Text = "Trade Blotter - Today's Executions",
                Location = new Point(20, 20),
                Size = new Size(400, 30),
                Font = new Font("Segoe UI", 14, FontStyle.Bold)
            };
            this.Controls.Add(lblTitle);

            dgvBlotter = new DataGridView
            {
                Location = new Point(20, 60),
                Size = new Size(1140, 450),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                RowHeadersVisible = false
            };
            this.Controls.Add(dgvBlotter);

            btnRefresh = new Button
            {
                Text = "Refresh",
                Location = new Point(20, 520),
                Size = new Size(120, 35)
            };
            btnRefresh.Click += (s, e) => RefreshBlotter();
            this.Controls.Add(btnRefresh);
        }

        private void SetupBlotter()
        {
            dgvBlotter.Columns.Clear();

            dgvBlotter.Columns.Add("TradeTime", "Time");
            dgvBlotter.Columns["TradeTime"].DefaultCellStyle.Format = "HH:mm:ss";
            dgvBlotter.Columns["TradeTime"].Width = 80;

            dgvBlotter.Columns.Add("ClOrdID", "Order ID");
            dgvBlotter.Columns["ClOrdID"].Width = 150;

            dgvBlotter.Columns.Add("LP", "LP");
            dgvBlotter.Columns["LP"].Width = 100;

            dgvBlotter.Columns.Add("Side", "Side");
            dgvBlotter.Columns["Side"].Width = 60;

            dgvBlotter.Columns.Add("Underlying", "Underlying");
            dgvBlotter.Columns["Underlying"].Width = 90;

            dgvBlotter.Columns.Add("StructureType", "Structure");
            dgvBlotter.Columns["StructureType"].Width = 100;

            dgvBlotter.Columns.Add("LegCount", "Legs");
            dgvBlotter.Columns["LegCount"].Width = 50;

            dgvBlotter.Columns.Add("NetPremium", "Net Premium");
            dgvBlotter.Columns["NetPremium"].DefaultCellStyle.Format = "N2";
            dgvBlotter.Columns["NetPremium"].Width = 100;

            dgvBlotter.Columns.Add("Status", "Status");
            dgvBlotter.Columns["Status"].Width = 80;

            dgvBlotter.Columns.Add("ExecID", "Exec ID");
            dgvBlotter.Columns["ExecID"].Width = 150;

            dgvBlotter.Columns.Add("RejectReason", "Reject Reason");
            dgvBlotter.Columns["RejectReason"].Width = 200;
        }

        private void RefreshBlotter()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(RefreshBlotter));
                return;
            }

            dgvBlotter.Rows.Clear();
            var trades = TradeBlotter.Instance.GetTodaysTrades();

            foreach (var trade in trades)
            {
                var rowIndex = dgvBlotter.Rows.Add(
                    trade.TradeTime,
                    trade.ClOrdID,
                    trade.LP,
                    trade.Side,
                    trade.Underlying,
                    trade.StructureType,
                    trade.LegCount,
                    trade.NetPremium,
                    trade.Status,
                    trade.ExecID ?? "",
                    trade.RejectReason ?? ""
                );

                // Color code by status
                var row = dgvBlotter.Rows[rowIndex];
                switch (trade.Status)
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
                }
            }

            lblTitle.Text = $"Trade Blotter - Today's Executions ({trades.Count} trades)";
        }

        private void OnTradeAddedOrUpdated(TradeBlotterEntry trade)
        {
            RefreshBlotter();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Unsubscribe from events
            TradeBlotter.Instance.OnTradeAdded -= OnTradeAddedOrUpdated;
            TradeBlotter.Instance.OnTradeUpdated -= OnTradeAddedOrUpdated;

            base.OnFormClosing(e);
        }
    }
}