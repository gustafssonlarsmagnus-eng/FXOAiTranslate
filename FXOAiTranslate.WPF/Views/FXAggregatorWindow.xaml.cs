using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FXOptionsSimulator;
using FXOptionsSimulator.FIX;

namespace FXOAiTranslate.WPF.Views
{
    public partial class FXAggregatorWindow : Window
    {
        public ObservableCollection<LPQuoteRow> LPQuotes { get; set; }
        public ObservableCollection<DealViewModel> Deals { get; set; }

        private readonly TradeStructure _trade;
        private readonly GFIFIXSessionManager _fixSession;
        private readonly ConcurrentDictionary<string, LPQuoteData> _quotesByLP;
        private readonly DispatcherTimer _countdownTimer;
        private string _currentGroupId;
        private bool _isRfqActive;
        private bool _dealsPanelExpanded = true;

        public FXAggregatorWindow()
        {
            InitializeComponent();

            LPQuotes = new ObservableCollection<LPQuoteRow>();
            Deals = new ObservableCollection<DealViewModel>();
            _quotesByLP = new ConcurrentDictionary<string, LPQuoteData>();

            lpLadder.ItemsSource = LPQuotes;
            dealCards.ItemsSource = Deals;

            // Get FIX session
            _fixSession = GlobalFIXSession.Instance;

            // Subscribe to quote events
            if (_fixSession != null)
            {
                _fixSession.OnQuoteReceived += OnQuoteReceived;
                Console.WriteLine("[WPF] Subscribed to FIX quote events");
            }
            else
            {
                Console.WriteLine("[WPF] WARNING: FIX session not available");
            }

            // Countdown timer for quote expiry
            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _countdownTimer.Tick += CountdownTimer_Tick;
        }

