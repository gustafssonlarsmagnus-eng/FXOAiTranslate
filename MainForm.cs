using System;
using System.Drawing;
using System.Text.RegularExpressions;
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
  
        private CheckBox chkAutoSend;
        private Label lblBloombergStatus;
        private DataGridView dgvTradeBlotter;
        private Button btnToggleDebug;
        private Panel pnlDebug;
        private TextBox txtDebugLog;
        private bool debugVisible = false;

        // Re-entrancy guard for processing
        private bool _processing;

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

            // Create top panel
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 110,
                Padding = new Padding(25, 15, 25, 5)
            };

            // Trade Input Section
            txtTradeInput = new TextBox
            {
                Location = new Point(15, 10),
                Size = new Size(800, 40),
                Font = new Font("Segoe UI", 9F),
                PlaceholderText = "Enter trade request (e.g., eursek 4m i buy a 11.00 put in 100 mio and sell a 11.5000 call in 50 mio)",
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.None,
                WordWrap = true
            };

            // Button row
            var buttonY = 60;

            btnClearAll = CreateBloombergButton("CANCEL", Color.FromArgb(220, 53, 69), 15, buttonY);   // red
            btnCopyOVML = CreateBloombergButton("OVML", Color.FromArgb(0, 200, 0), 105, buttonY);      // green
            btnCopyUBS = CreateBloombergButton("UBS", Color.FromArgb(200, 150, 255), 195, buttonY);    // pink/purple

            btnToggleDebug = CreateBloombergButton("Show Debug", Color.FromArgb(0, 120, 255), 285, buttonY);  // bright blue

            // Checkbox + Bloomberg status inline
            chkAutoSend = new CheckBox
            {
                Text = "Auto-send",
                Location = new Point(470, buttonY + 3),
                Size = new Size(100, 20),
                Checked = true,
                Font = new Font("Segoe UI", 9F)
            };

            lblBloombergStatus = new Label
            {
                Text = "Bloomberg: Disconnected",
                Location = new Point(chkAutoSend.Right + 10, chkAutoSend.Top),
                Size = new Size(200, 20),
                ForeColor = Color.Red,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };

            // Trade Blotter Label
            var lblBlotter = new Label
            {
                Text = "Trade Blotter (Click X to reject bad OVML patterns - good patterns auto-learn):",
                Dock = DockStyle.Top,
                Height = 20,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            // Add all controls to top panel
            pnlTop.Controls.AddRange(new Control[] {
                txtTradeInput,
                btnClearAll, btnCopyOVML, btnCopyUBS, btnToggleDebug,
                chkAutoSend, lblBloombergStatus
            });

            // Main content panel
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

            // Debug Panel
            pnlDebug = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 200,
                BackColor = Color.White,
                Padding = new Padding(5),
                Visible = false
            };

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 25,
                BackColor = Color.White
            };

            var lblDebug = new Label
            {
                Text = "Debug Log:",
                Dock = DockStyle.Left,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.Black,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 5, 0, 0)
            };

            var btnCopyDebug = new Button
            {
                Text = "Copy Debug",
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                BackColor = Color.Transparent,
                ForeColor = Color.DimGray,
                Cursor = Cursors.Hand,
                Padding = new Padding(0, 3, 0, 0)
            };
            btnCopyDebug.FlatAppearance.BorderSize = 0;
            btnCopyDebug.Click += (s, e) => Clipboard.SetText(txtDebugLog.Text);

            var btnClearDebug = new Button
            {
                Text = "Clear Debug",
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8F, FontStyle.Regular),
                BackColor = Color.Transparent,
                ForeColor = Color.DimGray,
                Cursor = Cursors.Hand,
                Padding = new Padding(0, 3, 0, 0)
            };
            btnClearDebug.FlatAppearance.BorderSize = 0;
            btnClearDebug.Click += (s, e) => txtDebugLog.Clear();

            txtDebugLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.White,
                ForeColor = Color.Black,
                Font = new Font("Consolas", 9F),
                BorderStyle = BorderStyle.None,
                WordWrap = false
            };

            headerPanel.Controls.Add(btnClearDebug);
            headerPanel.Controls.Add(btnCopyDebug);
            headerPanel.Controls.Add(lblDebug);

            pnlDebug.Controls.Add(txtDebugLog);
            pnlDebug.Controls.Add(headerPanel);

            // Inner grid panel
            var pnlGrid = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 0, 0, 5)
            };
            pnlGrid.Controls.Add(dgvTradeBlotter);

            // Add controls in order
            pnlContent.Controls.Add(pnlGrid);
            pnlContent.Controls.Add(pnlDebug);
            pnlContent.Controls.Add(lblBlotter);

            this.Controls.Add(pnlContent);
            this.Controls.Add(pnlTop);

            this.SetTabOrder();
        }

        private Button CreateBloombergButton(string text, Color backColor, int x, int y, int width = 80, int height = 24)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = backColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Margin = new Padding(2, 0, 2, 0),
                TabStop = false // removes dotted focus rectangle and excludes from tab order
            };

            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(backColor);
            btn.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(backColor);

            return btn;
        }

        private void SetTabOrder()
        {
            // Buttons have TabStop=false, so keep tabbing to main inputs only
            txtTradeInput.TabIndex = 0;
            chkAutoSend.TabIndex = 1;
            btnToggleDebug.TabIndex = 2; // TabStop=false by default from CreateBloombergButton; change if you want it tabbable
            dgvTradeBlotter.TabIndex = 3;
        }

        private void SetupDataGridView()
        {
            // Columns (no fixed Width when using AutoSizeMode)
            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Time",
                HeaderText = "Time",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            });

            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Request",
                HeaderText = "Request",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });

            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "OVML",
                HeaderText = "OVML",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });

            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Underlying",
                HeaderText = "Underlying",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            });

            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Legs",
                HeaderText = "Legs",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            });

            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Expiry",
                HeaderText = "Expiry",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            });

            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "SpotRef",
                HeaderText = "Spot Ref",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            });

            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Method",
                HeaderText = "Method",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            });

            var rejectCol = new DataGridViewButtonColumn
            {
                Name = "Reject",
                HeaderText = "Reject",
                UseColumnTextForButtonValue = true,
                Text = "X",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            };
            rejectCol.DefaultCellStyle.NullValue = "X";
            dgvTradeBlotter.Columns.Add(rejectCol);

            // Hidden UBS column (Width not needed when invisible)
            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "UBS",
                HeaderText = "UBS",
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

            // Hide the row headers
            dgvTradeBlotter.RowHeadersVisible = false;

            // Softer selection color
            dgvTradeBlotter.DefaultCellStyle.SelectionBackColor = Color.LightSkyBlue;
            dgvTradeBlotter.DefaultCellStyle.SelectionForeColor = Color.Black;

            // Alignment rules
            foreach (DataGridViewColumn col in dgvTradeBlotter.Columns)
            {
                switch (col.Name)
                {
                    case "Time":
                    case "Legs":
                    case "Expiry":
                    case "SpotRef":
                    case "Reject":
                    case "Method":
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
           
            btnToggleDebug.Click += BtnToggleDebug_Click;
            dgvTradeBlotter.CellClick += DgvTradeBlotter_CellClick;
            dgvTradeBlotter.CellToolTipTextNeeded += DgvTradeBlotter_CellToolTipTextNeeded; // single hookup only
        }

        private void DgvTradeBlotter_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                var grid = sender as DataGridView;
                var columnName = grid.Columns[e.ColumnIndex].Name;

                if (columnName == "Request" || columnName == "OVML" || columnName == "Method")
                {
                    var value = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
                    if (value != null)
                    {
                        e.ToolTipText = value.ToString();
                    }
                }
            }
        }

        private async void TxtTradeInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true; // prevents newline/beep in multiline textbox
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
            if (_processing) return;
            _processing = true;
            try
            {
                string input = txtTradeInput.Text.Trim();
                if (string.IsNullOrEmpty(input)) return;

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
            finally
            {
                _processing = false;
            }
        }

        private void AddTradeToBlotter(string request, TradeParseResult result)
        {
            string spotRef = ExtractSpotFromOVML(result.OVML);

            dgvTradeBlotter.Rows.Insert(0, new object[]
            {
                DateTime.Now.ToString("HH:mm:ss"),
                request,
                result.OVML,
                result.Underlying,
                result.LegCount,
                result.Expiry,
                spotRef,
                result.ParseMethod,
                null,             // Reject button column (value unused)
                result.UBS ?? ""  // hidden UBS
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

            // Always highlight/select the newest row and keep it visible at the top
            dgvTradeBlotter.ClearSelection();
            row.Selected = true;
            dgvTradeBlotter.CurrentCell = row.Cells["Request"];
            dgvTradeBlotter.FirstDisplayedScrollingRowIndex = 0;
        }

        private string ExtractSpotFromOVML(string ovml)
        {
            if (string.IsNullOrEmpty(ovml)) return "";

            var match = System.Text.RegularExpressions.Regex.Match(ovml, @"SP(\d+(?:[.,]\d+)?)");
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
                    LogDebugMessage("[UI] UBS copied to clipboard");
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
                        var match = Regex.Match(method, @"Learned-Pattern-(\d{8}-\d{6})");
                        if (match.Success)
                        {
                            string patternTimestamp = match.Groups[1].Value;
                            message += $"⚠ This will permanently delete the learned pattern: '{patternTimestamp}'\n" +
                                      "• Future similar inputs will go back to AI\n" +
                                      "• This pattern can be re-learned if AI validates it again\n\n";
                        }
                    }
                    else if (method?.Contains("AI-Warning") == true)
                    {
                        message += "⚠ This trade already failed validation.\n" +
                                  "• Will prevent learning similar patterns\n\n";
                    }
                    else if (method?.Contains("AI-Success") == true)
                    {
                        message += "✓ This trade passed validation.\n" +
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
                            var match = Regex.Match(method, @"Learned-Pattern-(\d{8}-\d{6})");
                            if (match.Success)
                            {
                                string patternTimestamp = match.Groups[1].Value;
                                bool success = _tradeParser.RemoveLearnedPattern(patternTimestamp);

                                if (success)
                                {
                                    LogDebugMessage($"Deleted learned pattern: {patternTimestamp}");
                                    MessageBox.Show($"Learned pattern '{patternTimestamp}' has been deleted.\n\n" +
                                                  "Similar inputs will now use AI processing again.",
                                                  "Pattern Deleted", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                }
                                else
                                {
                                    MessageBox.Show("Failed to delete pattern. It may have already been removed.",
                                                  "Deletion Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                }
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
