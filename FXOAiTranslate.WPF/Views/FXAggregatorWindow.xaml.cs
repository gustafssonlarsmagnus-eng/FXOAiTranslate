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
using FXOAiTranslator;

namespace FXOAiTranslate.WPF.Views
{
    public partial class FXAggregatorWindow : Window
    {
        public ObservableCollection<LPQuoteRow> LPQuotes { get; set; }
        public ObservableCollection<DealViewModel> Deals { get; set; }

        private readonly TradeStructure _trade;
        private readonly GFIFIXSessionManager _fixSession;
        private readonly BloombergService _bloombergService;
        private readonly ConcurrentDictionary<string, LPQuoteData> _quotesByLP;
        private readonly ConcurrentDictionary<string, FIXMessage> _quotesByQuoteId; // Store FIXMessages for execution
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
            _quotesByQuoteId = new ConcurrentDictionary<string, FIXMessage>();

            lpLadder.ItemsSource = LPQuotes;
            dealCards.ItemsSource = Deals;

            // Initialize Bloomberg service for real market data
            _bloombergService = new BloombergService();
            Console.WriteLine($"[WPF] Bloomberg service initialized - Connected: {_bloombergService.IsConnected}");

            // Get FIX session
            _fixSession = GlobalFIXSession.Instance;

            // Subscribe to quote events
            if (_fixSession != null)
            {
                // Subscribe to FIXMessage version for execution
                _fixSession.Application.OnQuoteReceived += OnQuoteReceivedWithFIXMessage;

                // Subscribe to QuoteData version for UI updates
                _fixSession.OnQuoteReceived += OnQuoteReceived;
                _fixSession.OnQuoteRequestRejected += OnQuoteRequestRejected;
                Console.WriteLine("[WPF] Subscribed to FIX quote events (FIXMessage + QuoteData)");
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
                // Format trade text to match RFQ color and style: "E.g., buy 10mio EURUSD 1m call 1.1750"
                txtTradeInput.Text = $"E.g., {leg.Direction?.ToLower()} {leg.NotionalMM}mio {trade.Underlying?.ToUpper()} {leg.Tenor?.ToLower()} {leg.OptionType?.ToLower()} {leg.Strike}";
                txtTradeInput.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748b"));
            }
            else
            {
                // Show placeholder if no trade provided (matches RFQ inactive color)
                txtTradeInput.Text = txtTradeInput.Tag?.ToString() ?? "E.g., buy 10mio EURUSD 1m call 1.18";
                txtTradeInput.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748b"));
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from events
            if (_fixSession != null)
            {
                _fixSession.Application.OnQuoteReceived -= OnQuoteReceivedWithFIXMessage;
                _fixSession.OnQuoteReceived -= OnQuoteReceived;
                _fixSession.OnQuoteRequestRejected -= OnQuoteRequestRejected;
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
            
            // Hide STOP RFQ button
         if (FindName("btnStopRFQ") is Button stopButton)
     {
         stopButton.Visibility = Visibility.Collapsed;
         }

            lblNoQuotes.Visibility = Visibility.Visible;
        LPQuotes.Clear();
            
    // Show initial LP checkbox panel, hide dynamic ladder
   if (FindName("initialLPPanel") is StackPanel initialPanel)
        {
         initialPanel.Visibility = Visibility.Visible;
    }
   
            _isRfqActive = false;
        }

        private void ShowLiveState()
        {
            // Show bid tile in live quoting state (waiting for quotes)
            lblBidLabel.Visibility = Visibility.Visible;
            lblBidRfqHint.Visibility = Visibility.Collapsed;
            lblBidValue.Text = "---"; // Clear to blank/waiting state
            lblBidValue.FontSize = 72;
            lblBidValue.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)); // Dim grey while waiting
            lblBidSecondary.Text = "Requesting...";
            lblBidSecondary.Visibility = Visibility.Visible;
            bidLPPanel.Visibility = Visibility.Collapsed; // Hide LP badge until quote received

