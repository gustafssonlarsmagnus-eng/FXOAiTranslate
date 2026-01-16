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
            lblBidValue.FontSize = 56;
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
     lblOfferValue.FontSize = 56;
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
            lblBidValue.FontSize = 56;
            lblBidValue.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)); // Dim grey while waiting
            lblBidSecondary.Text = "Requesting...";
            lblBidSecondary.Visibility = Visibility.Visible;
            bidLPPanel.Visibility = Visibility.Collapsed; // Hide LP badge until quote received

            // Show offer tile in live quoting state (waiting for quotes)
            lblOfferLabel.Visibility = Visibility.Visible;
            lblOfferRfqHint.Visibility = Visibility.Collapsed;
            lblOfferValue.Text = ""; // Empty while waiting
            lblOfferValue.FontSize = 56;
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

          // Extract risk fields from quote
lpData.Delta = quote.Delta;

            // Extract market data fields from quote
            // SpotRate comes from Bloomberg (above) but we could also use quote.SpotRate if provided
            if (!string.IsNullOrEmpty(quote.SpotRate))
    {
          lpData.SpotRate = quote.SpotRate; // Use LP's spot rate if provided
    }
lpData.ForwardPoints = quote.ForwardPoints; // Tag 5191: LegForwardPoints

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
    lblOfferSecondary.Text = $"{Math.Abs(bestOffer.OfferPremium):N0} USD {premiumPips:F0}p";
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

// Update Risk section with data from best quotes
     UpdateRiskDetails(bestBid, bestOffer);

    // Update Market Data section with data from best quotes
 UpdateMarketData(bestBid, bestOffer);

    // Update Premium and Volatility in Option 1 section
         UpdatePremiumAndVolatility(bestBid, bestOffer);
}

    /// <summary>
        /// Update the Premium and Volatility fields in Option 1 section from quote data.
        /// Premium shows "RCV X / PAY Y" format, Volatility shows "X / Y" format.
  /// </summary>
        private void UpdatePremiumAndVolatility(LPQuoteData bestBid, LPQuoteData bestOffer)
        {
            // Get premium currency
     string pair = _trade?.Underlying ?? "EURUSD";
            string premiumCcy = pair.Length >= 6 ? pair.Substring(3, 3) : "USD";

   // Update Premium (shows bid/offer premiums)
            if (lblPremiumBid != null && lblPremiumOffer != null)
    {
         if (bestBid != null && bestBid.BidPremium != 0)
        {
        // Format: "RCV 68,778" (receive on bid = positive for seller)
          lblPremiumBid.Text = $"RCV {Math.Abs(bestBid.BidPremium):N0}";
        lblPremiumBid.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green
  }
 else
      {
        lblPremiumBid.Text = "RCV --";
  lblPremiumBid.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
  }

           if (bestOffer != null && bestOffer.OfferPremium != 0)
 {
    // Format: "PAY 71,699" (pay on offer = negative for buyer)
 lblPremiumOffer.Text = $"PAY {Math.Abs(bestOffer.OfferPremium):N0}";
         lblPremiumOffer.Foreground = Brushes.White;
          }
      else
                {
             lblPremiumOffer.Text = "PAY --";
      lblPremiumOffer.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
       }
        }

            // Update Volatility (shows bid/offer vols)
         if (lblVolatility != null)
     {
      string bidVol = bestBid != null && bestBid.BidVol > 0 ? bestBid.BidVol.ToString("F2") : "--";
     string offerVol = bestOffer != null && bestOffer.OfferVol > 0 ? bestOffer.OfferVol.ToString("F2") : "--";
         lblVolatility.Text = $"{bidVol} / {offerVol}";
            }

            Console.WriteLine($"[WPF] Premium/Volatility updated: Bid={bestBid?.BidPremium:N0}, Offer={bestOffer?.OfferPremium:N0}, BidVol={bestBid?.BidVol:F2}, OfferVol={bestOffer?.OfferVol:F2}");
    }

        /// <summary>
        /// Update the Risk section UI (Delta) from quote data.
        /// GFI FIX provides Delta (tag 6035).
        /// </summary>
    private void UpdateRiskDetails(LPQuoteData bestBid, LPQuoteData bestOffer)
        {
        // Get notional for delta calculation
       double notionalMM = _trade?.Legs?[0]?.NotionalMM ?? 10.0;
            string ccy1 = _trade?.Underlying?.Length >= 6 ? _trade.Underlying.Substring(0, 3) : "EUR";

            // Use offer quote delta if buying, bid quote delta if selling
            // Delta from GFI is in percentage format (e.g., 52 = 52%)
            double? delta = bestOffer?.Delta ?? bestBid?.Delta;

            if (delta.HasValue && delta.Value != 0)
            {
  // Delta % (raw from quote)
     double deltaPct = delta.Value;
              if (lblDeltaPct != null)
{
      lblDeltaPct.Text = $"{deltaPct:F1}%";
     }

     // Delta in currency amount (Delta % * Notional)
    double deltaAmount = (deltaPct / 100.0) * notionalMM * 1_000_000;
        if (lblDelta != null)
          {
     lblDelta.Text = $"{deltaAmount:N0}";
     }

     Console.WriteLine($"[WPF] Risk updated: Delta={deltaPct:F1}%, DeltaAmount={deltaAmount:N0} {ccy1}");
   }
    else
            {
       if (lblDeltaPct != null)
     {
         lblDeltaPct.Text = "--";
     }
          if (lblDelta != null)
  {
 lblDelta.Text = "--";
         }
            }
        }

        /// <summary>
        /// Update the Market Data section UI (Spot, Forward Points, Forward Rate) from quote data.
