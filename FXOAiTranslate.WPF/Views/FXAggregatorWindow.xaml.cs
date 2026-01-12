using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FXOptionsSimulator;
using FXOptionsSimulator.FIX;
using FXOAiTranslator;

// Aliases to resolve WinForms/WPF type conflicts (caused by UseWindowsForms=true for TradeParser)
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using MessageBox = System.Windows.MessageBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace FXOAiTranslate.WPF.Views
{
    public partial class FXAggregatorWindow : Window
    {
        public ObservableCollection<LPQuoteRow> LPQuotes { get; set; }
      public ObservableCollection<DealViewModel> Deals { get; set; }

        private TradeStructure _trade;
        private readonly GFIFIXSessionManager _fixSession;
        private readonly BloombergService _bloombergService;
        private readonly TradeParser _tradeParser; // Shared parsing logic with FXO AI Translator
        private readonly ConcurrentDictionary<string, LPQuoteData> _quotesByLP;
        private readonly ConcurrentDictionary<string, FIXMessage> _quotesByQuoteId; // Store FIXMessages for execution
        private readonly DispatcherTimer _countdownTimer;
        private readonly SpeechSynthesizer _speechSynthesizer;
        private string _currentGroupId;
        private bool _isRfqActive;
        private bool _dealsPanelExpanded = true;
        
        // Track which currency is currently selected for notional (true = base currency, false = quote currency)
  private bool _notionalInBaseCurrency = true;

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

            // Initialize TradeParser for shared parsing logic (same as FXO AI Translator)
            string openAIApiKey = LoadOpenAIApiKey();
            _tradeParser = new TradeParser(_bloombergService, openAIApiKey);
            _tradeParser.DebugCallback = msg => Console.WriteLine($"[TradeParser] {msg}");
            Console.WriteLine($"[WPF] TradeParser initialized - AI enabled: {!string.IsNullOrEmpty(openAIApiKey)}");

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

                // Subscribe to execution reports to update deal card status
                _fixSession.Application.OnExecutionReport += OnExecutionReport;

                Console.WriteLine("[WPF] Subscribed to FIX quote events (FIXMessage + QuoteData + ExecutionReport)");
            }
            else
            {
                Console.WriteLine("[WPF] WARNING: FIX session not available");
            }

            // Countdown timer for quote expiry
            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _countdownTimer.Tick += CountdownTimer_Tick;

            // Initialize speech synthesizer for audio confirmations
            _speechSynthesizer = new SpeechSynthesizer();
            _speechSynthesizer.Rate = 1; // Normal rate (SpeechSynthesizer uses -10 to 10, not 1.3 like web)
            _speechSynthesizer.Volume = 100; // 0-100

            // Try to select a female voice (like HTML mockup)
            var femaleVoice = _speechSynthesizer.GetInstalledVoices()
                .FirstOrDefault(v => v.VoiceInfo.Gender == VoiceGender.Female && v.VoiceInfo.Culture.Name.StartsWith("en"));
            if (femaleVoice != null)
            {
                _speechSynthesizer.SelectVoice(femaleVoice.VoiceInfo.Name);
                Console.WriteLine($"[WPF] Selected voice: {femaleVoice.VoiceInfo.Name}");
            }
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
                _fixSession.Application.OnExecutionReport -= OnExecutionReport;
                _fixSession.OnQuoteReceived -= OnQuoteReceived;
                _fixSession.OnQuoteRequestRejected -= OnQuoteRequestRejected;
            }
            _countdownTimer.Stop();
            _speechSynthesizer?.Dispose();
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
  lblBidSecondary.Text = ""; // Clear secondary text
            lblBidLP.Text = ""; // Clear LP name
        lblBidCountdown.Text = ""; // Clear countdown
            bidLPPanel.Visibility = Visibility.Collapsed;

     // Reset offer tile to RFQ state
            lblOfferLabel.Visibility = Visibility.Collapsed;
       lblOfferRfqHint.Visibility = Visibility.Visible;
    lblOfferValue.Text = "RFQ";
   lblOfferValue.FontSize = 72;
    lblOfferValue.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
          lblOfferSecondary.Visibility = Visibility.Collapsed;
          lblOfferSecondary.Text = ""; // Clear secondary text
            lblOfferLP.Text = ""; // Clear LP name
            lblOfferCountdown.Text = ""; // Clear countdown
    offerLPPanel.Visibility = Visibility.Collapsed;

