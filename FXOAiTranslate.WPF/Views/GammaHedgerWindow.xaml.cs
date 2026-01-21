using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Media;
using System.Runtime.CompilerServices;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FXOptionsSimulator;
using FXOAiTranslate.WPF.Views;

// Aliases to resolve WinForms/WPF type conflicts (caused by UseWindowsForms=true for TradeParser)
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;

namespace FXOAiTranslate.WPF.Views
{
    /// <summary>
    /// Gamma Hedger - monitors option positions and alerts when delta hedges are required.
    /// Receives parsed trades from FXOAi Translator and tracks live Bloomberg spot rates.
    /// </summary>
    public partial class GammaHedgerWindow : Window, INotifyPropertyChanged
    {
        #region Static Registry for Cross-Window Communication
        
        /// <summary>
        /// Registry of active Gamma Hedger windows indexed by currency pair.
        /// Used to notify hedgers when delta hedges are executed in FX Aggregator.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, GammaHedgerWindow> _activeHedgers 
            = new System.Collections.Concurrent.ConcurrentDictionary<string, GammaHedgerWindow>();

        /// <summary>
        /// Apply a delta hedge to the Gamma Hedger for a specific currency pair.
        /// Called from FX Aggregator when a hedge is executed.
        /// </summary>
        /// <param name="currencyPair">Currency pair (e.g., "EURUSD")</param>
        /// <param name="hedgeAmount">Signed hedge amount in base currency (positive = bought, negative = sold)</param>
        /// <param name="hedgeRate">Rate at which the hedge was executed</param>
        /// <param name="hedgeType">Type of hedge: "Spot" or "Forward"</param>
        /// <returns>True if a Gamma Hedger was found and updated</returns>
        public static bool ApplyDeltaHedge(string currencyPair, double hedgeAmount, double hedgeRate, string hedgeType = "Spot")
        {
            if (string.IsNullOrEmpty(currencyPair))
                return false;

            string key = currencyPair.ToUpperInvariant();
            
            if (_activeHedgers.TryGetValue(key, out var hedger))
            {
                // Marshal to UI thread
                hedger.Dispatcher.BeginInvoke(() =>
                {
                    hedger.ApplyHedgeInternal(hedgeAmount, hedgeRate, hedgeType);
                });
                
                Console.WriteLine($"[GammaHedger] Applied hedge to {currencyPair}: {hedgeAmount:N0} @ {hedgeRate:F4}");
                return true;
            }
            
            Console.WriteLine($"[GammaHedger] No active hedger found for {currencyPair}");
            return false;
        }
        
        /// <summary>
        /// Static initializer to subscribe to TradeBlotter hedge events.
        /// </summary>
        static GammaHedgerWindow()
        {
            // Subscribe to TradeBlotter hedge events to automatically update Gamma Hedgers
            TradeBlotter.Instance.OnHedgeExecuted += (currencyPair, hedgeAmount, hedgeRate, hedgeType) =>
            {
                ApplyDeltaHedge(currencyPair, hedgeAmount, hedgeRate, hedgeType);
            };
            
            Console.WriteLine("[GammaHedger] Subscribed to TradeBlotter hedge events");
        }

        /// <summary>
        /// Check if there's an active Gamma Hedger for a currency pair.
        /// </summary>
        public static bool HasActiveHedger(string currencyPair)
        {
            if (string.IsNullOrEmpty(currencyPair))
                return false;
            
            return _activeHedgers.ContainsKey(currencyPair.ToUpperInvariant());
        }

        /// <summary>
        /// Register this window in the static registry.
        /// </summary>
        private void RegisterInRegistry()
        {
            if (!string.IsNullOrEmpty(CurrencyPair))
            {
                string key = CurrencyPair.ToUpperInvariant();
                _activeHedgers[key] = this;
                Console.WriteLine($"[GammaHedger] Registered hedger for {key}");
            }
        }

        /// <summary>
        /// Unregister this window from the static registry.
        /// </summary>
        private void UnregisterFromRegistry()
        {
            if (!string.IsNullOrEmpty(CurrencyPair))
            {
                string key = CurrencyPair.ToUpperInvariant();
                _activeHedgers.TryRemove(key, out _);
                Console.WriteLine($"[GammaHedger] Unregistered hedger for {key}");
            }
        }

