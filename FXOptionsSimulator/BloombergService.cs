using Bloomberglp.Blpapi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FXOptionsSimulator
{
    public class BloombergService
    {
        private IntPtr _bloombergWindow = IntPtr.Zero;
        public bool IsConnected { get; private set; } = false;

        #region Streaming Subscription Support
        
        /// <summary>
        /// Event fired when a subscribed spot rate is updated.
        /// Parameters: currencyPair, spotRate, timestamp
        /// </summary>
        public event Action<string, double, DateTime> OnSpotRateUpdated;
        
        /// <summary>
        /// Event fired when a subscription encounters an error.
        /// Parameters: currencyPair, errorMessage
        /// </summary>
        public event Action<string, string> OnSubscriptionError;
        
        // Streaming session management
        private Session _streamingSession;
        private readonly ConcurrentDictionary<string, SubscriptionInfo> _activeSubscriptions = new();
        private Thread _eventProcessingThread;
        private volatile bool _isProcessingEvents;
        private readonly object _sessionLock = new object();
        
        /// <summary>
        /// Tracks information about each active subscription
        /// </summary>
        private class SubscriptionInfo
        {
            public string CurrencyPair { get; set; }
            public string BloombergTicker { get; set; }
            public CorrelationID CorrelationId { get; set; }
            public double? LastRate { get; set; }
            public DateTime? LastUpdate { get; set; }
        }
        
        /// <summary>
        /// Check if streaming subscriptions are active
        /// </summary>
        public bool IsStreamingActive => _isProcessingEvents && _streamingSession != null;
        
        /// <summary>
        /// Get count of active subscriptions
        /// </summary>
        public int ActiveSubscriptionCount => _activeSubscriptions.Count;
        
        #endregion

        public BloombergService()
        {
            TryConnect();
        }

        // Try to locate Bloomberg OVML window
        public void TryConnect()
        {
            _bloombergWindow = FindBloombergWindow();
            if (_bloombergWindow != IntPtr.Zero)
            {
                IsConnected = true;
                Console.WriteLine($"Found Bloomberg window: {GetWindowTitle(_bloombergWindow)}");
                Console.WriteLine("Bloomberg Terminal detected - Connected");
            }
            else
            {
                Console.WriteLine("Bloomberg Terminal not detected.");
            }
        }

        // Send OVML command to Bloomberg
        public void SendOVML(string ovmlCommand)
        {
            if (!IsConnected || _bloombergWindow == IntPtr.Zero)
            {
                Console.WriteLine("[Bloomberg API] Not connected to Bloomberg.");
                return;
            }

            try
            {
                // Re-verify Bloomberg window is still valid
                string currentTitle = GetWindowTitle(_bloombergWindow);
                if (currentTitle.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
                    currentTitle.Contains("Firefox", StringComparison.OrdinalIgnoreCase) ||
                    currentTitle.Contains("Edge", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[Bloomberg API] ERROR: Browser window detected, reconnecting...");
                    TryConnect();
                    if (!IsConnected) return;
                }

                Console.WriteLine($"DEBUG: Attempting to send to Bloomberg: '{ovmlCommand}'");
                Console.WriteLine($"DEBUG: Target window: '{currentTitle}'");

                // Copy command to clipboard
                System.Windows.Forms.Clipboard.SetText(ovmlCommand);

                // Activate Bloomberg window with retry
                for (int i = 0; i < 3; i++)
                {
                    SetForegroundWindow(_bloombergWindow);
                    Task.Delay(200).Wait();

                    // Verify window is in foreground
                    if (GetForegroundWindow() == _bloombergWindow)
                    {
                        break;
                    }
                }

                // Simulate CTRL+T (open new tab in Bloomberg)
                SendKeys.SendWait("^t");
                Task.Delay(150).Wait();

                // Paste command
                SendKeys.SendWait("^v");
                Task.Delay(100).Wait();

                // Press Enter
                SendKeys.SendWait("{ENTER}");

                Console.WriteLine("DEBUG: Commands sent to Bloomberg successfully");
                Console.WriteLine($"Sent to Bloomberg (new tab): {ovmlCommand}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bloomberg API] Failed to send command: {ex.Message}");
            }
        }

        // Fetch a spot rate from Bloomberg if possible
        public async Task<double?> GetSpotRate(string underlying)
        {
            try
            {
                Console.WriteLine($"[Bloomberg API] Requesting spot rate for {underlying}");

                // Try direct Bloomberg quote first - Bloomberg has most pairs including CNHSEK
                var bloombergTicker = GetBloombergTicker(underlying);
                Console.WriteLine($"[Bloomberg API] Using Bloomberg ticker: {bloombergTicker}");

                var directRate = await GetDirectSpotRate(underlying);
                if (directRate.HasValue)
                {
                    Console.WriteLine($"[Bloomberg API] Got direct spot rate for {underlying}: {directRate.Value:F4}");
                    return directRate;
                }

                // If direct quote failed and it's a CNH cross pair, try synthetic calculation as fallback
                if (IsCnhCrossPair(underlying))
                {
                    Console.WriteLine($"[Bloomberg API] Direct quote failed for {underlying}, trying synthetic calculation");
                    string ccy1 = underlying.Substring(0, 3).ToUpper();
                    string ccy2 = underlying.Substring(3, 3).ToUpper();
                    var crossRate = await GetCrossRate(ccy1, ccy2);
                    if (crossRate.HasValue)
                    {
                        Console.WriteLine($"[Bloomberg API] Synthetic {underlying} rate: {crossRate.Value:F6}");
                        return crossRate;
                    }
                }

                Console.WriteLine($"[Bloomberg API] No data returned for {underlying}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bloomberg API] Error fetching spot for {underlying}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the correct Bloomberg ticker for a currency pair.
        /// Bloomberg has direct quotes for most pairs including CNH crosses like CNHSEK.
        /// </summary>
        private string GetBloombergTicker(string underlying)
        {
            if (string.IsNullOrEmpty(underlying) || underlying.Length != 6)
                return $"{underlying} Curncy";

            // Standard format: "CNHSEK Curncy", "EURUSD Curncy", etc.
            return $"{underlying} Curncy";
        }

        /// <summary>
        /// Identifies CNH cross pairs that may need synthetic calculation.
        /// These pairs often don't have direct quotes in Bloomberg.
        /// </summary>
        private bool IsCnhCrossPair(string underlying)
        {
            if (string.IsNullOrEmpty(underlying) || underlying.Length != 6)
                return false;

            string ccy1 = underlying.Substring(0, 3).ToUpper();
            string ccy2 = underlying.Substring(3, 3).ToUpper();

            // CNH crosses with non-USD currencies typically need synthetic calculation
            // USDCNH is the primary liquid pair, others are derived
            bool hasCnh = ccy1 == "CNH" || ccy2 == "CNH";
            bool hasUsd = ccy1 == "USD" || ccy2 == "USD";

            return hasCnh && !hasUsd;
        }

        /// <summary>
        /// For cross pairs not directly available in Bloomberg, calculate from USD legs.
        /// CNH pairs are special because USDCNH is quoted as CNH per USD (like USDJPY).
        /// 
        /// Examples:
        /// - CNHSEK = USDSEK / USDCNH  (CNH per SEK = SEK per USD / CNH per USD)
        /// - CNHNOK = USDNOK / USDCNH
        /// - EURCNH = USDCNH / EURUSD  (CNH per EUR = CNH per USD / USD per EUR)
        /// </summary>
        public async Task<double?> GetCrossRate(string ccy1, string ccy2)
        {
            try
            {
                Console.WriteLine($"[Bloomberg API] Calculating cross rate for {ccy1}/{ccy2}");

                // Special handling for CNH pairs
                if (ccy1 == "CNH" || ccy2 == "CNH")
                {
                    return await GetCnhCrossRate(ccy1, ccy2);
                }

                // Get both USD rates using direct Bloomberg API call (not recursive GetSpotRate)
                double? usdCcy1 = await GetDirectSpotRate($"USD{ccy1}");
                double? usdCcy2 = await GetDirectSpotRate($"USD{ccy2}");

                // Try inverse if direct quote not available
                if (!usdCcy1.HasValue)
                {
                    var ccy1Usd = await GetDirectSpotRate($"{ccy1}USD");
                    if (ccy1Usd.HasValue && ccy1Usd.Value != 0)
                        usdCcy1 = 1.0 / ccy1Usd.Value;
                }

                if (!usdCcy2.HasValue)
                {
                    var ccy2Usd = await GetDirectSpotRate($"{ccy2}USD");
                    if (ccy2Usd.HasValue && ccy2Usd.Value != 0)
                        usdCcy2 = 1.0 / ccy2Usd.Value;
                }

                if (usdCcy1.HasValue && usdCcy2.HasValue && usdCcy1.Value != 0)
                {
                    // Cross rate: CCY1/CCY2 = (USD/CCY2) / (USD/CCY1)
                    double crossRate = usdCcy2.Value / usdCcy1.Value;
                    Console.WriteLine($"[Bloomberg API] Cross rate {ccy1}/{ccy2}: {crossRate:F6}");
                    return crossRate;
                }

                Console.WriteLine($"[Bloomberg API] Could not calculate cross rate for {ccy1}/{ccy2}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bloomberg API] Cross rate calculation error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Calculate CNH cross rates using USDCNH as the base.
        /// USDCNH is quoted as CNH per USD (similar to USDJPY convention).
        /// 
        /// For CNHSEK (how many SEK per CNH):
        ///   CNHSEK = USDSEK / USDCNH
        ///   If USDSEK = 10.50 and USDCNH = 7.25, then CNHSEK = 10.50 / 7.25 = 1.448
        ///   
        /// For EURCNH (how many CNH per EUR):
        ///   EURCNH = USDCNH / EURUSD (inverse)
        ///   If USDCNH = 7.25 and EURUSD = 1.08, then EURCNH = 7.25 / 1.08 = 6.713
        /// </summary>
        private async Task<double?> GetCnhCrossRate(string ccy1, string ccy2)
        {
            try
            {
                Console.WriteLine($"[Bloomberg API] Calculating CNH cross rate for {ccy1}{ccy2}");

                // Get USDCNH (primary CNH pair)
                double? usdCnh = await GetDirectSpotRate("USDCNH");
                if (!usdCnh.HasValue || usdCnh.Value == 0)
                {
                    Console.WriteLine("[Bloomberg API] Could not get USDCNH rate");
                    return null;
                }

                Console.WriteLine($"[Bloomberg API] USDCNH = {usdCnh.Value:F4}");

                // Determine the other currency
                string otherCcy = ccy1 == "CNH" ? ccy2 : ccy1;

                // Get USD rate for the other currency
                double? usdOther = await GetDirectSpotRate($"USD{otherCcy}");
                
                // Try inverse if direct quote not available
                if (!usdOther.HasValue)
                {
                    var otherUsd = await GetDirectSpotRate($"{otherCcy}USD");
                    if (otherUsd.HasValue && otherUsd.Value != 0)
                    {
                        usdOther = 1.0 / otherUsd.Value;
                    }
                }

                if (!usdOther.HasValue || usdOther.Value == 0)
                {
                    Console.WriteLine($"[Bloomberg API] Could not get USD{otherCcy} rate");
                    return null;
                }

                Console.WriteLine($"[Bloomberg API] USD{otherCcy} = {usdOther.Value:F4}");

                double crossRate;
                if (ccy1 == "CNH")
                {
                    // CNHXXX format: How many XXX per CNH
                    // CNHSEK = USDSEK / USDCNH
                    crossRate = usdOther.Value / usdCnh.Value;
                    Console.WriteLine($"[Bloomberg API] {ccy1}{ccy2} = USD{otherCcy} / USDCNH = {usdOther.Value:F4} / {usdCnh.Value:F4} = {crossRate:F6}");
                }
                else
                {
                    // XXXCNH format: How many CNH per XXX
                    // EURCNH = USDCNH / EURUSD (need to invert if we have XXXUSD)
                    crossRate = usdCnh.Value / usdOther.Value;
                    Console.WriteLine($"[Bloomberg API] {ccy1}{ccy2} = USDCNH / USD{otherCcy} = {usdCnh.Value:F4} / {usdOther.Value:F4} = {crossRate:F6}");
                }

                return crossRate;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bloomberg API] CNH cross rate calculation error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get spot rate directly from Bloomberg without cross-rate calculation fallback.
        /// Used internally to avoid recursion when calculating synthetic rates.
        /// </summary>
        private async Task<double?> GetDirectSpotRate(string underlying)
        {
            try
            {
                var bloombergTicker = $"{underlying} Curncy";

                using var session = new Session();
                if (!session.Start())
                {
                    return null;
                }

                if (!session.OpenService("//blp/refdata"))
                {
                    return null;
                }

                var service = session.GetService("//blp/refdata");
                var request = service.CreateRequest("ReferenceDataRequest");
                request.Append("securities", bloombergTicker);
                request.Append("fields", "PX_LAST");

                session.SendRequest(request, null);

                while (true)
                {
                    var evt = session.NextEvent(5000);
                    if (evt == null) break;

                    foreach (var msg in evt)
                    {
                        if (msg.HasElement("securityData"))
                        {
                            var securityDataArray = msg.GetElement("securityData");
                            for (int i = 0; i < securityDataArray.NumValues; i++)
                            {
                                var securityData = securityDataArray.GetValueAsElement(i);
                                if (securityData.HasElement("fieldData"))
                                {
                                    var fieldData = securityData.GetElement("fieldData");
                                    if (fieldData.HasElement("PX_LAST"))
                                    {
                                        return fieldData.GetElementAsFloat64("PX_LAST");
                                    }
                                }
                            }
                        }
                    }

                    if (evt.Type == Event.EventType.RESPONSE) break;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        #region Streaming Subscription Methods

        /// <summary>
        /// Subscribe to live spot rate updates for a currency pair.
        /// Updates will be delivered via the OnSpotRateUpdated event.
        /// </summary>
        /// <param name="currencyPair">Currency pair (e.g., "EURUSD")</param>
        /// <returns>True if subscription was successful</returns>
        public bool SubscribeToSpot(string currencyPair)
        {
            if (string.IsNullOrEmpty(currencyPair) || currencyPair.Length != 6)
            {
                Console.WriteLine($"[Bloomberg Stream] Invalid currency pair: {currencyPair}");
                return false;
            }

            string key = currencyPair.ToUpperInvariant();
            
            // Check if already subscribed
            if (_activeSubscriptions.ContainsKey(key))
            {
                Console.WriteLine($"[Bloomberg Stream] Already subscribed to {key}");
                return true;
            }

            try
            {
                // Ensure streaming session is running
                if (!EnsureStreamingSession())
                {
                    Console.WriteLine($"[Bloomberg Stream] Could not start streaming session");
                    return false;
                }

                string bloombergTicker = $"{key} Curncy";
                var correlationId = new CorrelationID(key);
                
                var subscriptionInfo = new SubscriptionInfo
                {
                    CurrencyPair = key,
                    BloombergTicker = bloombergTicker,
                    CorrelationId = correlationId
                };

                // Create subscription list
                // Create subscription
                var topics = new List<Subscription>
                {
                    new Subscription(bloombergTicker, "LAST_PRICE,BID,ASK", correlationId)
                };

                // Subscribe
                _streamingSession.Subscribe(topics);
                _activeSubscriptions[key] = subscriptionInfo;

                Console.WriteLine($"[Bloomberg Stream] ✓ Subscribed to {bloombergTicker}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bloomberg Stream] Subscription error for {currencyPair}: {ex.Message}");
                OnSubscriptionError?.Invoke(currencyPair, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Subscribe to multiple currency pairs at once.
        /// </summary>
        public bool SubscribeToSpots(IEnumerable<string> currencyPairs)
        {
            bool allSuccess = true;
            foreach (var pair in currencyPairs)
            {
                if (!SubscribeToSpot(pair))
                {
                    allSuccess = false;
                }
            }
            return allSuccess;
        }

        /// <summary>
        /// Unsubscribe from a specific currency pair.
        /// </summary>
        public void UnsubscribeFromSpot(string currencyPair)
        {
            if (string.IsNullOrEmpty(currencyPair))
                return;

            string key = currencyPair.ToUpperInvariant();
            
            if (_activeSubscriptions.TryRemove(key, out var subInfo))
            {
                try
                {
                    if (_streamingSession != null)
                    {
                        var topics = new List<Subscription>
                        {
                            new Subscription(subInfo.BloombergTicker, "", subInfo.CorrelationId)
                        };
                        _streamingSession.Unsubscribe(topics);
                    }
                    
                    Console.WriteLine($"[Bloomberg Stream] Unsubscribed from {key}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Bloomberg Stream] Unsubscribe error: {ex.Message}");
                }
            }
            
            // If no more subscriptions, stop the session
            if (_activeSubscriptions.IsEmpty)
            {
                StopStreamingSession();
            }
        }

        /// <summary>
        /// Unsubscribe from all active subscriptions.
        /// </summary>
        public void UnsubscribeAll()
        {
            foreach (var key in _activeSubscriptions.Keys.ToArray())
            {
                UnsubscribeFromSpot(key);
            }
            
            StopStreamingSession();
        }

        /// <summary>
        /// Get the last known rate for a subscribed currency pair.
        /// </summary>
        public double? GetCachedSpotRate(string currencyPair)
        {
            if (string.IsNullOrEmpty(currencyPair))
                return null;

            string key = currencyPair.ToUpperInvariant();
            
            if (_activeSubscriptions.TryGetValue(key, out var subInfo))
            {
                return subInfo.LastRate;
            }
            
            return null;
        }

        /// <summary>
        /// Ensure the streaming session is running.
        /// </summary>
        private bool EnsureStreamingSession()
        {
            lock (_sessionLock)
            {
                if (_streamingSession != null && _isProcessingEvents)
                {
                    return true;
                }

                try
                {
                    // Create session options for market data
                    var sessionOptions = new SessionOptions();
                    sessionOptions.ServerHost = "localhost";
                    sessionOptions.ServerPort = 8194; // Default Bloomberg API port
                    
                    _streamingSession = new Session(sessionOptions);
                    
                    if (!_streamingSession.Start())
                    {
                        Console.WriteLine("[Bloomberg Stream] Failed to start session");
                        _streamingSession = null;
                        return false;
                    }

                    if (!_streamingSession.OpenService("//blp/mktdata"))
                    {
                        Console.WriteLine("[Bloomberg Stream] Failed to open mktdata service");
                        _streamingSession.Stop();
                        _streamingSession = null;
                        return false;
                    }

                    // Start event processing thread
                    _isProcessingEvents = true;
                    _eventProcessingThread = new Thread(ProcessEvents)
                    {
                        IsBackground = true,
                        Name = "BloombergStreamProcessor"
                    };
                    _eventProcessingThread.Start();

                    Console.WriteLine("[Bloomberg Stream] ✓ Streaming session started");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Bloomberg Stream] Session start error: {ex.Message}");
                    _streamingSession = null;
                    return false;
                }
            }
        }

        /// <summary>
        /// Stop the streaming session.
        /// </summary>
        private void StopStreamingSession()
        {
            lock (_sessionLock)
            {
                _isProcessingEvents = false;
                
                if (_streamingSession != null)
                {
                    try
                    {
                        _streamingSession.Stop();
                    }
                    catch { }
                    
                    _streamingSession = null;
                }
                
                Console.WriteLine("[Bloomberg Stream] Session stopped");
            }
        }

        /// <summary>
        /// Background thread to process streaming events.
        /// </summary>
        private void ProcessEvents()
        {
            Console.WriteLine("[Bloomberg Stream] Event processing started");
            
            while (_isProcessingEvents && _streamingSession != null)
            {
                try
                {
                    var evt = _streamingSession.NextEvent(1000); // 1 second timeout
                    if (evt == null) continue;

                    foreach (Bloomberglp.Blpapi.Message msg in evt)
                    {
                        ProcessMessage(msg, evt.Type);
                    }
                }
                catch (Exception ex)
                {
                    if (_isProcessingEvents)
                    {
                        Console.WriteLine($"[Bloomberg Stream] Event processing error: {ex.Message}");
                    }
                }
            }
            
            Console.WriteLine("[Bloomberg Stream] Event processing stopped");
        }

        /// <summary>
        /// Process a single Bloomberg message.
        /// </summary>
        private void ProcessMessage(Bloomberglp.Blpapi.Message msg, Event.EventType eventType)
        {
            try
            {
                var correlationId = msg.CorrelationID;
                if (correlationId == null) return;

                string key = correlationId.Object?.ToString();
                if (string.IsNullOrEmpty(key)) return;

                if (!_activeSubscriptions.TryGetValue(key, out var subInfo))
                    return;

                // Handle subscription status messages
                if (eventType == Event.EventType.SUBSCRIPTION_STATUS)
                {
                    if (msg.HasElement("reason"))
                    {
                        var reason = msg.GetElement("reason");
                        string category = reason.HasElement("category") ? reason.GetElementAsString("category") : "";
                        string desc = reason.HasElement("description") ? reason.GetElementAsString("description") : "";
                        
                        Console.WriteLine($"[Bloomberg Stream] {key} status: {category} - {desc}");
                        
                        if (category == "BAD_SEC" || category == "NOT_ENTITLED")
                        {
                            OnSubscriptionError?.Invoke(key, $"{category}: {desc}");
                        }
                    }
                    return;
                }

                // Handle subscription data
                if (eventType == Event.EventType.SUBSCRIPTION_DATA)
                {
                    double? lastPrice = null;
                    
                    // Try to get LAST_PRICE first, then fall back to MID of BID/ASK
                    if (msg.HasElement("LAST_PRICE"))
                    {
                        lastPrice = msg.GetElementAsFloat64("LAST_PRICE");
                    }
                    else if (msg.HasElement("BID") && msg.HasElement("ASK"))
                    {
                        double bid = msg.GetElementAsFloat64("BID");
                        double ask = msg.GetElementAsFloat64("ASK");
                        lastPrice = (bid + ask) / 2.0;
                    }

                    if (lastPrice.HasValue && lastPrice.Value > 0)
                    {
                        var now = DateTime.Now;
                        subInfo.LastRate = lastPrice.Value;
                        subInfo.LastUpdate = now;
                        
                        // Fire the event
                        OnSpotRateUpdated?.Invoke(key, lastPrice.Value, now);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bloomberg Stream] Message processing error: {ex.Message}");
            }
        }

        #endregion

        public string DetermineCallOrPut(double strikePrice, string currencyPair)
        {
            if (strikePrice > 1.0)
                return "CALL";
            else
                return "PUT";
        }

        public string DetermineCallOrPut(string tradeRequest)
        {
            var lowerRequest = tradeRequest.ToLower();

            if (lowerRequest.Contains("call") || lowerRequest.Contains("buy option"))
                return "C";
            else if (lowerRequest.Contains("put") || lowerRequest.Contains("sell option"))
                return "P";
            else
                return "C";
        }

        // --- Bloomberg Window Detection ---

        private IntPtr FindBloombergWindow()
        {
            // Try to find Bloomberg by process name first (most reliable)
            IntPtr bloombergWnd = FindWindowByProcessName("bplus");
            if (bloombergWnd != IntPtr.Zero) return bloombergWnd;

            // Fallback: Look for OVML windows (any currency pair), excluding browsers
            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                if (GetWindowText(hWnd, out string windowTitle))
                {
                    // Check if it contains OVML and is NOT a browser
                    if (windowTitle.Contains("OVML", StringComparison.OrdinalIgnoreCase) &&
                        !windowTitle.Contains("Chrome", StringComparison.OrdinalIgnoreCase) &&
                        !windowTitle.Contains("Firefox", StringComparison.OrdinalIgnoreCase) &&
                        !windowTitle.Contains("Edge", StringComparison.OrdinalIgnoreCase) &&
                        !windowTitle.Contains("Safari", StringComparison.OrdinalIgnoreCase))
                    {
                        found = hWnd;
                        return false; // Stop searching
                    }
                }
                return true; // Continue searching
            }, IntPtr.Zero);

            return found;
        }

        private IntPtr FindWindowByProcessName(string processName)
        {
            IntPtr found = IntPtr.Zero;

            try
            {
                Process[] processes = Process.GetProcessesByName(processName);
                if (processes.Length > 0)
                {
                    found = processes[0].MainWindowHandle;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bloomberg] Error finding by process: {ex.Message}");
            }

            return found;
        }

        private IntPtr FindWindowByTitle(string title)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                if (GetWindowText(hWnd, out string windowTitle) &&
                    windowTitle.Contains(title, StringComparison.OrdinalIgnoreCase))
                {
                    found = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private string GetWindowTitle(IntPtr hWnd)
        {
            if (GetWindowText(hWnd, out string title))
                return title;
            return "";
        }

        // --- Win32 API Imports ---

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        private static bool GetWindowText(IntPtr hWnd, out string text)
        {
            var sb = new StringBuilder(256);
            int length = GetWindowText(hWnd, sb, sb.Capacity);
            text = sb.ToString();
            return length > 0;
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}