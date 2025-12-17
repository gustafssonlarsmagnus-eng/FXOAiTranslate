using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using FXOptionsSimulator;
using FXOptionsSimulator.FIX;

namespace FXOAiTranslate.WPF.Views
{
    /// <summary>
    /// FX Aggregator window displaying HTML-based multi-LP quote interface
    /// Integrates with GFI FIX session for live quote streaming
    /// </summary>
    public partial class WebQuoteWindow : Window
    {
        private readonly TradeStructure _trade;
        private readonly string _groupId;
        private bool _isWebViewReady = false;
        private readonly Dictionary<string, FIXMessage> _quotesByQuoteId = new Dictionary<string, FIXMessage>();
        private readonly string[] _lps = { "DEUT", "JPM", "CITI", "BARX" };

        public WebQuoteWindow(TradeStructure trade)
        {
            InitializeComponent();
            _trade = trade;
            _groupId = $"WebAgg_{Guid.NewGuid():N}";
            InitializeWebView();
        }

        private async void InitializeWebView()
        {
            try
            {
                // Ensure the CoreWebView2 environment is initialized
                await webView.EnsureCoreWebView2Async();

                // Map a virtual host to the Documentation directory
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string documentationFolder = Path.Combine(baseDir, "Documentation");

                if (!Directory.Exists(documentationFolder))
                {
                    Directory.CreateDirectory(documentationFolder);
                }

                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app.local",
                    documentationFolder,
                    CoreWebView2HostResourceAccessKind.Allow
                );

                // Keep dev tools enabled for debugging
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                // Set up message handler for JavaScript -> C# communication
                webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                // Navigate to the FX aggregator page
                webView.CoreWebView2.Navigate("https://app.local/fx_aggregator_fixed_380.html");

                // Subscribe to quotes after page loads
                webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                _isWebViewReady = true;

                // Send trade details to JavaScript for UI initialization
                SendTradeDetailsToUI();

                // Subscribe to GFI FIX quote updates
                if (GlobalFIXSession.Instance != null)
                {
                    // Subscribe to quote received event (with FIXMessage)
                    GlobalFIXSession.Instance.Application.OnQuoteReceived += HandleQuoteReceivedWithFIXMessage;

                    // Also subscribe to QuoteData events for convenience
                    GlobalFIXSession.Instance.OnQuoteReceived += HandleQuoteDataReceived;

                    // Send quote requests to all LPs
                    SendQuoteRequestsToAllLPs();
                }
                else
                {
                    Console.WriteLine("[WebQuoteWindow] WARNING: GlobalFIXSession is not initialized");
                }
            }
        }

        /// <summary>
        /// Send trade details to JavaScript UI on initialization
        /// </summary>
        private void SendTradeDetailsToUI()
        {
            try
            {
                var message = new
                {
                    type = "tradeDetails",
                    underlying = _trade.Underlying,
                    expiry = _trade.Expiry.ToString("dd-MMM-yy"),
                    expiryISO = _trade.Expiry.ToString("yyyy-MM-dd"),
                    spot = _trade.SpotReference,
                    structureType = _trade.StructureType,
                    legs = _trade.Legs.Select(leg => new
                    {
                        direction = leg.Direction,
                        optionType = leg.OptionType,
                        strike = leg.Strike,
                        notionalMM = leg.NotionalMM
                    }).ToList()
                };

                var json = JsonSerializer.Serialize(message);
                webView.CoreWebView2.PostWebMessageAsJson(json);

                Console.WriteLine($"[WebQuoteWindow] Sent trade details to UI: {_trade.Underlying} {_trade.Legs.Count} leg(s)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebQuoteWindow] Failed to send trade details: {ex.Message}");
            }
        }