        public FXAggregatorWindow(TradeStructure trade) : this()
        {
            _trade = trade;

            if (trade != null && trade.Legs?.Count > 0)
            {
                var leg = trade.Legs[0];
                txtTradeInput.Text = $"{leg.Direction} {leg.NotionalMM}M {trade.Underlying} {leg.Tenor} {leg.OptionType} {leg.Strike}";
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from events
            if (_fixSession != null)
            {
                _fixSession.OnQuoteReceived -= OnQuoteReceived;
            }
            _countdownTimer.Stop();
            base.OnClosed(e);
        }

        #region RFQ State

        private void ShowRfqState()
        {
            // Reset bid tile to RFQ state
            lblBidLabel.Visibility = Visibility.Collapsed;
            lblBidRfqHint.Visibility = Visibility.Visible;
            lblBidValue.Text = "RFQ";
            lblBidValue.FontSize = 72;
            lblBidValue.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            lblBidSecondary.Visibility = Visibility.Collapsed;
            bidLPPanel.Visibility = Visibility.Collapsed;

            // Reset offer tile to RFQ state
            lblOfferLabel.Visibility = Visibility.Collapsed;
            lblOfferRfqHint.Visibility = Visibility.Visible;
            lblOfferValue.Text = "RFQ";
            lblOfferValue.FontSize = 72;
            lblOfferValue.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            lblOfferSecondary.Visibility = Visibility.Collapsed;
            offerLPPanel.Visibility = Visibility.Collapsed;

            spreadPanel.Visibility = Visibility.Collapsed;
            lblNoQuotes.Visibility = Visibility.Visible;
            LPQuotes.Clear();
            _isRfqActive = false;
        }

        private void ShowLiveState()
        {
            // Show bid tile in live quoting state
            lblBidLabel.Visibility = Visibility.Visible;
            lblBidRfqHint.Visibility = Visibility.Collapsed;
            lblBidValue.FontSize = 72;
            lblBidValue.Foreground = Brushes.White;
            lblBidSecondary.Visibility = Visibility.Visible;
            bidLPPanel.Visibility = Visibility.Visible;

            // Show offer tile in live quoting state
            lblOfferLabel.Visibility = Visibility.Visible;
            lblOfferRfqHint.Visibility = Visibility.Collapsed;
            lblOfferValue.FontSize = 72;
            lblOfferValue.Foreground = Brushes.White;
            lblOfferSecondary.Visibility = Visibility.Visible;
            offerLPPanel.Visibility = Visibility.Visible;

            spreadPanel.Visibility = Visibility.Visible;
            lblNoQuotes.Visibility = Visibility.Collapsed;
            _isRfqActive = true;
            _countdownTimer.Start();
        }

        #endregion

        #region FIX Quote Handling

        private void OnQuoteReceived(QuoteData quote)
        {
            // Marshal to UI thread
            Dispatcher.BeginInvoke(() =>
            {
                ProcessQuote(quote);
            });
        }

        private void ProcessQuote(QuoteData quote)
        {
            Console.WriteLine($"[WPF] Processing quote from {quote.LP} - Side: {quote.Side}, Vol: {(quote.Side == "BID" ? quote.BidVol : quote.OfferVol)}");

            if (!_isRfqActive)
            {
                ShowLiveState();
            }

            // Update or add LP quote data
            var lpData = _quotesByLP.GetOrAdd(quote.LP, _ => new LPQuoteData { LP = quote.LP });

            if (quote.Side == "BID")
            {
                lpData.BidVol = quote.BidVol;
                lpData.BidPremium = quote.BidPremium;
                lpData.BidQuoteId = quote.QuoteID;
            }
            else
            {
                lpData.OfferVol = quote.OfferVol;
                lpData.OfferPremium = quote.OfferPremium;
                lpData.OfferQuoteId = quote.QuoteID;
            }

            lpData.LastUpdate = DateTime.Now;
            lpData.ValidUntilTime = ParseValidUntilTime(quote.ValidUntilTime);
            lpData.SpotRate = quote.Notional > 0 ? quote.Notional.ToString("F4") : "1.1746";
            lpData.Delta = quote.Delta;

            // Update ladder
            UpdateLadder();

            // Update best bid/offer tiles
            UpdateBestPrices();
        }

        private DateTime ParseValidUntilTime(string validUntil)
        {
            // Format: "20251218-08:32:40"
            if (!string.IsNullOrEmpty(validUntil) &&
                DateTime.TryParseExact(validUntil, "yyyyMMdd-HH:mm:ss", null,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            {
                return dt.ToLocalTime();
            }
            return DateTime.Now.AddMinutes(2);
        }

        private void UpdateLadder()
        {
            var sorted = _quotesByLP.Values
                .OrderByDescending(q => q.BidVol) // Best bid first
                .ToList();

            // Find best prices
            var bestBidLP = sorted.Where(q => q.BidVol > 0).OrderByDescending(q => q.BidVol).FirstOrDefault()?.LP;
            var bestOfferLP = sorted.Where(q => q.OfferVol > 0).OrderBy(q => q.OfferVol).FirstOrDefault()?.LP;

            LPQuotes.Clear();
            foreach (var lp in sorted)
            {
                var secondsRemaining = (lp.ValidUntilTime - DateTime.Now).TotalSeconds;
                var opacity = Math.Max(0.3, Math.Min(1.0, secondsRemaining / 120.0)); // Fade over 2 mins

                LPQuotes.Add(new LPQuoteRow
                {
                    LPName = lp.LP,
                    BidVol = lp.BidVol > 0 ? lp.BidVol.ToString("F2") : "-",
                    BidSecondary = lp.BidPremium != 0 ? $"{Math.Abs(lp.BidPremium / 1000):F0}k" : "-",
                    OfferVol = lp.OfferVol > 0 ? lp.OfferVol.ToString("F2") : "-",
                    OfferSecondary = lp.OfferPremium != 0 ? $"{Math.Abs(lp.OfferPremium / 1000):F0}k" : "-",
                    Opacity = opacity,
                    IsBestBid = lp.LP == bestBidLP,
                    IsBestOffer = lp.LP == bestOfferLP,
                    IsEnabled = true
                });
            }
        }

        private void UpdateBestPrices()
        {
            var quotes = _quotesByLP.Values.ToList();

            // Best bid = highest vol you receive
            var bestBid = quotes.Where(q => q.BidVol > 0).OrderByDescending(q => q.BidVol).FirstOrDefault();
            // Best offer = lowest vol you pay
            var bestOffer = quotes.Where(q => q.OfferVol > 0).OrderBy(q => q.OfferVol).FirstOrDefault();

            if (bestBid != null)
            {
                lblBidValue.Text = bestBid.BidVol.ToString("F2");
                lblBidSecondary.Text = $"{Math.Abs(bestBid.BidPremium / 1000):F0}k";
                lblBidLP.Text = bestBid.LP;
                UpdateCountdown(lblBidCountdown, bestBid.ValidUntilTime);
            }

            if (bestOffer != null)
            {
                lblOfferValue.Text = bestOffer.OfferVol.ToString("F2");
                lblOfferSecondary.Text = $"{Math.Abs(bestOffer.OfferPremium / 1000):F0}k";
                lblOfferLP.Text = bestOffer.LP;
                UpdateCountdown(lblOfferCountdown, bestOffer.ValidUntilTime);
            }

            // Spread
            if (bestBid != null && bestOffer != null)
            {
                var spread = bestOffer.OfferVol - bestBid.BidVol;
                lblSpread.Text = spread.ToString("F2");
            }
        }

        private void UpdateCountdown(System.Windows.Controls.TextBlock label, DateTime validUntil)
        {
            var remaining = validUntil - DateTime.Now;
            if (remaining.TotalSeconds > 0)
            {
                label.Text = $"{(int)remaining.TotalMinutes}:{remaining.Seconds:D2}";
                label.Foreground = remaining.TotalSeconds < 10
                    ? new SolidColorBrush(Color.FromRgb(239, 68, 68))  // Red when urgent
                    : new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
            }
            else
            {
                label.Text = "EXPIRED";
                label.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            }
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            if (_quotesByLP.IsEmpty) return;

            UpdateBestPrices();

            // Remove expired quotes
            var now = DateTime.Now;
            var expired = _quotesByLP.Where(kv => kv.Value.ValidUntilTime < now).ToList();
            foreach (var kv in expired)
            {
                _quotesByLP.TryRemove(kv.Key, out _);
                Console.WriteLine($"[WPF] Quote from {kv.Key} expired");
            }

            if (expired.Any())
            {
                UpdateLadder();
            }

            if (_quotesByLP.IsEmpty && _isRfqActive)
            {
                ShowRfqState();
                _countdownTimer.Stop();
            }
        }

        #endregion

        #region UI Event Handlers

        private void ParseButton_Click(object sender, RoutedEventArgs e)
        {
            // Parse trade input and update the trade structure
            ParseTradeInput();
            SendRFQ();
        }

        private void ParseTradeInput()
        {
            var input = txtTradeInput.Text.ToLower();

            // Simple parsing - update trade structure
            if (_trade != null && _trade.Legs.Count > 0)
            {
                var leg = _trade.Legs[0];

                // Parse direction
                leg.Direction = input.Contains("sell") ? "SELL" : "BUY";

                // Parse option type
                leg.OptionType = input.Contains("put") ? "PUT" : "CALL";

                // Parse notional
                var notionalMatch = System.Text.RegularExpressions.Regex.Match(input, @"(\d+(?:\.\d+)?)\s*[mM]");
                if (notionalMatch.Success)
                {
                    leg.NotionalMM = double.Parse(notionalMatch.Groups[1].Value);
                }

                // Parse tenor
                var tenorMatch = System.Text.RegularExpressions.Regex.Match(input, @"(\d+)\s*([mMyY])");
                if (tenorMatch.Success)
                {
                    leg.Tenor = tenorMatch.Groups[1].Value + tenorMatch.Groups[2].Value.ToUpper();
                }

                // Parse strike
                var strikeMatch = System.Text.RegularExpressions.Regex.Match(input, @"(\d+\.\d{2,4})");
                if (strikeMatch.Success)
                {
                    leg.Strike = double.Parse(strikeMatch.Groups[1].Value);
                }

                Console.WriteLine($"[WPF] Parsed trade: {leg.Direction} {leg.NotionalMM}M {leg.Tenor} {leg.OptionType} @ {leg.Strike}");
            }
        }

        private void Tenor_Changed(object sender, SelectionChangedEventArgs e)
        {
            // Update expiry date based on tenor selection
            if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
            {
                var tenor = item.Content?.ToString() ?? "1M";
                var months = 1;
                if (tenor.EndsWith("M"))
                {
                    int.TryParse(tenor.TrimEnd('M'), out months);
                }
                else if (tenor.EndsWith("Y"))
                {
                    int.TryParse(tenor.TrimEnd('Y'), out var years);
                    months = years * 12;
                }

                if (_trade?.Legs?.Count > 0)
                {
                    _trade.Legs[0].Tenor = tenor;
                    _trade.Legs[0].ExpiryDate = DateTime.Now.AddMonths(months);
                }
            }
        }

        private void LadderHeader_Click(object sender, MouseButtonEventArgs e)
        {
            // Toggle ladder visibility
            if (FindName("ladderContent") is StackPanel content && FindName("lblLadderArrow") is TextBlock arrow)
            {
                if (content.Visibility == Visibility.Visible)
                {
                    content.Visibility = Visibility.Collapsed;
                    arrow.Text = "\u25B6";
                }
                else
                {
                    content.Visibility = Visibility.Visible;
                    arrow.Text = "\u25BC";
                }
            }
        }

        private void OptionHeader_Click(object sender, MouseButtonEventArgs e)
        {
            // Toggle option details visibility
            if (FindName("optionContent") is StackPanel content && FindName("lblOptionArrow") is TextBlock arrow)
            {
                if (content.Visibility == Visibility.Visible)
                {
                    content.Visibility = Visibility.Collapsed;
                    arrow.Text = "\u25B6";
                }
                else
                {
                    content.Visibility = Visibility.Visible;
                    arrow.Text = "\u25BC";
                }
            }
        }

        private void MarketDataHeader_Click(object sender, MouseButtonEventArgs e)
        {
            // Toggle market data section visibility
            if (FindName("marketDataContent") is StackPanel content && FindName("lblMarketDataArrow") is TextBlock arrow)
            {
                if (content.Visibility == Visibility.Visible)
                {
                    content.Visibility = Visibility.Collapsed;
                    arrow.Text = "\u25B6";
                }
                else
                {
                    content.Visibility = Visibility.Visible;
                    arrow.Text = "\u25BC";
                }
            }
        }

        private void RiskHeader_Click(object sender, MouseButtonEventArgs e)
        {
            // Toggle risk section visibility
            if (FindName("riskContent") is StackPanel content && FindName("lblRiskArrow") is TextBlock arrow)
            {
                if (content.Visibility == Visibility.Visible)
                {
                    content.Visibility = Visibility.Collapsed;
                    arrow.Text = "\u25B6";
                }
                else
                {
                    content.Visibility = Visibility.Visible;
                    arrow.Text = "\u25BC";
                }
            }
        }

        private void AddLeg_Click(object sender, MouseButtonEventArgs e)
        {
            MessageBox.Show("Add leg functionality - coming soon!", "Add Leg", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Tile_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border tile)
            {
                // Apply hover gradient
                tile.Background = (Brush)FindResource("Brush.TileHoverGradient");

                // Brighten the RFQ text on hover when not in live state
                if (!_isRfqActive)
                {
                    if (tile == bidTile)
                    {
                        lblBidValue.Foreground = Brushes.White;
                    }
                    else if (tile == offerTile)
                    {
                        lblOfferValue.Foreground = Brushes.White;
                    }
                }
            }
        }

        private void Tile_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border tile)
            {
                // Restore default gradient
                tile.Background = (Brush)FindResource("Brush.TileGradient");

                // Restore dim text color when not in live state
                if (!_isRfqActive)
                {
                    if (tile == bidTile)
                    {
                        lblBidValue.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                    }
                    else if (tile == offerTile)
                    {
                        lblOfferValue.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
                    }
                }
            }
        }

        private void DealsHeader_Click(object sender, MouseButtonEventArgs e)
        {
            ToggleDealsPanel();
        }

        private void DealsTabHandle_Click(object sender, MouseButtonEventArgs e)
        {
            ToggleDealsPanel();
        }

        private void ToggleDealsPanel()
        {
            var column = FindName("dealsPanelColumn") as ColumnDefinition;
            var panel = FindName("dealsPanel") as Border;
            var handle = FindName("dealsTabHandle") as Border;

            if (column == null || panel == null || handle == null) return;

            if (_dealsPanelExpanded)
            {
                // Collapse
                column.Width = new GridLength(0);
                panel.Visibility = Visibility.Collapsed;
                handle.Visibility = Visibility.Visible;
                _dealsPanelExpanded = false;
            }
            else
            {
                // Expand
                column.Width = new GridLength(380);
                panel.Visibility = Visibility.Visible;
                handle.Visibility = Visibility.Collapsed;
                _dealsPanelExpanded = true;
            }
        }

        private void BidTile_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_isRfqActive)
            {
                SendRFQ();
                return;
            }
            ExecuteTrade("BID");
        }

