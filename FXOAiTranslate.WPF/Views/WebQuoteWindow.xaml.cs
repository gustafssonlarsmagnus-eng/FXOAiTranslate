using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using FXOptionsSimulator;

namespace FXOAiTranslate.WPF.Views
{
    /// <summary>
    /// FX Aggregator window displaying HTML-based multi-LP quote interface
    /// Integrates with GFI FIX session for live quote streaming
    /// </summary>
    public partial class WebQuoteWindow : Window
    {
        private readonly TradeStructure _trade;
        private bool _isWebViewReady = false;

        public WebQuoteWindow(TradeStructure trade)
        {
            InitializeComponent();
            _trade = trade;
            InitializeWebView();
        }

        private async void InitializeWebView()
        {
            try
            {
                // Ensure the CoreWebView2 environment is initialized
                await webView.EnsureCoreWebView2Async();

                // Map a virtual host to the Documentation directory
                // This allows accessing files via https://app.local/ instead of file:///
                // which is more secure and avoids CORS issues.
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string documentationFolder = Path.Combine(baseDir, "Documentation");

                // Create Documentation folder if it doesn't exist
                if (!Directory.Exists(documentationFolder))
                {
                    Directory.CreateDirectory(documentationFolder);
                }

                webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app.local",
                    documentationFolder,
                    CoreWebView2HostResourceAccessKind.Allow
                );

                // Disable default context menus for a "native" feel
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                // Keep dev tools enabled for debugging
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

                // Set up message handler for JavaScript -> C# communication
                webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                // Navigate to the FX aggregator page via the virtual host
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

                // Subscribe to GFI FIX quote updates
                if (GlobalFIXSession.Instance != null)
                {
                    GlobalFIXSession.Instance.OnQuoteReceived += HandleQuoteReceived;

                    // Send initial quote request to GFI
                    try
                    {
                        GlobalFIXSession.Instance.SendQuoteRequest(_trade);
                        Console.WriteLine($"[WebQuoteWindow] Sent quote request for {_trade.Underlying}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WebQuoteWindow] Failed to send quote request: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("[WebQuoteWindow] WARNING: GlobalFIXSession is not initialized");
                }
            }
        }

        /// <summary>
        /// Handle quote received from GFI FIX session
        /// </summary>
        private void HandleQuoteReceived(QuoteData quote)
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
                        timestamp = quote.Timestamp.ToString("o")
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

                        case "cancel":
                            HandleCancelRequest(message);
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

                // Send order to GFI
                if (GlobalFIXSession.Instance != null)
                {
                    GlobalFIXSession.Instance.SendNewOrderMultileg(_trade, quoteId, side);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebQuoteWindow] Failed to execute trade: {ex.Message}");
                MessageBox.Show($"Failed to execute trade: {ex.Message}", "Execution Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Handle quote cancel request from JavaScript UI
        /// </summary>
        private void HandleCancelRequest(JsonElement message)
        {
            try
            {
                var quoteId = message.GetProperty("quoteId").GetString();

                Console.WriteLine($"[WebQuoteWindow] Cancel requested for QuoteID {quoteId}");

                if (GlobalFIXSession.Instance != null)
                {
                    GlobalFIXSession.Instance.CancelQuote(quoteId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebQuoteWindow] Failed to cancel quote: {ex.Message}");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from quote events when window closes
            if (GlobalFIXSession.Instance != null)
            {
                GlobalFIXSession.Instance.OnQuoteReceived -= HandleQuoteReceived;
            }

            base.OnClosed(e);
        }
    }
}