            // Show offer tile in live quoting state (waiting for quotes)
            lblOfferLabel.Visibility = Visibility.Visible;
            lblOfferRfqHint.Visibility = Visibility.Collapsed;
            lblOfferValue.Text = "---"; // Clear to blank/waiting state
            lblOfferValue.FontSize = 72;
            lblOfferValue.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)); // Dim grey while waiting
            lblOfferSecondary.Text = "Requesting...";
            lblOfferSecondary.Visibility = Visibility.Visible;
    offerLPPanel.Visibility = Visibility.Collapsed; // Hide LP badge until quote received

    // Don't show spread panel until we have actual quotes
    // It will be shown in UpdateBestPrices when both bid and offer exist
        spreadPanel.Visibility = Visibility.Collapsed;
  lblSpread.Text = "---";

   // Show STOP RFQ button in control bar
     if (FindName("btnStopRFQ") is Button stopButton)
      {
       stopButton.Visibility = Visibility.Visible;
  }

            lblNoQuotes.Visibility = Visibility.Collapsed;

   // Hide initial LP checkbox panel, show dynamic ladder with quotes
         if (FindName("initialLPPanel") is StackPanel initialPanel)
   {
     initialPanel.Visibility = Visibility.Collapsed;
 }

            _isRfqActive = true;

            // Reset countdown timer for new RFQ (stop first, then start to reset)
            _countdownTimer.Stop();
            _countdownTimer.Start();
    }

        #endregion

        #region FIX Quote Handling

        /// <summary>
        /// Handle quote received with FIXMessage (needed for execution)
        /// </summary>
        private void OnQuoteReceivedWithFIXMessage(string quoteReqID, FIXMessage fixMsg)
        {
            try
            {
                string quoteID = fixMsg.Get(QuickFix.Fields.Tags.QuoteID.ToString());

                // Store FIXMessage by QuoteID for later execution
                _quotesByQuoteId[quoteID] = fixMsg;

                Console.WriteLine($"[WPF] Stored FIXMessage for execution: QuoteID={quoteID}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WPF] Error storing FIXMessage: {ex.Message}");
            }
        }

        private void OnQuoteReceived(QuoteData quote)
        {
            // Marshal to UI thread
            Dispatcher.BeginInvoke(async () =>
            {
                await ProcessQuoteAsync(quote);
            });
        }

        private void OnQuoteRequestRejected(string quoteReqID, int rejectReason, string rejectText)
        {
            // Marshal to UI thread
            Dispatcher.BeginInvoke(() =>
            {
                Console.WriteLine($"[WPF] Quote request rejected for {quoteReqID}: {rejectText} (Reason: {rejectReason})");

                // DON'T reset to RFQ state - other LPs may still quote successfully
                // Just log the rejection and continue waiting for other quotes

                // Optional: Show a non-blocking notification instead of modal dialog
                // (You could use a toast notification or status bar message here)

                // Only show MessageBox if ALL LPs have responded (none pending)
                // For now, just log it - user will see successful quotes from other LPs
            });
        }

        private async Task ProcessQuoteAsync(QuoteData quote)
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

            // Fetch real-time Bloomberg spot instead of using hardcoded fallback
            string pair = _trade?.Underlying ?? "EURUSD";
            double bloombergSpot = await GetBloombergSpotAsync(pair);
            lpData.SpotRate = bloombergSpot.ToString("F4");
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
            // Don't clear and re-add rows - just update existing LP rows in place
        // This preserves the original LP order from the XAML (MS, HSBC, BNP, NATWEST, etc.)
      
            // Find best prices for highlighting
            var allQuotes = _quotesByLP.Values.ToList();
       var bestBidLP = allQuotes.Where(q => q.BidVol > 0).OrderByDescending(q => q.BidVol).FirstOrDefault()?.LP;
    var bestOfferLP = allQuotes.Where(q => q.OfferVol > 0).OrderBy(q => q.OfferVol).FirstOrDefault()?.LP;

      // Update each LP row with its quote data (if available)
     foreach (var lpName in new[] { "MS", "HSBC", "BNP", "NATWEST", "SOCGEN", "CIBC", "SCBL", "NOMURA", "BAML" })
     {
    if (_quotesByLP.TryGetValue(lpName, out var lpData))
   {
     // LP has quoted - add/update in the ladder
     var existingRow = LPQuotes.FirstOrDefault(r => r.LPName == lpName);
       
 var secondsRemaining = (lpData.ValidUntilTime - DateTime.Now).TotalSeconds;
      var opacity = Math.Max(0.3, Math.Min(1.0, secondsRemaining / 120.0)); // Fade over 2 mins

              var bidVol = lpData.BidVol > 0 ? lpData.BidVol.ToString("F2") : "-";
           var offerVol = lpData.OfferVol > 0 ? lpData.OfferVol.ToString("F2") : "-";

      if (existingRow != null)
          {
        // Update existing row
    existingRow.BidVol = bidVol;
          existingRow.OfferVol = offerVol;
  existingRow.BidSecondary = lpData.BidPremium != 0 ? $"{Math.Abs(lpData.BidPremium / 1000):F0}k" : "-";
           existingRow.OfferSecondary = lpData.OfferPremium != 0 ? $"{Math.Abs(lpData.OfferPremium / 1000):F0}k" : "-";
existingRow.Opacity = opacity;
          existingRow.IsBestBid = lpName == bestBidLP;
      existingRow.IsBestOffer = lpName == bestOfferLP;
                  }
         else
  {
          // Add new row (in order)
         var row = new LPQuoteRow(lpName, bidVol, offerVol)
     {
       BidSecondary = lpData.BidPremium != 0 ? $"{Math.Abs(lpData.BidPremium / 1000):F0}k" : "-",
        OfferSecondary = lpData.OfferPremium != 0 ? $"{Math.Abs(lpData.OfferPremium / 1000):F0}k" : "-",
Opacity = opacity,
       IsBestBid = lpName == bestBidLP,
               IsBestOffer = lpName == bestOfferLP,
        IsEnabled = true
                 };
     
             // Insert at correct position to maintain order
     int insertIndex = GetInsertIndexForLP(lpName);
     if (insertIndex >= 0 && insertIndex <= LPQuotes.Count)
        {
       LPQuotes.Insert(insertIndex, row);
              }
          else
              {
           LPQuotes.Add(row);
                }
          }
       }
   }
        }

        private int GetInsertIndexForLP(string lpName)
      {
            // Define the standard LP order (matching XAML checkbox order)
            var lpOrder = new[] { "MS", "HSBC", "BNP", "NATWEST", "SOCGEN", "CIBC", "SCBL", "NOMURA", "BAML" };
          int targetIndex = Array.IndexOf(lpOrder, lpName);
     
    if (targetIndex < 0) return LPQuotes.Count; // Unknown LP, add at end
         
   // Find the first LP in the list that should come after this one
            for (int i = 0; i < LPQuotes.Count; i++)
         {
    int existingIndex = Array.IndexOf(lpOrder, LPQuotes[i].LPName);
     if (existingIndex > targetIndex)
         {
     return i;
       }
            }
     
         return LPQuotes.Count; // Add at end
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
     // Display vol as main value (large white text)
    lblBidValue.Text = bestBid.BidVol.ToString("F2");
     lblBidValue.Foreground = Brushes.White;
        
   // Calculate pips: premium_usd / (notional_usd * 10000)
            double notionalUSD = _trade?.Legs?[0]?.NotionalMM ?? 10.0; // Default 10M
     double premiumPips = Math.Abs(bestBid.BidPremium) / (notionalUSD * 1_000_000) * 10000;
          
         // Format secondary text: "68,778 USD  43p"
  lblBidSecondary.Text = $"{Math.Abs(bestBid.BidPremium):N0} USD    {premiumPips:F0}p";
       lblBidSecondary.Visibility = Visibility.Visible;
     
 // Show LP badge with gradient background
         lblBidLP.Text = bestBid.LP;
     UpdateCountdown(lblBidCountdown, bestBid.ValidUntilTime);
    bidLPPanel.Visibility = Visibility.Visible;
          }
  else
     {
        lblBidValue.Text = "---";
         lblBidValue.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
     lblBidSecondary.Text = "---";
       lblBidLP.Text = "";
       bidLPPanel.Visibility = Visibility.Collapsed;
     }

            if (bestOffer != null)
            {
      // Display vol as main value (large white text)
          lblOfferValue.Text = bestOffer.OfferVol.ToString("F2");
            lblOfferValue.Foreground = Brushes.White;
    
    // Calculate pips
     double notionalUSD = _trade?.Legs?[0]?.NotionalMM ?? 10.0;
 double premiumPips = Math.Abs(bestOffer.OfferPremium) / (notionalUSD * 1_000_000) * 10000;
        
   // Format secondary text: "71,699 USD  44p"
    lblOfferSecondary.Text = $"{Math.Abs(bestOffer.OfferPremium):N0} USD    {premiumPips:F0}p";
lblOfferSecondary.Visibility = Visibility.Visible;
       
       // Show LP badge with gradient background
      lblOfferLP.Text = bestOffer.LP;
       UpdateCountdown(lblOfferCountdown, bestOffer.ValidUntilTime);
       offerLPPanel.Visibility = Visibility.Visible;
   }
        else
         {
    lblOfferValue.Text = "---";
       lblOfferValue.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
      lblOfferSecondary.Text = "---";
       lblOfferLP.Text = "";
       offerLPPanel.Visibility = Visibility.Collapsed;
  }

      // Spread - only show when we have both bid and offer
   if (bestBid != null && bestOffer != null)
      {
       var spread = bestOffer.OfferVol - bestBid.BidVol;
   lblSpread.Text = spread.ToString("F2");
     spreadPanel.Visibility = Visibility.Visible;
            }
         else
         {
   lblSpread.Text = "---";
     spreadPanel.Visibility = Visibility.Collapsed;
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

        private async void ParseButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button parseButton) return;

            // Store original state
            var originalContent = parseButton.Content;
            var originalBackground = parseButton.Background;
            var originalForeground = parseButton.Foreground;

            try
            {
                // Show loading state
                parseButton.Content = "Parsing...";
                parseButton.IsEnabled = false;
                parseButton.Background = new SolidColorBrush(Color.FromRgb(51, 37, 235)); // Slightly darker blue
                Console.WriteLine("[WPF] Parsing trade input...");

                // Parse trade input and update the trade structure
                await ParseTradeInput();

                // Show success state
                parseButton.Content = "✓ Parsed";
                parseButton.Background = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green
                parseButton.Foreground = Brushes.White;
                Console.WriteLine("[WPF] Trade parsed successfully");

                UpdateUIFieldsFromTrade();

                // Reset button after short delay
                await Task.Delay(1500);
            }
            catch (Exception ex)
            {
                // Show error state
                parseButton.Content = "✗ Error";
                parseButton.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                parseButton.Foreground = Brushes.White;
                Console.WriteLine($"[WPF] ERROR parsing trade: {ex.Message}");

                // Reset button after longer delay for error
                await Task.Delay(2000);
            }
            finally
            {
                // Restore original state
                parseButton.Content = originalContent;
                parseButton.Background = originalBackground;
                parseButton.Foreground = originalForeground;
                parseButton.IsEnabled = true;
            }

            // Don't auto-send RFQ - let user click RFQ tiles manually to request quotes
            Console.WriteLine("[WPF] Trade ready for RFQ (user should click tiles)");
        }

        private void txtTradeInput_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize placeholder on load (matches RFQ inactive color)
            if (string.IsNullOrWhiteSpace(txtTradeInput.Text) || txtTradeInput.Text == txtTradeInput.Tag?.ToString())
            {
                txtTradeInput.Text = txtTradeInput.Tag?.ToString() ?? "E.g., buy 10mio EURUSD 1m call 1.18";
                txtTradeInput.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748b"));
            }

            // Initialize window to RFQ state (not live state)
            ShowRfqState();
            Console.WriteLine("[WPF] Window initialized to RFQ state (_isRfqActive = false)");
        }

        private void txtTradeInput_GotFocus(object sender, RoutedEventArgs e)
        {
            // Clear placeholder when textbox gets focus
            // Check if it's the placeholder (either the static Tag or any text starting with "E.g.,")
            if (txtTradeInput.Text == txtTradeInput.Tag?.ToString() ||
                txtTradeInput.Text.StartsWith("E.g.,", StringComparison.OrdinalIgnoreCase))
            {
                txtTradeInput.Text = "";
                txtTradeInput.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#e2e8f0"));
            }
        }

        private async void txtTradeInput_LostFocus(object sender, RoutedEventArgs e)
        {
            // Format notional amounts before checking if empty
            // Don't format if it's placeholder text
            if (!string.IsNullOrWhiteSpace(txtTradeInput.Text) &&
                txtTradeInput.Text != txtTradeInput.Tag?.ToString() &&
                !txtTradeInput.Text.StartsWith("E.g.,", StringComparison.OrdinalIgnoreCase))
            {
                FormatNotionalAmounts();

                // Automatically parse trade input and populate all fields
                await ParseTradeInput();
                UpdateUIFieldsFromTrade();

                // Don't call ShowRfqState() here - tiles should remain in current state
                // Only the form fields should update, not the execution tiles
                Console.WriteLine("[WPF] Trade parsed, fields updated - tiles remain in RFQ state");
            }

            // Show placeholder when textbox loses focus and is empty
            if (string.IsNullOrWhiteSpace(txtTradeInput.Text))
            {
                txtTradeInput.Text = txtTradeInput.Tag?.ToString() ?? "";
                txtTradeInput.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748b"));
            }
        }

        private void FormatNotionalAmounts()
        {
            var text = txtTradeInput.Text;

            // Pattern to match numbers with k or m suffix (case insensitive)
            // Context-aware: only expand notional-sized numbers, not tenors
            var pattern = @"\b(\d+(?:\.\d+)?)\s*([kmKM])\b";

            text = System.Text.RegularExpressions.Regex.Replace(text, pattern, match =>
            {
                var number = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var suffix = match.Groups[2].Value.ToLower();

                // Heuristic: only expand if number suggests notional (not tenor)
                // Tenors are typically small numbers: 1m, 3m, 6m, 1y
                // Notionals are typically large: 10m, 50m, 100k
                if (suffix == "m" && number >= 10)
                {
                    // Format millions with space as thousand separator
                    long formatted = (long)(number * 1_000_000);
                    return formatted.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
                }
                else if (suffix == "k")
                {
                    // Format thousands with space as thousand separator
                    long formatted = (long)(number * 1000);
                    return formatted.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
                }

                // Leave as-is (likely tenor like "1m", "3m")
                return match.Value;
            });

            txtTradeInput.Text = text;
        }

        // OLD HANDLER - Commented out after replacing with dual notional fields (txtNotional1/txtNotional2)
        // private void txtNotional_LostFocus(object sender, RoutedEventArgs e)
        // {
        //     // Format notional amounts in the Quantity field
        //     if (!string.IsNullOrWhiteSpace(txtNotional.Text))
        //     {
        //         var text = txtNotional.Text;
        //
        //         // Pattern to match numbers with k or m suffix (case insensitive)
        //         var pattern = @"\b(\d+(?:\.\d+)?)\s*([kmKM])\b";
        //
        //         text = System.Text.RegularExpressions.Regex.Replace(text, pattern, match =>
        //         {
        //             var number = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        //             var suffix = match.Groups[2].Value.ToLower();
        //
        //             // Format millions (10m -> 10 000 000)
        //             if (suffix == "m" && number >= 10)
        //             {
        //                 long formatted = (long)(number * 1_000_000);
        //                 return formatted.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
        //             }
        //             // Format thousands (100k -> 100 000)
        //             else if (suffix == "k")
        //             {
        //                 long formatted = (long)(number * 1000);
        //                 return formatted.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
        //             }
        //
        //             // Leave as-is (small numbers like "1m" for tenor)
        //             return match.Value;
        //         });
        //
        //         txtNotional.Text = text;
        //     }
        // }

        private void txtNotional1_LostFocus(object sender, RoutedEventArgs e)
        {
            // Format notional1 and calculate notional2
            if (FindName("txtNotional1") is not TextBox notional1Box || string.IsNullOrWhiteSpace(notional1Box.Text))
                return;

            var text = notional1Box.Text;

            // Pattern to match numbers with k or m suffix (case insensitive)
            var pattern = @"\b(\d+(?:\.\d+)?)\s*([kmKM])\b";

            text = System.Text.RegularExpressions.Regex.Replace(text, pattern, match =>
            {
                var number = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var suffix = match.Groups[2].Value.ToLower();

                // Format millions (10m -> 10 000 000)
                if (suffix == "m" && number >= 10)
                {
                    long formatted = (long)(number * 1_000_000);
                    return formatted.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
                }
                // Format thousands (100k -> 100 000)
                else if (suffix == "k")
                {
                    long formatted = (long)(number * 1000);
                    return formatted.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
                }

                return match.Value;
            });

            notional1Box.Text = text;

            // Update trade object with new notional (in millions)
            try
            {
                string cleanText = text.Replace(" ", "");
                if (double.TryParse(cleanText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out double notional1))
                {
                    if (_trade?.Legs != null && _trade.Legs.Count > 0)
                    {
                        _trade.Legs[0].NotionalMM = notional1 / 1_000_000.0;
                        Console.WriteLine($"[WPF] Updated trade notional: {_trade.Legs[0].NotionalMM}M");
                    }

                    // Calculate notional2 based on spot rate
                    if (_trade?.SpotReference > 0 && FindName("txtNotional2") is TextBox notional2Box)
                    {
                        // Calculate notional2 = notional1 * spot
                        double notional2 = notional1 * _trade.SpotReference;
                        notional2Box.Text = ((long)notional2).ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
                        Console.WriteLine($"[WPF] Auto-calculated Notional2: {notional1:N0} * {_trade.SpotReference} = {notional2:N0}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WPF] Error updating notional: {ex.Message}");
            }
        }

        private void txtNotional2_LostFocus(object sender, RoutedEventArgs e)
        {
            // Format notional2 and calculate notional1
            if (FindName("txtNotional2") is not TextBox notional2Box || string.IsNullOrWhiteSpace(notional2Box.Text))
                return;

            var text = notional2Box.Text;

            // Pattern to match numbers with k or m suffix (case insensitive)
            var pattern = @"\b(\d+(?:\.\d+)?)\s*([kmKM])\b";

            text = System.Text.RegularExpressions.Regex.Replace(text, pattern, match =>
            {
                var number = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var suffix = match.Groups[2].Value.ToLower();

                // Format millions (10m -> 10 000 000)
                if (suffix == "m" && number >= 10)
                {
                    long formatted = (long)(number * 1_000_000);
                    return formatted.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
                }
                // Format thousands (100k -> 100 000)
                else if (suffix == "k")
                {
                    long formatted = (long)(number * 1000);
                    return formatted.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
                }

                return match.Value;
            });

            notional2Box.Text = text;

            // Calculate notional1 based on spot rate and update trade object
            if (_trade?.SpotReference > 0 && FindName("txtNotional1") is TextBox notional1Box)
            {
                try
                {
                    // Parse notional2 (remove spaces)
                    string cleanText = text.Replace(" ", "");
                    if (double.TryParse(cleanText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out double notional2))
                    {
                        // Calculate notional1 = notional2 / spot
                        double notional1 = notional2 / _trade.SpotReference;
                        notional1Box.Text = ((long)notional1).ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");

                        // Update trade object (notional is always in base currency, i.e., notional1)
                        if (_trade?.Legs != null && _trade.Legs.Count > 0)
                        {
                            _trade.Legs[0].NotionalMM = notional1 / 1_000_000.0;
                            Console.WriteLine($"[WPF] Updated trade notional from Notional2: {_trade.Legs[0].NotionalMM}M");
                        }

                        Console.WriteLine($"[WPF] Auto-calculated Notional1: {notional2:N0} / {_trade.SpotReference} = {notional1:N0}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WPF] Error calculating notional1: {ex.Message}");
                }
            }
        }

        private void txtCurrencyPair_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Auto-convert to uppercase as user types (e.g., "eurusd" → "EURUSD")
            if (sender is TextBox textBox)
            {
                int selectionStart = textBox.SelectionStart;
                string original = textBox.Text;
                string upper = original.ToUpper();

                if (original != upper)
                {
                    textBox.Text = upper;
                    textBox.SelectionStart = selectionStart; // Maintain cursor position
                }
            }
        }

        private async void txtCurrencyPair_LostFocus(object sender, RoutedEventArgs e)
        {
            // Update all related fields when currency pair is manually changed
            if (FindName("txtCurrencyPair") is not TextBox pairBox || string.IsNullOrWhiteSpace(pairBox.Text))
                return;

            string newPair = pairBox.Text.Trim().ToUpper();

            // Validate format (6 characters)
            if (newPair.Length != 6)
                return;

            // Update trade object
            if (_trade != null)
            {
                _trade.Underlying = newPair;
            }

            string ccy1 = newPair.Substring(0, 3);
            string ccy2 = newPair.Substring(3, 3);

            // Update notional labels
            if (FindName("lblNotional1") is TextBlock lbl1)
            {
                lbl1.Text = $"Notional ({ccy1})";
            }
            if (FindName("lblNotional2") is TextBlock lbl2)
            {
                lbl2.Text = $"Notional ({ccy2})";
            }

            // Update Call/Put display
            if (FindName("txtCallPut") is TextBlock callPutText && _trade?.Legs != null && _trade.Legs.Count > 0)
            {
                var leg = _trade.Legs[0];
                callPutText.Text = leg.OptionType == "PUT"
                    ? $"{ccy1} Put / {ccy2} Call"
                    : $"{ccy1} Call / {ccy2} Put";
            }

            // Fetch new spot rate from Bloomberg and recalculate dates
            try
            {
                double newSpot = await GetBloombergSpotAsync(newPair);
                if (newSpot > 0 && _trade != null)
                {
                    _trade.SpotReference = newSpot;
                    Console.WriteLine($"[WPF] Updated spot for {newPair}: {newSpot}");

                    // Update spot rate in Market Data section
                    if (FindName("lblSpotRate") is TextBlock spotLabel)
                    {
                        spotLabel.Text = newSpot.ToString("F4");
                    }

                    // Recalculate notional2 based on new spot
                    if (FindName("txtNotional1") is TextBox notional1Box &&
                        FindName("txtNotional2") is TextBox notional2Box &&
                        !string.IsNullOrWhiteSpace(notional1Box.Text))
                    {
                        try
                        {
                            string cleanText = notional1Box.Text.Replace(" ", "");
                            if (double.TryParse(cleanText, System.Globalization.NumberStyles.Number,
                                System.Globalization.CultureInfo.InvariantCulture, out double notional1))
                            {
                                double notional2 = notional1 * newSpot;
                                notional2Box.Text = ((long)notional2).ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
                                Console.WriteLine($"[WPF] Recalculated Notional2 for new pair: {notional1:N0} * {newSpot} = {notional2:N0}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[WPF] Error recalculating notional2: {ex.Message}");
                        }
                    }

                    // Recalculate expiry date based on tenor and new currency pair calendar
                    if (_trade.Legs != null && _trade.Legs.Count > 0 && !string.IsNullOrEmpty(_trade.Legs[0].Tenor))
                    {
                        try
                        {
                            var rules = new FxDateRules
                            {
                                Ccy1 = ccy1,
                                Ccy2 = ccy2,
                                SpotLag = PairSpotLag.TwoBD,
                                ExpiryConvention = QLNet.BusinessDayConvention.ModifiedFollowing
                            };

                            var premCcy = _trade.PremiumCurrency ?? ccy2;
                            var (spotDate, deliveryDate, expiryDate, _, _) = FxDateService.ComputeDates(
                                DateTime.UtcNow, newPair, _trade.Legs[0].Tenor, premCcy, rules);

                            _trade.Legs[0].ExpiryDate = expiryDate;

                            // Update expiry date display
                            if (FindName("txtExpiryDate") is TextBox expiryBox)
                            {
                                string dayOfWeek = expiryDate.ToString("ddd");
                                string formattedDate = expiryDate.ToString("dd-MMM-yy");
                                expiryBox.Text = $"{formattedDate}, {dayOfWeek} ({_trade.Legs[0].Tenor})";
                                Console.WriteLine($"[WPF] Recalculated expiry for {newPair}: {expiryBox.Text}");
                            }

                            // Update hedge value date (spot date for the new pair)
                            if (FindName("lblHedgeValueDate") is TextBlock hedgeDateLabel)
                            {
                                hedgeDateLabel.Text = spotDate.ToString("dd-MMM-yy");
                                Console.WriteLine($"[WPF] Updated hedge value date for {newPair}: {spotDate:dd-MMM-yy}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[WPF] Error recalculating dates for {newPair}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WPF] Error fetching spot for {newPair}: {ex.Message}");
            }
        }

        private void txtExpiryDate_LostFocus(object sender, RoutedEventArgs e)
        {
            // Parse tenor input like "1m", "1w", "3m" and calculate expiry date
            if (_trade == null || _trade.Legs == null || _trade.Legs.Count == 0)
                return;

            string input = txtExpiryDate.Text?.Trim().ToUpper();
            if (string.IsNullOrEmpty(input))
                return;

            // Parse tenor format: 1W, 1M, 3M, 6M, 1Y, 2W, ON, TN)
            var tenorMatch = System.Text.RegularExpressions.Regex.Match(input, @"^(\d+)\s*([WDMY])$");
            if (tenorMatch.Success)
            {
                string tenor = tenorMatch.Groups[1].Value + tenorMatch.Groups[2].Value;
                var leg = _trade.Legs[0];
                leg.Tenor = tenor;

                string pair = _trade.Underlying ?? "EURUSD";

                try
                {
                    var rules = new FxDateRules
                    {
                        Ccy1 = pair.Length >= 6 ? pair.Substring(0, 3) : "EUR",
                        Ccy2 = pair.Length >= 6 ? pair.Substring(3, 3) : "USD",
                        SpotLag = PairSpotLag.TwoBD,
                        ExpiryConvention = QLNet.BusinessDayConvention.ModifiedFollowing
                    };

                    var premCcy = _trade.PremiumCurrency ?? rules.Ccy2;
                    var (_, _, expiryDate, _, _) = FxDateService.ComputeDates(DateTime.UtcNow, pair, tenor, premCcy, rules);

                    leg.ExpiryDate = expiryDate;

                    // Format: "23-Jan-26, Fri (1M)"
                    string dayOfWeek = expiryDate.ToString("ddd");
                    string formattedDate = expiryDate.ToString("dd-MMM-yy");
                    txtExpiryDate.Text = $"{formattedDate}, {dayOfWeek} ({tenor})";

                    Console.WriteLine($"[WPF] Expiry calculated: {tenor} → {txtExpiryDate.Text}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WPF] Error calculating expiry from input: {ex.Message}");
                }
            }
        }

        private async Task ParseTradeInput()
        {
            // Don't parse if showing placeholder
            if (string.IsNullOrWhiteSpace(txtTradeInput.Text) ||
                txtTradeInput.Text == txtTradeInput.Tag?.ToString() ||
                txtTradeInput.Text.StartsWith("E.g.,", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var input = txtTradeInput.Text.ToLower();

            // Simple parsing - update trade structure
            if (_trade != null && _trade.Legs.Count > 0)
            {
                var leg = _trade.Legs[0];

                // Parse direction
                leg.Direction = input.Contains("sell") ? "SELL" : "BUY";

                // Parse currency pair first (needed for Bloomberg lookup)
                var pairMatch = System.Text.RegularExpressions.Regex.Match(input, @"\b([a-z]{3})([a-z]{3})\b");
                if (pairMatch.Success)
                {
                    _trade.Underlying = pairMatch.Value.ToUpper();
                }

                // Parse tenor FIRST (specific patterns: 1M, 3M, 1Y, 2W, ON, TN)
                // Must come before notional to avoid confusion
                var tenorMatch = System.Text.RegularExpressions.Regex.Match(input, @"\b(\d+)\s*([mywWdD])\s+");
                if (tenorMatch.Success)
                {
                    leg.Tenor = tenorMatch.Groups[1].Value + tenorMatch.Groups[2].Value.ToUpper();
                }

                // Parse notional - look for patterns like "25mio", "25m ", "10mm"
                // Use word boundaries and specific patterns to avoid matching tenor
                var notionalMatch = System.Text.RegularExpressions.Regex.Match(input, @"(\d+(?:\.\d+)?)\s*(?:mio|mm)\b");
                if (notionalMatch.Success)
                {
                    leg.NotionalMM = double.Parse(notionalMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    // Try "in 25m" or "25m in" patterns (space after helps distinguish from tenor)
                    var notionalMatch2 = System.Text.RegularExpressions.Regex.Match(input, @"(?:in\s+)?(\d+)\s*m(?:\s+|$|io)");
                    if (notionalMatch2.Success && notionalMatch2.Groups[1].Value != leg.Tenor?.TrimEnd('M'))
                    {
                        leg.NotionalMM = double.Parse(notionalMatch2.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    }
                }

                // Parse strike
                var strikeMatch = System.Text.RegularExpressions.Regex.Match(input, @"(\d+\.\d{2,4})");
                if (strikeMatch.Success)
                {
                    leg.Strike = double.Parse(strikeMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                }

                // Determine option type using Bloomberg spot
                bool explicitCallPut = input.Contains("call") || input.Contains("put");
                if (explicitCallPut)
                {
                    leg.OptionType = input.Contains("put") ? "PUT" : "CALL";
                }
                else if (leg.Strike > 0)
                {
                    string pair = _trade.Underlying ?? "EURUSD";
                    double spotRef = await GetBloombergSpotAsync(pair);
                    _trade.SpotReference = spotRef;

                    if (leg.Strike < spotRef)
                    {
                        leg.OptionType = "PUT"; // Strike below spot = PUT
                    }
                    else if (leg.Strike > spotRef)
                    {
                        leg.OptionType = "CALL"; // Strike above spot = CALL
                    }
                    else
                    {
                        leg.OptionType = "CALL"; // ATM default
                    }

                    Console.WriteLine($"[WPF] Auto-determined: Strike {leg.Strike} vs Spot {spotRef:F4} => {leg.OptionType}");
                }
                else
                {
                    leg.OptionType = "CALL"; // Default
                }

                Console.WriteLine($"[WPF] Parsed trade: {leg.Direction} {leg.NotionalMM}M {_trade.Underlying} {leg.Tenor} {leg.OptionType} @ {leg.Strike}");
            }
        }
        /// <summary>
        /// Update all UI fields from the parsed trade structure
        /// </summary>
        private async void UpdateUIFieldsFromTrade()
        {
            if (_trade == null || _trade.Legs == null || _trade.Legs.Count == 0)
                return;

            var leg = _trade.Legs[0];
            string pair = _trade.Underlying ?? "EURUSD";

            // === OPTION 1 FIELDS ===

            // Currency Pair
            if (FindName("txtCurrencyPair") is TextBox currencyPairBox)
            {
                currencyPairBox.Text = pair;
            }

            // Option type combo
            if (FindName("cmbOptionType") is ComboBox optTypeCombo)
            {
                optTypeCombo.SelectedIndex = leg.Direction == "SELL" ? 1 : 0; // 0=Buy, 1=Sell
            }

            // Dual Notionals (like GFI Fenics)
            string ccy1 = pair.Length >= 6 ? pair.Substring(0, 3) : "EUR";
            string ccy2 = pair.Length >= 6 ? pair.Substring(3, 3) : "USD";

            // Update labels to show currency names
            if (FindName("lblNotional1") is TextBlock lbl1)
            {
                lbl1.Text = $"Notional ({ccy1})";
            }
            if (FindName("lblNotional2") is TextBlock lbl2)
            {
                lbl2.Text = $"Notional ({ccy2})";
            }

            // Set notional 1 (base currency) from parsed trade
            if (FindName("txtNotional1") is TextBox notional1Box)
            {
                if (leg.NotionalMM > 0)
                {
                    // Convert millions to base units and format with spaces
                    long notionalBase = (long)(leg.NotionalMM * 1_000_000);
                    notional1Box.Text = notionalBase.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
                }
                else
                {
                    notional1Box.Text = "";
                }
            }

            // Calculate notional 2 (quote currency) based on spot rate
            if (FindName("txtNotional2") is TextBox notional2Box)
            {
                if (leg.NotionalMM > 0 && _trade.SpotReference > 0)
                {
                    // For EURUSD: 10M EUR * 1.0850 = 10.85M USD
                    double notional2MM = leg.NotionalMM * _trade.SpotReference;
                    long notional2Base = (long)(notional2MM * 1_000_000);
                    notional2Box.Text = notional2Base.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
                    Console.WriteLine($"[WPF] Calculated Notional2: {leg.NotionalMM}M {ccy1} * {_trade.SpotReference} = {notional2MM:F2}M {ccy2}");
                }
                else
                {
                    notional2Box.Text = "";
                }
            }

          // Call/Put display (use ccy1/ccy2 defined above)
          if (FindName("txtCallPut") is TextBlock callPutText)
          {
              callPutText.Text = leg.OptionType == "PUT"
                  ? $"{ccy1} Put / {ccy2} Call"
                  : $"{ccy1} Call / {ccy2} Put";
          }

            // Tenor combo box selection
            if (FindName("cmbTenor") is ComboBox tenorCombo && !string.IsNullOrEmpty(leg.Tenor))
            {
                // Find matching tenor in combo box
                for (int i = 0; i < tenorCombo.Items.Count; i++)
                {
                    if (tenorCombo.Items[i] is ComboBoxItem item &&
                        item.Content?.ToString() == leg.Tenor)
                    {
                        tenorCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            // Calculate and display expiry date from tenor using FX calendar
            if (!string.IsNullOrEmpty(leg.Tenor))
            {
                try
                {
                    var rules = new FxDateRules
                    {
                        Ccy1 = pair.Length >= 6 ? pair.Substring(0, 3) : "EUR",
                        Ccy2 = pair.Length >= 6 ? pair.Substring(3, 3) : "USD",
                        SpotLag = PairSpotLag.TwoBD,
                        ExpiryConvention = QLNet.BusinessDayConvention.ModifiedFollowing
                    };

                    var premCcy = _trade.PremiumCurrency ?? rules.Ccy2;
                    var (_, _, expiryDate, _, _) = FxDateService.ComputeDates(DateTime.UtcNow, pair, leg.Tenor, premCcy, rules);

                    leg.ExpiryDate = expiryDate;

                    if (FindName("txtExpiryDate") is TextBox expiryBox)
                    {
                        // Format: "23-Jan-26, Fri (1M)"
                        string dayOfWeek = expiryDate.ToString("ddd");
                        string formattedDate = expiryDate.ToString("dd-MMM-yy");
                        expiryBox.Text = $"{formattedDate}, {dayOfWeek} ({leg.Tenor})";
                        Console.WriteLine($"[WPF] Expiry date set: {expiryBox.Text}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WPF] Error calculating expiry: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"[WPF] WARNING: Tenor is empty, cannot calculate expiry date");
            }

      // Strike
  if (FindName("txtStrike") is TextBox strikeBox)
            {
                strikeBox.Text = leg.Strike > 0 ? leg.Strike.ToString("F4") : "";
            }

            // === MARKET DATA FIELDS ===

            // Fetch Bloomberg spot
            double spot = await GetBloombergSpotAsync(pair);

            if (FindName("lblSpotRate") is TextBlock spotLabel)
            {
                spotLabel.Text = spot.ToString("F4");
            }

            // Forward points and rate (using MarketData for now)
            var marketData = GetMarketDataForPair(pair);
            if (marketData != null && !string.IsNullOrEmpty(leg.Tenor))
            {
                double fwdRate = marketData.GetForwardRate(leg.Tenor);
                double fwdPts = (fwdRate - spot) * 10000;

                if (FindName("lblForwardPts") is TextBlock fwdPtsLabel)
                {
                    fwdPtsLabel.Text = fwdPts.ToString("F1");
                }

                if (FindName("lblForwardRate") is TextBlock fwdRateLabel)
                {
                    fwdRateLabel.Text = fwdRate.ToString("F4");
                }

                // ATM Vol
                double atmVol = marketData.GetVolatility(leg.Tenor, 50);
                if (FindName("lblAtmVol") is TextBlock atmVolLabel)
                {
                    atmVolLabel.Text = $"{atmVol:F2}%";
                }
            }

            // === HEDGE FIELDS ===

            if (FindName("txtHedgeRate") is TextBox hedgeRateBox)
            {
                hedgeRateBox.Text = spot.ToString("F4");
            }

            // Calculate hedge amount (delta-based)
            double delta = EstimateDelta();
            double hedgeAmount = leg.NotionalMM * delta * 1_000_000; // Convert to base currency units

            if (FindName("lblHedgeAmount") is TextBlock hedgeAmountLabel)
            {
                hedgeAmountLabel.Text = Math.Abs(hedgeAmount).ToString("N0");
            }

            // === RISK FIELDS ===

            // Delta
            if (FindName("lblDelta") is TextBlock deltaLabel)
            {
                deltaLabel.Text = (delta * leg.NotionalMM * 1_000_000).ToString("N0");
            }

            if (FindName("lblDeltaPct") is TextBlock deltaPctLabel)
            {
                deltaPctLabel.Text = $"{(delta * 100):F1}%";
            }

            // Vega (simplified estimate)
            double vega = leg.NotionalMM * 0.01 * Math.Sqrt(0.25); // Rough estimate
            if (FindName("lblVega") is TextBlock vegaLabel)
            {
                vegaLabel.Text = (vega * 1000).ToString("F0"); // Per 1% vol
            }

            // Gamma (simplified estimate)
            double gamma = leg.NotionalMM * 0.001;
            if (FindName("lblGamma") is TextBlock gammaLabel)
            {
                gammaLabel.Text = (gamma * 1000).ToString("F0");
            }

            // Update hedge details if a hedge type is selected
            if (FindName("cmbDeltaExchange") is ComboBox deltaExchangeCombo && 
      deltaExchangeCombo.SelectedItem is ComboBoxItem selectedItem)
      {
string hedgeSelection = selectedItem.Content?.ToString() ?? "";
   bool isForwardHedge = hedgeSelection.Contains("Forward");
   bool hasHedge = hedgeSelection.Contains("Spot") || hedgeSelection.Contains("Forward");
  
     if (hasHedge)
  {
           UpdateHedgeDetails(isForwardHedge);
     }
       }

            Console.WriteLine($"[WPF] Updated UI fields from trade: {pair} {leg.Tenor} {leg.OptionType} {leg.Strike}");
        }

        private void Tenor_Changed(object sender, SelectionChangedEventArgs e)
        {
            // Update expiry date based on tenor selection using FX calendar
            if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
            {
                var tenor = item.Content?.ToString() ?? "1M";
       
        if (_trade?.Legs?.Count > 0)
      {
      // Get currency pair info
       string pair = _trade.Underlying ?? "EURUSD";
         string ccy1 = pair.Length >= 6 ? pair.Substring(0, 3) : "EUR";
        string ccy2 = pair.Length >= 6 ? pair.Substring(3, 3) : "USD";
     string premCcy = _trade.PremiumCurrency ?? ccy2;

            try
       {
     // Use FxDateService to compute proper dates with FX calendar
  var rules = new FxDateRules
              {
    Ccy1 = ccy1,
      Ccy2 = ccy2,
  SpotLag = PairSpotLag.TwoBD,
    ExpiryConvention = QLNet.BusinessDayConvention.ModifiedFollowing,
  ExpiryEOM = true
    };

             var (tradeDate, spotDate, expiryDate, deliveryDate, premiumDate) = 
 FxDateService.ComputeDates(DateTime.UtcNow, pair, tenor, premCcy, rules);

  // Update trade structure
   _trade.Legs[0].Tenor = tenor;
     _trade.Legs[0].ExpiryDate = expiryDate;
              _trade.Legs[0].DeliveryDate = deliveryDate;

          // Update expiry date display
  var expiryDateTextBox = FindName("txtExpiryDate") as TextBox;
                 if (expiryDateTextBox != null)
            {
      expiryDateTextBox.Text = expiryDate.ToString("dd MMM yy");
       }

           // Update forward rate in market data section if visible
         UpdateMarketDataDisplay(tenor);

      // If hedge details are visible, update them too (value date and forward rate change with tenor)
         var deltaExchangeCombo = FindName("cmbDeltaExchange") as ComboBox;
             if (deltaExchangeCombo?.SelectedItem is ComboBoxItem deltaItem)
         {
        var hedgeSelection = deltaItem.Content?.ToString() ?? "";
          if (hedgeSelection.Contains("Forward") || hedgeSelection.Contains("Spot"))
    {
        // Refresh hedge details with new tenor
     UpdateHedgeDetails(isForward: hedgeSelection.Contains("Forward"));
       }
       }

               Console.WriteLine($"[WPF] Tenor changed to {tenor}: Expiry={expiryDate:dd MMM yy}, Delivery={deliveryDate:dd MMM yy}");
   }
         catch (Exception ex)
         {
 Console.WriteLine($"[WPF] Error calculating dates for tenor {tenor}: {ex.Message}");
       // Fallback to simple calculation
       var months = tenor.EndsWith("Y") ? int.Parse(tenor.TrimEnd('Y')) * 12 : int.Parse(tenor.TrimEnd('M'));
         _trade.Legs[0].Tenor = tenor;
     _trade.Legs[0].ExpiryDate = DateTime.Now.AddMonths(months);
       
      var expiryDateTextBox = FindName("txtExpiryDate") as TextBox;
       if (expiryDateTextBox != null)
             {
   expiryDateTextBox.Text = _trade.Legs[0].ExpiryDate.ToString("dd MMM yy");
     }
   }
      }
       }
        }

      /// <summary>
        /// Update market data display (forward rate, forward points) based on tenor
        /// </summary>
        private void UpdateMarketDataDisplay(string tenor)
        {
            if (_trade == null) return;

            var marketData = GetMarketDataForPair(_trade.Underlying ?? "EURUSD");
            if (marketData == null) return;

          // Update forward rate display
            var lblForwardRate = FindName("lblForwardRate") as System.Windows.Controls.TextBlock;
 var lblForwardPts = FindName("lblForwardPts") as System.Windows.Controls.TextBlock;

            if (lblForwardRate != null)
       {
    var forwardRate = marketData.GetForwardRate(tenor);
      lblForwardRate.Text = forwardRate.ToString("F4");
  }

         if (lblForwardPts != null && marketData.ForwardPoints.TryGetValue(tenor, out var points))
            {
       lblForwardPts.Text = points.ToString("F1");
}

         // Update forward reference in trade structure
        _trade.ForwardReference = marketData.GetForwardRate(tenor);
 }

        private void DeltaExchange_Changed(object sender, SelectionChangedEventArgs e)
        {
      // Update the hedge details panel based on selection
   if (sender is ComboBox combo && combo.SelectedItem is ComboBoxItem item)
    {
   var selection = item.Content?.ToString() ?? "";
     var detailsPanel = FindName("hedgeDetailsPanel") as Border;
  var rateHeader = FindName("lblHedgeRateHeader") as System.Windows.Controls.TextBlock;
   
    if (selection.Contains("Forward"))
           {
     // Show hedge details with Forward settings
      if (detailsPanel != null) detailsPanel.Visibility = Visibility.Visible;
    if (rateHeader != null) rateHeader.Text = "Outright";
       
       // Update with forward rate and delivery date
     UpdateHedgeDetails(isForward: true);
          }
       else if (selection.Contains("Spot"))
           {
    // Show hedge details with Spot settings
   if (detailsPanel != null) detailsPanel.Visibility = Visibility.Visible;
    if (rateHeader != null) rateHeader.Text = "Spot";
 
   // Update with spot rate and spot date
       UpdateHedgeDetails(isForward: false);
        }
      else
   {
   // No Hedge (Live) - hide hedge details
        if (detailsPanel != null) detailsPanel.Visibility = Visibility.Collapsed;
   }
   }
     }

     /// <summary>
 /// Update hedge details panel with real data:
        /// - Spot Rate: From TradeStructure.SpotReference (Bloomberg) or MarketData
        /// - Value Date: Calculated using FxDateService calendar
      /// - Amount: Delta � Notional (from FIX quotes or calculated)
    /// </summary>
        private void UpdateHedgeDetails(bool isForward)
        {
  var hedgeRateTextBox = FindName("txtHedgeRate") as TextBox;
    var valueDateLabel = FindName("lblHedgeValueDate") as System.Windows.Controls.TextBlock;
     var amountLabel = FindName("lblHedgeAmount") as System.Windows.Controls.TextBlock;
   var amountHeader = FindName("lblHedgeAmountHeader") as System.Windows.Controls.TextBlock;

    // Get currency pair from trade
     string pair = _trade?.Underlying ?? "EURUSD";
   string ccy1 = pair.Length >= 6 ? pair.Substring(0, 3) : "EUR";
            string ccy2 = pair.Length >= 6 ? pair.Substring(3, 3) : "USD";

        // === RATE: Use TradeStructure.SpotReference or MarketData ===
   double rate = 0;
    if (isForward)
  {
         // Forward rate = spot + forward points
   rate = _trade?.ForwardReference ?? 0;
      if (rate == 0)
       {
       // Calculate from market data if not set
    var marketData = GetMarketDataForPair(pair);
          var tenor = _trade?.Legs?.FirstOrDefault()?.Tenor ?? "1M";
   rate = marketData?.GetForwardRate(tenor) ?? marketData?.SpotRate ?? 1.0;
       }
   }
     else
     {
  // Spot rate from trade or market data
     rate = _trade?.SpotReference ?? 0;
  if (rate == 0)
   {
          var marketData = GetMarketDataForPair(pair);
   rate = marketData?.SpotRate ?? 1.0;
}
     }

    if (hedgeRateTextBox != null)
   {
       hedgeRateTextBox.Text = rate.ToString("F4");
  }

   // === VALUE DATE: Calculate using FxDateService calendar ===
    DateTime valueDate;
 try
     {
    var rules = new FxDateRules
           {
     Ccy1 = ccy1,
     Ccy2 = ccy2,
   SpotLag = PairSpotLag.TwoBD,
    ExpiryConvention = QLNet.BusinessDayConvention.ModifiedFollowing
        };

   if (isForward && _trade?.Legs?.Count > 0)
   {
        // Forward: use delivery date from expiry
    var tenor = _trade.Legs[0].Tenor ?? "1M";
          var premCcy = _trade.PremiumCurrency ?? ccy2;
      var (_, _, deliveryDate, _, _) = FxDateService.ComputeDates(DateTime.UtcNow, pair, tenor, premCcy, rules);
         valueDate = deliveryDate;
        }
  else
       {
   // Spot: T+2 calculated using calendar
  var (_, spotDate, _, _, _) = FxDateService.ComputeDates(DateTime.UtcNow, pair, "1M", ccy2, rules);
   valueDate = spotDate;
     }
       }
  catch (Exception ex)
      {
   Console.WriteLine($"[WPF] Error calculating value date: {ex.Message}");
       // Fallback to simple T+2
      valueDate = DateTime.Now.AddDays(isForward ? 30 : 2);
   }

     if (valueDateLabel != null)
 {
       valueDateLabel.Text = valueDate.ToString("dd MMM yy");
  }

   // === AMOUNT: Delta � Notional (from FIX quotes or calculated) ===
           // Update header to show currency
   if (amountHeader != null)
     {
       amountHeader.Text = $"Amount({ccy1})";
         }

   if (amountLabel != null && _trade?.Legs?.Count > 0)
   {
   var notional = _trade.Legs[0].NotionalMM * 1_000_000;
         
      // Try to get delta from latest quote
    double delta = GetLatestDeltaFromQuotes();
    if (delta == 0)
     {
          // Fallback: estimate delta based on option type and moneyness
        delta = EstimateDelta();
   }
      
         var hedgeAmount = notional * Math.Abs(delta);
        amountLabel.Text = hedgeAmount.ToString("N0");
   }
  }

        // Cache for Bloomberg spot rates to avoid excessive API calls
        private readonly ConcurrentDictionary<string, (double Rate, DateTime Timestamp)> _spotRateCache
            = new ConcurrentDictionary<string, (double, DateTime)>();

        /// <summary>
        /// Get real-time spot rate from Bloomberg API with caching
        /// </summary>
        private async Task<double> GetBloombergSpotAsync(string pair)
        {
            if (string.IsNullOrEmpty(pair))
                return 1.0;

            // Check cache (5 second expiry for real-time data)
            if (_spotRateCache.TryGetValue(pair, out var cached))
            {
                if ((DateTime.Now - cached.Timestamp).TotalSeconds < 5)
                {
                    return cached.Rate;
                }
            }

            // Fetch from Bloomberg
            if (_bloombergService?.IsConnected == true)
            {
                var spot = await _bloombergService.GetSpotRate(pair);
                if (spot.HasValue && spot.Value > 0)
                {
                    _spotRateCache[pair] = (spot.Value, DateTime.Now);
                    Console.WriteLine($"[WPF] Bloomberg spot for {pair}: {spot.Value:F4}");
                    return spot.Value;
                }
            }

            // Fallback to mock data if Bloomberg unavailable
            Console.WriteLine($"[WPF] WARNING: Bloomberg not available, using mock spot for {pair}");
            return GetMockSpotRate(pair);
        }

        /// <summary>
        /// Fallback mock spot rates when Bloomberg is unavailable
        /// </summary>
        private double GetMockSpotRate(string pair)
        {
            return pair?.ToUpperInvariant() switch
            {
                "EURUSD" => 1.0850,
                "USDSEK" => 10.4560,
                "EURNOK" => 11.7800,
                "GBPUSD" => 1.2650,
                _ => 1.0
            };
        }

        /// <summary>
 /// Get market data for the given currency pair (DEPRECATED - use GetBloombergSpotAsync)
        /// </summary>
        private MarketData GetMarketDataForPair(string pair)
        {
            return pair?.ToUpperInvariant() switch
            {
                "EURUSD" => MarketData.GetEURUSD(),
                "USDSEK" => MarketData.GetUSDSEK(),
                _ => MarketData.GetEURUSD() // Default
            };
        }

        /// <summary>
 /// Get latest delta from received FIX quotes
   /// </summary>
      private double GetLatestDeltaFromQuotes()
  {
   // Check if we have any quotes with delta
       if (_quotesByLP?.Values != null)
   {
          foreach (var lpData in _quotesByLP.Values)
      {
            if (lpData.Delta > 0)
 {
  return lpData.Delta / 100.0; // Convert from percentage to decimal
          }
    }
   }
   return 0;
   }

   /// <summary>
   /// Estimate delta based on option type and strike vs spot
   /// </summary>
      private double EstimateDelta()
 {
     if (_trade?.Legs == null || _trade.Legs.Count == 0) return 0.50;

       var leg = _trade.Legs[0];
   var spot = _trade.SpotReference > 0 ? _trade.SpotReference : 1.0;
      var strike = leg.Strike;

      // Simple ATM approximation - adjust based on moneyness
       double moneyness = strike / spot;
        
  if (leg.OptionType == "CALL")
     {
 // Call delta: roughly 0.5 ATM, higher for ITM, lower for OTM
    if (moneyness < 0.97) return 0.70; // ITM
        if (moneyness > 1.03) return 0.30; // OTM
   return 0.50; // ATM
        }
    else
     {
   // Put delta: roughly -0.5 ATM
     if (moneyness > 1.03) return 0.70; // ITM put
       if (moneyness < 0.97) return 0.30; // OTM put
return 0.50; // ATM
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

        /// <summary>
        /// Event handler for deal card clicks
        /// </summary>
        private void DealCard_Click(object sender, MouseButtonEventArgs e)
 {
   // Expand/collapse deal details
 if (sender is Border border && border.DataContext is DealViewModel deal)
      {
    deal.IsExpanded = !deal.IsExpanded;
  Console.WriteLine($"[WPF] Deal {deal.OrderId} {(deal.IsExpanded ? "expanded" : "collapsed")}");
        }
        }

        /// <summary>
     /// Bid tile click - execute on best bid price (you receive)
        /// </summary>
        private async void BidTile_Click(object sender, MouseButtonEventArgs e)
        {
   if (!_isRfqActive)
 {
        // Validate trade before sending RFQ
        if (_trade == null || _trade.Legs == null || _trade.Legs.Count == 0 || _trade.Legs[0].Strike <= 0)
        {
            Console.WriteLine("[WPF] Cannot send RFQ - trade not parsed. Please click Parse button first.");
            MessageBox.Show("Please parse a trade first using the Parse button.", "Trade Not Ready", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Not quoting - trigger RFQ
    _  = SendQuoteRequestsAsync();
   }
  else
    {
  // Quoting - execute trade at best bid
        await ExecuteTradeOnBestPrice("BID");
     }
  }

        /// <summary>
    /// Offer tile click - execute on best offer price (you pay)
/// </summary>
        private async void OfferTile_Click(object sender, MouseButtonEventArgs e)
    {
 if (!_isRfqActive)
   {
        // Validate trade before sending RFQ
        if (_trade == null || _trade.Legs == null || _trade.Legs.Count == 0 || _trade.Legs[0].Strike <= 0)
        {
            Console.WriteLine("[WPF] Cannot send RFQ - trade not parsed. Please click Parse button first.");
            MessageBox.Show("Please parse a trade first using the Parse button.", "Trade Not Ready", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

 // Not quoting - trigger RFQ
   _ = SendQuoteRequestsAsync();
   }
            else
  {
         // Quoting - execute trade at best offer
    await ExecuteTradeOnBestPrice("OFFER");
  }
        }
      
        private async Task SendQuoteRequestsAsync()
    {
            try
        {
      _quotesByLP.Clear();
                LPQuotes.Clear();

    // Generate GroupID in GFI format: "{numLegs}-{randomCode}"
           int numLegs = _trade?.Legs?.Count ?? 1;
                string randomCode = GenerateRandomGroupCode();
             _currentGroupId = $"{numLegs}-{randomCode}";

     // Get selected LPs from checkboxes
       var selectedLPs = GetSelectedLPs();

    if (selectedLPs.Count == 0)
{
         MessageBox.Show("Please select at least one LP.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
             return;
                }

    // Ensure SpotReference is populated
        if (_trade.SpotReference <= 0)
             {
   string pair = _trade?.Underlying ?? "EURUSD";
                double bloombergSpot = await GetBloombergSpotAsync(pair);
      if (bloombergSpot > 0)
         {
       _trade.SpotReference = bloombergSpot;
        }
           else
     {
           MessageBox.Show($"Unable to fetch spot rate for {pair}.", "Missing Spot Rate", MessageBoxButton.OK, MessageBoxImage.Warning);
return;
   }
                }

            // Apply hedge settings
      ApplyHedgeSettingsToTrade();

          Console.WriteLine($"\n[WPF] ========== SENDING RFQ ==========");
    Console.WriteLine($"[WPF] GroupID: {_currentGroupId}");
  Console.WriteLine($"[WPF] Selected LPs: {string.Join(", ", selectedLPs)}");

   var hedgeType = GetHedgeTypeTag();
    var premiumType = GetPremiumTypeTag();

            foreach (var lp in selectedLPs)
     {
 var quoteReqId = _fixSession.SendQuoteRequest(_trade, lp, _currentGroupId, hedgeType, premiumType);
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

        private async Task ExecuteTradeOnBestPrice(string side)
        {
 if (!_isRfqActive || _quotesByLP.IsEmpty)
      {
     MessageBox.Show("No active quotes available.", "Cannot Execute", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
          }

     try
      {
             // Get best quote for the selected side
     var quotes = _quotesByLP.Values.ToList();
        LPQuoteData bestQuote = null;

        if (side == "BID")
   {
        // Best bid = highest vol you receive
         bestQuote = quotes.Where(q => q.BidVol > 0).OrderByDescending(q => q.BidVol).FirstOrDefault();
    }
    else // OFFER
    {
      // Best offer = lowest vol you pay
         bestQuote = quotes.Where(q => q.OfferVol > 0).OrderBy(q => q.OfferVol).FirstOrDefault();
     }

       if (bestQuote == null)
      {
  MessageBox.Show($"No valid {side} quote available.", "Cannot Execute", MessageBoxButton.OK, MessageBoxImage.Warning);
     return;
     }

            // Execute immediately (no confirmation popup)
            Console.WriteLine($"[WPF] Executing {side} with {bestQuote.LP}");

            double executedVol = side == "BID" ? bestQuote.BidVol : bestQuote.OfferVol;
            double executedPremium = side == "BID" ? bestQuote.BidPremium : bestQuote.OfferPremium;

            // Get the correct QuoteID for this side
            string quoteId = side == "BID" ? bestQuote.BidQuoteId : bestQuote.OfferQuoteId;

            // Retrieve the FIXMessage for execution
            if (!_quotesByQuoteId.TryGetValue(quoteId, out var fixMsg))
            {
                Console.WriteLine($"[WPF] ERROR: Quote {quoteId} not found in FIXMessage cache");
                MessageBox.Show($"Quote not found: {quoteId}", "Execution Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Send execution order via FIX session
            string clOrdID = _fixSession.SendExecution(fixMsg, side, _trade);
            Console.WriteLine($"[WPF] Execution sent to GFI: ClOrdID={clOrdID}, QuoteID={quoteId}");

            // Create deal card (initially PENDING, will be updated by ExecutionReport)
            var deal = new DealViewModel
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Instrument = _trade?.Underlying ?? "EURUSD",
                LP = bestQuote.LP,
                Side = side == "BID" ? "YOU REC" : "YOU PAY",
                SideColor = side == "BID"
                    ? new SolidColorBrush(Color.FromRgb(34, 197, 94))  // Green
                    : new SolidColorBrush(Color.FromRgb(239, 68, 68)), // Red
                Status = "PENDING",
                StatusBackground = new SolidColorBrush(Color.FromRgb(251, 191, 36)), // Amber/Yellow
                StatusForeground = new SolidColorBrush(Colors.White),
                Volatility = $"{executedVol:F2}",
                EurPips = $"{Math.Abs(executedPremium):N0}", // Same as premium display
                PremiumLabel = executedPremium >= 0 ? "RCV" : "PAY",
                PremiumDisplay = $"{Math.Abs(executedPremium):N0}",
                PremiumColor = executedPremium >= 0
                    ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
                    : new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                Strike = _trade?.Legs?.FirstOrDefault()?.Strike.ToString("F4") ?? "",
                ExpiryDate = _trade?.Legs?.FirstOrDefault()?.ExpiryDate.ToString("dd-MMM-yy") ?? "",
                Notional = $"{_trade?.Legs?.FirstOrDefault()?.NotionalMM ?? 0}M",
                ExpiryCut = "10:00 NY",
                OrderId = clOrdID, // Use actual ClOrdID from GFI
                SpotRate = _trade?.SpotReference.ToString("F4") ?? ""
            };

            // Add to deals collection (shows on right panel)
            Deals.Insert(0, deal); // Insert at top (most recent first)

            // Hide "No deals" message
            if (FindName("lblNoDeals") is TextBlock noDealsLabel)
            {
                noDealsLabel.Visibility = Visibility.Collapsed;
            }

            Console.WriteLine($"[WPF] Trade execution sent - added to deals panel as PENDING (awaiting ExecutionReport)");

            // Clear quotes after execution
            ShowRfqState();
     }
        catch (Exception ex)
  {
    MessageBox.Show($"Error executing trade: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        Console.WriteLine($"[WPF] ERROR executing trade: {ex.Message}");
        }
        }

        // Helper methods
        private string GenerateRandomGroupCode()
        {
     const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
  var random = new Random();
    return new string(Enumerable.Repeat(chars, 8).Select(s => s[random.Next(s.Length)]).ToArray());
     }

      private List<string> GetSelectedLPs()
     {
            var selected = new List<string>();
     if (FindName("chkMS") is CheckBox chkMS && chkMS.IsChecked == true) selected.Add("MS");
      if (FindName("chkHSBC") is CheckBox chkHSBC && chkHSBC.IsChecked == true) selected.Add("HSBC");
         if (FindName("chkBNP") is CheckBox chkBNP && chkBNP.IsChecked == true) selected.Add("BNP");
            if (FindName("chkNATWEST") is CheckBox chkNATWEST && chkNATWEST.IsChecked == true) selected.Add("NATWEST");
            if (FindName("chkSOCGEN") is CheckBox chkSOCGEN && chkSOCGEN.IsChecked == true) selected.Add("SOCGEN");
      if (FindName("chkCIBC") is CheckBox chkCIBC && chkCIBC.IsChecked == true) selected.Add("CIBC");
            if (FindName("chkSCBL") is CheckBox chkSCBL && chkSCBL.IsChecked == true) selected.Add("SCBL");
        if (FindName("chkNOMURA") is CheckBox chkNOMURA && chkNOMURA.IsChecked == true) selected.Add("NOMURA");
            if (FindName("chkBAML") is CheckBox chkBAML && chkBAML.IsChecked == true) selected.Add("BAML");
     return selected;
        }

    private void ApplyHedgeSettingsToTrade()
        {
            if (_trade == null) return;
   var deltaExchangeCombo = FindName("cmbDeltaExchange") as ComboBox;
     if (deltaExchangeCombo?.SelectedItem is ComboBoxItem item)
          {
       var selection = item.Content?.ToString() ?? "";
     if (selection.Contains("Forward")) _trade.HedgeType = "FORWARD";
           else if (selection.Contains("Spot")) _trade.HedgeType = "SPOT";
            else _trade.HedgeType = "NONE";
       }
  }

        private string GetHedgeTypeTag()
        {
            // Return semantic name, not FIX tag value
            // Session manager will convert to tag value internally
            return _trade?.HedgeType switch
            {
                "SPOT" => "Spot",
                "FORWARD" => "Forward",
                _ => "Live"  // Default: No hedge
            };
        }

   private string GetPremiumTypeTag()
        {
     var premiumCombo = FindName("cmbPremiumDue") as ComboBox;
    if (premiumCombo?.SelectedItem is ComboBoxItem item)
            {
      var selection = item.Content?.ToString() ?? "";
     return selection.Contains("FORWARD") ? "1" : "0";
      }
            return "0";
        }

        #region UI Event Handlers

        private void Tile_MouseEnter(object sender, MouseEventArgs e)
        {
            // Brighten tile on hover (only when in RFQ state)
            if (!_isRfqActive && sender is Border tile)
            {
                // Store original background
                tile.Tag = tile.Background;
                // Brighten by adjusting opacity or color
                tile.Opacity = 1.0;
            }
        }

        private void Tile_MouseLeave(object sender, MouseEventArgs e)
        {
            // Restore original appearance
            if (!_isRfqActive && sender is Border tile && tile.Tag is Brush originalBrush)
            {
                tile.Background = originalBrush;
                tile.Opacity = 0.9;
            }
        }

        private void LPCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkbox)
            {
                // Find the parent Grid to update its opacity
                var parent = FindVisualParent<Grid>(checkbox);
                if (parent != null)
                {
                    // Set opacity based on checkbox state
                    parent.Opacity = checkbox.IsChecked == true ? 1.0 : 0.4;

                    // Also update the LP name TextBlock foreground color
                    // Find the TextBlock that shows LP name (it's a sibling of the checkbox in the StackPanel)
                    var stackPanel = FindVisualParent<StackPanel>(checkbox);
                    if (stackPanel != null)
                    {
                        foreach (var child in LogicalTreeHelper.GetChildren(stackPanel))
                        {
                            if (child is TextBlock textBlock && textBlock != checkbox)
                            {
                                // Set text color: White when checked, gray when unchecked
                                textBlock.Foreground = checkbox.IsChecked == true
                                    ? Brushes.White
                                    : new SolidColorBrush(Color.FromRgb(100, 116, 139)); // #64748b
                                break;
                            }
                        }
                    }

                    Console.WriteLine($"[WPF] LP {checkbox.Name} {(checkbox.IsChecked == true ? "checked" : "unchecked")} - Opacity: {parent.Opacity}");
                }
            }
        }

        // Helper method to find parent of specific type
        private T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;

            if (parent is T typedParent)
                return typedParent;

            return FindVisualParent<T>(parent);
        }

        private void CancelRFQ_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Console.WriteLine($"[WPF] ========== CANCELING RFQ ==========");
                Console.WriteLine($"[WPF] Timer running: {_countdownTimer.IsEnabled}");
                Console.WriteLine($"[WPF] Active quotes: {_quotesByLP.Count}");

                // Stop countdown timer
                _countdownTimer.Stop();
                Console.WriteLine($"[WPF] Countdown timer stopped");

                // Clear pending quotes
                _quotesByLP.Clear();
                LPQuotes.Clear();
                Console.WriteLine($"[WPF] Cleared all quotes");

                // Clear current group ID
                _currentGroupId = null;

                // Reset to RFQ state
                ShowRfqState();
                Console.WriteLine($"[WPF] Returned to RFQ state");
                Console.WriteLine($"[WPF] ========== RFQ CANCELED ==========\n");

                // Visual confirmation
                MessageBox.Show("RFQ canceled successfully", "Canceled", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WPF] ERROR canceling RFQ: {ex.Message}");
                MessageBox.Show($"Error canceling RFQ: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CallPutToggle_Click(object sender, MouseButtonEventArgs e)
        {
            // Toggle between Call and Put
            if (FindName("txtCallPut") is TextBlock callPutText && _trade?.Legs != null && _trade.Legs.Count > 0)
            {
                var leg = _trade.Legs[0];
                string pair = _trade.Underlying ?? "EURUSD";
                string ccy1 = pair.Length >= 6 ? pair.Substring(0, 3) : "EUR";
                string ccy2 = pair.Length >= 6 ? pair.Substring(3, 3) : "USD";

                // Toggle the option type
                leg.OptionType = leg.OptionType == "CALL" ? "PUT" : "CALL";

                // Update display
                callPutText.Text = leg.OptionType == "PUT"
                    ? $"{ccy1} Put / {ccy2} Call"
                    : $"{ccy1} Call / {ccy2} Put";

                Console.WriteLine($"[WPF] Toggled option type to {leg.OptionType}");
            }
        }

        private void DealsHeader_Click(object sender, MouseButtonEventArgs e)
        {
            // Toggle deals panel expansion
            Console.WriteLine($"[WPF] Deals header clicked");
        }

        private void DealsTabHandle_Click(object sender, MouseButtonEventArgs e)
        {
            // Handle deals tab click
            Console.WriteLine($"[WPF] Deals tab handle clicked");
        }

        #endregion

 #endregion
    }
}
