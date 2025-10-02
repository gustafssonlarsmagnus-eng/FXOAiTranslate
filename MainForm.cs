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
        private CheckBox chkAutoSend;
        private DataGridView dgvTradeBlotter;
        private MenuStrip menuStrip;
        private Panel pnlDebug;
        private TextBox txtDebugLog;
        private ContextMenuStrip ctxRowMenu;
        private bool debugVisible = false;

        // Progress indicator
        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatus;
        private ToolStripProgressBar progressBar;
        private ToolStripStatusLabel lblBloombergStatus;

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

            // Create menu bar
            menuStrip = new MenuStrip
            {
                BackColor = Color.White,
                Font = new Font("Segoe UI", 9F)
            };

            SetupMenuBar();

            // Request Blotter Label
            var lblBlotter = new Label
            {
                Text = "Request Blotter (Right-click for options | Paste anywhere with Ctrl+V to process)",
                Dock = DockStyle.Top,
                Height = 25,
                Padding = new Padding(15, 5, 0, 0),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                BackColor = Color.White
            };

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
            this.Controls.Add(menuStrip);
            this.MainMenuStrip = menuStrip;

            // Add status strip at bottom with all status info
            statusStrip = new StatusStrip
            {
                BackColor = Color.FromArgb(248, 249, 250),
                Font = new Font("Segoe UI", 9F),
                Padding = new Padding(5, 0, 5, 0),
                SizingGrip = false
            };

            lblStatus = new ToolStripStatusLabel
            {
                Text = "Ready",
                TextAlign = ContentAlignment.MiddleLeft,
                Width = 200
            };

            // Progress bar - align to right (rightmost)
            progressBar = new ToolStripProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                Visible = false,
                Size = new Size(100, 16),
                MarqueeAnimationSpeed = 30,
                Alignment = ToolStripItemAlignment.Right,
                Margin = new Padding(15, 0, 0, 0)  // Add left margin
            };

            // Auto-send checkbox - align to right
            chkAutoSend = new CheckBox
            {
                Text = "Auto-send",
                Checked = true,
                Font = new Font("Segoe UI", 9F),
                AutoSize = true,
                Margin = new Padding(20, 0, 25, 0)  // Add left and right margin
            };

            var chkAutoSendHost = new ToolStripControlHost(chkAutoSend)
            {
                Alignment = ToolStripItemAlignment.Right
            };

            // Bloomberg status label - align to right
            lblBloombergStatus = new ToolStripStatusLabel
            {
                Text = "Bloomberg: Disconnected",
                ForeColor = Color.Red,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Alignment = ToolStripItemAlignment.Right,
                AutoSize = true,
                Margin = new Padding(15, 0, 0, 0)  // Add left margin
            };

            statusStrip.Items.Add(lblStatus);
            statusStrip.Items.Add(progressBar);
            statusStrip.Items.Add(lblBloombergStatus);
            statusStrip.Items.Add(chkAutoSendHost);

            this.Controls.Add(statusStrip);

            this.SetTabOrder();
        }

        private void SetupMenuBar()
        {
            // View Menu
            var viewMenu = new ToolStripMenuItem("View");

            var todayMenuItem = new ToolStripMenuItem("Today's Requests");
            todayMenuItem.Click += (s, e) => { _currentFilter = TradeFilter.Today; ApplyFilter(); };

            var allMenuItem = new ToolStripMenuItem("All Requests");
            allMenuItem.Click += (s, e) => { _currentFilter = TradeFilter.All; ApplyFilter(); };

            viewMenu.DropDownItems.Add(todayMenuItem);
            viewMenu.DropDownItems.Add(allMenuItem);
            viewMenu.DropDownItems.Add(new ToolStripSeparator());

            var debugMenuItem = new ToolStripMenuItem("Debug Log");
            debugMenuItem.Click += (s, e) => ToggleDebug();
            viewMenu.DropDownItems.Add(debugMenuItem);

            // Tools Menu
            var toolsMenu = new ToolStripMenuItem("Tools");

            var copyOVMLMenuItem = new ToolStripMenuItem("Copy OVML");
            copyOVMLMenuItem.ShortcutKeys = Keys.Control | Keys.O;
            copyOVMLMenuItem.Click += (s, e) => CopySelectedCell("OVML");

            var copyUBSMenuItem = new ToolStripMenuItem("Copy UBS");
            copyUBSMenuItem.ShortcutKeys = Keys.Control | Keys.U;
            copyUBSMenuItem.Click += (s, e) => CopySelectedCell("UBS");

            toolsMenu.DropDownItems.Add(copyOVMLMenuItem);
            toolsMenu.DropDownItems.Add(copyUBSMenuItem);

            menuStrip.Items.Add(viewMenu);
            menuStrip.Items.Add(toolsMenu);
        }

        private void SetTabOrder()
        {
            chkAutoSend.TabIndex = 0;
            dgvTradeBlotter.TabIndex = 1;
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

        private void ToggleDebug()
        {
            debugVisible = !debugVisible;
            pnlDebug.Visible = debugVisible;
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

            // Setup global clipboard monitoring for paste-anywhere functionality
            SetupClipboardMonitoring();
        }

        private void SetupClipboardMonitoring()
        {
            // Hook into form's KeyDown event to capture Ctrl+V globally
            this.KeyPreview = true;
            this.KeyDown += async (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.V)
                {
                    if (Clipboard.ContainsText())
                    {
                        string clipboardText = Clipboard.GetText().Trim();
                        if (!string.IsNullOrEmpty(clipboardText) && clipboardText.Length > 10)
                        {
                            await ProcessTrade(clipboardText);
                        }
                    }
                }
            };
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
            dgvTradeBlotter.CellClick += DgvTradeBlotter_CellClick;
            dgvTradeBlotter.CellToolTipTextNeeded += DgvTradeBlotter_CellToolTipTextNeeded;
            this.FormClosing += MainForm_FormClosing;
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
                    SetProcessingStatus(true, "Re-parsing with AI...");

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

                                SetProcessingStatus(false, "Re-parse complete");
                                Task.Delay(2000).ContinueWith(_ => SetProcessingStatus(false, "Ready"));
                            }));
                        }
                        else
                        {
                            this.Invoke(new Action(() => SetProcessingStatus(false, "Re-parse failed")));
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

        private async Task ProcessTrade(string input)
        {
            if (_processing) return;
            _processing = true;

            // Show progress
            SetProcessingStatus(true, "Processing trade request...");

            try
            {
                if (string.IsNullOrEmpty(input)) return;

                var result = await _tradeParser.ParseTradeAsync(input);
                if (result != null)
                {
                    AddTradeToBlotter(input, result);

                    if (chkAutoSend.Checked && _bloombergService.IsConnected && !string.IsNullOrEmpty(result.OVML))
                    {
                        SetProcessingStatus(true, "Sending to Bloomberg...");
                        _bloombergService.SendOVML(result.OVML);
                    }

                    SetProcessingStatus(false, "Trade processed successfully");

                    // Reset status after 2 seconds
                    await Task.Delay(2000);
                    SetProcessingStatus(false, "Ready");
                }
                else
                {
                    SetProcessingStatus(false, "Failed to process trade");
                }
            }
            catch (Exception ex)
            {
                SetProcessingStatus(false, "Error occurred");
                MessageBox.Show($"Error processing trade: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _processing = false;
            }
        }

        private void SetProcessingStatus(bool isProcessing, string message)
        {
            if (statusStrip.InvokeRequired)
            {
                statusStrip.Invoke(new Action<bool, string>(SetProcessingStatus), isProcessing, message);
                return;
            }

            lblStatus.Text = message;
            progressBar.Visible = isProcessing;

            // Change cursor to indicate busy state
            this.Cursor = isProcessing ? Cursors.WaitCursor : Cursors.Default;
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
            if (statusStrip.InvokeRequired)
            {
                statusStrip.Invoke(new Action(UpdateBloombergStatus));
                return;
            }

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

            statusStrip.Refresh();
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
                        SetProcessingStatus(true, "Re-parsing with AI...");

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

                                    SetProcessingStatus(false, "Re-parse complete");
                                    Task.Delay(2000).ContinueWith(_ => SetProcessingStatus(false, "Ready"));
                                }));
                            }
                            else
                            {
                                this.Invoke(new Action(() => SetProcessingStatus(false, "Re-parse failed")));
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
            dgvTradeBlotter.Focus();
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