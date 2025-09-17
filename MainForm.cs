using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FXOAiTranslator
{
    public partial class MainForm : Form
    {
        private TradeParser _tradeParser;
        private BloombergService _bloombergService;

        // UI Controls
        private TextBox txtTradeInput;
        private Button btnClearAll;
        private Button btnCopyOVML;
        private Button btnCopyUBS;
        private Button btnClearPatterns;
        private CheckBox chkAutoSend;
        private Label lblBloombergStatus;
        private DataGridView dgvTradeBlotter;
        private Button btnToggleDebug;
        private Panel pnlDebug;
        private TextBox txtDebugLog;
        private bool debugVisible = false;

        public MainForm()
        {
            InitializeComponent();
            SetupServices();
            SetupEventHandlers();
        }

        private void InitializeComponent()
        {
            this.Text = "FXO AI Translator";
            this.Size = new Size(1200, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.White;

            // Trade Input Section
            var lblTradeInput = new Label
            {
                Text = "Enter trade request:",
                Location = new Point(20, 20),
                Size = new Size(150, 23),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            txtTradeInput = new TextBox
            {
                Location = new Point(20, 45),
                Size = new Size(800, 23),
                Font = new Font("Segoe UI", 9F),
                PlaceholderText = "e.g., eursek 4m i buy a 11.00 put in 100 mio and sell a 11.5000 call in 50 mio"
            };

            // Bloomberg Status
            lblBloombergStatus = new Label
            {
                Text = "Bloomberg: Disconnected",
                Location = new Point(850, 20),
                Size = new Size(200, 23),
                ForeColor = Color.Red,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            // Control Buttons
            btnClearAll = new Button
            {
                Text = "Clear All",
                Location = new Point(20, 80),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(220, 53, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btnClearAll.FlatAppearance.BorderSize = 0;

            btnCopyOVML = new Button
            {
                Text = "Copy OVML",
                Location = new Point(130, 80),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(40, 167, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btnCopyOVML.FlatAppearance.BorderSize = 0;

            btnCopyUBS = new Button
            {
                Text = "Copy UBS",
                Location = new Point(240, 80),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(40, 167, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btnCopyUBS.FlatAppearance.BorderSize = 0;

            btnClearPatterns = new Button
            {
                Text = "Clear AI Patterns",
                Location = new Point(350, 80),
                Size = new Size(130, 30),
                BackColor = Color.FromArgb(220, 53, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btnClearPatterns.FlatAppearance.BorderSize = 0;

            chkAutoSend = new CheckBox
            {
                Text = "Auto-send to Bloomberg",
                Location = new Point(500, 85),
                Size = new Size(180, 20),
                Checked = true,
                Font = new Font("Segoe UI", 9F)
            };

            btnToggleDebug = new Button
            {
                Text = "Show Debug",
                Location = new Point(700, 80),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(108, 117, 125),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btnToggleDebug.FlatAppearance.BorderSize = 0;

            // Trade Blotter
            var lblBlotter = new Label
            {
                Text = "Trade Blotter (Click X to reject bad OVML patterns - good patterns auto-learn):",
                Location = new Point(20, 125),
                Size = new Size(500, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            dgvTradeBlotter = new DataGridView
            {
                Location = new Point(20, 150),
                Size = new Size(1150, 300),
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D,
                Font = new Font("Segoe UI", 9F)
            };

            SetupDataGridView();

            // Debug Panel (initially hidden)
            pnlDebug = new Panel
            {
                Location = new Point(20, 460),
                Size = new Size(1150, 180),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(248, 249, 250),
                Visible = false
            };

            var lblDebug = new Label
            {
                Text = "Debug Log:",
                Location = new Point(10, 10),
                Size = new Size(100, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            txtDebugLog = new TextBox
            {
                Location = new Point(10, 35),
                Size = new Size(1125, 120),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.White,
                Font = new Font("Consolas", 8F),
                WordWrap = false
            };

            var btnClearDebug = new Button
            {
                Text = "Clear Debug",
                Location = new Point(1050, 8),
                Size = new Size(85, 25),
                BackColor = Color.FromArgb(220, 53, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold)
            };
            btnClearDebug.FlatAppearance.BorderSize = 0;
            btnClearDebug.Click += (s, e) => txtDebugLog.Clear();

            pnlDebug.Controls.Add(lblDebug);
            pnlDebug.Controls.Add(txtDebugLog);
            pnlDebug.Controls.Add(btnClearDebug);

            // Add all controls to form
            this.Controls.AddRange(new Control[] {
                lblTradeInput, txtTradeInput, lblBloombergStatus,
                btnClearAll, btnCopyOVML, btnCopyUBS, btnClearPatterns, chkAutoSend, btnToggleDebug,
                lblBlotter, dgvTradeBlotter, pnlDebug
            });
        }

        private void SetupDataGridView()
        {
            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Time",
                HeaderText = "Time",
                Width = 80,
                ReadOnly = true
            });

            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Request",
                HeaderText = "Request",
                Width = 300,
                ReadOnly = true
            });

            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "OVML",
                HeaderText = "OVML",
                Width = 350,
                ReadOnly = true
            });

            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Underlying",
                HeaderText = "Underlying",
                Width = 80,
                ReadOnly = true
            });

            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Legs",
                HeaderText = "Legs",
                Width = 50,
                ReadOnly = true
            });

            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Expiry",
                HeaderText = "Expiry",
                Width = 80,
                ReadOnly = true
            });

            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "SpotRef",
                HeaderText = "Spot Ref",
                Width = 80,
                ReadOnly = true
            });

            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Method",
                HeaderText = "Method",
                Width = 100,
                ReadOnly = true
            });

            dgvTradeBlotter.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "Reject",
                HeaderText = "X Reject",
                Width = 70,
                Text = "X",
                UseColumnTextForButtonValue = true
            });

            // Style the header
            dgvTradeBlotter.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(52, 58, 64);
            dgvTradeBlotter.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvTradeBlotter.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            dgvTradeBlotter.EnableHeadersVisualStyles = false;

            // Alternate row colors
            dgvTradeBlotter.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 250);
        }

        private void SetupServices()
        {
            _bloombergService = new BloombergService();
            _tradeParser = new TradeParser(_bloombergService);

            // Hook into debug callback to capture parsing logs
            _tradeParser.DebugCallback = LogDebugMessage;

            // Update Bloomberg status
            UpdateBloombergStatus();
        }

        private void SetupEventHandlers()
        {
            txtTradeInput.KeyDown += TxtTradeInput_KeyDown;
            txtTradeInput.TextChanged += TxtTradeInput_TextChanged;
            btnClearAll.Click += BtnClearAll_Click;
            btnCopyOVML.Click += BtnCopyOVML_Click;
            btnCopyUBS.Click += BtnCopyUBS_Click;
            btnClearPatterns.Click += BtnClearPatterns_Click;
            btnToggleDebug.Click += BtnToggleDebug_Click;
            dgvTradeBlotter.CellClick += DgvTradeBlotter_CellClick;
        }

        private async void TxtTradeInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                await ProcessTrade();
            }
        }

        private async void TxtTradeInput_TextChanged(object sender, EventArgs e)
        {
            string input = txtTradeInput.Text.Trim();

            // Process automatically when text is pasted (longer than typical typing)
            if (input.Length > 10 && input.Contains(" "))
            {
                // Small delay to ensure paste operation is complete
                await Task.Delay(100);

                // Check if text is still there (user didn't clear it)
                if (txtTradeInput.Text.Trim() == input)
                {
                    await ProcessTrade();
                }
            }
        }

        private async Task ProcessTrade()
        {
            string input = txtTradeInput.Text.Trim();
            if (string.IsNullOrEmpty(input)) return;

            try
            {
                var result = await _tradeParser.ParseTradeAsync(input);
                if (result != null)
                {
                    AddTradeToBlotter(input, result);

                    if (chkAutoSend.Checked && _bloombergService.IsConnected && !string.IsNullOrEmpty(result.OVML))
                    {
                        _bloombergService.SendOVML(result.OVML);
                    }
                }

                // Clear the input box after processing
                txtTradeInput.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error processing trade: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddTradeToBlotter(string request, TradeParseResult result)
        {
            string spotRef = ExtractSpotFromOVML(result.OVML);

            dgvTradeBlotter.Rows.Insert(0, new object[]
            {
                DateTime.Now.ToString("HH:mm:ss"),
                request.Length > 50 ? request.Substring(0, 47) + "..." : request,
                result.OVML,
                result.Underlying,
                result.LegCount,
                result.Expiry,
                spotRef,
                result.ParseMethod
            });

            // Color code by method
            var row = dgvTradeBlotter.Rows[0];
            if (result.ParseMethod.StartsWith("Regex"))
                row.DefaultCellStyle.BackColor = Color.LightGreen;
            else if (result.ParseMethod.StartsWith("AI"))
                row.DefaultCellStyle.BackColor = Color.LightBlue;
            else if (result.ParseMethod.Contains("Error"))
                row.DefaultCellStyle.BackColor = Color.LightCoral;
        }

        private string ExtractSpotFromOVML(string ovml)
        {
            if (string.IsNullOrEmpty(ovml)) return "";

            var match = System.Text.RegularExpressions.Regex.Match(ovml, @"SP(\d+\.?\d*)");
            return match.Success ? match.Groups[1].Value : "";
        }

        private string GenerateUBSFormat(string ovml, string originalRequest)
        {
            if (string.IsNullOrEmpty(ovml)) return "";

            try
            {
                // Parse OVML to extract components
                var parts = ovml.Split(' ');
                if (parts.Length < 6) return "";

                string currency = parts[1];
                string legs = parts[2];
                string directions = parts[3];
                string strikes = parts[4];
                string expiry = parts[5];
                string notionals = "";
                string spotRef = "";

                // Find notionals and spot ref
                for (int i = 6; i < parts.Length; i++)
                {
                    if (parts[i].StartsWith("N"))
                        notionals = parts[i].Substring(1); // Remove 'N'
                    else if (parts[i].StartsWith("SP"))
                        spotRef = parts[i].Substring(2); // Remove 'SP'
                }

                // Convert to UBS format
                if (legs == "1L")
                {
                    // Single leg
                    var dir = directions == "B" ? "BUY" : "SELL";
                    var notional = notionals.Replace("M", "");
                    var ubsNotional = (int.Parse(notional) / 10).ToString(); // Divide by 10
                    var optionType = strikes.Contains("C") ? "CALL" : "PUT";
                    var strike = strikes.Replace("C", "").Replace("P", "");

                    return $"{currency} {expiry} {dir} {ubsNotional}M {strike} {optionType}" +
                           (string.IsNullOrEmpty(spotRef) ? "" : $" @ SP {spotRef}");
                }
                else
                {
                    // Multi-leg
                    var dirs = directions.Split(',');
                    var strikeArray = strikes.Split(',');
                    var notionalArray = notionals.Split(',');

                    var ubsLegs = new string[dirs.Length];
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        var dir = dirs[i] == "B" ? "BUY" : "SELL";
                        var notional = notionalArray[i].Replace("M", "");
                        var ubsNotional = (int.Parse(notional) / 10).ToString(); // Divide by 10
                        var optionType = strikeArray[i].Contains("C") ? "CALL" : "PUT";
                        var strike = strikeArray[i].Replace("C", "").Replace("P", "");

                        ubsLegs[i] = $"{dir} {ubsNotional}M {strike} {optionType}";
                    }

                    return $"{currency} {expiry} " + string.Join(" / ", ubsLegs) +
                           (string.IsNullOrEmpty(spotRef) ? "" : $" @ SP {spotRef}");
                }
            }
            catch
            {
                return $"Error converting OVML to UBS format: {ovml}";
            }
        }

        private void BtnToggleDebug_Click(object sender, EventArgs e)
        {
            debugVisible = !debugVisible;
            pnlDebug.Visible = debugVisible;

            if (debugVisible)
            {
                btnToggleDebug.Text = "Hide Debug";
                this.Size = new Size(1200, 700); // More reasonable expansion
                dgvTradeBlotter.Size = new Size(1150, 300); // Slightly smaller blotter
            }
            else
            {
                btnToggleDebug.Text = "Show Debug";
                this.Size = new Size(1200, 500); // Original size
                dgvTradeBlotter.Size = new Size(1150, 300); // Restore blotter size
            }
        }

        private void LogDebugMessage(string message)
        {
            if (txtDebugLog.InvokeRequired)
            {
                txtDebugLog.Invoke(new Action<string>(LogDebugMessage), message);
                return;
            }

            txtDebugLog.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n");
            txtDebugLog.ScrollToCaret();
        }

        private void UpdateBloombergStatus()
        {
            if (_bloombergService.IsConnected)
            {
                lblBloombergStatus.Text = "Bloomberg: Connected";
                lblBloombergStatus.ForeColor = Color.Green;
            }
            else
            {
                lblBloombergStatus.Text = "Bloomberg: Disconnected";
                lblBloombergStatus.ForeColor = Color.Red;
            }
        }

        private void BtnClearAll_Click(object sender, EventArgs e)
        {
            dgvTradeBlotter.Rows.Clear();
            txtTradeInput.Clear();
        }

        private void BtnCopyOVML_Click(object sender, EventArgs e)
        {
            if (dgvTradeBlotter.SelectedRows.Count > 0)
            {
                var ovml = dgvTradeBlotter.SelectedRows[0].Cells["OVML"].Value?.ToString();
                if (!string.IsNullOrEmpty(ovml))
                {
                    Clipboard.SetText(ovml);
                    MessageBox.Show("OVML copied to clipboard!", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else
            {
                MessageBox.Show("Please select a row first.", "No Selection",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BtnCopyUBS_Click(object sender, EventArgs e)
        {
            if (dgvTradeBlotter.SelectedRows.Count > 0)
            {
                var ovml = dgvTradeBlotter.SelectedRows[0].Cells["OVML"].Value?.ToString();
                var request = dgvTradeBlotter.SelectedRows[0].Cells["Request"].Value?.ToString();

                if (!string.IsNullOrEmpty(ovml))
                {
                    var ubs = GenerateUBSFormat(ovml, request);
                    Clipboard.SetText(ubs);
                    MessageBox.Show("UBS format copied to clipboard!", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else
            {
                MessageBox.Show("Please select a row first.", "No Selection",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BtnClearPatterns_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show("Clear all learned AI patterns?", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                // TODO: Clear learned patterns
                MessageBox.Show("AI patterns cleared.", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void DgvTradeBlotter_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                var column = dgvTradeBlotter.Columns[e.ColumnIndex];
                if (column.Name == "Reject")
                {
                    var result = MessageBox.Show("Reject this trade pattern?", "Confirm",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                    if (result == DialogResult.Yes)
                    {
                        dgvTradeBlotter.Rows.RemoveAt(e.RowIndex);
                    }
                }
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            txtTradeInput.Focus();
        }
    }
}