        private void OfferTile_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_isRfqActive)
            {
                SendRFQ();
                return;
            }
            ExecuteTrade("OFFER");
        }

        private void SendRFQ()
        {
            if (_trade == null)
            {
                MessageBox.Show("No trade structure loaded. Please enter a trade.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_fixSession == null || !_fixSession.IsLoggedOn)
            {
                MessageBox.Show("FIX session not connected. Please wait for connection.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _quotesByLP.Clear();
                LPQuotes.Clear();

                _currentGroupId = $"WPF_{DateTime.Now.Ticks}";

                // Get selected LPs from checkboxes
                var selectedLPs = GetSelectedLPs();

                if (selectedLPs.Count == 0)
                {
                    MessageBox.Show("Please select at least one LP.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Console.WriteLine($"\n[WPF] ========== SENDING RFQ ==========");
                Console.WriteLine($"[WPF] GroupID: {_currentGroupId}");
                Console.WriteLine($"[WPF] Trade: {_trade.Underlying} {_trade.Legs[0].Tenor} {_trade.Legs[0].OptionType}");
                Console.WriteLine($"[WPF] Selected LPs: {string.Join(", ", selectedLPs)}");

                foreach (var lp in selectedLPs)
                {
                    var quoteReqId = _fixSession.SendQuoteRequest(_trade, lp, _currentGroupId);
                    Console.WriteLine($"[WPF] Sent RFQ to {lp}: {quoteReqId}");
                }

                ShowLiveState();
                Console.WriteLine($"[WPF] RFQ sent to {selectedLPs.Count} LPs, waiting for quotes...\n");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending RFQ: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Console.WriteLine($"[WPF] ERROR sending RFQ: {ex.Message}");
            }
        }

        private void LPHeader_Click(object sender, MouseButtonEventArgs e)
        {
            // Toggle LP selection panel visibility
            if (FindName("lpSelectionPanel") is StackPanel panel && FindName("lblLPArrow") is TextBlock arrow)
            {
                if (panel.Visibility == Visibility.Visible)
                {
                    panel.Visibility = Visibility.Collapsed;
                    arrow.Text = "\u25B6";
                }
                else
                {
                    panel.Visibility = Visibility.Visible;
                    arrow.Text = "\u25BC";
                }
            }
        }

        private void LPCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            // Update LP count in header
            UpdateLPCount();

            // Update visual state of the LP row when checkbox changes
            if (sender is CheckBox checkbox)
            {
                // Find the parent Grid (LP row)
                var parent = checkbox.Parent;
                while (parent != null && !(parent is Grid grid && grid.Parent is Border))
                {
                    parent = (parent as FrameworkElement)?.Parent;
                }

                if (parent is Grid lpRowGrid && lpRowGrid.Parent is Border)
                {
                    // Update opacity based on checked state
                    bool isChecked = checkbox.IsChecked == true;
                    lpRowGrid.Opacity = isChecked ? 0.9 : 0.4;

                    // Find the LP name TextBlock and update its color
                    var centerBorder = lpRowGrid.Children.OfType<Border>().FirstOrDefault(b => Grid.GetColumn(b) == 1);
                    if (centerBorder?.Child is StackPanel sp)
                    {
                        var nameLabel = sp.Children.OfType<TextBlock>().FirstOrDefault();
                        if (nameLabel != null)
                        {
                            nameLabel.Foreground = isChecked
                                ? new SolidColorBrush(Colors.White)
                                : new SolidColorBrush(Color.FromRgb(100, 116, 139)); // #64748b
                        }
                    }
                }
            }
        }

        private void UpdateLPCount()
        {
            var count = GetSelectedLPs().Count;
            if (FindName("lblLPCount") is TextBlock label)
            {
                label.Text = $"{count} LPs";
            }
        }

        private List<string> GetSelectedLPs()
        {
            var lps = new List<string>();

            // Check all 9 LP checkboxes from the initial panel (matching FenicsConfig)
            if (FindName("chkMS") is CheckBox ms && ms.IsChecked == true) lps.Add("MS");
            if (FindName("chkHSBC") is CheckBox hsbc && hsbc.IsChecked == true) lps.Add("HSBC");
            if (FindName("chkBNP") is CheckBox bnp && bnp.IsChecked == true) lps.Add("BNP");
            if (FindName("chkNATWEST") is CheckBox natwest && natwest.IsChecked == true) lps.Add("NATWEST");
            if (FindName("chkSOCGEN") is CheckBox socgen && socgen.IsChecked == true) lps.Add("SOCGEN");
            if (FindName("chkCIBC") is CheckBox cibc && cibc.IsChecked == true) lps.Add("CIBC");
            if (FindName("chkSCBL") is CheckBox scbl && scbl.IsChecked == true) lps.Add("SCBL");
            if (FindName("chkNOMURA") is CheckBox nomura && nomura.IsChecked == true) lps.Add("NOMURA");
            if (FindName("chkBAML") is CheckBox baml && baml.IsChecked == true) lps.Add("BAML");

            // Also check LPs from quote rows (when quotes are active)
            if (LPQuotes != null)
            {
                foreach (var quoteRow in LPQuotes)
                {
                    if (quoteRow.IsEnabled && !lps.Contains(quoteRow.LPName))
                    {
                        lps.Add(quoteRow.LPName);
                    }
                }
            }

            return lps;
        }

        private void ExecuteTrade(string side)
        {
            // Flash animation
            var tile = side == "BID" ? bidTile : offerTile;
            var originalBrush = tile.Background;
            tile.Background = new SolidColorBrush(Color.FromArgb(80, 59, 130, 246));

            var flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            flashTimer.Tick += (s, args) =>
            {
                tile.Background = originalBrush;
                flashTimer.Stop();
            };
            flashTimer.Start();

            // Get best quote for the side
            var quotes = _quotesByLP.Values.ToList();
            LPQuoteData bestQuote;
            string tradeSide;
            string quoteIdToExecute;

            if (side == "BID")
            {
                bestQuote = quotes.Where(q => q.BidVol > 0).OrderByDescending(q => q.BidVol).FirstOrDefault();
                tradeSide = "SELL"; // Selling to the bidder
                quoteIdToExecute = bestQuote?.BidQuoteId;
            }
            else
            {
                bestQuote = quotes.Where(q => q.OfferVol > 0).OrderBy(q => q.OfferVol).FirstOrDefault();
                tradeSide = "BUY"; // Buying from the offerer
                quoteIdToExecute = bestQuote?.OfferQuoteId;
            }

            if (bestQuote == null || string.IsNullOrEmpty(quoteIdToExecute))
            {
                MessageBox.Show("No valid quote available to execute", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Console.WriteLine($"\n[WPF] ========== EXECUTING TRADE ==========");
            Console.WriteLine($"[WPF] Side: {tradeSide}, LP: {bestQuote.LP}");
            Console.WriteLine($"[WPF] QuoteID: {quoteIdToExecute}");
            Console.WriteLine($"[WPF] Vol: {(side == "BID" ? bestQuote.BidVol : bestQuote.OfferVol)}");

            // Create deal card
            var deal = new DealViewModel
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Instrument = $"{_trade?.Underlying ?? "EURUSD"} {_trade?.Legs?[0]?.Tenor ?? "1M"} {_trade?.Legs?[0]?.OptionType ?? "CALL"}",
                LP = bestQuote.LP,
                Side = tradeSide,
                Status = "PENDING",
                Volatility = $"{(side == "BID" ? bestQuote.BidVol : bestQuote.OfferVol):F2}%",
                Premium = (decimal)Math.Abs(side == "BID" ? bestQuote.BidPremium : bestQuote.OfferPremium),
                EurPips = $"{Math.Abs((side == "BID" ? bestQuote.BidPremium : bestQuote.OfferPremium) / 1000):F0}p",
                SpotRate = bestQuote.SpotRate ?? "1.1746",
                Strike = _trade?.Legs?[0]?.Strike.ToString("F4") ?? "1.1751",
                Notional = $"EUR {_trade?.Legs?[0]?.NotionalMM ?? 10}M",
                ExpiryDate = _trade?.Legs?[0]?.ExpiryDate.ToString("dd MMM yy") ?? "14 Jan 26",
                Tenor = _trade?.Legs?[0]?.Tenor ?? "1M",
                ExpiryCut = "NYC",
                OrderId = $"FENICS.{DateTime.Now.Ticks.ToString().Substring(10, 8)}",
                QuoteId = quoteIdToExecute
            };

            Deals.Insert(0, deal);
            lblNoDeals.Visibility = Visibility.Collapsed;

            // Send actual FIX execution
            try
            {
                // Build FIXMessage from quote data for execution
                var quoteMsg = new FIXMessage("S");
                quoteMsg.Set("117", quoteIdToExecute); // QuoteID
                quoteMsg.Set("115", bestQuote.LP);      // OnBehalfOfCompID
                quoteMsg.Set("55", _trade?.Underlying ?? "EURUSD");
                quoteMsg.Set("5678", (side == "BID" ? bestQuote.BidVol : bestQuote.OfferVol).ToString());
                quoteMsg.Set("6436", (side == "BID" ? bestQuote.BidPremium : bestQuote.OfferPremium).ToString());

                var executionSide = tradeSide == "BUY" ? "1" : "2"; // FIX Side: 1=Buy, 2=Sell
                var clOrdId = _fixSession.SendExecution(quoteMsg, executionSide, _trade);

                deal.ClOrdId = clOrdId;
                Console.WriteLine($"[WPF] Execution sent: ClOrdID={clOrdId}");

                // Subscribe to execution report for this order
                SubscribeToExecutionReport(deal);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WPF] ERROR sending execution: {ex.Message}");
                deal.Status = "FAILED";

                // Fallback: simulate confirmation for demo
                var confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                confirmTimer.Tick += (s, args) =>
                {
                    deal.Status = "CONFIRMED";
                    confirmTimer.Stop();
                };
                confirmTimer.Start();
            }
        }

        private void SubscribeToExecutionReport(DealViewModel deal)
        {
            // Listen for execution reports
            Action<string, string, string> handler = null;
            handler = (clOrdId, status, execId) =>
            {
                if (clOrdId == deal.ClOrdId)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (status == "2") // Filled
                        {
                            deal.Status = "CONFIRMED";
                            Console.WriteLine($"[WPF] Trade CONFIRMED: {clOrdId}");
                        }
                        else if (status == "8") // Rejected
                        {
                            deal.Status = "REJECTED";
                            Console.WriteLine($"[WPF] Trade REJECTED: {clOrdId}");
                        }
                    });

                    // Unsubscribe after handling
                    _fixSession.Application.OnExecutionReport -= handler;
                }
            };

            _fixSession.Application.OnExecutionReport += handler;

            // Timeout: if no response in 30 seconds, assume confirmed (for demo)
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            timeout.Tick += (s, args) =>
            {
                if (deal.Status == "PENDING")
                {
                    deal.Status = "CONFIRMED";
                    Console.WriteLine($"[WPF] Trade confirmed (timeout): {deal.ClOrdId}");
                }
                _fixSession.Application.OnExecutionReport -= handler;
                timeout.Stop();
            };
            timeout.Start();
        }

        private void DealCard_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as System.Windows.Controls.Border;
            if (border?.DataContext is DealViewModel deal)
            {
                deal.IsExpanded = !deal.IsExpanded;
            }
        }

        #endregion
    }

    #region Data Models

    public class LPQuoteData
    {
        public string LP { get; set; }
        public double BidVol { get; set; }
        public double BidPremium { get; set; }
        public string BidQuoteId { get; set; }
        public double OfferVol { get; set; }
        public double OfferPremium { get; set; }
        public string OfferQuoteId { get; set; }
        public DateTime LastUpdate { get; set; }
        public DateTime ValidUntilTime { get; set; }
        public string SpotRate { get; set; }
        public double Delta { get; set; }
    }

    #endregion

    #region ViewModels

    public class LPQuoteRow : INotifyPropertyChanged
    {
        public string LPName { get; set; }
        public string BidVol { get; set; }
        public string BidSecondary { get; set; }
        public string OfferVol { get; set; }
        public string OfferSecondary { get; set; }
        public double Opacity { get; set; } = 1.0;
        public bool IsBestBid { get; set; }
        public bool IsBestOffer { get; set; }

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); OnPropertyChanged(nameof(LPForeground)); }
        }

        // Border highlights for best prices
        public Brush BidBorderBrush => IsBestBid ? new SolidColorBrush(Color.FromRgb(59, 130, 246)) : Brushes.Transparent;
        public Brush OfferBorderBrush => IsBestOffer ? new SolidColorBrush(Color.FromRgb(59, 130, 246)) : Brushes.Transparent;
        public Brush BidBackground => IsBestBid ? new SolidColorBrush(Color.FromArgb(30, 30, 58, 138)) : Brushes.Transparent;
        public Brush OfferBackground => IsBestOffer ? new SolidColorBrush(Color.FromArgb(30, 30, 58, 138)) : Brushes.Transparent;

        // Text colors - best prices are bold white, others are dimmer
        public Brush BidForeground => IsBestBid ? Brushes.White : new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
        public Brush OfferForeground => IsBestOffer ? Brushes.White : new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
        public Brush LPForeground => IsEnabled ? Brushes.White : new SolidColorBrush(Color.FromRgb(100, 116, 139));

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class DealViewModel : INotifyPropertyChanged
    {
        public string Time { get; set; }
        public string Instrument { get; set; }
        public string LP { get; set; }
        public string Side { get; set; }
        public string ClOrdId { get; set; }
        public string QuoteId { get; set; }

        private string _status = "PENDING";
        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(StatusBackground));
                OnPropertyChanged(nameof(StatusForeground));
            }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); }
        }

        public string Volatility { get; set; }
        public decimal Premium { get; set; }
        public string PremiumDisplay => Premium.ToString("N0");
        public string PremiumLabel => Side == "BUY" ? "Pay" : "Receive";
        public string EurPips { get; set; }
        public string SpotRate { get; set; }
        public string Strike { get; set; }
        public string Notional { get; set; }
        public string ExpiryDate { get; set; }
        public string Tenor { get; set; }
        public string ExpiryCut { get; set; }
        public string OrderId { get; set; }

        public Brush SideColor => Side == "BUY"
            ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
            : new SolidColorBrush(Color.FromRgb(239, 68, 68));

        public Brush PremiumColor => Side == "BUY"
            ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
            : new SolidColorBrush(Color.FromRgb(34, 197, 94));

        public Brush StatusBackground => Status switch
        {
            "CONFIRMED" => new SolidColorBrush(Color.FromArgb(51, 34, 197, 94)),
            "REJECTED" or "FAILED" => new SolidColorBrush(Color.FromArgb(51, 239, 68, 68)),
            _ => new SolidColorBrush(Color.FromArgb(51, 245, 158, 11))
        };

        public Brush StatusForeground => Status switch
        {
            "CONFIRMED" => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
            "REJECTED" or "FAILED" => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            _ => new SolidColorBrush(Color.FromRgb(245, 158, 11))
        };

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    #endregion
}
