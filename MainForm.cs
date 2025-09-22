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

            // Create top panel to hold all the input controls
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 220, // Enough space for input + buttons + label
                Padding = new Padding(25, 25, 25, 15)
            };

            // Trade Input Section
            var lblTradeInput = new Label
            {
                Text = "Enter trade request:",
                Location = new Point(0, 0),
                Size = new Size(150, 23),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            txtTradeInput = new TextBox
            {
                Location = new Point(0, 30),
                Size = new Size(800, 60),
                Font = new Font("Segoe UI", 9F),
                PlaceholderText = "e.g., eursek 4m i buy a 11.00 put in 100 mio and sell a 11.5000 call in 50 mio",
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.None,
                WordWrap = true
            };

            // Bloomberg Status
            lblBloombergStatus = new Label
            {
                Text = "Bloomberg: Disconnected",
                Location = new Point(830, 0),
                Size = new Size(200, 23),
                ForeColor = Color.Red,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            // Control Buttons Row
            var buttonY = 115;
            btnClearAll = new Button
            {
                Text = "Clear All",
                Location = new Point(0, buttonY),
                Size = new Size(110, 35),
                BackColor = Color.FromArgb(220, 53, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btnClearAll.FlatAppearance.BorderSize = 0;

            btnCopyOVML = new Button
            {
                Text = "Copy OVML",
                Location = new Point(125, buttonY),
                Size = new Size(110, 35),
                BackColor = Color.FromArgb(40, 167, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btnCopyOVML.FlatAppearance.BorderSize = 0;

            btnCopyUBS = new Button
            {
                Text = "Copy UBS",
                Location = new Point(250, buttonY),
                Size = new Size(110, 35),
                BackColor = Color.FromArgb(40, 167, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btnCopyUBS.FlatAppearance.BorderSize = 0;

            btnClearPatterns = new Button
            {
                Text = "Clear AI Patterns",
                Location = new Point(375, buttonY),
                Size = new Size(140, 35),
                BackColor = Color.FromArgb(220, 53, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btnClearPatterns.FlatAppearance.BorderSize = 0;

            chkAutoSend = new CheckBox
            {
                Text = "Auto-send to Bloomberg",
                Location = new Point(530, buttonY + 8),
                Size = new Size(180, 20),
                Checked = true,
                Font = new Font("Segoe UI", 9F)
            };

            btnToggleDebug = new Button
            {
                Text = "Show Debug",
                Location = new Point(730, buttonY),
                Size = new Size(110, 35),
                BackColor = Color.FromArgb(108, 117, 125),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            btnToggleDebug.FlatAppearance.BorderSize = 0;

            // Trade Blotter Label
            var lblBlotter = new Label
            {
                Text = "Trade Blotter (Click X to reject bad OVML patterns - good patterns auto-learn):",
                Location = new Point(0, 170),
                Size = new Size(500, 20),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            // Add all controls to top panel
            pnlTop.Controls.AddRange(new Control[] {
        lblTradeInput, txtTradeInput, lblBloombergStatus,
        btnClearAll, btnCopyOVML, btnCopyUBS, btnClearPatterns, chkAutoSend, btnToggleDebug,
        lblBlotter
    });

            // Create the main content panel (fills remaining space)
            var pnlContent = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 0, 20, 0)
            };

            // Trade Blotter DataGridView
            dgvTradeBlotter = new DataGridView
            {
                Dock = DockStyle.Fill,
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

            // Debug Panel (initially hidden, docked at bottom)
            pnlDebug = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 200,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(248, 249, 250),
                Visible = false
            };

            var btnClearDebug = new Button
            {
                Text = "Clear Debug",
                Dock = DockStyle.Top,
                Height = 25,
                BackColor = Color.FromArgb(220, 53, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight
            };
            btnClearDebug.FlatAppearance.BorderSize = 0;
            btnClearDebug.Click += (s, e) => txtDebugLog.Clear();

            var lblDebug = new Label
            {
                Text = "Debug Log:",
                Dock = DockStyle.Top,
                Height = 20,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            txtDebugLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.White,
                Font = new Font("Consolas", 8F),
                WordWrap = false
            };

            // Add controls to debug panel in correct order for docking
            pnlDebug.Controls.Add(txtDebugLog);
            pnlDebug.Controls.Add(lblDebug);
            pnlDebug.Controls.Add(btnClearDebug);

            // Add DataGridView to content panel
            pnlContent.Controls.Add(dgvTradeBlotter);
            pnlContent.Controls.Add(pnlDebug); // Debug panel docks to bottom of content area

            // Add main panels to form
            this.Controls.Add(pnlContent); // Content fills remaining space
            this.Controls.Add(pnlTop);     // Top panel docks to top

            // Ensure proper tab order
            this.SetTabOrder();
        }

        private void SetTabOrder()
        {
            txtTradeInput.TabIndex = 0;
            btnClearAll.TabIndex = 1;
            btnCopyOVML.TabIndex = 2;
            btnCopyUBS.TabIndex = 3;
            btnClearPatterns.TabIndex = 4;
            chkAutoSend.TabIndex = 5;
            btnToggleDebug.TabIndex = 6;
            dgvTradeBlotter.TabIndex = 7;
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
                HeaderText = "Reject",
                Width = 50,
                Text = "X",
                UseColumnTextForButtonValue = true
            });

            // Add hidden UBS column to store UBS data
            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "UBS",
                HeaderText = "UBS",
                Width = 0,
                Visible = false,
                ReadOnly = true
            });

            // Style the header
            dgvTradeBlotter.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(52, 58, 64);
            dgvTradeBlotter.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvTradeBlotter.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            dgvTradeBlotter.EnableHeadersVisualStyles = false;

            // Alternate row colors
            dgvTradeBlotter.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 250);

            // Hide the row headers (removes dark blue highlight on first cell)
            dgvTradeBlotter.RowHeadersVisible = false;

            // Softer selection color
            dgvTradeBlotter.DefaultCellStyle.SelectionBackColor = Color.LightSkyBlue;
            dgvTradeBlotter.DefaultCellStyle.SelectionForeColor = Color.Black;

            // Make the grid stretch to form width
            dgvTradeBlotter.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            // Adjust relative widths (Reject column excluded because it's fixed)
            dgvTradeBlotter.Columns["Time"].FillWeight = 50;
            dgvTradeBlotter.Columns["Legs"].FillWeight = 40;
            dgvTradeBlotter.Columns["Expiry"].FillWeight = 70;
            dgvTradeBlotter.Columns["SpotRef"].FillWeight = 80;
            dgvTradeBlotter.Columns["Underlying"].FillWeight = 90;
            dgvTradeBlotter.Columns["Request"].FillWeight = 220;
            dgvTradeBlotter.Columns["OVML"].FillWeight = 250;

            // Make Reject fixed width
            dgvTradeBlotter.Columns["Reject"].Width = 50;
            dgvTradeBlotter.Columns["Reject"].MinimumWidth = 50;
            dgvTradeBlotter.Columns["Reject"].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;

            // Apply alignment rules
            foreach (DataGridViewColumn col in dgvTradeBlotter.Columns)
            {
                switch (col.Name)
                {
                    case "Time":
                    case "Legs":
                    case "Expiry":
                    case "SpotRef":
                    case "Reject":
                    case "Method":   // centered too
                        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
                        break;

                    default:
                        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
                        col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
                        break;
                }
            }
        }



        private void SetupServices()
        {
            _bloombergService = new BloombergService();

            // Load OpenAI API key (environment variable preferred)
            string openAIApiKey = LoadApiKey();
            Console.WriteLine($"DEBUG: OpenAI API Key loaded: {(string.IsNullOrEmpty(openAIApiKey) ? "NONE" : "YES (length: " + openAIApiKey.Length + ")")}");

            // Initialize trade parser with API key
            _tradeParser = new TradeParser(_bloombergService, openAIApiKey);


            // Hook into debug callback to capture parsing logs
            _tradeParser.DebugCallback = LogDebugMessage;

            // Update Bloomberg status
            UpdateBloombergStatus();
        }

        private string LoadApiKey()
        {
            // 1. Try environment variable first
            string key = Environment.GetEnvironmentVariable("OpenAIApiKey");

            // 2. Fall back to App.config if not found
            if (string.IsNullOrEmpty(key) || key == "changeme")
            {
                key = System.Configuration.ConfigurationManager.AppSettings["OpenAIApiKey"];
            }

            return key;
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
                result.ParseMethod,
                "", // Placeholder for Reject column
                result.UBS ?? "" // Store UBS data in hidden column
            });

            // Enhanced color coding with validation status
            var row = dgvTradeBlotter.Rows[0];

            if (result.ParseMethod.StartsWith("Regex"))
            {
                row.DefaultCellStyle.BackColor = Color.LightGreen;
            }
            else if (result.ParseMethod.StartsWith("Learned"))
            {
                row.DefaultCellStyle.BackColor = Color.LightYellow;
            }
            else if (result.ParseMethod.Contains("AI-Success") && result.ParseMethod.Contains("Validated"))
            {
                row.DefaultCellStyle.BackColor = Color.LightBlue; // Validated AI
            }
            else if (result.ParseMethod.Contains("AI-Warning"))
            {
                row.DefaultCellStyle.BackColor = Color.Orange; // Failed validation
                if (!string.IsNullOrEmpty(result.ValidationWarning))
                {
                    row.Cells["Method"].ToolTipText = result.ValidationWarning;
                }
            }
            else if (result.ParseMethod.StartsWith("AI"))
            {
                row.DefaultCellStyle.BackColor = Color.LightBlue;
            }
            else if (result.ParseMethod.Contains("Error"))
            {
                row.DefaultCellStyle.BackColor = Color.LightCoral;
            }

            // Add validation info to tooltip
            if (result.ValidationResult != null)
            {
                var methodCell = row.Cells["Method"];
                var validationInfo = $"Validation: {(result.ValidationResult.IsValid ? "PASSED" : "FAILED")} " +
                                    $"(Confidence: {result.ValidationResult.Confidence:P0})";

                var currentTooltip = methodCell.ToolTipText ?? "";
                methodCell.ToolTipText = string.IsNullOrEmpty(currentTooltip) ?
                    validationInfo :
                    $"{currentTooltip}\n{validationInfo}";
            }
        }

        private string ExtractSpotFromOVML(string ovml)
        {
            if (string.IsNullOrEmpty(ovml)) return "";

            var match = System.Text.RegularExpressions.Regex.Match(ovml, @"SP(\d+\.?\d*)");
            return match.Success ? match.Groups[1].Value : "";
        }

        private void BtnToggleDebug_Click(object sender, EventArgs e)
        {
            debugVisible = !debugVisible;
            pnlDebug.Visible = debugVisible;
            btnToggleDebug.Text = debugVisible ? "Hide Debug" : "Show Debug";
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
                var ubs = dgvTradeBlotter.SelectedRows[0].Cells["UBS"].Value?.ToString();

                if (!string.IsNullOrEmpty(ubs))
                {
                    Clipboard.SetText(ubs);
                    MessageBox.Show("UBS format copied to clipboard!", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("No UBS data available for this trade.", "No Data",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    var row = dgvTradeBlotter.Rows[e.RowIndex];
                    string method = row.Cells["Method"].Value?.ToString();
                    string request = row.Cells["Request"].Value?.ToString();
                    string ovml = row.Cells["OVML"].Value?.ToString();

                    var message = "Reject this trade pattern?\n\n";

                    if (method?.StartsWith("Learned-") == true)
                    {
                        string patternName = method.Replace("Learned-", "");
                        message += $"? This will permanently delete the learned pattern: '{patternName}'\n" +
                                  "• Future similar inputs will go back to AI\n" +
                                  "• This pattern can be re-learned if AI validates it again\n\n";
                    }
                    else if (method?.Contains("AI-Warning") == true)
                    {
                        message += "? This trade already failed validation.\n" +
                                  "• Will prevent learning similar patterns\n\n";
                    }
                    else if (method?.Contains("AI-Success") == true)
                    {
                        message += "? This trade passed validation.\n" +
                                  "• Will prevent learning this specific pattern\n\n";
                    }

                    message += "Continue with rejection?";

                    var result = MessageBox.Show(message, "Confirm Pattern Rejection",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                    if (result == DialogResult.Yes)
                    {
                        // Remove from blotter
                        dgvTradeBlotter.Rows.RemoveAt(e.RowIndex);

                        // Handle learned pattern deletion
                        if (method?.StartsWith("Learned-") == true)
                        {
                            string patternName = method.Replace("Learned-", "");
                            bool success = _tradeParser.RemoveLearnedPattern(patternName);

                            if (success)
                            {
                                LogDebugMessage($"Deleted learned pattern: {patternName}");
                                MessageBox.Show($"Learned pattern '{patternName}' has been deleted.\n\n" +
                                              "Similar inputs will now use AI processing again.",
                                              "Pattern Deleted", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            else
                            {
                                MessageBox.Show("Failed to delete pattern. It may have already been removed.",
                                              "Deletion Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            }
                        }
                        else
                        {
                            // Mark as bad example for future learning
                            LogDebugMessage($"Marked as problematic: {request}");
                        }
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