spreadPanel.Visibility = Visibility.Collapsed;
            lblSpread.Text = ""; // Clear spread text  
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
            lblBidValue.Text = ""; // Empty while waiting
            lblBidValue.FontSize = 72;
            lblBidValue.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)); // Dim grey while waiting
            lblBidSecondary.Text = "Requesting...";
            lblBidSecondary.Visibility = Visibility.Visible;
            bidLPPanel.Visibility = Visibility.Collapsed; // Hide LP badge until quote received

            // Show offer tile in live quoting state (waiting for quotes)
            lblOfferLabel.Visibility = Visibility.Visible;
            lblOfferRfqHint.Visibility = Visibility.Collapsed;
            lblOfferValue.Text = ""; // Empty while waiting
            lblOfferValue.FontSize = 72;
            lblOfferValue.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)); // Dim grey while waiting
            lblOfferSecondary.Text = "Requesting...";
            lblOfferSecondary.Visibility = Visibility.Visible;
    offerLPPanel.Visibility = Visibility.Collapsed; // Hide LP badge until quote received

    // Don't show spread panel until we have actual quotes
    // It will be shown in UpdateBestPrices when both bid and offer exist
        spreadPanel.Visibility = Visibility.Collapsed;
  lblSpread.Text = "";

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

        /// <summary>
        /// Handle execution report to update deal card status
        /// </summary>
        private void OnExecutionReport(string clOrdID, string status, string execID)
        {
            // Marshal to UI thread
            Dispatcher.BeginInvoke(() =>
            {
                Console.WriteLine($"[WPF] ExecutionReport received: ClOrdID={clOrdID}, Status={status}, ExecID={execID}");

                // Find the deal card with this ClOrdID and update its status
                var deal = Deals.FirstOrDefault(d => d.OrderId == clOrdID);
                if (deal != null)
                {
                    // Update status based on execution report
                    if (status == "FILLED" || status == "Filled")
                    {
                        deal.Status = "CONFIRMED";
                        deal.StatusBackground = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green
                        deal.StatusForeground = new SolidColorBrush(Colors.White);
                        Console.WriteLine($"[WPF] Deal {clOrdID} updated to CONFIRMED");

                        // Speak "Confirmed" with female voice (like HTML mockup)
                        try
                        {
                            _speechSynthesizer.SpeakAsync("Confirmed");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[WPF] Error speaking confirmation: {ex.Message}");
                        }
                    }
                    else if (status == "REJECTED" || status == "Rejected")
                    {
                        deal.Status = "REJECTED";
                        deal.StatusBackground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                        deal.StatusForeground = new SolidColorBrush(Colors.White);
                        Console.WriteLine($"[WPF] Deal {clOrdID} updated to REJECTED");
                    }
                    else
                    {
                        deal.Status = status;
                        Console.WriteLine($"[WPF] Deal {clOrdID} status updated to {status}");
                    }
                }
                else
                {
                    Console.WriteLine($"[WPF] WARNING: Deal with ClOrdID {clOrdID} not found in deals list");
                }
            });
        }

        private async Task ProcessQuoteAsync(QuoteData quote)
        {
            Console.WriteLine($"[WPF] Processing quote from {quote.LP} - Side: {quote.Side}, Vol: {(quote.Side == "BID" ? quote.BidVol : quote.OfferVol)}");

   // Ignore quotes if RFQ has been canceled (no active RFQ session)
          if (string.IsNullOrEmpty(_currentGroupId))
            {
 Console.WriteLine($"[WPF] Ignoring quote from {quote.LP} - No active RFQ session (RFQ was canceled)");
    return;
     }

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
                lpData.BidLegPremPrice = quote.BidLegPremPrice;  // Tag 5844: Premium per MM ("pips")
                lpData.BidQuoteId = quote.QuoteID;
            }
            else
            {
                lpData.OfferVol = quote.OfferVol;
                lpData.OfferPremium = quote.OfferPremium;
                lpData.OfferLegPremPrice = quote.OfferLegPremPrice;  // Tag 5844: Premium per MM ("pips")
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
     // Display vol as main value (large text in white/very light gray)
    lblBidValue.Text = bestBid.BidVol.ToString("F2");
     lblBidValue.Foreground = Brushes.White; // White color for prices
     
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
 // Display vol as main value (large text in white/very light gray)
          lblOfferValue.Text = bestOffer.OfferVol.ToString("F2");
            lblOfferValue.Foreground = Brushes.White; // White color for prices
    
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
 var parseButton = sender as Button ?? FindName("btnParse") as Button;
 await ParseTradeWithButtonFeedbackAsync(parseButton);
 }

 /// <summary>
 /// Parse trade input and provide Parse button feedback (loading/success/error).
 /// Used by both clicking the button and pressing Enter in the trade input box.
 /// </summary>
 private async Task ParseTradeWithButtonFeedbackAsync(Button? parseButton)
 {
 // Fallback: if button isn't available (e.g., XAML changed), just parse/update.
 if (parseButton == null)
 {
 await ParseTradeInputAndUpdate();
 return;
 }

 // Store original state
 var originalContent = parseButton.Content;
 var originalBackground = parseButton.Background;
 var originalForeground = parseButton.Foreground;

 try
 {
 // Show loading state
 parseButton.Content = "Parsing...";
 parseButton.IsEnabled = false;
 parseButton.Background = new SolidColorBrush(Color.FromRgb(51,37,235)); // Slightly darker blue
 Console.WriteLine("[WPF] Parsing trade input...");

 // Parse trade input and update the trade structure
 await ParseTradeInput();

 // Show success state
 parseButton.Content = "✓ Parsed";
 parseButton.Background = new SolidColorBrush(Color.FromRgb(34,197,94)); // Green
 parseButton.Foreground = Brushes.White;
 Console.WriteLine("[WPF] Trade parsed successfully");

 UpdateUIFieldsFromTrade();

 // Keep success visible briefly
 await Task.Delay(1500);
 }
 catch (Exception ex)
 {
 // Show error state
 parseButton.Content = "✗ Error";
 parseButton.Background = new SolidColorBrush(Color.FromRgb(239,68,68)); // Red
 parseButton.Foreground = Brushes.White;
 Console.WriteLine($"[WPF] ERROR parsing trade: {ex.Message}");

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

 /// <summary>
 /// Parse trade input and update UI fields (used by both button click and Enter key)
 /// </summary>
 private async Task ParseTradeInputAndUpdate()
 {
 try
 {
 Console.WriteLine("[WPF] Parsing trade input...");

 // Parse trade input and update the trade structure
 await ParseTradeInput();

 Console.WriteLine("[WPF] Trade parsed successfully");

 UpdateUIFieldsFromTrade();

 // Don't auto-send RFQ - let user click RFQ tiles manually to request quotes
 Console.WriteLine("[WPF] Trade ready for RFQ (user should click tiles)");
 }
 catch (Exception ex)
 {
 Console.WriteLine($"[WPF] ERROR parsing trade: {ex.Message}");
 System.Windows.MessageBox.Show($"Error parsing trade: {ex.Message}", "Parse Error",
 MessageBoxButton.OK, MessageBoxImage.Error);
 }
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

            // Pattern to match numbers with k, m, mio, or million suffix (case insensitive)
      // Context-aware: only expand notional-sized numbers, not tenors
     var pattern = @"\b(\d+(?:\.\d+)?)\s*(k|m(?:io)?|million)\b";

            text = System.Text.RegularExpressions.Regex.Replace(text, pattern, match =>
        {
           var number = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
     var suffix = match.Groups[2].Value.ToLower();

                // Heuristic: only expand if number suggests notional (not tenor)
              // Tenors are typically small numbers: 1m, 3m, 6m, 1y
       // Notionals are typically large: 10m, 50m, 100k
      // BUT: "mio" and "million" are ALWAYS notionals (not tenors)
         if ((suffix == "m" && number >= 10) || suffix == "mio" || suffix == "million")
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
    }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            txtTradeInput.Text = text;
   }
        /// <summary>
   /// Handle notional text field losing focus - format the value
      /// </summary>
     private void txtNotional_LostFocus(object sender, RoutedEventArgs e)
    {
 if (txtNotional == null || string.IsNullOrWhiteSpace(txtNotional.Text))
       return;

  var text = txtNotional.Text;

  // Pattern to match numbers with k, m, mio, or million suffix (case insensitive)
  var pattern = @"\b(\d+(?:\.\d+)?)\s*(k|m(?:io)?|million)\b";

  text = System.Text.RegularExpressions.Regex.Replace(text, pattern, match =>
{
  var number = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
     var suffix = match.Groups[2].Value.ToLower();

   // Format millions (10m -> 10 000 000, 15mio -> 15 000 000)
   // "mio" and "million" are ALWAYS notionals (not tenors)
   if ((suffix == "m" && number >= 10) || suffix == "mio" || suffix == "million")
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

// Leave as-is (small numbers like "1m" for tenor)
return match.Value;
 }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            txtNotional.Text = text;

     // Update trade structure with notional
   UpdateTradeNotionalFromUI();
   }

        /// <summary>
  /// Toggle notional currency between base and quote currency (e.g., EUR <-> USD for EURUSD)
        /// </summary>
        private void NotionalCurrencyToggle_Click(object sender, MouseButtonEventArgs e)
        {
    string pair = _trade?.Underlying ?? "EURUSD";
   string ccy1 = pair.Length >= 6 ? pair.Substring(0, 3) : "EUR";
 string ccy2 = pair.Length >= 6 ? pair.Substring(3, 3) : "USD";

            // Toggle the currency
     _notionalInBaseCurrency = !_notionalInBaseCurrency;
  string newCurrency = _notionalInBaseCurrency ? ccy1 : ccy2;

  // Update UI
     if (txtNotionalCurrency != null)
      {
     txtNotionalCurrency.Text = newCurrency;
   }
  if (lblNotional != null)
           {
     lblNotional.Text = $"Notional ({newCurrency})";
       }

 // Convert the displayed notional value if we have a spot rate
            if (_trade?.SpotReference > 0 && txtNotional != null && !string.IsNullOrWhiteSpace(txtNotional.Text))
  {
       try
  {
    string cleanText = txtNotional.Text.Replace(" ", "");
   if (double.TryParse(cleanText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out double currentValue))
  {
         double convertedValue;
                    if (_notionalInBaseCurrency)
   {
       // Converting from quote currency to base currency (divide by spot)
   convertedValue = currentValue / _trade.SpotReference;
 }
      else
         {
 // Converting from base currency to quote currency (multiply by spot)
          convertedValue = currentValue * _trade.SpotReference;
        }
   
    txtNotional.Text = ((long)convertedValue).ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
   }
        }
     catch (Exception ex)
    {
   Console.WriteLine($"[WPF] Error converting notional currency: {ex.Message}");
            }
  }
    }

    /// <summary>
        /// Update trade structure notional from the UI value (always stored in base currency MM)
    /// </summary>
  private void UpdateTradeNotionalFromUI()
        {
  if (_trade?.Legs == null || _trade.Legs.Count == 0 || txtNotional == null)
   return;

 try
   {
    string cleanText = txtNotional.Text?.Replace(" ", "") ?? "";
      if (!double.TryParse(cleanText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out double notionalValue))
   return;

 double notionalInBaseCurrency;
   if (_notionalInBaseCurrency)
   {
        notionalInBaseCurrency = notionalValue;
  }
  else if (_trade.SpotReference > 0)
      {
     // Convert from quote currency to base currency
  notionalInBaseCurrency = notionalValue / _trade.SpotReference;
     }
            else
     {
    notionalInBaseCurrency = notionalValue; // Fallback
        }

   // Store as MM (millions)
 _trade.Legs[0].NotionalMM = notionalInBaseCurrency / 1_000_000.0;
        }
 catch (Exception ex)
            {
     Console.WriteLine($"[WPF] Error updating trade notional: {ex.Message}");
   }
        }

        // OLD HANDLERS - Removed after consolidating to single notional field
 // private void txtNotional1_LostFocus(object sender, RoutedEventArgs e)
        // private void txtNotional2_LostFocus(object sender, RoutedEventArgs e)

        #endregion

        #region Trade Parsing and UI Update

        /// <summary>
        /// Load OpenAI API key from environment or configuration (same as MainForm)
  /// </summary>
        private string LoadOpenAIApiKey()
        {
   // Try environment variable first
   string key = Environment.GetEnvironmentVariable("OpenAIApiKey");

         // Fall back to app.config if not found
        if (string.IsNullOrEmpty(key) || key == "changeme")
{
     key = System.Configuration.ConfigurationManager.AppSettings["OpenAIApiKey"];
        }

       return key;
        }

        /// <summary>
        /// Parse the trade input text using TradeParser (same logic as FXO AI Translator)
        /// </summary>
        private async Task ParseTradeInput()
        {
   string input = txtTradeInput.Text?.Trim() ?? "";
  
       // Skip if placeholder or empty
   if (string.IsNullOrWhiteSpace(input) || 
   input.StartsWith("E.g.,", StringComparison.OrdinalIgnoreCase))
  {
             return;
    }

    // Use TradeParser for parsing (same logic as FXO AI Translator)
        var result = await _tradeParser.ParseTradeAsync(input);
            
            if (result != null && !string.IsNullOrEmpty(result.OVML))
       {
         Console.WriteLine($"[WPF] TradeParser result: Method={result.ParseMethod}, OVML={result.OVML}");
        
     // Convert OVML result to TradeStructure
     var ovmlResult = new OVMLParseResult
      {
        OVML = result.OVML,
     Underlying = result.Underlying,
        Expiry = result.Expiry,
       LegCount = result.LegCount
     };
           
 // Use OVMLBridge to convert to TradeStructure
       var tradeStructure = OVMLBridge.ConvertToTradeStructure(ovmlResult);
                
 // Copy trade structure fields to _trade
       if (_trade != null)
    {
         _trade.Underlying = tradeStructure.Underlying;
                _trade.StructureType = tradeStructure.StructureType;
   _trade.SpotReference = tradeStructure.SpotReference;
      _trade.Legs = tradeStructure.Legs;
   }
     
           Console.WriteLine($"[WPF] Parsed trade: {tradeStructure.Underlying} {tradeStructure.StructureType} with {tradeStructure.Legs?.Count ?? 0} legs");
            }
            else
  {
  Console.WriteLine($"[WPF] TradeParser returned no result for input: {input}");
   }
  }

        /// <summary>
        /// Update UI fields from the current trade structure
      /// </summary>
        private void UpdateUIFieldsFromTrade()
     {
          if (_trade == null || _trade.Legs == null || _trade.Legs.Count == 0)
    return;

var leg = _trade.Legs[0];
            string pair = _trade.Underlying ?? "EURUSD";
  string ccy1 = pair.Length >= 6 ? pair.Substring(0, 3) : "EUR";
 string ccy2 = pair.Length >= 6 ? pair.Substring(3, 3) : "USD";

          // Update currency pair
       if (txtCurrencyPair != null)
      {
       txtCurrencyPair.Text = pair;
            }

            // Update notional field and currency toggle
            string notionalCurrency = _notionalInBaseCurrency ? ccy1 : ccy2;
            if (lblNotional != null)
            {
    lblNotional.Text = $"Notional ({notionalCurrency})";
   }
 if (txtNotionalCurrency != null)
     {
     txtNotionalCurrency.Text = notionalCurrency;
     }

  if (txtNotional != null && leg.NotionalMM > 0)
   {
     // NotionalMM is always in base currency (millions)
     double notionalValue;
          if (_notionalInBaseCurrency)
      {
 notionalValue = leg.NotionalMM * 1_000_000;
                }
       else if (_trade.SpotReference > 0)
    {
  // Convert to quote currency
         notionalValue = leg.NotionalMM * 1_000_000 * _trade.SpotReference;
        }
    else
{
         notionalValue = leg.NotionalMM * 1_000_000;
                }
                txtNotional.Text = ((long)notionalValue).ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(",", " ");
 }

            // Update Call/Put toggle
       if (txtCallPut != null)
            {
        txtCallPut.Text = leg.OptionType == "PUT"
        ? $"{ccy1} Put / {ccy2} Call"
     : $"{ccy1} Call / {ccy2} Put";
     }

            // Update expiry date
     if (txtExpiryDate != null && !string.IsNullOrEmpty(leg.Tenor))
            {
     // Calculate expiry from tenor
        var expiry = CalculateExpiryFromTenor(leg.Tenor);
  txtExpiryDate.Text = $"{expiry:dd-MMM-yy} ({leg.Tenor})";
      }

            // Update strike
            if (txtStrike != null && leg.Strike > 0)
       {
                txtStrike.Text = leg.Strike.ToString("F4");
      }

            // Update spot rate in market data section
  if (lblSpotRate != null && _trade.SpotReference > 0)
          {
             lblSpotRate.Text = _trade.SpotReference.ToString("F4");
            }

         // Update hedge rate
            if (txtHedgeRate != null && _trade.SpotReference > 0)
        {
     txtHedgeRate.Text = _trade.SpotReference.ToString("F4");
            }

         // Update hedge value date
        if (lblHedgeValueDate != null)
     {
   lblHedgeValueDate.Text = DateTime.Now.AddDays(2).ToString("dd-MMM");
       }

    // Update hedge amount
    if (lblHedgeAmount != null && leg.NotionalMM > 0)
            {
      lblHedgeAmount.Text = $"{leg.NotionalMM:F1}MM";
    }

   Console.WriteLine($"[WPF] UI fields updated from trade structure");
     }

      /// <summary>
 /// Calculate expiry date from tenor string
      /// </summary>
    private DateTime CalculateExpiryFromTenor(string tenor)
  {
        if (string.IsNullOrEmpty(tenor))
           return DateTime.Now.AddMonths(1);

    var match = System.Text.RegularExpressions.Regex.Match(tenor, @"(\d+)([mMwWyYdD])");
     if (!match.Success)
  return DateTime.Now.AddMonths(1);

 int amount = int.Parse(match.Groups[1].Value);
      string unit = match.Groups[2].Value.ToUpper();

          return unit switch
     {
      "D" => DateTime.Now.AddDays(amount),
   "W" => DateTime.Now.AddDays(amount * 7),
   "M" => DateTime.Now.AddMonths(amount),
       "Y" => DateTime.Now.AddYears(amount),
         _ => DateTime.Now.AddMonths(amount)
         };
        }

     /// <summary>
      /// Get Bloomberg spot rate for currency pair
        /// </summary>
        private async Task<double> GetBloombergSpotAsync(string currencyPair)
        {
try
  {
   if (_bloombergService != null && _bloombergService.IsConnected)
   {
   var spot = await Task.Run(() => _bloombergService.GetSpotRate(currencyPair));
        if (spot.HasValue && spot.Value > 0)
{
    Console.WriteLine($"[WPF] Bloomberg spot for {currencyPair}: {spot.Value:F4}");
         return spot.Value;
     }
 }
   }
        catch (Exception ex)
            {
       Console.WriteLine($"[WPF] Error getting Bloomberg spot: {ex.Message}");
     }

   // Fallback rates
  return currencyPair?.ToUpper() switch
   {
     "EURUSD" => 1.0850,
       "USDJPY" => 149.50,
     "GBPUSD" => 1.2650,
  "EURGBP" => 0.8580,
       "AUDUSD" => 0.6550,
"USDCAD" => 1.3650,
      "NZDUSD" => 0.6150,
         "USDCHF" => 0.8850,
      "EURCHF" => 0.9600,
                "EURJPY" => 162.25,
       "GBPJPY" => 189.10,
       _ => 1.0
            };
        }

      #endregion

        #region Option Details Event Handlers

        private void txtCurrencyPair_LostFocus(object sender, RoutedEventArgs e)
        {
      // Update notional currency label when pair changes
            string pair = txtCurrencyPair?.Text?.ToUpper() ?? "EURUSD";
      string ccy1 = pair.Length >= 6 ? pair.Substring(0, 3) : "EUR";
    string ccy2 = pair.Length >= 6 ? pair.Substring(3, 3) : "USD";

            string notionalCurrency = _notionalInBaseCurrency ? ccy1 : ccy2;
   if (lblNotional != null)
            {
       lblNotional.Text = $"Notional ({notionalCurrency})";
            }
      if (txtNotionalCurrency != null)
            {
       txtNotionalCurrency.Text = notionalCurrency;
            }

 // Update trade structure
            if (_trade != null)
            {
  _trade.Underlying = pair;
            }
        }

        private void txtCurrencyPair_TextChanged(object sender, TextChangedEventArgs e)
        {
         // Auto-uppercase the currency pair
    if (txtCurrencyPair != null)
    {
              int caretIndex = txtCurrencyPair.CaretIndex;
   txtCurrencyPair.Text = txtCurrencyPair.Text.ToUpper();
  txtCurrencyPair.CaretIndex = caretIndex;
            }
        }

    private void txtExpiryDate_LostFocus(object sender, RoutedEventArgs e)
        {
 // Parse expiry date and update trade structure
 // Could add date parsing logic here
        }

        private void CallPutToggle_Click(object sender, MouseButtonEventArgs e)
   {
            if (_trade?.Legs == null || _trade.Legs.Count == 0)
       return;

   var leg = _trade.Legs[0];
   string pair = _trade.Underlying ?? "EURUSD";
        string ccy1 = pair.Length >= 6 ? pair.Substring(0, 3) : "EUR";
            string ccy2 = pair.Length >= 6 ? pair.Substring(3, 3) : "USD";

    // Toggle between CALL and PUT
            leg.OptionType = leg.OptionType == "CALL" ? "PUT" : "CALL";

       // Update UI
            if (txtCallPut != null)
         {
             txtCallPut.Text = leg.OptionType == "PUT"
        ? $"{ccy1} Put / {ccy2} Call"
 : $"{ccy1} Call / {ccy2} Put";
          }

       Console.WriteLine($"[WPF] Toggled option type to: {leg.OptionType}");
        }

        private void DeltaExchange_Changed(object sender, SelectionChangedEventArgs e)
        {
if (cmbDeltaExchange == null || hedgeDetailsPanel == null)
           return;

      // Show/hide hedge details based on selection
            bool showDetails = cmbDeltaExchange.SelectedIndex > 0; // 0 = No Hedge
        hedgeDetailsPanel.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;

            // Update header text
            if (lblHedgeRateHeader != null)
            {
         lblHedgeRateHeader.Text = cmbDeltaExchange.SelectedIndex == 2 ? "Forward" : "Spot";
    }
     }

        #endregion

    #region Collapsible Sections

        private void OptionHeader_Click(object sender, MouseButtonEventArgs e)
        {
 if (optionContent == null || lblOptionArrow == null)
      return;

    bool isCollapsed = optionContent.Visibility == Visibility.Collapsed;
            optionContent.Visibility = isCollapsed ? Visibility.Visible : Visibility.Collapsed;
            lblOptionArrow.Text = isCollapsed ? "▼" : "▶";
        }

        private void MarketDataHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (marketDataContent == null || lblMarketDataArrow == null)
      return;

            bool isCollapsed = marketDataContent.Visibility == Visibility.Collapsed;
       marketDataContent.Visibility = isCollapsed ? Visibility.Visible : Visibility.Collapsed;
   lblMarketDataArrow.Text = isCollapsed ? "▼" : "▶";
        }

  private void RiskHeader_Click(object sender, MouseButtonEventArgs e)
        {
   if (riskContent == null || lblRiskArrow == null)
     return;

            bool isCollapsed = riskContent.Visibility == Visibility.Collapsed;
      riskContent.Visibility = isCollapsed ? Visibility.Visible : Visibility.Collapsed;
    lblRiskArrow.Text = isCollapsed ? "▼" : "▶";
        }

        private void LadderHeader_Click(object sender, MouseButtonEventArgs e)
        {
    if (ladderContent == null || lblLadderArrow == null)
 return;

   bool isCollapsed = ladderContent.Visibility == Visibility.Collapsed;
     ladderContent.Visibility = isCollapsed ? Visibility.Visible : Visibility.Collapsed;
          lblLadderArrow.Text = isCollapsed ? "▼" : "▶";
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
_dealsPanelExpanded = !_dealsPanelExpanded;

    if (_dealsPanelExpanded)
  {
     dealsPanelColumn.Width = new GridLength(380);
       dealsPanel.Visibility = Visibility.Visible;
   dealsTabHandle.Visibility = Visibility.Collapsed;
    lblDealsArrow.Text = "▶";
      }
 else
            {
      dealsPanelColumn.Width = new GridLength(0);
     dealsPanel.Visibility = Visibility.Collapsed;
    dealsTabHandle.Visibility = Visibility.Visible;
lblDealsArrow.Text = "◀";
  }
        }

        #endregion

        #region RFQ Tile Clicks

      private void BidTile_Click(object sender, MouseButtonEventArgs e)
     {
         if (_isRfqActive)
            {
                // Execute on bid (user is selling)
  ExecuteTrade("BID");
       }
        else
          {
       // Start RFQ
      StartRFQ();
  }
   }

      private void OfferTile_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isRfqActive)
            {
// Execute on offer (user is buying)
     ExecuteTrade("OFFER");
        }
else
       {
  // Start RFQ
         StartRFQ();
     }
 }

        private void StartRFQ()
        {
     if (_trade == null || _trade.Legs == null || _trade.Legs.Count == 0)
          {
     MessageBox.Show("Please enter a valid trade first.", "No Trade", MessageBoxButton.OK, MessageBoxImage.Warning);
       return;
          }

  // Generate group ID for this RFQ session
  _currentGroupId = Guid.NewGuid().ToString("N").Substring(0, 8);

   // Clear previous quotes
            _quotesByLP.Clear();
            _quotesByQuoteId.Clear();
            LPQuotes.Clear();

            // Show live state
 ShowLiveState();

    // Get selected LPs
            var selectedLPs = GetSelectedLPs();
            if (selectedLPs.Count == 0)
            {
     MessageBox.Show("Please select at least one LP.", "No LPs Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            ShowRfqState();
                return;
            }

   // Send RFQ to each selected LP
          foreach (var lp in selectedLPs)
      {
                try
                {
   string quoteReqId = $"QR-{_currentGroupId}-{lp}";
        Console.WriteLine($"[WPF] Sending RFQ to {lp}: {quoteReqId}");

      // Register with FIX application
     _fixSession?.Application?.RegisterQuoteRequest(quoteReqId, _currentGroupId);

         // Send the quote request
  _fixSession?.SendQuoteRequest(_trade, lp, quoteReqId);
     }
       catch (Exception ex)
        {
          Console.WriteLine($"[WPF] Error sending RFQ to {lp}: {ex.Message}");
     }
            }

            Console.WriteLine($"[WPF] RFQ started with GroupId: {_currentGroupId}, LPs: {string.Join(", ", selectedLPs)}");
        }

        private void CancelRFQ_Click(object sender, RoutedEventArgs e)
        {
       Console.WriteLine("[WPF] Canceling RFQ...");
 
            // Clear group ID to ignore any incoming quotes
 _currentGroupId = null;
  
            // Stop countdown timer
      _countdownTimer.Stop();
  
   // Clear quotes
         _quotesByLP.Clear();
_quotesByQuoteId.Clear();
      LPQuotes.Clear();
    
    // Reset to RFQ state
       ShowRfqState();
 
    Console.WriteLine("[WPF] RFQ canceled");
        }

  private System.Collections.Generic.List<string> GetSelectedLPs()
        {
        var selectedLPs = new System.Collections.Generic.List<string>();

            if (chkMS?.IsChecked == true) selectedLPs.Add("MS");
     if (chkHSBC?.IsChecked == true) selectedLPs.Add("HSBC");
if (chkBNP?.IsChecked == true) selectedLPs.Add("BNP");
            if (chkNATWEST?.IsChecked == true) selectedLPs.Add("NATWEST");
   if (chkSOCGEN?.IsChecked == true) selectedLPs.Add("SOCGEN");
            if (chkCIBC?.IsChecked == true) selectedLPs.Add("CIBC");
        if (chkSCBL?.IsChecked == true) selectedLPs.Add("SCBL");
  if (chkNOMURA?.IsChecked == true) selectedLPs.Add("NOMURA");
 if (chkBAML?.IsChecked == true) selectedLPs.Add("BAML");

          return selectedLPs;
        }

    private void ExecuteTrade(string side)
   {
      // Find the best quote for this side
            LPQuoteData bestQuote = null;
     if (side == "BID")
            {
       bestQuote = _quotesByLP.Values.Where(q => q.BidVol > 0).OrderByDescending(q => q.BidVol).FirstOrDefault();
         }
    else
  {
         bestQuote = _quotesByLP.Values.Where(q => q.OfferVol > 0).OrderBy(q => q.OfferVol).FirstOrDefault();
            }

            if (bestQuote == null)
            {
MessageBox.Show("No quote available to execute.", "No Quote", MessageBoxButton.OK, MessageBoxImage.Warning);
              return;
       }

            string quoteId = side == "BID" ? bestQuote.BidQuoteId : bestQuote.OfferQuoteId;
    if (string.IsNullOrEmpty(quoteId))
      {
         MessageBox.Show("Quote ID not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
     return;
       }

     // Get the FIXMessage for this quote
  if (!_quotesByQuoteId.TryGetValue(quoteId, out var fixMessage))
      {
           MessageBox.Show("Quote message not found.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
    }

   // Generate order ID
         string clOrdId = $"ORD-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 4)}";

            // Add deal card immediately with PENDING status
          var deal = new DealViewModel
      {
         Time = DateTime.Now.ToString("HH:mm:ss"),
         Instrument = $"{_trade?.Underlying} {_trade?.Legs?[0]?.Tenor}",
     Strike = _trade?.Legs?[0]?.Strike.ToString("F4") ?? "N/A",
     Side = side == "BID" ? "SELL" : "BUY",
    SideColor = new SolidColorBrush(side == "BID" ? Color.FromRgb(239, 68, 68) : Color.FromRgb(34, 197, 94)),
       Status = "PENDING",
         StatusBackground = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
          StatusForeground = new SolidColorBrush(Colors.Black),
                OrderId = clOrdId,
    Volatility = (side == "BID" ? bestQuote.BidVol : bestQuote.OfferVol).ToString("F2") + "%",
   EurPips = "N/A",
            PremiumLabel = side == "BID" ? "RCV" : "PAY",
        PremiumDisplay = Math.Abs(side == "BID" ? bestQuote.BidPremium : bestQuote.OfferPremium).ToString("N0"),
PremiumColor = new SolidColorBrush(side == "BID" ? Color.FromRgb(34, 197, 94) : Color.FromRgb(239, 68, 68)),
       SpotRate = bestQuote.SpotRate,
ExpiryDate = _trade?.Legs?[0]?.ExpiryDate.ToString("dd-MMM-yy") ?? "N/A",
       Notional = $"{_trade?.Legs?[0]?.NotionalMM:F1}MM",
                ExpiryCut = "NYC"
            };

         Deals.Insert(0, deal);
            lblNoDeals.Visibility = Visibility.Collapsed;

   // Send order
            try
         {
   Console.WriteLine($"[WPF] Executing trade: {clOrdId} on {bestQuote.LP} ({side})");
        _fixSession?.SendExecution(fixMessage, side, _trade);
     }
  catch (Exception ex)
 {
   Console.WriteLine($"[WPF] Error executing trade: {ex.Message}");
         deal.Status = "ERROR";
      deal.StatusBackground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
           deal.StatusForeground = new SolidColorBrush(Colors.White);
            }
        }

        #endregion

    #region LP Checkbox and Tile Hover Events

   private void LPCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            // Update LP count label
       int count = GetSelectedLPs().Count;
    lblLPCount.Text = $"{count} LPs";
    
            // Update the LP name text color based on checkbox state
            if (sender is CheckBox checkbox)
            {
          // Find the parent StackPanel containing the TextBlock
     if (checkbox.Parent is StackPanel stackPanel)
                {
                 var textBlock = stackPanel.Children.OfType<TextBlock>().FirstOrDefault();
       if (textBlock != null)
          {
     // White when checked/enabled, grey when unchecked/disabled
      textBlock.Foreground = checkbox.IsChecked == true 
? Brushes.White 
          : new SolidColorBrush(Color.FromRgb(100, 116, 139)); // #64748b
         }
           }
            }
        }

        private void Tile_MouseEnter(object sender, MouseEventArgs e)
  {
            if (sender is Border border)
      {
  border.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue highlight
   }
      }

        private void Tile_MouseLeave(object sender, MouseEventArgs e)
      {
            if (sender is Border border)
       {
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)); // Original border
            }
        }

        private void HeroValue_MouseEnter(object sender, MouseEventArgs e)
  {
       // Could add tooltip or highlight effect
  }

     private void HeroValue_MouseLeave(object sender, MouseEventArgs e)
        {
   // Could remove tooltip or highlight effect
        }

        private void LPName_MouseEnter(object sender, MouseEventArgs e)
        {
   if (sender is Border border)
            {
     border.Background = new SolidColorBrush(Color.FromRgb(20, 22, 27));
 
   // Change LP name text to white (active color) on hover
       var stackPanel = border.Child as StackPanel;
       if (stackPanel != null)
{
               var textBlock = stackPanel.Children.OfType<TextBlock>().FirstOrDefault();
  if (textBlock != null)
          {
            textBlock.Tag = textBlock.Foreground; // Store original color
          textBlock.Foreground = Brushes.White;
  }
      }
         }
        }

        private void LPName_MouseLeave(object sender, MouseEventArgs e)
      {
            if (sender is Border border)
      {
       border.Background = new SolidColorBrush(Color.FromRgb(15, 17, 20));
 
       // Restore LP name text to correct color based on checkbox state
       var stackPanel = border.Child as StackPanel;
   if (stackPanel != null)
       {
    var textBlock = stackPanel.Children.OfType<TextBlock>().FirstOrDefault();
    var checkbox = stackPanel.Children.OfType<CheckBox>().FirstOrDefault();
    
      if (textBlock != null)
         {
         // Set color based on checkbox state: white if checked, grey if unchecked
         textBlock.Foreground = checkbox?.IsChecked == true 
             ? Brushes.White 
 : new SolidColorBrush(Color.FromRgb(100, 116, 139)); // #64748b
 }
    }
  }
        }

 private void DealCard_Click(object sender, MouseButtonEventArgs e)
        {
  // Toggle expansion of deal card
            if (sender is Border border && border.DataContext is DealViewModel deal)
            {
      deal.IsExpanded = !deal.IsExpanded;
        }
        }

        private void AddLeg_Click(object sender, MouseButtonEventArgs e)
    {
   MessageBox.Show("Multi-leg trading coming soon!", "Add Leg", MessageBoxButton.OK, MessageBoxImage.Information);
        }

 #endregion
    }
}