        /// <summary>
        /// Internal method to apply a hedge (runs on UI thread).
        /// </summary>
        private void ApplyHedgeInternal(double hedgeAmount, double hedgeRate, string hedgeType)
        {
            // Calculate the delta impact of the hedge
            // Buying spot reduces delta (you're now long spot to offset short delta)
            // Selling spot increases delta (you're now short spot to offset long delta)
            double deltaReduction = hedgeAmount;
            
            double previousDelta = CurrentDelta;
            CurrentDelta -= deltaReduction;
            
            // Log the hedge
            string side = hedgeAmount > 0 ? "BUY" : "SELL";
            double absAmount = Math.Abs(hedgeAmount);
            
            LogActivity($"?? HEDGE APPLIED: {side} {absAmount:N0} {HedgeThresholdCcy} @ {hedgeRate:F4} ({hedgeType})", "HEDGE");
            LogActivity($"   Delta: {previousDelta:N0} ? {CurrentDelta:N0} (? {-deltaReduction:+#,##0;-#,##0})", "INFO");
            
            // Update threshold status
            UpdateThresholdStatus();
            
            // If we've reduced delta below threshold, hide any alert and reset alert state
            if (Math.Abs(CurrentDelta) < HedgeThreshold && _alertShown)
            {
                hedgeAlertOverlay.Visibility = Visibility.Collapsed;
                _alertShown = false;
                Status = "Active";
                LogActivity("? Delta within threshold after hedge", "INFO");
            }
            
            // Speak confirmation
            try
            {
                string amountInMillions = (absAmount / 1_000_000).ToString("F1");
                _speechSynthesizer.SpeakAsync($"Hedge applied. {side} {amountInMillions} million");
            }
            catch { }
        }

        #endregion

        #region Observable Collections
        
        public ObservableCollection<GammaHedgerTab> Tabs { get; set; }
        public ObservableCollection<GammaLadderRow> GammaLadder { get; set; }
        public ObservableCollection<ActivityLogEntry> ActivityLog { get; set; }
        public ObservableCollection<SpotDataPoint> SpotChartData { get; set; }
        
        #endregion

        #region Services
        
        private readonly BloombergService _bloombergService;
        private readonly DispatcherTimer _spotPollingTimer;
        private readonly DispatcherTimer _hedgeCheckTimer;
        private readonly DispatcherTimer _chartUpdateTimer;
        private readonly SpeechSynthesizer _speechSynthesizer;
        
        #endregion

        #region Alert State
        
        private bool _alertShown;
        private DateTime _lastAlertTime;
        private double _pendingHedgeAmount;
        private readonly TimeSpan _alertCooldown = TimeSpan.FromSeconds(10); // Don't spam alerts
        
        #endregion

        #region Hedge Parameters (Bindable)
        
        private double _hedgeThreshold = 500000; // Default 500k
        public double HedgeThreshold
        {
            get => _hedgeThreshold;
            set { _hedgeThreshold = value; OnPropertyChanged(); }
        }

        private string _hedgeThresholdCcy = "EUR";
        public string HedgeThresholdCcy
        {
            get => _hedgeThresholdCcy;
            set { _hedgeThresholdCcy = value; OnPropertyChanged(); }
        }

        private double _upperTradingLimit = 2.0;
        public double UpperTradingLimit
        {
            get => _upperTradingLimit;
            set { _upperTradingLimit = value; OnPropertyChanged(); }
        }

        private double _lowerTradingLimit = 0.5;
        public double LowerTradingLimit
        {
            get => _lowerTradingLimit;
            set { _lowerTradingLimit = value; OnPropertyChanged(); }
        }

        private double? _maxSpreadToTrade;
        public double? MaxSpreadToTrade
        {
            get => _maxSpreadToTrade;
            set { _maxSpreadToTrade = value; OnPropertyChanged(); }
        }

        private double? _tradeStepSize;
        public double? TradeStepSize
        {
            get => _tradeStepSize;
            set { _tradeStepSize = value; OnPropertyChanged(); }
        }

        private string _stpBook;
        public string StpBook
        {
            get => _stpBook;
            set { _stpBook = value; OnPropertyChanged(); }
        }

        #endregion

        #region Position State
        
        private string _currencyPair;
        public string CurrencyPair
        {
            get => _currencyPair;
            set 
            { 
                // Unregister old currency pair
                UnregisterFromRegistry();
                
                _currencyPair = value; 
                OnPropertyChanged(); 
                UpdateWindowTitle();
                
                // Register new currency pair
                RegisterInRegistry();
            }
        }

        private double _currentSpot;
        public double CurrentSpot
        {
            get => _currentSpot;
            set 
            { 
                _currentSpot = value; 
                OnPropertyChanged(); 
                RecalculatePosition();
                UpdateThresholdStatus();
            }
        }

        private double _currentDelta;
        public double CurrentDelta
        {
            get => _currentDelta;
            set 
            { 
                _currentDelta = value; 
                OnPropertyChanged();
                UpdateThresholdStatus();
            }
        }