        /// <summary>
        /// Send quote requests to all configured LPs
        /// </summary>
        private void SendQuoteRequestsToAllLPs()
        {
            try
            {
                foreach (var lp in _lps)
                {
                    string quoteReqID = GlobalFIXSession.Instance.SendQuoteRequest(
                        _trade,
                        lp,
                        _groupId,
                        "Spot",  // hedgeType
                        "Spot"   // premiumType
                    );
                    Console.WriteLine($"[WebQuoteWindow] Sent quote request to {lp}: {quoteReqID}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebQuoteWindow] Failed to send quote requests: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle quote received with FIXMessage (needed for execution)
        /// </summary>
        private void HandleQuoteReceivedWithFIXMessage(string quoteReqID, FIXMessage fixMsg)
        {
            try
            {
                string quoteID = fixMsg.Get(QuickFix.Fields.Tags.QuoteID.ToString());

                // Store FIXMessage by QuoteID for later execution
                _quotesByQuoteId[quoteID] = fixMsg;

                Console.WriteLine($"[WebQuoteWindow] Stored quote: {quoteID}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebQuoteWindow] Error storing FIXMessage: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle quote received as QuoteData (for UI updates)
        /// </summary>
        private void HandleQuoteDataReceived(QuoteData quote)
        {
            if (!_isWebViewReady) return;

            // Send quote to JavaScript via WebView2 bridge
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var message = new
                    {
                        type = "quote",
                        lp = quote.LP,
                        bidPremium = quote.BidPremium,
                        offerPremium = quote.OfferPremium,
                        bidVol = quote.BidVol,
                        offerVol = quote.OfferVol,
                        quoteId = quote.QuoteID,
                        side = quote.Side,
                        underlying = quote.Underlying,
                        timestamp = quote.Timestamp.ToString("o"),
                        notional = quote.Notional,
                        delta = quote.Delta,
                        premiumCurrency = quote.PremiumCurrency,
                        validUntilTime = quote.ValidUntilTime,
                        spread = quote.Spread
                    };

                    var json = JsonSerializer.Serialize(message);
                    webView.CoreWebView2.PostWebMessageAsJson(json);

                    Console.WriteLine($"[WebQuoteWindow] Sent quote to UI: {quote.LP} {quote.Side} {quote.BidPremium}/{quote.OfferPremium}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WebQuoteWindow] Failed to send quote to JavaScript: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Handle messages from JavaScript (e.g., execution requests)
        /// </summary>
        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.WebMessageAsJson;
                var message = JsonSerializer.Deserialize<JsonElement>(json);

                if (message.TryGetProperty("type", out var typeElement))
                {
                    var messageType = typeElement.GetString();

                    switch (messageType)
                    {
                        case "execute":
                            HandleExecutionRequest(message);
                            break;

                        case "ready":
                            Console.WriteLine("[WebQuoteWindow] JavaScript UI is ready");
                            break;

                        default:
                            Console.WriteLine($"[WebQuoteWindow] Unknown message type: {messageType}");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebQuoteWindow] Error handling web message: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle execution request from JavaScript UI
        /// </summary>
        private void HandleExecutionRequest(JsonElement message)
        {
            try
            {
                var quoteId = message.GetProperty("quoteId").GetString();
                var side = message.GetProperty("side").GetString();

                Console.WriteLine($"[WebQuoteWindow] Execution requested: {side} on QuoteID {quoteId}");

                // Get the stored FIXMessage for this QuoteID
                if (!_quotesByQuoteId.TryGetValue(quoteId, out var fixMsg))
                {
                    Console.WriteLine($"[WebQuoteWindow] ERROR: Quote {quoteId} not found in cache");
                    MessageBox.Show($"Quote not found: {quoteId}", "Execution Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Send execution to GFI
                if (GlobalFIXSession.Instance != null)
                {
                    string clOrdID = GlobalFIXSession.Instance.SendExecution(fixMsg, side, _trade);
                    Console.WriteLine($"[WebQuoteWindow] Execution sent: ClOrdID={clOrdID}");
                    MessageBox.Show($"Execution sent!\nClOrdID: {clOrdID}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebQuoteWindow] Failed to execute trade: {ex.Message}");
                MessageBox.Show($"Failed to execute trade: {ex.Message}", "Execution Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from quote events when window closes
            if (GlobalFIXSession.Instance != null)
            {
                GlobalFIXSession.Instance.OnQuoteReceived -= HandleQuoteDataReceived;
                GlobalFIXSession.Instance.Application.OnQuoteReceived -= HandleQuoteReceivedWithFIXMessage;
            }

            base.OnClosed(e);
        }
    }
}
