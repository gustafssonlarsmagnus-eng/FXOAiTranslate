using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FXOAiTranslator
{
    public partial class MainForm : Form
    {
        private TradeParser _tradeParser;
        private BloombergService _bloombergService;
        private string _tradesFilePath;
        private List<TradeRecord> _allTrades;

        // UI Controls
        private TextBox txtTradeInput;
        private Button btnCopyOVML;
        private Button btnCopyUBS;
        private CheckBox chkAutoSend;
        private Label lblBloombergStatus;
        private DataGridView dgvTradeBlotter;
        private Button btnToggleDebug;
        private Button btnFilterMenu;
        private Panel pnlDebug;
        private TextBox txtDebugLog;
        private ContextMenuStrip ctxRowMenu;
        private bool debugVisible = false;

        // Re-entrancy guard for processing
        private bool _processing;

        // Current filter
        private TradeFilter _currentFilter = TradeFilter.Today;

        public MainForm()
        {
            InitializeComponent();
            SetupServices();
            SetupEventHandlers();
            LoadTrades();
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

            btnFilterMenu = CreateBloombergButton("Filter ▼", Color.FromArgb(108, 117, 125), 15, buttonY, 90);
            btnCopyOVML = CreateBloombergButton("OVML", Color.FromArgb(0, 200, 0), 115, buttonY);
            btnCopyUBS = CreateBloombergButton("UBS", Color.FromArgb(200, 150, 255), 205, buttonY);
            btnToggleDebug = CreateBloombergButton("Debug", Color.FromArgb(0, 120, 255), 295, buttonY);

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
                Text = "Trade Blotter (Right-click for options):",
                Dock = DockStyle.Top,
                Height = 20,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };

            // Add all controls to top panel
            pnlTop.Controls.AddRange(new Control[] {
                txtTradeInput,
                btnFilterMenu, btnCopyOVML, btnCopyUBS, btnToggleDebug,
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
            SetupContextMenu();

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
                TabStop = false
            };

            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(backColor);
            btn.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(backColor);

            return btn;
        }

        private void SetTabOrder()
        {
            txtTradeInput.TabIndex = 0;
            chkAutoSend.TabIndex = 1;
            btnToggleDebug.TabIndex = 2;
            dgvTradeBlotter.TabIndex = 3;
        }

        private void SetupContextMenu()
        {
            ctxRowMenu = new ContextMenuStrip();
            ctxRowMenu.Items.Add("Copy OVML", null, (s, e) => CopySelectedCell("OVML"));
            ctxRowMenu.Items.Add("Copy UBS", null, (s, e) => CopySelectedCell("UBS"));
            ctxRowMenu.Items.Add("Copy Request", null, (s, e) => CopySelectedCell("Request"));
            ctxRowMenu.Items.Add(new ToolStripSeparator());
            ctxRowMenu.Items.Add("Re-parse with AI", null, CtxReParseAI_Click);
            ctxRowMenu.Items.Add(new ToolStripSeparator());
            ctxRowMenu.Items.Add("Delete Row", null, CtxDeleteRow_Click);

            dgvTradeBlotter.ContextMenuStrip = ctxRowMenu;
        }

        private void SetupDataGridView()
        {
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
                HeaderText = "✗",
                UseColumnTextForButtonValue = true,
                Text = "✗",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            };
            rejectCol.DefaultCellStyle.NullValue = "✗";
            dgvTradeBlotter.Columns.Add(rejectCol);

            var reParseCol = new DataGridViewButtonColumn
            {
                Name = "ReParseAI",
                HeaderText = "AI",
                UseColumnTextForButtonValue = true,
                Text = "↻",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            };
            reParseCol.DefaultCellStyle.NullValue = "↻";
            dgvTradeBlotter.Columns.Add(reParseCol);

            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "UBS",
                HeaderText = "UBS",
                Visible = false,
                ReadOnly = true
            });

            // Hidden ID column for tracking
            dgvTradeBlotter.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "TradeId",
                HeaderText = "TradeId",
                Visible = false,
                ReadOnly = true
            });

            // Style the header
            dgvTradeBlotter.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(52, 58, 64);
            dgvTradeBlotter.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            dgvTradeBlotter.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            dgvTradeBlotter.EnableHeadersVisualStyles = false;

            dgvTradeBlotter.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 249, 250);
            dgvTradeBlotter.RowHeadersVisible = false;
            dgvTradeBlotter.DefaultCellStyle.SelectionBackColor = Color.LightSkyBlue;
            dgvTradeBlotter.DefaultCellStyle.SelectionForeColor = Color.Black;

            foreach (DataGridViewColumn col in dgvTradeBlotter.Columns)
            {
                switch (col.Name)
                {
                    case "Time":
                    case "Legs":
                    case "Expiry":
                    case "SpotRef":
                    case "Reject":
                    case "ReParseAI":
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

            string openAIApiKey = LoadApiKey();
            Console.WriteLine($"DEBUG: OpenAI API Key loaded: {(string.IsNullOrEmpty(openAIApiKey) ? "NONE" : "YES (length: " + openAIApiKey.Length + ")")}");

            _tradeParser = new TradeParser(_bloombergService, openAIApiKey);
            _tradeParser.DebugCallback = LogDebugMessage;

            // Setup trades file path
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FXOAiTranslator");

            Directory.CreateDirectory(appDataPath);
            _tradesFilePath = Path.Combine(appDataPath, "trades.json");
            _allTrades = new List<TradeRecord>();

            UpdateBloombergStatus();
        }

        private string LoadApiKey()
        {
            string key = Environment.GetEnvironmentVariable("OpenAIApiKey");

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
            btnCopyOVML.Click += BtnCopyOVML_Click;
            btnCopyUBS.Click += BtnCopyUBS_Click;
            btnToggleDebug.Click += BtnToggleDebug_Click;
            btnFilterMenu.Click += BtnFilterMenu_Click;
            dgvTradeBlotter.CellClick += DgvTradeBlotter_CellClick;
            dgvTradeBlotter.CellToolTipTextNeeded += DgvTradeBlotter_CellToolTipTextNeeded;
            this.FormClosing += MainForm_FormClosing;
        }

        private void BtnFilterMenu_Click(object sender, EventArgs e)
        {
            var filterMenu = new ContextMenuStrip();

            var todayItem = new ToolStripMenuItem("Today's Requests");
            todayItem.Checked = (_currentFilter == TradeFilter.Today);
            todayItem.Click += (s, ev) => { _currentFilter = TradeFilter.Today; ApplyFilter(); };

            var allItem = new ToolStripMenuItem("All Requests");
            allItem.Checked = (_currentFilter == TradeFilter.All);
            allItem.Click += (s, ev) => { _currentFilter = TradeFilter.All; ApplyFilter(); };

            filterMenu.Items.Add(todayItem);
            filterMenu.Items.Add(allItem);

            filterMenu.Show(btnFilterMenu, new Point(0, btnFilterMenu.Height));
        }

        private void ApplyFilter()
        {
            dgvTradeBlotter.Rows.Clear();

            IEnumerable<TradeRecord> filtered = _allTrades;

            if (_currentFilter == TradeFilter.Today)
            {
                var today = DateTime.Today;
                filtered = _allTrades.Where(t => t.Timestamp.Date == today);
            }

            foreach (var trade in filtered.OrderByDescending(t => t.Timestamp))
            {
                AddTradeRowToGrid(trade);
            }

            UpdateFilterButtonText();
        }

        private void UpdateFilterButtonText()
        {
            btnFilterMenu.Text = _currentFilter == TradeFilter.Today ? "Today ▼" : "All ▼";
        }

        private void LoadTrades()
        {
            try
            {
                if (File.Exists(_tradesFilePath))
                {
                    string json = File.ReadAllText(_tradesFilePath);
                    _allTrades = JsonSerializer.Deserialize<List<TradeRecord>>(json) ?? new List<TradeRecord>();
                    LogDebugMessage($"Loaded {_allTrades.Count} trades from storage");
                }
            }
            catch (Exception ex)
            {
                LogDebugMessage($"Error loading trades: {ex.Message}");
                _allTrades = new List<TradeRecord>();
            }

            ApplyFilter();
        }

        private void SaveTrades()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_allTrades, options);
                File.WriteAllText(_tradesFilePath, json);
                LogDebugMessage($"Saved {_allTrades.Count} trades to storage");
            }
            catch (Exception ex)
            {
                LogDebugMessage($"Error saving trades: {ex.Message}");
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveTrades();
        }

        private void CopySelectedCell(string columnName)
        {
            if (dgvTradeBlotter.SelectedRows.Count > 0)
            {
                var value = dgvTradeBlotter.SelectedRows[0].Cells[columnName].Value?.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    Clipboard.SetText(value);
                    LogDebugMessage($"Copied {columnName} to clipboard");
                }
            }
        }

        private void CtxReParseAI_Click(object sender, EventArgs e)
        {
            if (dgvTradeBlotter.SelectedRows.Count > 0)
            {
                var row = dgvTradeBlotter.SelectedRows[0];
                string request = row.Cells["Request"].Value?.ToString();
                string tradeId = row.Cells["TradeId"].Value?.ToString();

                var result = MessageBox.Show(
                    "Re-parse this trade using AI only?\n\nCurrent result will be replaced.",
                    "Force AI Re-parse",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (result == DialogResult.Yes)
                {
                    // Remove from memory and grid
                    _allTrades.RemoveAll(t => t.Id == tradeId);
                    dgvTradeBlotter.Rows.Remove(row);

                    LogDebugMessage($"Force re-parsing with AI: {request}");

                    _ = Task.Run(async () =>
                    {
                        var parseResult = await _tradeParser.ParseTradeAsync(request, forceAI: true);

                        if (parseResult != null)
                        {
                            this.Invoke(new Action(() =>
                            {
                                AddTradeToBlotter(request, parseResult);

                                if (chkAutoSend.Checked && _bloombergService.IsConnected && !string.IsNullOrEmpty(parseResult.OVML))
                                {
                                    _bloombergService.SendOVML(parseResult.OVML);
                                }
                            }));
                        }
                    });
                }
            }
        }

        private void CtxDeleteRow_Click(object sender, EventArgs e)
        {
            if (dgvTradeBlotter.SelectedRows.Count > 0)
            {
                var row = dgvTradeBlotter.SelectedRows[0];
                string tradeId = row.Cells["TradeId"].Value?.ToString();

                var result = MessageBox.Show(
                    "Delete this trade permanently?",
                    "Confirm Delete",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (result == DialogResult.Yes)
                {
                    _allTrades.RemoveAll(t => t.Id == tradeId);
                    dgvTradeBlotter.Rows.Remove(row);
                    SaveTrades();
                    LogDebugMessage($"Deleted trade: {tradeId}");
                }
            }
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
                e.SuppressKeyPress = true;
                await ProcessTrade();
            }
        }

        private async void TxtTradeInput_TextChanged(object sender, EventArgs e)
        {
            string input = txtTradeInput.Text.Trim();

            if (input.Length > 10 && input.Contains(" "))
            {
                await Task.Delay(100);

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
            var trade = new TradeRecord
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = DateTime.Now,
                Request = request,
                OVML = result.OVML,
                UBS = result.UBS ?? "",
                Underlying = result.Underlying,
                LegCount = result.LegCount.ToString(),
                Expiry = result.Expiry,
                SpotRef = ExtractSpotFromOVML(result.OVML),
                ParseMethod = result.ParseMethod,
                ValidationWarning = result.ValidationWarning
            };

            _allTrades.Add(trade);
            SaveTrades();

            // Only add to grid if it matches current filter
            if (_currentFilter == TradeFilter.All || trade.Timestamp.Date == DateTime.Today)
            {
                AddTradeRowToGrid(trade, insertAtTop: true);
            }
        }

        private void AddTradeRowToGrid(TradeRecord trade, bool insertAtTop = false)
        {
            int rowIndex = insertAtTop ? 0 : dgvTradeBlotter.Rows.Count;

            if (insertAtTop)
            {
                dgvTradeBlotter.Rows.Insert(0, new object[]
                {
                    trade.Timestamp.ToString("HH:mm:ss"),
                    trade.Request,
                    trade.OVML,
                    trade.Underlying,
                    trade.LegCount,
                    trade.Expiry,
                    trade.SpotRef,
                    trade.ParseMethod,
                    null,
                    null,
                    trade.UBS,
                    trade.Id
                });
            }
            else
            {
                dgvTradeBlotter.Rows.Add(new object[]
                {
                    trade.Timestamp.ToString("HH:mm:ss"),
                    trade.Request,
                    trade.OVML,
                    trade.Underlying,
                    trade.LegCount,
                    trade.Expiry,
                    trade.SpotRef,
                    trade.ParseMethod,
                    null,
                    null,
                    trade.UBS,
                    trade.Id
                });
            }

            var row = dgvTradeBlotter.Rows[rowIndex];

            // Color coding
            if (trade.ParseMethod.StartsWith("Regex"))
            {
                row.DefaultCellStyle.BackColor = Color.LightGreen;
            }
            else if (trade.ParseMethod.StartsWith("Learned"))
            {
                row.DefaultCellStyle.BackColor = Color.LightYellow;
            }
            else if (trade.ParseMethod.Contains("AI-Success") && trade.ParseMethod.Contains("Validated"))
            {
                row.DefaultCellStyle.BackColor = Color.LightBlue;
            }
            else if (trade.ParseMethod.Contains("AI-Warning"))
            {
                row.DefaultCellStyle.BackColor = Color.Orange;
                if (!string.IsNullOrEmpty(trade.ValidationWarning))
                {
                    row.Cells["Method"].ToolTipText = trade.ValidationWarning;
                }
            }
            else if (trade.ParseMethod.StartsWith("AI"))
            {
                row.DefaultCellStyle.BackColor = Color.LightBlue;
            }
            else if (trade.ParseMethod.Contains("Error"))
            {
                row.DefaultCellStyle.BackColor = Color.LightCoral;
            }

            if (insertAtTop)
            {
                dgvTradeBlotter.ClearSelection();
                row.Selected = true;
                dgvTradeBlotter.CurrentCell = row.Cells["Request"];
                dgvTradeBlotter.FirstDisplayedScrollingRowIndex = 0;
            }
        }

        private string ExtractSpotFromOVML(string ovml)
        {
            if (string.IsNullOrEmpty(ovml)) return "";

            var match = Regex.Match(ovml, @"SP(\d+(?:[.,]\d+)?)");
            return match.Success ? match.Groups[1].Value : "";
        }

        private void BtnToggleDebug_Click(object sender, EventArgs e)
        {
            debugVisible = !debugVisible;
            pnlDebug.Visible = debugVisible;
            btnToggleDebug.Text = debugVisible ? "Hide" : "Debug";
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

        private void BtnCopyOVML_Click(object sender, EventArgs e)
        {
            if (dgvTradeBlotter.SelectedRows.Count > 0)
            {
                var ovml = dgvTradeBlotter.SelectedRows[0].Cells["OVML"].Value?.ToString();
                if (!string.IsNullOrEmpty(ovml))
                {
                    Clipboard.SetText(ovml);
                    LogDebugMessage("[UI] OVML copied to clipboard");
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
                var row = dgvTradeBlotter.Rows[e.RowIndex];
                string request = row.Cells["Request"].Value?.ToString();
                string method = row.Cells["Method"].Value?.ToString();
                string tradeId = row.Cells["TradeId"].Value?.ToString();

                if (column.Name == "ReParseAI")
                {
                    var result = MessageBox.Show(
                        "Re-parse this trade using AI only?\n\n" +
                        "Current result will be replaced.",
                        "Force AI Re-parse",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                    if (result == DialogResult.Yes)
                    {
                        _allTrades.RemoveAll(t => t.Id == tradeId);
                        dgvTradeBlotter.Rows.RemoveAt(e.RowIndex);
                        LogDebugMessage($"Force re-parsing with AI: {request}");

                        _ = Task.Run(async () =>
                        {
                            var parseResult = await _tradeParser.ParseTradeAsync(request, forceAI: true);

                            if (parseResult != null)
                            {
                                this.Invoke(new Action(() =>
                                {
                                    AddTradeToBlotter(request, parseResult);

                                    if (chkAutoSend.Checked && _bloombergService.IsConnected && !string.IsNullOrEmpty(parseResult.OVML))
                                    {
                                        _bloombergService.SendOVML(parseResult.OVML);
                                    }
                                }));
                            }
                        });
                    }
                }
                else if (column.Name == "Reject")
                {
                    string ovml = row.Cells["OVML"].Value?.ToString();

                    if (method?.StartsWith("Learned-") == true)
                    {
                        var match = Regex.Match(method, @"Learned-Pattern-(\d{8}-\d{6})");
                        if (match.Success)
                        {
                            string patternTimestamp = match.Groups[1].Value;

                            var choice = MessageBox.Show(
                                $"This trade used learned pattern '{patternTimestamp}'.\n\n" +
                                "YES - Delete the entire pattern permanently\n" +
                                "NO - Re-parse this trade using AI only (keeps pattern)\n" +
                                "CANCEL - Keep as is",
                                "Pattern Action",
                                MessageBoxButtons.YesNoCancel,
                                MessageBoxIcon.Question
                            );

                            if (choice == DialogResult.Yes)
                            {
                                bool success = _tradeParser.RemoveLearnedPattern(patternTimestamp);

                                if (success)
                                {
                                    _allTrades.RemoveAll(t => t.Id == tradeId);
                                    dgvTradeBlotter.Rows.RemoveAt(e.RowIndex);
                                    SaveTrades();
                                    LogDebugMessage($"Deleted learned pattern: {patternTimestamp}");
                                    MessageBox.Show($"Pattern '{patternTimestamp}' deleted.\nSimilar trades will use AI.",
                                        "Pattern Deleted", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                }
                                else
                                {
                                    MessageBox.Show("Failed to delete pattern.",
                                        "Deletion Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                }
                            }
                            else if (choice == DialogResult.No)
                            {
                                _allTrades.RemoveAll(t => t.Id == tradeId);
                                dgvTradeBlotter.Rows.RemoveAt(e.RowIndex);
                                LogDebugMessage($"Re-parsing trade with AI, bypassing pattern {patternTimestamp}");

                                _ = Task.Run(async () =>
                                {
                                    var parseResult = await _tradeParser.ParseTradeAsync(request, forceAI: true);

                                    if (parseResult != null)
                                    {
                                        this.Invoke(new Action(() =>
                                        {
                                            AddTradeToBlotter(request, parseResult);
                                        }));
                                    }
                                });

                                MessageBox.Show("Trade will be re-parsed using AI only.",
                                    "Re-parsing", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                        }
                    }
                    else
                    {
                        var message = "Reject this trade?\n\n";
                        if (method?.Contains("AI-Warning") == true)
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

                        var result = MessageBox.Show(message, "Confirm Rejection",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                        if (result == DialogResult.Yes)
                        {
                            _allTrades.RemoveAll(t => t.Id == tradeId);
                            dgvTradeBlotter.Rows.RemoveAt(e.RowIndex);
                            SaveTrades();
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

    // Supporting classes
    public class TradeRecord
    {
        public string Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Request { get; set; }
        public string OVML { get; set; }
        public string UBS { get; set; }
        public string Underlying { get; set; }
        public string LegCount { get; set; }
        public string Expiry { get; set; }
        public string SpotRef { get; set; }
        public string ParseMethod { get; set; }
        public string ValidationWarning { get; set; }
    }

    public enum TradeFilter
    {
        Today,
        All
    }
}