/// GFI FIX provides: SpotRate (tag 5235), ForwardPoints (tag 5191)
/// </summary>
   private void UpdateMarketData(LPQuoteData bestBid, LPQuoteData bestOffer)
     {
            // Use the best available quote for market data (prefer offer for buy scenario)
            var quoteForMarketData = bestOffer ?? bestBid;
            
            if (quoteForMarketData == null)
  {
  // Clear market data if no quotes
         if (lblSpotRate != null) lblSpotRate.Text = "--";
      if (lblForwardPts != null) lblForwardPts.Text = "--";
    if (lblForwardRate != null) lblForwardRate.Text = "--";
     return;
            }

            // Spot Rate (Tag 5235)
        if (lblSpotRate != null && !string.IsNullOrEmpty(quoteForMarketData.SpotRate))
    {
            lblSpotRate.Text = quoteForMarketData.SpotRate;
    }
   else if (lblSpotRate != null)
     {
     lblSpotRate.Text = "--";
            }

            // Forward Points (Tag 5191)
 if (lblForwardPts != null)
         {
          if (quoteForMarketData.ForwardPoints != 0)
        {
   lblForwardPts.Text = quoteForMarketData.ForwardPoints.ToString("F2");
    }
        else
    {
            lblForwardPts.Text = "--";
    }
       }

      // Forward Rate = Spot + (Forward Points / divisor)
    // Divisor depends on currency pair (10000 for most, 100 for JPY pairs)
  if (lblForwardRate != null && !string.IsNullOrEmpty(quoteForMarketData.SpotRate))
            {
  if (double.TryParse(quoteForMarketData.SpotRate, out double spot) && quoteForMarketData.ForwardPoints != 0)
           {
// Determine divisor based on currency pair
        double divisor = 10000.0; // Standard for most pairs
  string underlying = _trade?.Underlying ?? "";
       if (underlying.Contains("JPY") || underlying.Contains("HUF") || underlying.Contains("KRW"))
    {
             divisor = 100.0; // For JPY and other low-decimal pairs
       }

                    double forwardRate = spot + (quoteForMarketData.ForwardPoints / divisor);
    lblForwardRate.Text = forwardRate.ToString("F4");
          }
      else if (double.TryParse(quoteForMarketData.SpotRate, out double spotOnly))
              {
                    lblForwardRate.Text = spotOnly.ToString("F4"); // No forward points, use spot
     }
     else
          {
              lblForwardRate.Text = "--";
      }
   }

    Console.WriteLine($"[WPF] Market data updated: Spot={quoteForMarketData.SpotRate}, FwdPts={quoteForMarketData.ForwardPoints}");
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

            // Initialize hedge details panel
            InitializeHedgeDetailsPanel();
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
  // For the dedicated notional field, ANY "m" suffix means millions (1m = 1,000,000)
  var pattern = @"\b(\d+(?:\.\d+)?)\s*(k|m(?:io)?|million)\b";

  text = System.Text.RegularExpressions.Regex.Replace(text, pattern, match =>
{
  var number = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
     var suffix = match.Groups[2].Value.ToLower();

   // In the notional field, ALL "m" suffixes mean millions (1m = 1,000,000)
   // This is different from the trade input where we have to distinguish tenors
   if (suffix == "m" || suffix == "mio" || suffix == "million")
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

    // Leave as-is for any other case
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
        /// Handle notional text field gaining focus - select all text for easy replacement
        /// </summary>
        private void txtNotional_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtNotional != null && !string.IsNullOrEmpty(txtNotional.Text))
            {
                // Use Dispatcher to select all text AFTER the default focus behavior completes
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    txtNotional.SelectAll();
                }));
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

            // Always assign the parsed trade structure to _trade
        // If _trade is null, create a new one; otherwise update existing fields
         if (_trade == null)
           {
            _trade = tradeStructure;
    Console.WriteLine($"[WPF] Created new trade structure from parsed input");
   }
       else
          {
     // Update existing trade structure fields
         _trade.Underlying = tradeStructure.Underlying;
   _trade.StructureType = tradeStructure.StructureType;
     _trade.SpotReference = tradeStructure.SpotReference;
 _trade.Legs = tradeStructure.Legs;
    }

     Console.WriteLine($"[WPF] Parsed trade: {_trade.Underlying} {_trade.StructureType} with {_trade.Legs?.Count ?? 0} legs");

         // Log notional for debugging
          if (_trade.Legs != null && _trade.Legs.Count > 0)
       {
                  Console.WriteLine($"[WPF] Leg 0 NotionalMM: {_trade.Legs[0].NotionalMM}");
    }
      }
       else
       {
                Console.WriteLine($"[WPF] TradeParser returned no result for input: {input}");
 }
        }

        /// <summary>
      /// Update UI fields from the current trade structure
      /// </summary>
        private async void UpdateUIFieldsFromTrade()
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

    // Update notional field and currency toggle - use the NEW pair's currencies
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

            // Update Call/Put toggle - use the NEW pair's currencies
       if (txtCallPut != null)
            {
        txtCallPut.Text = leg.OptionType == "PUT"
        ? $"{ccy1} Put / {ccy2} Call"
     : $"{ccy1} Call / {ccy2} Put";
     }

            // Update expiry date - use Tenor if available, otherwise try ExpiryDate
            if (txtExpiryDate != null)
            {
                string tenor = leg.Tenor?.ToUpperInvariant();
                Console.WriteLine($"[WPF] UpdateUIFieldsFromTrade - leg.Tenor='{leg.Tenor}', ExpiryDate='{leg.ExpiryDate}'");
                
                DateTime expiryDate = DateTime.MinValue;
                DateTime deliveryDate = DateTime.MinValue;
                
                if (!string.IsNullOrEmpty(tenor) && System.Text.RegularExpressions.Regex.IsMatch(tenor, @"^\d+[MWYD]$"))
                {
                    // Calculate expiry from tenor using calendar service
                    try
                    {
                        string premiumCcy = pair.Length >= 6 ? pair.Substring(3, 3) : "USD";
                        var (tradeDate, spotDate, expiry, delivery, premiumDate) = 
                            FxDateService.ComputeDates(
                                DateTime.UtcNow,
                                pair,
                                tenor,
                                premiumCcy,
                                new FxDateRules()
                            );
                        
                        expiryDate = expiry;
                        deliveryDate = delivery;
                        
                        // Format: "13-Feb-26, Fri (1M)"
                        string dayOfWeek = expiryDate.ToString("ddd");
                        txtExpiryDate.Text = $"{expiryDate:dd-MMM-yy}, {dayOfWeek} ({tenor})";
                        Console.WriteLine($"[WPF] Set expiry date from tenor: {txtExpiryDate.Text}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WPF] Error calculating expiry from tenor '{tenor}': {ex.Message}");
                        txtExpiryDate.Text = tenor;
                    }
                }
                else if (leg.ExpiryDate != default && leg.ExpiryDate > DateTime.Now.AddDays(-30))
                {
                    // Use the pre-calculated ExpiryDate from OVMLBridge
                    expiryDate = leg.ExpiryDate;
                    deliveryDate = CalculateDeliveryDate(expiryDate);
                    
                    string dayOfWeek = expiryDate.ToString("ddd");
                    string tenorDisplay = !string.IsNullOrEmpty(leg.Tenor) ? $" ({leg.Tenor.ToUpperInvariant()})" : "";
                    txtExpiryDate.Text = $"{expiryDate:dd-MMM-yy}, {dayOfWeek}{tenorDisplay}";
                    Console.WriteLine($"[WPF] Set expiry date from leg.ExpiryDate: {txtExpiryDate.Text}");
                }
                else
                {
                    // Fallback - show what we have or placeholder
                    txtExpiryDate.Text = !string.IsNullOrEmpty(leg.Tenor) ? leg.Tenor.ToUpperInvariant() : "--";
                    Console.WriteLine($"[WFP] Expiry date fallback: {txtExpiryDate.Text}");
                }
                
                // Update Value Date from delivery date
                if (deliveryDate != DateTime.MinValue && lblHedgeValueDate != null)
                {
                    lblHedgeValueDate.Text = deliveryDate.ToString("dd-MMM-yy", System.Globalization.CultureInfo.InvariantCulture);
                    Console.WriteLine($"[WPF] Set Value Date: {lblHedgeValueDate.Text}");
                }
                
                // Update trade structure with calculated dates
                if (expiryDate != DateTime.MinValue)
                {
                    leg.ExpiryDate = expiryDate;
                }
                if (deliveryDate != DateTime.MinValue)
                {
                    leg.DeliveryDate = deliveryDate;
                }
            }

            // Update strike with proper decimal formatting for currency pair
            if (txtStrike != null && leg.Strike > 0)
            {
                txtStrike.Text = FormatStrike(leg.Strike, pair);
            }

            // Fetch live Bloomberg spot rate if we don't have one
            double spotRate = _trade.SpotReference;
            if (spotRate <= 0)
            {
                spotRate = await GetBloombergSpotAsync(pair);
                if (spotRate > 0)
                {
                    _trade.SpotReference = spotRate;
                    Console.WriteLine($"[WPF] Fetched Bloomberg spot for {pair}: {spotRate:F4}");
                }
            }

            // Update spot rate in market data section
            if (lblSpotRate != null && spotRate > 0)
            {
                lblSpotRate.Text = spotRate.ToString("F4");
            }

            // Update hedge rate label with spot rate (panel is already visible with "--" placeholders)
            if (spotRate > 0)
            {
                var hedgeRateLabel = FindName("lblHedgeRate") as System.Windows.Controls.TextBlock;
                if (hedgeRateLabel != null)
                {
                    int decimals = pair.Contains("JPY") ? 2 : 4;
                    hedgeRateLabel.Text = $"Spot: {spotRate.ToString($"F{decimals}")}";
                }
            }

            Console.WriteLine($"[WPF] UI fields updated from trade structure");
        }
        private DateTime CalculateDeliveryDate(DateTime expiryDate)
        {
            string currencyPair = _trade?.Underlying ?? "EURUSD";
            
            try
            {
                // Use FxDateService to compute delivery date properly
                // We need to use an "ON" (overnight) tenor from the expiry date
                // to get the proper T+2 delivery date with calendar adjustment
                string premiumCcy = currencyPair.Length >= 6 ? currencyPair.Substring(3, 3) : "USD";
                
                var (_, _, _, deliveryDate, _) = FxDateService.ComputeDates(
                    expiryDate,
                    currencyPair,
                    "2D", // 2 business days from expiry
                    premiumCcy,
                    new FxDateRules()
                );
                
                Console.WriteLine($"[WPF] Calendar-adjusted delivery date: {deliveryDate:dd-MMM-yy}");
                return deliveryDate;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WPF] FxDateService error for delivery: {ex.Message}, using simple T+2 adjustment");
                
                // Fallback: simple T+2 with weekend adjustment
                DateTime deliveryDate = expiryDate.AddDays(2);
                while (deliveryDate.DayOfWeek == DayOfWeek.Saturday || 
                       deliveryDate.DayOfWeek == DayOfWeek.Sunday)
                {
                    deliveryDate = deliveryDate.AddDays(1);
                }
                return deliveryDate;
            }
        }

        /// <summary>
        /// Handle currency pair text field gaining focus - select all text for easy replacement
        /// </summary>
        private void txtCurrencyPair_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtCurrencyPair != null && !string.IsNullOrEmpty(txtCurrencyPair.Text))
            {
                // Use Dispatcher to select all text AFTER the default focus behavior completes
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    txtCurrencyPair.SelectAll();
                }));
            }
        }

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

        private async void txtCurrencyPair_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Auto-uppercase the currency pair as user types
            if (txtCurrencyPair != null)
            {
                int caretIndex = txtCurrencyPair.CaretIndex;
                string upperText = txtCurrencyPair.Text.ToUpper();
    
                // Only update if text actually changed (avoid infinite loop)
                if (txtCurrencyPair.Text != upperText)
                {
                    txtCurrencyPair.Text = upperText;
                    txtCurrencyPair.CaretIndex = Math.Min(caretIndex, upperText.Length);
                }
         
                // Update related fields when we have a valid 6-character currency pair
                string pair = upperText;
                if (pair.Length >= 6)
                {
                    string ccy1 = pair.Substring(0, 3);
                    string ccy2 = pair.Substring(3, 3);
 
                    // Update notional currency label and toggle
                    string notionalCurrency = _notionalInBaseCurrency ? ccy1 : ccy2;
                    if (lblNotional != null)
                    {
                        lblNotional.Text = $"Notional ({notionalCurrency})";
                    }
                    if (txtNotionalCurrency != null)
                    {
                        txtNotionalCurrency.Text = notionalCurrency;
                    }
       
                    // Update Call/Put toggle display
                    if (txtCallPut != null)
                    {
                        string optionType = _trade?.Legs?[0]?.OptionType ?? "CALL";
                        txtCallPut.Text = optionType == "PUT"
                            ? $"{ccy1} Put / {ccy2} Call"
                            : $"{ccy1} Call / {ccy2} Put";
                    }
           
                    // Hedge amount label header removed - column no longer exists
  
                    // Update trade structure if it exists
                    if (_trade != null)
                    {
                        _trade.Underlying = pair.Substring(0, 6); // Take only first 6 chars
                    }
        
                    Console.WriteLine($"[WPF] Currency pair updated to {pair} - Notional: {notionalCurrency}");
                    
                    // Fetch and display Bloomberg spot rate for the new currency pair
                    string validPair = pair.Substring(0, 6);
                    double spotRate = await GetBloombergSpotAsync(validPair);
                    if (spotRate > 0)
                    {
                        // Update trade structure
                        if (_trade != null)
                        {
                            _trade.SpotReference = spotRate;
                        }
                        
                        // Update spot rate display in Market Data section
                        if (lblSpotRate != null)
                        {
                            lblSpotRate.Text = spotRate.ToString("F4");
                        }
                        
                        // Update hedge rate label with spot rate
                        var hedgeRateLabel = FindName("lblHedgeRate") as System.Windows.Controls.TextBlock;
                        if (hedgeRateLabel != null)
                        {
                            int decimals = validPair.Contains("JPY") ? 2 : 4;
                            hedgeRateLabel.Text = $"Spot: {spotRate.ToString($"F{decimals}")}";
                        }
                        
                        Console.WriteLine($"[WPF] Fetched Bloomberg spot for {validPair}: {spotRate:F4}");
                    }
                }
            }
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

        private void txtExpiryDate_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtExpiryDate != null && !string.IsNullOrEmpty(txtExpiryDate.Text))
            {
                // Use Dispatcher to select all text AFTER the default focus behavior completes
                // This ensures the selection isn't immediately cleared by the mouse click
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    txtExpiryDate.SelectAll();
                }));
            }
        }

        /// <summary>
        /// Handle expiry date text field losing focus - parse tenor and calculate actual date
        /// </summary>
        private void txtExpiryDate_LostFocus(object sender, RoutedEventArgs e)
        {
            if (txtExpiryDate == null || string.IsNullOrWhiteSpace(txtExpiryDate.Text))
                return;

            string input = txtExpiryDate.Text.Trim().ToUpperInvariant();
            
            // Check if it's already in the formatted output (e.g., "13-Feb-26, Thu (1M)")
            if (input.Contains(",") && input.Contains("("))
            {
                // Already formatted, no need to re-parse
                return;
            }

            try
            {
                string currencyPair = _trade?.Underlying ?? "EURUSD";
                DateTime expiryDate;
                DateTime deliveryDate;
                string tenorDisplay = "";

                // Check if input is a tenor (1M, 2M, 3M, 6M, 1Y, 1W, 7D, etc.)
                if (System.Text.RegularExpressions.Regex.IsMatch(input, @"^\d+[MWYD]$"))
                {
                    // Input is a TENOR - use FxDateService for proper calendar-aware calculation
                    tenorDisplay = input;
                    
                    try
                    {
                        string premiumCcy = currencyPair.Length >= 6 ? currencyPair.Substring(3, 3) : "USD";
                        var (tradeDate, spotDate, expiry, delivery, premiumDate) = 
                            FxDateService.ComputeDates(
                                DateTime.UtcNow,
                                currencyPair,
                                input,
                                premiumCcy,
                                new FxDateRules()
                            );
                        
                        expiryDate = expiry;
                        deliveryDate = delivery;
                        Console.WriteLine($"[WPF] Expiry LostFocus: FxDateService calculated tenor '{input}' → Expiry={expiryDate:dd-MMM-yy}, Delivery={deliveryDate:dd-MMM-yy}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WPF] Error calculating expiry from tenor '{input}': {ex.Message}");
                        expiryDate = CalculateExpiryFromTenor(input);
                        deliveryDate = CalculateDeliveryDate(expiryDate);
                    }
                }
                else
                {
                    // Try to parse as a date (various formats)
                    bool parsed = false;
                    
                    // Try dd-MMM-yy format (13-Feb-26)
                    if (DateTime.TryParseExact(input, new[] { "dd-MMM-yy", "dd-MMM-yyyy", "d-MMM-yy", "d-MMM-yyyy" },
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out expiryDate))
                    {
                        parsed = true;
                    }
                    // Try MM/dd/yy format
                    else if (DateTime.TryParseExact(input, new[] { "MM/dd/yy", "M/d/yy", "MM/dd/yyyy", "M/d/yyyy" },
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out expiryDate))
                    {
                        parsed = true;
                    }
                    // Try general date parsing
                    else if (DateTime.TryParse(input, out expiryDate))
                    {
                        parsed = true;
                    }

                    if (!parsed)
                    {
                        Console.WriteLine($"[WPF] Expiry LostFocus: Could not parse '{input}' as date or tenor");
                        return; // Leave as-is if can't parse
                    }
                    
                    // Calculate delivery date using calendar service
                    deliveryDate = CalculateDeliveryDate(expiryDate);
                    Console.WriteLine($"[WPF] Expiry LostFocus: Parsed date '{input}' → Expiry={expiryDate:dd-MMM-yy}, Delivery={deliveryDate:dd-MMM-yy}");
                }

                // Format the display: "13-Feb-26, Thu (1M)" or "13-Feb-26, Thu" if no tenor
                string dayOfWeek = expiryDate.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture);
                string dateStr = expiryDate.ToString("dd-MMM-yy", System.Globalization.CultureInfo.InvariantCulture);
                
                if (!string.IsNullOrEmpty(tenorDisplay))
                {
                    txtExpiryDate.Text = $"{dateStr}, {dayOfWeek} ({tenorDisplay})";
                }
                else
                {
                    txtExpiryDate.Text = $"{dateStr}, {dayOfWeek}";
                }

                // Update trade structure if available
                if (_trade != null && _trade.Legs != null && _trade.Legs.Count > 0)
                {
                    _trade.Legs[0].ExpiryDate = expiryDate;
                    _trade.Legs[0].Tenor = tenorDisplay;
                    _trade.Legs[0].DeliveryDate = deliveryDate;
                    
                    Console.WriteLine($"[WPF] Updated trade: ExpiryDate={expiryDate:dd-MMM-yy}, DeliveryDate={deliveryDate:dd-MMM-yy}");
                }

                // Update Value Date field in hedge details section
                if (lblHedgeValueDate != null)
                {
                    lblHedgeValueDate.Text = deliveryDate.ToString("dd-MMM-yy", System.Globalization.CultureInfo.InvariantCulture);
                    Console.WriteLine($"[WPF] Updated Value Date: {lblHedgeValueDate.Text}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WPF] Expiry LostFocus error: {ex.Message}");
            }
        }

        /// <summary>
        /// Calculate expiry date from tenor string using FX calendar service
        /// </summary>
    private DateTime CalculateExpiryFromTenor(string tenor)
  {
        if (string.IsNullOrEmpty(tenor))
           return DateTime.Now.AddMonths(1);

        string currencyPair = _trade?.Underlying ?? "EURUSD";

        try
        {
            // Use FxDateService for proper calendar-aware date calculation
            var (tradeDate, spotDate, expiryDate, deliveryDate, premiumDate) = 
                FxDateService.ComputeDates(
                    DateTime.UtcNow,
                    currencyPair,
                    tenor.ToUpperInvariant(),
                    currencyPair.Substring(3, 3), // Premium currency (quote ccy)
                    new FxDateRules()
                );

            Console.WriteLine($"[WPF] Calculated expiry for {tenor} on {currencyPair}: {expiryDate:dd-MMM-yy (ddd)}");
            return expiryDate;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WPF] Error calculating expiry from FxDateService: {ex.Message}");
            
            // Fallback to simple calculation
            var match = System.Text.RegularExpressions.Regex.Match(tenor, @"(\d+)([mMwWyYdD])");
            if (!match.Success)
                return DateTime.Now.AddMonths(1);

            int amount = int.Parse(match.Groups[1].Value);
            string unit = match.Groups[2].Value.ToUpper();

            var result = unit switch
            {
                "D" => DateTime.Now.AddDays(amount),
                "W" => DateTime.Now.AddDays(amount * 7),
                "M" => DateTime.Now.AddMonths(amount),
                "Y" => DateTime.Now.AddYears(amount),
                _ => DateTime.Now.AddMonths(amount)
            };

            // Simple weekend adjustment
            while (result.DayOfWeek == DayOfWeek.Saturday || result.DayOfWeek == DayOfWeek.Sunday)
            {
                result = result.AddDays(1);
            }

            return result;
        }
  }

        /// <summary>
        /// Check if we have a valid currency pair that has been parsed or picked by the user.
        /// Returns true only when a 6-character currency pair exists from trade structure or UI.
        /// Works with all FX pairs: EURUSD, EURSEK, NOKSEK, USDSEK, etc.
        /// </summary>
        private bool HasValidCurrencyPair()
        {
            return GetValidCurrencyPair() != null;
        }

        /// <summary>
        /// Get the current valid currency pair from trade structure or UI.
        /// Returns null if no valid pair is available.
        /// Works with all FX pairs: EURUSD, EURSEK, NOKSEK, USDSEK, etc.
        /// </summary>
        private string GetValidCurrencyPair()
        {
            // Check trade structure first - this is the authoritative source after parsing
            string pair = _trade?.Underlying;
            
            // Validate trade structure pair
            if (!string.IsNullOrEmpty(pair) && pair.Length >= 6)
            {
                // Valid pair from trade structure
                return pair.Substring(0, 6).ToUpperInvariant();
            }
            
            // Check UI field as fallback
            pair = txtCurrencyPair?.Text?.Trim();
            
            // Validate: must be at least 6 characters and only letters
            if (string.IsNullOrEmpty(pair) || pair.Length < 6)
                return null;
            
            // Check it's a valid currency pair format (6 uppercase letters like EURUSD, EURSEK, NOKSEK)
            string upperPair = pair.ToUpperInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(upperPair.Substring(0, 6), @"^[A-Z]{6}$"))
                return null;
            
            // Return first 6 characters (the currency pair)
            return upperPair.Substring(0, 6);
        }

        private async Task<double> GetBloombergSpotAsync(string currencyPair)
        {
            // Skip Bloomberg call if currency pair is invalid or not yet set
            if (string.IsNullOrEmpty(currencyPair) || currencyPair.Length < 6)
            {
                Console.WriteLine($"[WPF] Skipping Bloomberg spot fetch - no valid currency pair");
                return 0;
            }

            try
            {
                if (_bloombergService != null)
                {
                    var spot = await Task.Run(() => _bloombergService.GetSpotRate(currencyPair));
                    if (spot.HasValue && spot.Value > 0)
                    {
                        Console.WriteLine($"[WPF] Bloomberg spot for {currencyPair}: {spot.Value:F4}");
                        return spot.Value;
                    }
                    else
                    {
                        Console.WriteLine($"[WPF] Bloomberg returned no spot rate for {currencyPair}");
                    }
                }
                else
                {
                    Console.WriteLine($"[WPF] Bloomberg service not available for {currencyPair}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WPF] Error getting Bloomberg spot for {currencyPair}: {ex.Message}");
            }

            // No fallback - return 0 to indicate no rate available
            Console.WriteLine($"[WPF] WARNING: No live spot rate available for {currencyPair} - Bloomberg required");
            return 0;
        }

        /// <summary>
        /// Get the number of decimal places for a currency pair's strike price.
        /// JPY pairs use 2 decimals, most others use 4 decimals.
        /// </summary>
        private int GetStrikeDecimalPlaces(string currencyPair)
        {
            if (string.IsNullOrEmpty(currencyPair) || currencyPair.Length < 6)
                return 4;

            // JPY, HUF, KRW pairs typically use 2 decimal places
            string quoteCurrency = currencyPair.Substring(3, 3).ToUpperInvariant();
            return quoteCurrency switch
            {
                "JPY" => 2,
                "HUF" => 2,
                "KRW" => 2,
                "CLP" => 2,
                "ISK" => 2,
                _ => 4
            };
        }

        /// <summary>
        /// Format strike price according to currency pair conventions.
        /// </summary>
        private string FormatStrike(double strike, string currencyPair)
        {
            int decimals = GetStrikeDecimalPlaces(currencyPair);
            return strike.ToString($"F{decimals}");
        }

      #endregion

        #region Option Details Event Handlers

        /// <summary>
        /// Handle strike text field gaining focus - select all text for easy replacement
        /// </summary>
        private void txtStrike_GotFocus(object sender, RoutedEventArgs e)
        {
            if (txtStrike != null && !string.IsNullOrEmpty(txtStrike.Text))
            {
                // Use Dispatcher to select all text AFTER the default focus behavior completes
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    txtStrike.SelectAll();
                }));
            }
        }

        /// <summary>
        /// Handle strike text field losing focus - auto-format to correct decimal places
        /// E.g., "1.18" -> "1.1800" for EURUSD (4 decimals), "150.5" -> "150.50" for USDJPY (2 decimals)
        /// Also auto-defaults Call/Put based on comparing strike with Bloomberg spot rate.
        /// </summary>
        private async void txtStrike_LostFocus(object sender, RoutedEventArgs e)
        {
            if (txtStrike == null || string.IsNullOrWhiteSpace(txtStrike.Text))
                return;

            try
            {
                string input = txtStrike.Text.Trim();
                
                // Normalize decimal separator (handle both comma and period)
                input = input.Replace(",", ".");
                
                // Try to parse the strike value
                if (double.TryParse(input, System.Globalization.NumberStyles.Float, 
                    System.Globalization.CultureInfo.InvariantCulture, out double strikeValue))
                {
                    string currencyPair = _trade?.Underlying ?? txtCurrencyPair?.Text ?? "EURUSD";
                    
                    // Format with correct decimal places for currency pair
                    // This pads with zeros: 1.18 -> 1.1800 for EURUSD (4 decimals)
                    string formattedStrike = FormatStrike(strikeValue, currencyPair);
                    txtStrike.Text = formattedStrike;
                    
                    // Update trade structure if available
                    if (_trade?.Legs != null && _trade.Legs.Count > 0)
                    {
                        _trade.Legs[0].Strike = strikeValue;
                    }
                    
                    Console.WriteLine($"[WPF] Strike formatted: {input} → {formattedStrike} (for {currencyPair})");
                    
                    // Auto-default Call/Put based on strike vs Bloomberg spot rate
                    await UpdateOptionTypeFromStrikeVsSpotAsync(strikeValue, currencyPair);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WPF] Error formatting strike: {ex.Message}");
            }
        }

        /// <summary>
        /// Auto-default the option type (Call/Put) based on comparing strike with Bloomberg spot rate.
        /// If strike > spot → CALL (out-of-the-money call)
        /// If strike < spot → PUT (out-of-the-money put)
        /// </summary>
        private async Task UpdateOptionTypeFromStrikeVsSpotAsync(double strike, string currencyPair)
        {
            try
            {
                // Get Bloomberg spot rate
                double spotRate = _trade?.SpotReference ?? 0;
                
                // Fetch live rate if we don't have one
                if (spotRate <= 0)
                {
                    spotRate = await GetBloombergSpotAsync(currencyPair);
                    if (_trade != null && spotRate > 0)
                    {
                        _trade.SpotReference = spotRate;
                    }
                }
                
                if (spotRate <= 0)
                {
                    Console.WriteLine($"[WPF] Cannot auto-default Call/Put - no spot rate available");
                    return;
                }
                
                // Determine option type based on strike vs spot
                // Strike above spot = CALL (OTM call), Strike below spot = PUT (OTM put)
                string newOptionType = strike > spotRate ? "CALL" : "PUT";
                
                // Update trade structure
                if (_trade?.Legs != null && _trade.Legs.Count > 0)
                {
                    string oldOptionType = _trade.Legs[0].OptionType;
                    _trade.Legs[0].OptionType = newOptionType;
                    
                    Console.WriteLine($"[WPF] Auto-defaulted option type: Strike={strike:F4} vs Spot={spotRate:F4} → {newOptionType} (was {oldOptionType})");
                }
                
                // Update UI
                string ccy1 = currencyPair.Length >= 6 ? currencyPair.Substring(0, 3) : "EUR";
                string ccy2 = currencyPair.Length >= 6 ? currencyPair.Substring(3, 3) : "USD";
                
                if (txtCallPut != null)
                {
                    txtCallPut.Text = newOptionType == "PUT"
                        ? $"{ccy1} Put / {ccy2} Call"
                        : $"{ccy1} Call / {ccy2} Put";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WPF] Error auto-defaulting option type: {ex.Message}");
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

        private void LadderHeader_Click(object sender, MouseButtonEventArgs e)
        {
    if (ladderContent == null || lblLadderArrow == null)
        return;

  bool isCollapsed = ladderContent.Visibility == Visibility.Collapsed;
    ladderContent.Visibility = isCollapsed ? Visibility.Visible : Visibility.Collapsed;
lblLadderArrow.Text = isCollapsed ? "▼" : "▶";
}

private void MarketDataHeader_Click(object sender, MouseButtonEventArgs e)
{
 // This method is no longer needed - replaced by MarketRiskHeader_Click
}

private void RiskHeader_Click(object sender, MouseButtonEventArgs e)
{
    // This method is no longer needed - replaced by MarketRiskHeader_Click
}

private void MarketRiskHeader_Click(object sender, MouseButtonEventArgs e)
{
    if (marketRiskContent == null || lblMarketRiskArrow == null)
        return;

    bool isCollapsed = marketRiskContent.Visibility == Visibility.Collapsed;
    marketRiskContent.Visibility = isCollapsed ? Visibility.Visible : Visibility.Collapsed;
    lblMarketRiskArrow.Text = isCollapsed ? "▼" : "▶";
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

            // Get the initial LP panel to read checkbox states
            if (FindName("initialLPPanel") is StackPanel initialPanel)
{
          foreach (var child in initialPanel.Children)
         {
 if (child is CheckBox chk && chk.IsChecked == true)
          {
  // Extract LP name from checkbox Tag or Content
            string lpName = chk.Tag?.ToString() ?? chk.Content?.ToString()?.ToUpper();
     if (!string.IsNullOrEmpty(lpName))
         {
   // Map display names to LP codes
lpName = lpName switch
  {
       "MORGAN STANLEY" or "MORGANSTANLEY" => "MS",
         "SOCIETE GENERALE" or "SOCIETEGENERALE" => "SOCGEN",
          "NATWEST MARKETS" or "NATWESTMARKETS" => "NATWEST",
    "STANDARD CHARTERED" or "STANDARDCHARTERED" => "SCBL",
             "BANK OF AMERICA" or "BANKOFAMERICA" => "BAML",
      "BNP PARIBAS" or "BNPPARIBAS" => "BNP",
  _ => lpName.ToUpper().Replace(" ", "")
         };
    selectedLPs.Add(lpName);
      }
  }
              }
            }

       // Fallback: Check named checkboxes if panel search didn't find any
       if (selectedLPs.Count == 0)
        {
       if (FindName("chkMS") is CheckBox chkMS && chkMS.IsChecked == true) selectedLPs.Add("MS");
      if (FindName("chkHSBC") is CheckBox chkHSBC && chkHSBC.IsChecked == true) selectedLPs.Add("HSBC");
                if (FindName("chkBNP") is CheckBox chkBNP && chkBNP.IsChecked == true) selectedLPs.Add("BNP");
  if (FindName("chkNATWEST") is CheckBox chkNATWEST && chkNATWEST.IsChecked == true) selectedLPs.Add("NATWEST");
       if (FindName("chkSOCGEN") is CheckBox chkSOCGEN && chkSOCGEN.IsChecked == true) selectedLPs.Add("SOCGEN");
        if (FindName("chkCIBC") is CheckBox chkCIBC && chkCIBC.IsChecked == true) selectedLPs.Add("CIBC");
     if (FindName("chkSCBL") is CheckBox chkSCBL && chkSCBL.IsChecked == true) selectedLPs.Add("SCBL");
    if (FindName("chkNOMURA") is CheckBox chkNOMURA && chkNOMURA.IsChecked == true) selectedLPs.Add("NOMURA");
        if (FindName("chkBAML") is CheckBox chkBAML && chkBAML.IsChecked == true) selectedLPs.Add("BAML");
            }

Console.WriteLine($"[WPF] Selected LPs: {string.Join(", ", selectedLPs)}");
       return selectedLPs;
        }

        /// <summary>
        /// Execute trade against the best quote for the given side (BID or OFFER)
    /// </summary>
  private void ExecuteTrade(string side)
     {
      if (!_isRfqActive || _quotesByLP.IsEmpty)
       {
 MessageBox.Show("No active quotes available for execution.", "Cannot Execute",
MessageBoxButton.OK, MessageBoxImage.Warning);
    return;
            }

      try
       {
            // Find the best quote for the requested side
   LPQuoteData bestQuote = null;
    FIXMessage bestQuoteMessage = null;

    if (side == "BID")
         {
       // User is selling - find best bid (highest vol)
         bestQuote = _quotesByLP.Values
 .Where(q => q.BidVol > 0 && !string.IsNullOrEmpty(q.BidQuoteId))
.OrderByDescending(q => q.BidVol)
       .FirstOrDefault();

    if (bestQuote != null && _quotesByQuoteId.TryGetValue(bestQuote.BidQuoteId, out var msg))
               {
    bestQuoteMessage = msg;
       }
      }
   else // OFFER
 {
    // User is buying - find best offer (lowest vol)
      bestQuote = _quotesByLP.Values
   .Where(q => q.OfferVol > 0 && !string.IsNullOrEmpty(q.OfferQuoteId))
      .OrderBy(q => q.OfferVol)
     .FirstOrDefault();

        if (bestQuote != null && _quotesByQuoteId.TryGetValue(bestQuote.OfferQuoteId, out var msg))
     {
        bestQuoteMessage = msg;
   }
  }

 if (bestQuote == null || bestQuoteMessage == null)
{
     MessageBox.Show($"No valid {side} quotes available.", "Cannot Execute",
        MessageBoxButton.OK, MessageBoxImage.Warning);
  return;
     }

         // Check quote validity
if (bestQuote.ValidUntilTime < DateTime.Now)
 {
         MessageBox.Show($"Quote from {bestQuote.LP} has expired.", "Quote Expired",
    MessageBoxButton.OK, MessageBoxImage.Warning);
          return;
    }

       // Send execution to FIX session
      string executionSide = side == "BID" ? "SELL" : "BUY";
     string clOrdID = _fixSession?.SendExecution(bestQuoteMessage, executionSide, _trade);

     if (!string.IsNullOrEmpty(clOrdID))
      {
 Console.WriteLine($"[WPF] Execution sent: ClOrdID={clOrdID}, Side={executionSide}, LP={bestQuote.LP}");

         // Create deal card entry using existing DealViewModel properties
         double vol = side == "BID" ? bestQuote.BidVol : bestQuote.OfferVol;
         double premium = side == "BID" ? bestQuote.BidPremium : bestQuote.OfferPremium;
         double legPremPrice = side == "BID" ? bestQuote.BidLegPremPrice : bestQuote.OfferLegPremPrice;  // Tag 5844: pips
         string optionType = _trade?.Legs?[0]?.OptionType ?? "CALL";
         
         // Get currency pair and premium currency
         string pair = _trade?.Underlying ?? "EURUSD";
         string premiumCcy = pair.Length >= 6 ? pair.Substring(0, 3) : "EUR";  // Premium typically in base currency
         
         // Get hedge details from the Delta Exchange dropdown
         string hedgeType = "No Hedge";
         string hedgeSide = "";
         string hedgeAmount = "";
         string hedgeRate = "";
         string hedgeValueDate = "";
         string deltaDisplay = "";
         
         // Check if user selected a hedge type (1 = Spot Hedge, 2 = Forward Hedge)
         bool hasHedgeSelected = cmbDeltaExchange != null && cmbDeltaExchange.SelectedIndex > 0;
         
         if (hasHedgeSelected)
         {
             // Get hedge type from dropdown
             hedgeType = cmbDeltaExchange.SelectedIndex == 1 ? "Spot Hedge" : "Forward Hedge";
             
             // Delta from quote (bestOffer for buy, bestBid for sell)
             double? delta = side == "OFFER" ? bestQuote.Delta : bestQuote.Delta;
             
             // Get notional for calculations
             double notionalMM = _trade?.Legs?[0]?.NotionalMM ?? 10.0;
             string hedgeCcy = pair.Length >= 6 ? pair.Substring(0, 3) : "EUR";
             
             // Try to get delta from quote first, then fall back to UI
             double deltaPct = 0;
             if (delta.HasValue && delta.Value != 0)
             {
                 deltaPct = delta.Value;
             }
             else if (lblDeltaPct != null && !string.IsNullOrEmpty(lblDeltaPct.Text) && lblDeltaPct.Text != "--")
             {
                 // Try to parse delta from UI (format: "52.5%")
                 string deltaText = lblDeltaPct.Text.Replace("%", "").Trim();
                 double.TryParse(deltaText, System.Globalization.NumberStyles.Float, 
                     System.Globalization.CultureInfo.InvariantCulture, out deltaPct);
             }
             
             if (deltaPct != 0)
             {
                 deltaDisplay = $"{deltaPct:F1}%";
                 
                 // Calculate hedge amount: Delta% * Notional
                 double hedgeAmountValue = (deltaPct / 100.0) * notionalMM * 1_000_000;
                 
                 // Hedge side is opposite of option direction for calls, same for puts
                 // If buying a call, you sell delta; if selling a call, you buy delta
                 bool isCall = optionType == "CALL";
                 bool isBuying = executionSide == "BUY";
                 hedgeSide = (isCall == isBuying) ? "SELL" : "BUY";
                 
                 hedgeAmount = $"{hedgeAmountValue:N0} {hedgeCcy}";
             }
             else
             {
                 // Even without delta value, show the hedge type selected
                 deltaDisplay = "--";
                 hedgeSide = "--";
                 hedgeAmount = "--";
             }
             
             // Hedge rate from spot or forward
             if (!string.IsNullOrEmpty(bestQuote.SpotRate))
             {
                 if (hedgeType == "Forward Hedge" && bestQuote.ForwardPoints != 0)
                 {
                     // Calculate forward rate
                     if (double.TryParse(bestQuote.SpotRate, out double spot))
                     {
                         double divisor = pair.Contains("JPY") ? 100.0 : 10000.0;
                         double fwdRate = spot + (bestQuote.ForwardPoints / divisor);
                         hedgeRate = fwdRate.ToString("F4");
                     }
                     else
                     {
                         hedgeRate = bestQuote.SpotRate;
                     }
                 }
                 else
                 {
                     hedgeRate = bestQuote.SpotRate;
                 }
             }
             else
             {
                 // Fallback to UI spot rate
                 hedgeRate = lblSpotRate?.Text ?? "--";
             }
             
             // Value date from trade delivery date
             if (_trade?.Legs?[0]?.DeliveryDate != default && _trade.Legs[0].DeliveryDate > DateTime.Now)
             {
                 hedgeValueDate = _trade.Legs[0].DeliveryDate.ToString("dd-MMM-yy");
             }
             else if (lblHedgeValueDate != null && !string.IsNullOrEmpty(lblHedgeValueDate.Text) && lblHedgeValueDate.Text != "--")
             {
                 hedgeValueDate = lblHedgeValueDate.Text;
             }
             else
             {
                 hedgeValueDate = "--";
             }
             
             Console.WriteLine($"[WPF] Hedge details: Type={hedgeType}, Side={hedgeSide}, Delta={deltaDisplay}, Amount={hedgeAmount}, Rate={hedgeRate}, ValueDate={hedgeValueDate}");
         }
         
     var deal = new DealViewModel
   {
      OrderId = clOrdID,
      Time = DateTime.Now.ToString("HH:mm:ss"),
    LP = bestQuote.LP,
      Side = executionSide,
      SideColor = executionSide == "BUY" 
 ? new SolidColorBrush(Color.FromRgb(34, 197, 94))   // Green for buy
          : new SolidColorBrush(Color.FromRgb(239, 68, 68)),  // Red for sell
     Instrument = $"{_trade?.Underlying ?? "EURUSD"} {optionType}",
     Strike = FormatStrike(_trade?.Legs?[0]?.Strike ?? 0, _trade?.Underlying ?? "EURUSD"),
Notional = $"{_trade?.Legs?[0]?.NotionalMM ?? 0:F1}MM",
     Volatility = vol.ToString("F2",
 System.Globalization.CultureInfo.InvariantCulture),
     EurPips = legPremPrice != 0 ? legPremPrice.ToString("F2") : "--",  // Tag 5844: premium per MM (pips)
     PremiumLabel = executionSide == "BUY" ? "PAY" : "RCV",  // Buy = pay premium, Sell = receive premium
     PremiumDisplay = $"{premiumCcy} {Math.Abs(premium):N0} U",  // Format: "EUR 54 800 U" showing currency
     PremiumColor = executionSide == "BUY" 
         ? Brushes.White  // White for pay
         : new SolidColorBrush(Color.FromRgb(34, 197, 94)),  // Green for receive
     SpotRate = bestQuote.SpotRate ?? "--",
     ExpiryDate = _trade?.Legs?[0]?.ExpiryDate.ToString("dd-MMM-yy") ?? "--",
     ExpiryCut = _trade?.Legs?[0]?.Cutoff ?? "NY",
   Status = "PENDING",
          StatusBackground = new SolidColorBrush(Color.FromRgb(245, 158, 11)), // Amber for pending
       StatusForeground = new SolidColorBrush(Colors.White),
       
       // Delta hedge details
       HedgeType = hedgeType,
       HedgeSide = hedgeSide,
       HedgeAmount = hedgeAmount,
       HedgeRate = hedgeRate,
       HedgeValueDate = hedgeValueDate,
       Delta = deltaDisplay
           };

       Deals.Insert(0, deal);

         // Speak "Pending" to indicate order sent
         try
        {
 _speechSynthesizer?.SpeakAsync("Pending");
         }
 catch (Exception ex)
 {
      Console.WriteLine($"[WPF] Error speaking pending: {ex.Message}");
            }
         }
   else
   {
         MessageBox.Show("Failed to send execution order.", "Execution Failed",
     MessageBoxButton.OK, MessageBoxImage.Error);
     }
            }
catch (Exception ex)
         {
  Console.WriteLine($"[WPF] Error executing trade: {ex.Message}");
      MessageBox.Show($"Error executing trade: {ex.Message}", "Execution Error",
   MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Mouse Hover Event Handlers (UI visual feedback)

        private void Tile_MouseEnter(object sender, MouseEventArgs e)
        {
            // Add hover visual feedback for tiles - brighten background
            if (sender is Border border)
            {
                // Store original background for restoration
                border.Tag = border.Background;
                
                // Create a brighter hover effect
                if (_isRfqActive)
                {
                    // When quoting, use a subtle highlight
                    border.Background = new SolidColorBrush(Color.FromRgb(30, 35, 45));
                }
                else
                {
                    // When in RFQ state, show clickable highlight
                    border.Background = new SolidColorBrush(Color.FromRgb(25, 30, 40));
                }
                
                // Add subtle border glow
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246)); // Blue highlight
            }
        }

        private void Tile_MouseLeave(object sender, MouseEventArgs e)
        {
            // Remove hover visual feedback for tiles - restore original
            if (sender is Border border)
            {
                // Restore original background from Tag
                if (border.Tag is System.Windows.Media.Brush originalBrush)
                {
                    border.Background = originalBrush;
                }
                else
                {
                    // Fallback to gradient from resources
                    border.Background = (System.Windows.Media.Brush)FindResource("Brush.TileGradient");
                }
                
                // Restore border color
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)); // #1e293b
            }
        }

        private void LPName_MouseEnter(object sender, MouseEventArgs e)
        {
            // Optional: Add hover visual feedback for LP names
        }

        private void LPName_MouseLeave(object sender, MouseEventArgs e)
        {
            // Optional: Remove hover visual feedback for LP names
        }

        private void HeroValue_MouseEnter(object sender, MouseEventArgs e)
        {
            // Add hover visual feedback for hero value display - scale up slightly
            if (sender is System.Windows.Controls.TextBlock textBlock)
            {
                // Make text slightly brighter on hover
                if (textBlock.Foreground is SolidColorBrush brush && brush.Color != Colors.White)
                {
                    textBlock.Tag = textBlock.Foreground;
                    textBlock.Foreground = Brushes.White;
                }
                
                // Apply subtle scale transform for "pop" effect
                textBlock.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                textBlock.RenderTransform = new ScaleTransform(1.02, 1.02);
            }
        }

        private void HeroValue_MouseLeave(object sender, MouseEventArgs e)
        {
            // Remove hover visual feedback for hero value display
            if (sender is System.Windows.Controls.TextBlock textBlock)
            {
                // Restore original foreground
                if (textBlock.Tag is System.Windows.Media.Brush originalBrush)
                {
                    textBlock.Foreground = originalBrush;
                    textBlock.Tag = null;
                }
                
                // Remove scale transform
                textBlock.RenderTransform = null;
            }
        }

        private void LPCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            // Optional: Handle LP checkbox state changes
            // Could update selected LP count or validate selections
        }

        private void DealCard_Click(object sender, MouseButtonEventArgs e)
        {
            // Handle deal card click - toggle expansion or show details
            if (sender is FrameworkElement element && element.DataContext is DealViewModel deal)
            {
                deal.IsExpanded = !deal.IsExpanded;
            }
        }

        private void AddLeg_Click(object sender, MouseButtonEventArgs e)
        {
            // Handle add leg button click - add a new option leg to the trade
            if (_trade == null)
            {
                _trade = new TradeStructure
                {
                    Underlying = "EURUSD",
                    StructureType = "Vanilla",
                    Legs = new System.Collections.Generic.List<TradeStructure.OptionLeg>()
                };
            }

            // Add a new default leg
            var newLeg = new TradeStructure.OptionLeg
            {
                Direction = "BUY",
                OptionType = "CALL",
                Strike = 1.10,
                NotionalMM = 10.0,
                Tenor = "1M",
                ExpiryDate = DateTime.Now.AddMonths(1),
                DeliveryDate = DateTime.Now.AddMonths(1).AddDays(2),
                NotionalCurrency = _trade.Underlying?.Substring(0, 3) ?? "EUR"
            };

            _trade.Legs.Add(newLeg);
            Console.WriteLine($"[WPF] Added new leg - Total legs: {_trade.Legs.Count}");
        }

        private void CopyDealDetails_Click(object sender, RoutedEventArgs e)
        {
            // Copy deal details to clipboard
            if (sender is FrameworkElement element && element.DataContext is DealViewModel deal)
            {
                string details = $"Order: {deal.OrderId}\n" +
                    $"Time: {deal.Time}\n" +
                    $"LP: {deal.LP}\n" +
                    $"Side: {deal.Side}\n" +
                    $"Instrument: {deal.Instrument}\n" +
                    $"Strike: {deal.Strike}\n" +
                    $"Notional: {deal.Notional}\n" +
                    $"Volatility: {deal.Volatility}\n" +
                    $"Premium: {deal.PremiumLabel} {deal.PremiumDisplay}\n" +
                    $"Status: {deal.Status}";

                try
                {
                    System.Windows.Clipboard.SetText(details);
                    Console.WriteLine($"[WPF] Deal details copied to clipboard: {deal.OrderId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WPF] Error copying to clipboard: {ex.Message}");
                }
            }
        }

        #endregion

        /// <summary>
        /// Handle window loaded event - initialize default UI state
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Initialize hedge details panel to show spot rate by default
                InitializeHedgeDetailsPanel();
                
                Console.WriteLine("[WPF] Window loaded - hedge details panel initialized");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WPF] Error in Window_Loaded: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize hedge details panel - visible with placeholder text until a trade is parsed.
        /// Fields show "--" until a valid currency pair is selected and Bloomberg spot is fetched.
        /// </summary>
        private void InitializeHedgeDetailsPanel()
        {
            try
            {
                // Show hedge details panel with placeholder values
                if (hedgeDetailsPanel != null)
                {
                    // Only show if Delta Exchange dropdown is not set to "No Hedge"
                    if (cmbDeltaExchange == null || cmbDeltaExchange.SelectedIndex != 1)
                    {
                        hedgeDetailsPanel.Visibility = Visibility.Visible;
                    }
                }
                
                // Set placeholder values - will be replaced when trade is parsed
                var hedgeRateLabel = FindName("lblHedgeRate") as System.Windows.Controls.TextBlock;
                if (hedgeRateLabel != null)
                {
                    hedgeRateLabel.Text = "Spot: --";
                }
                
                if (lblHedgeValueDate != null)
                {
                    lblHedgeValueDate.Text = "--";
                }
                
                if (lblSpotRate != null)
                {
                    lblSpotRate.Text = "--";
                }
                
                Console.WriteLine("[WPF] Hedge details panel initialized with placeholder values - waiting for currency pair");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WPF] Error initializing hedge details panel: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle delta exchange dropdown change - update hedge details panel visibility and rate display.
        /// Only fetches Bloomberg spot if a valid currency pair has been parsed or picked.
        /// </summary>
        private async void DeltaExchange_Changed(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (cmbDeltaExchange == null || hedgeDetailsPanel == null)
                    return;

                int selectedIndex = cmbDeltaExchange.SelectedIndex;

                // Find the hedge rate label
                var hedgeRateLabel = FindName("lblHedgeRate") as System.Windows.Controls.TextBlock;
                
                // Get currency pair - only if valid
                string currencyPair = GetValidCurrencyPair();
                int decimals = (currencyPair?.Contains("JPY") == true) ? 2 : 4;

                // Update hedge details panel visibility and value date based on selection
                // 0 = Spot, 1 = No Hedge (Live), 2 = Forward Hedge
                if (selectedIndex == 1) // No Hedge (Live)
                {
                    hedgeDetailsPanel.Visibility = Visibility.Collapsed;
                    if (_trade != null)
                    {
                        _trade.HedgeType = "NONE";
                    }
                }
                else
                {
                    hedgeDetailsPanel.Visibility = Visibility.Visible;
                    
                    if (selectedIndex == 0) // Spot
                    {
                        if (_trade != null)
                        {
                            _trade.HedgeType = "SPOT";
                        }
                        
                        // Only fetch Bloomberg spot if we have a valid currency pair
                        double spotRate = 0;
                        if (!string.IsNullOrEmpty(currencyPair))
                        {
                            // Get spot rate - either from trade or fetch from Bloomberg
                            spotRate = _trade?.SpotReference ?? 0;
                            if (spotRate <= 0)
                            {
                                spotRate = await GetBloombergSpotAsync(currencyPair);
                                if (_trade != null && spotRate > 0)
                                {
                                    _trade.SpotReference = spotRate;
                                }
                            }
                        }
                        
                        // Show spot rate
                        if (hedgeRateLabel != null)
                        {
                            if (spotRate > 0)
                            {
                                hedgeRateLabel.Text = $"Spot: {spotRate.ToString($"F{decimals}")}";
                            }
                            else
                            {
                                hedgeRateLabel.Text = "Spot: --";
                            }
                        }
                        
                        // Calculate T+2 spot date
                        if (lblHedgeValueDate != null)
                        {
                            var spotDate = DateTime.Today.AddDays(2);
                            // Adjust for weekends
                            while (spotDate.DayOfWeek == DayOfWeek.Saturday || spotDate.DayOfWeek == DayOfWeek.Sunday)
                                spotDate = spotDate.AddDays(1);
                            lblHedgeValueDate.Text = spotDate.ToString("dd-MMM-yyyy");
                        }
                    }
                    else if (selectedIndex == 2) // Forward Hedge
                    {
                        if (_trade != null)
                        {
                            _trade.HedgeType = "FORWARD";
                        }
                        
                        // Only fetch Bloomberg spot if we have a valid currency pair
                        double spotRate = 0;
                        double forwardRate = 0;
                        
                        if (!string.IsNullOrEmpty(currencyPair))
                        {
                            // Get spot rate - from trade or fetch from Bloomberg
                            spotRate = _trade?.SpotReference ?? 0;
                            if (spotRate <= 0)
                            {
                                spotRate = await GetBloombergSpotAsync(currencyPair);
                                if (_trade != null && spotRate > 0)
                                {
                                    _trade.SpotReference = spotRate;
                                }
                            }
                            
                            forwardRate = _trade?.ForwardReference ?? 0;
                            
                            // If forward reference not set, use spot as approximation
                            if (forwardRate <= 0 && spotRate > 0)
                            {
                                forwardRate = spotRate;
                            }
                        }
                        
                        // Show forward rate
                        if (hedgeRateLabel != null)
                        {
                            if (forwardRate > 0)
                            {
                                hedgeRateLabel.Text = $"Fwd: {forwardRate.ToString($"F{decimals}")}";
                            }
                            else
                            {
                                hedgeRateLabel.Text = "Fwd: --";
                            }
                        }
                        
                        // Use delivery date from trade if available
                        if (lblHedgeValueDate != null)
                        {
                            if (_trade?.Legs?.Count > 0 && _trade.Legs[0].DeliveryDate != default)
                            {
                                lblHedgeValueDate.Text = _trade.Legs[0].DeliveryDate.ToString("dd-MMM-yyyy");
                            }
                            else
                            {
                                lblHedgeValueDate.Text = "--";
                            }
                        }
                    }
                }
                
                Console.WriteLine($"[WPF] Delta Exchange changed to index {selectedIndex}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WPF] Error in DeltaExchange_Changed: {ex.Message}");
            }
        }
    }
}