        private double _currentGamma;
        public double CurrentGamma
        {
            get => _currentGamma;
            set { _currentGamma = value; OnPropertyChanged(); }
        }

        private string _counterparty = "Internal Position";
        public string Counterparty
        {
            get => _counterparty;
            set { _counterparty = value; OnPropertyChanged(); }
        }

        private string _status = "Incomplete";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusBackground)); }
        }

        public SolidColorBrush StatusBackground => Status switch
        {
            "Active" => new SolidColorBrush(Color.FromRgb(34, 197, 94)),    // Green
            "Paused" => new SolidColorBrush(Color.FromRgb(245, 158, 11)),   // Amber
            "Alert" => new SolidColorBrush(Color.FromRgb(239, 68, 68)),     // Red
            _ => new SolidColorBrush(Color.FromRgb(234, 179, 8))            // Yellow (Incomplete)
        };

        private string _createdBy = Environment.UserName;
        public string CreatedBy
        {
            get => _createdBy;
            set { _createdBy = value; OnPropertyChanged(); }
        }

        private bool _isHedgingActive;
        public bool IsHedgingActive
        {
            get => _isHedgingActive;
            set { _isHedgingActive = value; OnPropertyChanged(); UpdateStatus(); }
        }

        private bool _isLocked;
        public bool IsLocked
        {
            get => _isLocked;
            set { _isLocked = value; OnPropertyChanged(); }
        }

        #endregion

        #region Stored Positions
        
        private TradeStructure _optionPosition;
        
        #endregion

        public GammaHedgerWindow()
        {
            InitializeComponent();
            DataContext = this;

            // Initialize collections
            Tabs = new ObservableCollection<GammaHedgerTab>();
            GammaLadder = new ObservableCollection<GammaLadderRow>();
            ActivityLog = new ObservableCollection<ActivityLogEntry>();
            SpotChartData = new ObservableCollection<SpotDataPoint>();

            // Initialize Bloomberg service
            _bloombergService = new BloombergService();

            // Spot polling timer (500ms for responsive UI)
            _spotPollingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _spotPollingTimer.Tick += SpotPollingTimer_Tick;

            // Hedge check timer (check every 200ms for fast response)
            _hedgeCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _hedgeCheckTimer.Tick += HedgeCheckTimer_Tick;

            // Chart update timer (redraw every second)
            _chartUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _chartUpdateTimer.Tick += ChartUpdateTimer_Tick;

            // Initialize speech synthesizer for audio alerts
            _speechSynthesizer = new SpeechSynthesizer();
            _speechSynthesizer.Rate = 0; // Normal speed
            _speechSynthesizer.Volume = 100;
            
            // Try to select a clear voice
            var voice = _speechSynthesizer.GetInstalledVoices()
                .FirstOrDefault(v => v.VoiceInfo.Culture.Name.StartsWith("en"));
            if (voice != null)
            {
                _speechSynthesizer.SelectVoice(voice.VoiceInfo.Name);
            }

            // Add default gamma ladder rows
            InitializeGammaLadder();

            LogActivity("Gamma Hedger initialized - waiting for position", "INFO");
        }

        /// <summary>
        /// Constructor accepting a parsed trade from FXOAi Translator
        /// </summary>
        public GammaHedgerWindow(TradeStructure trade) : this()
        {
            LoadPosition(trade);
        }

        #region Position Loading

        /// <summary>
        /// Load an option position for gamma hedging
        /// </summary>
        public void LoadPosition(TradeStructure trade)
        {
            if (trade == null) return;

            _optionPosition = trade;
            CurrencyPair = trade.Underlying;
            
            // Add tab for this position
            var tab = new GammaHedgerTab
            {
                CurrencyPair = trade.Underlying,
                IsActive = true
            };
            
            // Deactivate other tabs
            foreach (var existingTab in Tabs)
            {
                existingTab.IsActive = false;
            }
            Tabs.Add(tab);

            // Set initial hedge parameters based on position
            if (trade.Legs?.Count > 0)
            {
                var leg = trade.Legs[0];
                double notional = leg.NotionalMM * 1_000_000;
                
                // Default threshold: 10% of notional
                HedgeThreshold = notional * 0.10;
                HedgeThresholdCcy = trade.Underlying?.Substring(0, 3) ?? "EUR";
                
                // Trading limits based on strike ±5%
                UpperTradingLimit = leg.Strike * 1.05;
                LowerTradingLimit = leg.Strike * 0.95;
                
                // Get initial spot
                if (trade.SpotReference > 0)
                {
                    CurrentSpot = trade.SpotReference;
                }
            }

            // Start spot monitoring
            StartSpotMonitoring();
            
            LogActivity($"Loaded position: {trade.Underlying} {trade.StructureType}", "INFO");
            LogActivity($"Threshold set to {HedgeThreshold:N0} {HedgeThresholdCcy}", "INFO");
        }

        #endregion

        #region Spot Monitoring

        private bool _useStreaming = true; // Try streaming first
        
        private void StartSpotMonitoring()
        {
            if (string.IsNullOrEmpty(CurrencyPair)) return;

            // Try to use Bloomberg streaming first
            if (_useStreaming)
            {
                try
                {
                    // Subscribe to streaming updates
                    _bloombergService.OnSpotRateUpdated += OnBloombergSpotUpdate;
                    _bloombergService.OnSubscriptionError += OnBloombergSubscriptionError;
                    
                    if (_bloombergService.SubscribeToSpot(CurrencyPair))
                    {
                        LogActivity($"?? Bloomberg LIVE STREAM started for {CurrencyPair}", "INFO");
                        // Don't start polling timer - we're using streaming
                    }
                    else
                    {
                        LogActivity($"Bloomberg streaming unavailable - falling back to polling", "WARN");
                        _useStreaming = false;
                        _spotPollingTimer.Start();
                    }
                }
                catch (Exception ex)
                {
                    LogActivity($"Streaming setup failed: {ex.Message} - using polling", "WARN");
                    _useStreaming = false;
                    _spotPollingTimer.Start();
                }
            }
            else
            {
                _spotPollingTimer.Start();
            }

            _hedgeCheckTimer.Start();
            _chartUpdateTimer.Start();
            
            LogActivity($"Started monitoring {CurrencyPair}", "INFO");
        }

        private void StopSpotMonitoring()
        {
            _spotPollingTimer.Stop();
            _hedgeCheckTimer.Stop();
            _chartUpdateTimer.Stop();
            
            // Unsubscribe from streaming
            if (_useStreaming && !string.IsNullOrEmpty(CurrencyPair))
            {
                try
                {
                    _bloombergService.OnSpotRateUpdated -= OnBloombergSpotUpdate;
                    _bloombergService.OnSubscriptionError -= OnBloombergSubscriptionError;
                    _bloombergService.UnsubscribeFromSpot(CurrencyPair);
                    LogActivity($"?? Bloomberg stream stopped for {CurrencyPair}", "INFO");
                }
                catch { }
            }
            
            LogActivity("Stopped spot monitoring", "INFO");
        }

        /// <summary>
        /// Handle streaming spot rate updates from Bloomberg
        /// </summary>
        private void OnBloombergSpotUpdate(string currencyPair, double spotRate, DateTime timestamp)
        {
            // Only process updates for our currency pair
            if (!string.Equals(currencyPair, CurrencyPair, StringComparison.OrdinalIgnoreCase))
                return;

            // Marshal to UI thread
            Dispatcher.BeginInvoke(() =>
            {
                CurrentSpot = spotRate;
                
                // Add to chart data
                SpotChartData.Add(new SpotDataPoint
                {
                    Time = timestamp,
                    Spot = spotRate
                });

                // Keep only last 300 data points (5 mins at ~1/sec)
                while (SpotChartData.Count > 300)
                {
                    SpotChartData.RemoveAt(0);
                }
            });
        }

        /// <summary>
        /// Handle Bloomberg subscription errors
        /// </summary>
        private void OnBloombergSubscriptionError(string currencyPair, string errorMessage)
        {
            if (!string.Equals(currencyPair, CurrencyPair, StringComparison.OrdinalIgnoreCase))
                return;

            Dispatcher.BeginInvoke(() =>
            {
                LogActivity($"Bloomberg stream error: {errorMessage}", "ERROR");
                
                // Fall back to polling if streaming fails
                if (_useStreaming && !_spotPollingTimer.IsEnabled)
                {
                    _useStreaming = false;
                    _spotPollingTimer.Start();
                    LogActivity("Switched to polling mode due to stream error", "WARN");
                }
            });
        }

        private async void SpotPollingTimer_Tick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(CurrencyPair) || _bloombergService == null) return;

            try
            {
                var spot = await System.Threading.Tasks.Task.Run(() => 
                    _bloombergService.GetSpotRate(CurrencyPair));
                
                if (spot.HasValue && spot.Value > 0)
                {
                    CurrentSpot = spot.Value;
                    
                    // Add to chart data
                    SpotChartData.Add(new SpotDataPoint
                    {
                        Time = DateTime.Now,
                        Spot = spot.Value
                    });

                    // Keep only last 300 data points (5 mins at 1/sec)
                    while (SpotChartData.Count > 300)
                    {
                        SpotChartData.RemoveAt(0);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GammaHedger] Spot polling error: {ex.Message}");
            }
        }

        #endregion

        #region Position Calculation

        private void RecalculatePosition()
        {
            if (_optionPosition?.Legs == null || _optionPosition.Legs.Count == 0) return;

            var leg = _optionPosition.Legs[0];
            double notional = leg.NotionalMM * 1_000_000;
            
            // Simplified delta/gamma calculation
            // In production, connect to a proper Greeks engine or pricer
            double moneyness = CurrentSpot / leg.Strike;
            
            // Approximate delta (simplified)
            double baseDelta;
            if (leg.OptionType == "CALL")
            {
                baseDelta = Math.Max(0, Math.Min(1, 0.5 + (moneyness - 1) * 3));
            }
            else
            {
                baseDelta = Math.Max(-1, Math.Min(0, -0.5 + (moneyness - 1) * 3));
            }
            
            // Direction multiplier
            double direction = leg.Direction == "BUY" ? 1 : -1;
            CurrentDelta = baseDelta * notional * direction;
            
            // Gamma is highest ATM, decreases as we move away
            double atmDistance = Math.Abs(moneyness - 1);
            CurrentGamma = Math.Max(0, (1 - atmDistance * 10)) * notional * 0.001 * Math.Abs(direction);
        }

        private void UpdateThresholdStatus()
        {
            if (lblThresholdStatus == null || lblThresholdPct == null || thresholdStatusBorder == null)
                return;

            double absDelta = Math.Abs(CurrentDelta);
            double thresholdPct = HedgeThreshold > 0 ? (absDelta / HedgeThreshold) * 100 : 0;
            
            lblThresholdPct.Text = $"{thresholdPct:F0}%";
            
            if (absDelta >= HedgeThreshold)
            {
                lblThresholdStatus.Text = "BREACH";
                lblThresholdStatus.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                thresholdStatusBorder.Background = new SolidColorBrush(Color.FromRgb(127, 29, 29)); // Dark red bg
            }
            else if (absDelta >= HedgeThreshold * 0.8)
            {
                lblThresholdStatus.Text = "WARNING";
                lblThresholdStatus.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
                thresholdStatusBorder.Background = new SolidColorBrush(Color.FromRgb(120, 53, 15)); // Dark amber bg
            }
            else
            {
                lblThresholdStatus.Text = "OK";
                lblThresholdStatus.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Green
                thresholdStatusBorder.Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)); // Normal bg
            }
        }

        #endregion

        #region Hedge Alert Logic

        private void HedgeCheckTimer_Tick(object sender, EventArgs e)
        {
            if (!IsHedgingActive || IsLocked) return;
            CheckHedgeTrigger();
        }

        private void CheckHedgeTrigger()
        {
            if (!IsHedgingActive || IsLocked) return;
            
            double absDelta = Math.Abs(CurrentDelta);
            
            // Check if delta exceeds threshold
            if (absDelta < HedgeThreshold) return;

            // Check trading limits
            if (CurrentSpot > UpperTradingLimit)
            {
                LogActivity($"Spot {CurrentSpot:F4} above upper limit {UpperTradingLimit:F4} - hedge deferred", "WARN");
                return;
            }
            if (CurrentSpot < LowerTradingLimit)
            {
                LogActivity($"Spot {CurrentSpot:F4} below lower limit {LowerTradingLimit:F4} - hedge deferred", "WARN");
                return;
            }

            // Check cooldown to prevent alert spam
            if (_alertShown && (DateTime.Now - _lastAlertTime) < _alertCooldown)
            {
                return;
            }

            // Trigger alert!
            TriggerHedgeAlert();
        }

        private void TriggerHedgeAlert()
        {
            _alertShown = true;
            _lastAlertTime = DateTime.Now;
            _pendingHedgeAmount = -CurrentDelta; // Opposite to neutralize
            
            // Apply step size if configured
            if (TradeStepSize.HasValue && TradeStepSize.Value > 0)
            {
                _pendingHedgeAmount = Math.Round(_pendingHedgeAmount / TradeStepSize.Value) * TradeStepSize.Value;
            }

            string side = _pendingHedgeAmount > 0 ? "BUY" : "SELL";
            double absAmount = Math.Abs(_pendingHedgeAmount);

            // Update alert overlay
            lblAlertDetails.Text = $"{CurrencyPair} @ {CurrentSpot:F4}";
            lblAlertDelta.Text = $"Delta: {CurrentDelta:N0} {HedgeThresholdCcy} (Threshold: {HedgeThreshold:N0})";
            lblAlertAction.Text = $"Suggested: {side} {absAmount:N0} {HedgeThresholdCcy}";
            
            // Show overlay
            hedgeAlertOverlay.Visibility = Visibility.Visible;
            
            // Update status
            Status = "Alert";
            
            // Play audio alert
            PlayHedgeAlert(side, absAmount);
            
            // Log
            LogActivity($"?? HEDGE ALERT: {side} {absAmount:N0} {HedgeThresholdCcy} at {CurrentSpot:F4}", "ALERT");
            
            // Flash window
            FlashWindow();
        }

        private void PlayHedgeAlert(string side, double amount)
        {
            try
            {
                // Play system sound first for immediate attention
                SystemSounds.Exclamation.Play();
                
                // Then speak the alert
                string amountInMillions = (amount / 1_000_000).ToString("F1");
                string message = $"Hedge required. {side} {amountInMillions} million {HedgeThresholdCcy}";
                _speechSynthesizer.SpeakAsync(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GammaHedger] Audio alert error: {ex.Message}");
            }
        }

        private void FlashWindow()
        {
            try
            {
                // Simple visual flash by toggling opacity
                var flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                int flashCount = 0;
                
                flashTimer.Tick += (s, e) =>
                {
                    flashCount++;
                    if (flashCount >= 6)
                    {
                        flashTimer.Stop();
                        hedgeAlertOverlay.Opacity = 1.0;
                        return;
                    }
                    
                    hedgeAlertOverlay.Opacity = flashCount % 2 == 0 ? 1.0 : 0.8;
                };
                
                flashTimer.Start();
            }
            catch { }
        }

        private void ExecuteHedgeFromAlert_Click(object sender, RoutedEventArgs e)
        {
            // Log the hedge execution (actual FIX order would go here when enabled)
            string side = _pendingHedgeAmount > 0 ? "BUY" : "SELL";
            double absAmount = Math.Abs(_pendingHedgeAmount);
            
            LogActivity($"? HEDGE EXECUTED: {side} {absAmount:N0} {HedgeThresholdCcy} at {CurrentSpot:F4}", "TRADE");
            LogActivity("Note: FIX spot trading not yet enabled - manual execution required", "INFO");
            
            // Simulate delta reset (in production, this would come from position update)
            CurrentDelta = 0;
            
            // Hide overlay
            hedgeAlertOverlay.Visibility = Visibility.Collapsed;
            _alertShown = false;
            
            // Update status
            Status = "Active";
            
            // Speak confirmation
            try
            {
                _speechSynthesizer.SpeakAsync("Hedge executed");
            }
            catch { }
        }

        private void DismissAlert_Click(object sender, RoutedEventArgs e)
        {
            hedgeAlertOverlay.Visibility = Visibility.Collapsed;
            _alertShown = false;
            Status = "Active";
            
            LogActivity("Alert dismissed - will re-trigger if threshold still breached", "INFO");
        }

        private void FlattenNow_Click(object sender, RoutedEventArgs e)
        {
            if (Math.Abs(CurrentDelta) < 1000)
            {
                LogActivity("Position already flat (delta < 1,000)", "INFO");
                return;
            }

            // Trigger immediate alert for manual execution
            TriggerHedgeAlert();
        }

        #endregion

        #region Manual Hedge

        /// <summary>
        /// Parse the manual hedge amount from the text field.
        /// Supports formats: "5000000", "5M", "5m", "5 000 000", "500K"
        /// </summary>
        private double? ParseManualHedgeAmount()
        {
            string input = txtManualHedgeAmount?.Text?.Trim();
            if (string.IsNullOrEmpty(input))
            {
                MessageBox.Show("Please enter a hedge amount", "Missing Amount", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            // Remove spaces and commas
            input = input.Replace(" ", "").Replace(",", "");

            // Check for M/m suffix (millions)
            if (input.EndsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                string numPart = input.Substring(0, input.Length - 1);
                if (double.TryParse(numPart, out double millions))
                {
                    return millions * 1_000_000;
                }
            }
            
            // Check for K/k suffix (thousands)
            if (input.EndsWith("K", StringComparison.OrdinalIgnoreCase))
            {
                string numPart = input.Substring(0, input.Length - 1);
                if (double.TryParse(numPart, out double thousands))
                {
                    return thousands * 1_000;
                }
            }

            // Try to parse as raw number
            if (double.TryParse(input, out double amount))
            {
                return amount;
            }

            MessageBox.Show($"Invalid amount format: '{input}'\n\nUse formats like: 5000000, 5M, 500K", 
                "Invalid Amount", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        /// <summary>
        /// Parse the manual hedge rate from the text field.
        /// </summary>
        private double? ParseManualHedgeRate()
        {
            string input = txtManualHedgeRate?.Text?.Trim();
            if (string.IsNullOrEmpty(input))
            {
                // Use current spot as default
                if (CurrentSpot > 0)
                {
                    return CurrentSpot;
                }
                
                MessageBox.Show("Please enter a hedge rate", "Missing Rate", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            if (double.TryParse(input, out double rate) && rate > 0)
            {
                return rate;
            }

            MessageBox.Show($"Invalid rate format: '{input}'", "Invalid Rate", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        /// <summary>
        /// Execute a manual BUY hedge (reduces short delta / increases long delta)
        /// </summary>
        private void ManualHedgeBuy_Click(object sender, RoutedEventArgs e)
        {
            ExecuteManualHedge(isBuy: true);
        }

        /// <summary>
        /// Execute a manual SELL hedge (reduces long delta / increases short delta)
        /// </summary>
        private void ManualHedgeSell_Click(object sender, RoutedEventArgs e)
        {
            ExecuteManualHedge(isBuy: false);
        }

        /// <summary>
        /// Calculate and execute a hedge to flatten the current delta position.
        /// </summary>
        private void HedgeToFlat_Click(object sender, RoutedEventArgs e)
        {
            if (Math.Abs(CurrentDelta) < 1000)
            {
                MessageBox.Show("Position already flat (delta < 1,000)", "Already Flat", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var rate = ParseManualHedgeRate();
            if (!rate.HasValue) return;

            // Calculate the hedge amount needed to flatten
            // If delta is positive (long), we need to SELL to flatten
            // If delta is negative (short), we need to BUY to flatten
            double hedgeAmount = -CurrentDelta;
            
            string side = hedgeAmount > 0 ? "BUY" : "SELL";
            double absAmount = Math.Abs(hedgeAmount);
            
            var result = MessageBox.Show(
                $"Execute hedge to flatten delta?\n\n" +
                $"Current Delta: {CurrentDelta:N0} {HedgeThresholdCcy}\n" +
                $"Hedge: {side} {absAmount:N0} {HedgeThresholdCcy} @ {rate.Value:F4}\n\n" +
                $"This will bring delta to approximately zero.",
                "Confirm Flatten",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                ApplyHedgeInternal(hedgeAmount, rate.Value, "Spot");
                LogActivity($"? FLATTENED: Delta neutralized via {side} {absAmount:N0}", "TRADE");
            }
        }

        /// <summary>
        /// Execute a manual hedge and update the delta position.
        /// </summary>
        private void ExecuteManualHedge(bool isBuy)
        {
            var amount = ParseManualHedgeAmount();
            if (!amount.HasValue) return;

            var rate = ParseManualHedgeRate();
            if (!rate.HasValue) return;

            // Make amount signed based on direction
            double signedAmount = isBuy ? Math.Abs(amount.Value) : -Math.Abs(amount.Value);
            
            // Apply the hedge
            ApplyHedgeInternal(signedAmount, rate.Value, "Spot");
            
            // Clear the input field for next hedge
            txtManualHedgeAmount.Text = "";
            
            // Update the rate field to current spot
            txtManualHedgeRate.Text = CurrentSpot.ToString("F4");
        }

        #endregion

        #region Chart Drawing

        private void ChartUpdateTimer_Tick(object sender, EventArgs e)
        {
            DrawSpotChart();
        }

        private void DrawSpotChart()
        {
            if (SpotChartData.Count < 2 || spotChartCanvas == null) return;

            spotChartCanvas.Children.Clear();

            double width = spotChartCanvas.ActualWidth;
            double height = spotChartCanvas.ActualHeight;
            
            if (width <= 0 || height <= 0) return;

            var data = SpotChartData.ToList();
            double minSpot = data.Min(d => d.Spot) * 0.9999;
            double maxSpot = data.Max(d => d.Spot) * 1.0001;
            double spotRange = maxSpot - minSpot;
            
            if (spotRange <= 0) return;

            // Draw spot line
            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(59, 130, 246)), // Blue
                StrokeThickness = 2
            };

            for (int i = 0; i < data.Count; i++)
            {
                double x = (double)i / (data.Count - 1) * width;
                double y = height - ((data[i].Spot - minSpot) / spotRange * height);
                polyline.Points.Add(new System.Windows.Point(x, y));
            }

            spotChartCanvas.Children.Add(polyline);

            // Draw strike line if we have a position
            if (_optionPosition?.Legs?.Count > 0)
            {
                double strike = _optionPosition.Legs[0].Strike;
                if (strike >= minSpot && strike <= maxSpot)
                {
                    double strikeY = height - ((strike - minSpot) / spotRange * height);
                    var strikeLine = new Line
                    {
                        X1 = 0,
                        Y1 = strikeY,
                        X2 = width,
                        Y2 = strikeY,
                        Stroke = new SolidColorBrush(Color.FromRgb(245, 158, 11)), // Amber
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 4, 2 }
                    };
                    spotChartCanvas.Children.Add(strikeLine);
                }
            }
        }

        #endregion

        #region Gamma Ladder

        private void InitializeGammaLadder()
        {
            for (int i = 0; i < 3; i++)
            {
                GammaLadder.Add(new GammaLadderRow());
            }
        }

        private void AddLadderRow_Click(object sender, RoutedEventArgs e)
        {
            GammaLadder.Add(new GammaLadderRow());
        }

        private void ClearLadder_Click(object sender, RoutedEventArgs e)
        {
            GammaLadder.Clear();
            InitializeGammaLadder();
        }

        private void RemoveLadderRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is GammaLadderRow row)
            {
                GammaLadder.Remove(row);
            }
        }

        #endregion

        #region Activity Log

        private void LogActivity(string message, string level)
        {
            Dispatcher.BeginInvoke(() =>
            {
                ActivityLog.Insert(0, new ActivityLogEntry
                {
                    Time = DateTime.Now,
                    Message = message,
                    Level = level
                });

                // Keep only last 100 entries
                while (ActivityLog.Count > 100)
                {
                    ActivityLog.RemoveAt(ActivityLog.Count - 1);
                }
            });

            Console.WriteLine($"[GammaHedger] [{level}] {message}");
        }

        private void CopyActivityLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logText = string.Join("\n", ActivityLog.Select(a => $"{a.TimeDisplay} [{a.Level}] {a.Message}"));
                Clipboard.SetText(logText);
                LogActivity("Activity log copied to clipboard", "INFO");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GammaHedger] Error copying log: {ex.Message}");
            }
        }

        #endregion

        #region UI Helpers

        private void UpdateWindowTitle()
        {
            Title = $"Gamma Hedger - {CurrencyPair ?? "No Position"}";
        }

        private void UpdateStatus()
        {
            if (!IsHedgingActive)
            {
                Status = "Paused";
            }
            else if (_alertShown)
            {
                Status = "Alert";
            }
            else
            {
                Status = "Active";
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            IsHedgingActive = true;
            Status = "Active";
            LogActivity($"Monitoring activated - Threshold: {HedgeThreshold:N0} {HedgeThresholdCcy}", "INFO");
            LogActivity($"Trading limits: {LowerTradingLimit:F4} - {UpperTradingLimit:F4}", "INFO");
        }

        private void Lock_Click(object sender, RoutedEventArgs e)
        {
            IsLocked = !IsLocked;
            btnLock.Content = IsLocked ? "??" : "??";
            LogActivity(IsLocked ? "Position LOCKED - auto-hedging disabled" : "Position UNLOCKED", "INFO");
        }

        private void HedgeThreshold_LostFocus(object sender, RoutedEventArgs e)
        {
            // Recalculate threshold status when threshold changes
            UpdateThresholdStatus();
        }

        #endregion

        #region Tab Management

        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            LogActivity("Use FXOAi Translator to parse a trade and send to Gamma Hedger", "INFO");
            MessageBox.Show("To add a position:\n\n1. Open FXOAi Translator or FX Aggregator\n2. Parse your option trade\n3. Click 'Send to Gamma Hedger'", 
                "Add Position", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is GammaHedgerTab tab)
            {
                Tabs.Remove(tab);
                LogActivity($"Closed tab: {tab.CurrencyPair}", "INFO");
                
                if (Tabs.Count == 0)
                {
                    StopSpotMonitoring();
                    Status = "Incomplete";
                    _optionPosition = null;
                    CurrencyPair = null;
                }
            }
        }

        #endregion

        #region Window Lifecycle

        protected override void OnClosed(EventArgs e)
        {
            // Unregister from static registry
            UnregisterFromRegistry();
            
            StopSpotMonitoring();
            _speechSynthesizer?.Dispose();
            base.OnClosed(e);
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    #region View Models

    public class GammaHedgerTab : INotifyPropertyChanged
    {
        private string _currencyPair;
        public string CurrencyPair
        {
            get => _currencyPair;
            set { _currencyPair = value; OnPropertyChanged(); }
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class GammaLadderRow : INotifyPropertyChanged
    {
        private double? _spot;
        public double? Spot
        {
            get => _spot;
            set { _spot = value; OnPropertyChanged(); }
        }

        private double? _gamma;
        public double? Gamma
        {
            get => _gamma;
            set { _gamma = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ActivityLogEntry
    {
        public DateTime Time { get; set; }
        public string Message { get; set; }
        public string Level { get; set; }
        
        public string TimeDisplay => Time.ToString("HH:mm:ss");
    }

    public class SpotDataPoint
    {
        public DateTime Time { get; set; }
        public double Spot { get; set; }
    }

    #endregion
}
