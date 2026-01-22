using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Media;
using System.Runtime.CompilerServices;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    /// Converter to show/hide elements based on collection count.
    /// </summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public static readonly CountToVisibilityConverter Instance = new CountToVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int count = value is int c ? c : 0;
            bool invert = parameter?.ToString() == "invert";
            
            if (invert)
            {
                // Show when count is 0
                return count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                // Show when count > 0
                return count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter for tab background based on IsActive state.
    /// </summary>
    public class BoolToBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isActive = value is bool b && b;
            return isActive 
                ? new SolidColorBrush(Color.FromRgb(30, 41, 59))   // #1e293b - active/selected
                : new SolidColorBrush(Color.FromRgb(15, 23, 42));  // #0f172a - inactive
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converter for tab border color based on IsActive state.
    /// </summary>
    public class BoolToBorderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isActive = value is bool b && b;
            return isActive 
                ? new SolidColorBrush(Color.FromRgb(59, 130, 246))  // #3b82f6 - blue accent
                : new SolidColorBrush(Color.FromRgb(51, 65, 85));   // #334155 - muted border
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converter for status indicator color based on hedging state.
    /// </summary>
    public class BoolToStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isActive = value is bool b && b;
            return isActive 
                ? new SolidColorBrush(Color.FromRgb(34, 197, 94))   // #22c55e - green (active)
                : new SolidColorBrush(Color.FromRgb(234, 179, 8));  // #eab308 - yellow (inactive)
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

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
        /// Get or create a Gamma Hedger window for a specific currency pair.
        /// If a hedger exists for this pair, returns it; otherwise creates a new one.
        /// </summary>
        public static GammaHedgerWindow GetOrCreateHedger(string currencyPair)
        {
            if (string.IsNullOrEmpty(currencyPair))
            {
                // No pair specified - create a blank hedger
                var newHedger = new GammaHedgerWindow();
                newHedger.Show();
                return newHedger;
            }

            string key = currencyPair.ToUpperInvariant();
            
            if (_activeHedgers.TryGetValue(key, out var existingHedger))
            {
                // Bring existing window to front
                existingHedger.Activate();
                if (existingHedger.WindowState == WindowState.Minimized)
                {
                    existingHedger.WindowState = WindowState.Normal;
                }
                Console.WriteLine($"[GammaHedger] Activated existing hedger for {key}");
                return existingHedger;
            }

            // Create new hedger for this pair
            var hedger = new GammaHedgerWindow();
            hedger.CurrencyPair = currencyPair;
            hedger.HedgeThresholdCcy = currencyPair.Substring(0, 3);
            hedger.Show();
            Console.WriteLine($"[GammaHedger] Created new hedger for {key}");
            return hedger;
        }

        /// <summary>
        /// Get a list of all active currency pairs with hedgers.
        /// </summary>
        public static IEnumerable<string> GetActiveHedgerPairs()
        {
            return _activeHedgers.Keys.ToList();
        }

        /// <summary>
        /// Route a trade to the appropriate Gamma Hedger (creates one if needed).
        /// </summary>
        public static void RouteTradeToHedger(TradeStructure trade)
        {
            if (trade == null) return;

            string pair = trade.Underlying ?? trade.CurrencyPair;
            if (string.IsNullOrEmpty(pair)) return;

            var hedger = GetOrCreateHedger(pair);
            hedger.LoadPosition(trade);
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
        
        /// <summary>
        /// List of option positions (from parsed trades). Delta/gamma from these adds to ladder-based gamma.
        /// </summary>
        private readonly List<TradeStructure> _positions = new List<TradeStructure>();
        
        /// <summary>
        /// Observable collection for UI binding of positions.
        /// </summary>
        public ObservableCollection<PositionRow> Positions { get; set; }
        
        /// <summary>
        /// Last spot value used for delta accumulation calculation.
        /// </summary>
        private double _lastSpotForDelta;
        
        /// <summary>
        /// Accumulated delta from spot movements (Gamma × ΔSpot).
        /// </summary>
        private double _accumulatedDelta;
        
        /// <summary>
        /// Currently active/selected tab.
        /// </summary>
        private GammaHedgerTab _activeTab;
        
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
            Positions = new ObservableCollection<PositionRow>();

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
        /// Add an option position to the aggregate book.
        /// Positions are tracked by TradeId to avoid duplicates.
        /// Routes the trade to the correct currency pair tab.
        /// </summary>
        public void LoadPosition(TradeStructure trade)
        {
            if (trade == null) return;

            string tradePair = trade.Underlying ?? trade.CurrencyPair;
            
            // Get or create a tab for this currency pair
            GammaHedgerTab tab;
            if (string.IsNullOrEmpty(tradePair))
            {
                // No currency pair on trade, use active tab or first tab
                tab = _activeTab ?? Tabs.FirstOrDefault();
                if (tab == null)
                {
                    MessageBox.Show("Please add a currency pair tab first.", "No Active Tab", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                // Find or create tab for this pair
                tab = Tabs.FirstOrDefault(t => 
                    string.Equals(t.CurrencyPair, tradePair, StringComparison.OrdinalIgnoreCase));
                
                if (tab == null)
                {
                    tab = AddTab(tradePair);
                }
                else
                {
                    SwitchToTab(tab);
                }
            }

            // Check for duplicate by TradeId
            var existingIndex = tab.Positions.FindIndex(p => 
                !string.IsNullOrEmpty(p.TradeId) && p.TradeId == trade.TradeId);
            
            if (existingIndex >= 0)
            {
                // Replace existing position
                tab.Positions[existingIndex] = trade;
                LogActivity($"Updated position: {trade.TradeId} ({trade.Underlying})", "INFO");
            }
            else
            {
                // Add new position
                tab.Positions.Add(trade);
                LogActivity($"Added position: {trade.Underlying} {trade.StructureType}", "INFO");
            }

            // Sync window-level positions if this is active tab
            if (tab.IsActive)
            {
                _positions.Clear();
                _positions.AddRange(tab.Positions);
            }

            // Refresh positions UI
            RefreshPositionsUI();
            
            // Recalculate aggregate greeks
            RecalculateAggregateGreeks();
        }

        /// <summary>
        /// Remove a position by TradeId.
        /// </summary>
        public void RemovePosition(string tradeId)
        {
            // Remove from active tab
            if (_activeTab != null)
            {
                var index = _activeTab.Positions.FindIndex(p => p.TradeId == tradeId);
                if (index >= 0)
                {
                    var trade = _activeTab.Positions[index];
                    _activeTab.Positions.RemoveAt(index);
                    
                    // Sync window-level
                    _positions.Clear();
                    _positions.AddRange(_activeTab.Positions);
                    
                    LogActivity($"Removed position: {tradeId} ({trade.Underlying})", "INFO");
                    
                    RefreshPositionsUI();
                    RecalculateAggregateGreeks();
                }
            }
        }

        /// <summary>
        /// Refresh the Positions observable collection from internal list.
        /// </summary>
        private void RefreshPositionsUI()
        {
            Positions.Clear();
            foreach (var trade in _positions)
            {
                if (trade.Legs?.Count > 0)
                {
                    var leg = trade.Legs[0];
                    double notional = leg.NotionalMM;
                    string direction = leg.Direction ?? "BUY";
                    double delta = CalculateLegDelta(leg, CurrentSpot) * (direction == "BUY" ? 1 : -1);
                    
                    Positions.Add(new PositionRow
                    {
                        TradeId = trade.TradeId ?? $"Trade{_positions.IndexOf(trade) + 1}",
                        Description = $"{leg.OptionType} {leg.Strike:F4} {leg.ExpiryDate:dd-MMM}",
                        NotionalMM = notional,
                        Direction = direction,
                        DeltaMM = delta / 1_000_000
                    });
                }
            }
        }

        /// <summary>
        /// Calculate delta for a single leg based on current spot.
        /// </summary>
        private double CalculateLegDelta(TradeStructure.OptionLeg leg, double spot)
        {
            if (leg.Strike <= 0 || spot <= 0) return 0;
            
            double notional = leg.NotionalMM * 1_000_000;
            double moneyness = spot / leg.Strike;
            
            // Simplified delta approximation
            double baseDelta;
            if (leg.OptionType == "CALL")
            {
                baseDelta = Math.Max(0, Math.Min(1, 0.5 + (moneyness - 1) * 3));
            }
            else // PUT
            {
                baseDelta = Math.Max(-1, Math.Min(0, -0.5 + (moneyness - 1) * 3));
            }
            
            return baseDelta * notional;
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
        /// Handle streaming spot rate updates from Bloomberg.
        /// Routes updates to all tabs that match the currency pair.
        /// </summary>
        private void OnBloombergSpotUpdate(string currencyPair, double spotRate, DateTime timestamp)
        {
            // Marshal to UI thread
            Dispatcher.BeginInvoke(() =>
            {
                // Find the tab(s) for this currency pair and update them
                foreach (var tab in Tabs)
                {
                    if (string.Equals(tab.CurrencyPair, currencyPair, StringComparison.OrdinalIgnoreCase))
                    {
                        tab.OnSpotUpdate(spotRate, timestamp);
                        
                        // If this is the active tab, also update window-level properties
                        if (tab.IsActive)
                        {
                            CurrentSpot = spotRate;
                            CurrentGamma = tab.CurrentGamma;
                            _accumulatedDelta = tab.AccumulatedDelta;
                            _lastSpotForDelta = tab.LastSpotForDelta;
                            
                            // Sync chart data
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
                        }
                    }
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

        /// <summary>
        /// Recalculate position based on spot movement.
        /// Uses gamma ladder interpolation to track delta accumulation.
        /// </summary>
        private void RecalculatePosition()
        {
            if (CurrentSpot <= 0) return;

            // Get gamma from ladder (interpolated at current spot)
            double ladderGamma = InterpolateGammaFromLadder(CurrentSpot);
            
            // Get aggregate gamma from parsed positions
            double positionsGamma = CalculatePositionsGamma();
            
            // Total gamma exposure
            CurrentGamma = ladderGamma + positionsGamma;
            
            // Calculate delta accumulation from spot movement
            if (_lastSpotForDelta > 0 && Math.Abs(_lastSpotForDelta - CurrentSpot) > 0.000001)
            {
                double spotMove = CurrentSpot - _lastSpotForDelta;
                double deltaChange = CurrentGamma * spotMove * 1_000_000; // Gamma is in MM, convert to units
                _accumulatedDelta += deltaChange;
            }
            _lastSpotForDelta = CurrentSpot;
            
            // Total delta = accumulated delta from spot moves
            CurrentDelta = _accumulatedDelta;
            
            // Refresh positions UI with updated deltas
            RefreshPositionsUI();
        }

        /// <summary>
        /// Recalculate aggregate Greeks from all positions (called when positions change).
        /// </summary>
        private void RecalculateAggregateGreeks()
        {
            // Reset accumulated delta when positions change
            // User should paste updated ladder or manually adjust
            RecalculatePosition();
        }

        /// <summary>
        /// Interpolate gamma from the gamma ladder at the given spot level.
        /// Returns gamma in millions (e.g., 18.20 = 18.2M CNH gamma).
        /// </summary>
        private double InterpolateGammaFromLadder(double spot)
        {
            var validRows = GammaLadder
                .Where(r => r.Spot.HasValue && r.Gamma.HasValue)
                .OrderBy(r => r.Spot.Value)
                .ToList();

            if (validRows.Count == 0) return 0;
            if (validRows.Count == 1) return validRows[0].Gamma.Value;

            // Find the two ladder rungs surrounding current spot
            for (int i = 0; i < validRows.Count - 1; i++)
            {
                double spotLow = validRows[i].Spot.Value;
                double spotHigh = validRows[i + 1].Spot.Value;

                if (spot >= spotLow && spot <= spotHigh)
                {
                    // Linear interpolation
                    double gammaLow = validRows[i].Gamma.Value;
                    double gammaHigh = validRows[i + 1].Gamma.Value;
                    double fraction = (spot - spotLow) / (spotHigh - spotLow);
                    return gammaLow + (gammaHigh - gammaLow) * fraction;
                }
            }

            // Spot is outside ladder range - extrapolate from nearest end
            if (spot < validRows.First().Spot.Value)
            {
                return validRows.First().Gamma.Value;
            }
            else
            {
                return validRows.Last().Gamma.Value;
            }
        }

        /// <summary>
        /// Interpolate delta from the gamma ladder at the given spot level.
        /// Returns delta in millions (e.g., 16.95 = 16.95M CNH/USD delta).
        /// Only works if ladder has delta column (3-column format).
        /// </summary>
        private double InterpolateDeltaFromLadder(double spot)
        {
            var validRows = GammaLadder
                .Where(r => r.Spot.HasValue && r.Delta.HasValue)
                .OrderBy(r => r.Spot.Value)
                .ToList();

            if (validRows.Count == 0) return 0;
            if (validRows.Count == 1) return validRows[0].Delta.Value;

            // Find the two ladder rungs surrounding current spot
            for (int i = 0; i < validRows.Count - 1; i++)
            {
                double spotLow = validRows[i].Spot.Value;
                double spotHigh = validRows[i + 1].Spot.Value;

                if (spot >= spotLow && spot <= spotHigh)
                {
                    // Linear interpolation
                    double deltaLow = validRows[i].Delta.Value;
                    double deltaHigh = validRows[i + 1].Delta.Value;
                    double fraction = (spot - spotLow) / (spotHigh - spotLow);
                    return deltaLow + (deltaHigh - deltaLow) * fraction;
                }
            }

            // Spot is outside ladder range - extrapolate from nearest end
            if (spot < validRows.First().Spot.Value)
            {
                return validRows.First().Delta.Value;
            }
            else
            {
                return validRows.Last().Delta.Value;
            }
        }

        /// <summary>
        /// Calculate aggregate gamma from parsed positions (simplified).
        /// </summary>
        private double CalculatePositionsGamma()
        {
            double totalGamma = 0;
            foreach (var trade in _positions)
            {
                if (trade.Legs == null) continue;
                foreach (var leg in trade.Legs)
                {
                    if (leg.Strike <= 0 || CurrentSpot <= 0) continue;
                    
                    double notional = leg.NotionalMM;
                    double moneyness = CurrentSpot / leg.Strike;
                    double atmDistance = Math.Abs(moneyness - 1);
                    
                    // Simplified gamma: highest ATM, decreases with distance
                    double gamma = Math.Max(0, (1 - atmDistance * 10)) * notional * 0.01;
                    double direction = leg.Direction == "BUY" ? 1 : -1;
                    totalGamma += gamma * direction;
                }
            }
            return totalGamma;
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

            // Draw strike lines for all positions
            foreach (var trade in _positions)
            {
                if (trade.Legs?.Count > 0)
                {
                    double strike = trade.Legs[0].Strike;
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
        }

        #endregion

        #region Gamma Ladder

        private void InitializeGammaLadder()
        {
            // Don't add empty rows - wait for paste
            GammaLadder.Clear();
        }

        private void AddLadderRow_Click(object sender, RoutedEventArgs e)
        {
            GammaLadder.Add(new GammaLadderRow());
        }

        private void ClearLadder_Click(object sender, RoutedEventArgs e)
        {
            GammaLadder.Clear();
            _accumulatedDelta = 0;
            _lastSpotForDelta = 0;
            CurrentDelta = 0;
            CurrentGamma = 0;
            LogActivity("Gamma ladder cleared, delta reset to zero", "INFO");
        }

        private void RemoveLadderRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is GammaLadderRow row)
            {
                GammaLadder.Remove(row);
                RecalculatePosition();
            }
        }

        /// <summary>
        /// Paste gamma ladder from clipboard.
        /// Expected format: Tab-separated with header line containing currency pair.
        /// Example:
        /// CNHSEK	
        /// Spot	mio CNH
        /// 1.324837	18.20
        /// 1.322224	16.48
        /// </summary>
        private void PasteLadder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    MessageBox.Show("Clipboard is empty. Copy your gamma ladder from Excel first.", 
                        "Paste Ladder", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string clipboardText = Clipboard.GetText();
                ParseAndLoadGammaLadder(clipboardText);
            }
            catch (Exception ex)
            {
                LogActivity($"Error pasting ladder: {ex.Message}", "ERROR");
                MessageBox.Show($"Error pasting ladder: {ex.Message}", "Paste Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Parse gamma ladder from tab-separated text.
        /// Supports 2-column (Spot, Gamma) or 3-column (Spot, Delta, Gamma) formats.
        /// </summary>
        private void ParseAndLoadGammaLadder(string text)
        {
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                MessageBox.Show("Ladder data must have at least a header and one data row.", 
                    "Invalid Format", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            GammaLadder.Clear();
            string detectedCcy = null;
            string detectedDeltaCcy = null;
            int rowsParsed = 0;
            bool hasThreeColumns = false;

            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                
                // Try to parse as data row - 3-column format: Spot, Delta, Gamma
                if (parts.Length >= 3)
                {
                    if (TryParseDouble(parts[0], out double spot) && 
                        TryParseDouble(parts[1], out double delta) &&
                        TryParseDouble(parts[2], out double gamma))
                    {
                        GammaLadder.Add(new GammaLadderRow { Spot = spot, Delta = delta, Gamma = gamma });
                        rowsParsed++;
                        hasThreeColumns = true;
                        continue;
                    }
                }
                // Try 2-column format: Spot, Gamma
                else if (parts.Length >= 2)
                {
                    if (TryParseDouble(parts[0], out double spot) && 
                        TryParseDouble(parts[1], out double gamma))
                    {
                        GammaLadder.Add(new GammaLadderRow { Spot = spot, Delta = null, Gamma = gamma });
                        rowsParsed++;
                        continue;
                    }
                }

                // Check if this is a header with currency pair (e.g., "CNHSEK" or "USDSEK")
                if (detectedCcy == null)
                {
                    var match = Regex.Match(line, @"\b([A-Z]{6})\b");
                    if (match.Success)
                    {
                        detectedCcy = match.Groups[1].Value;
                    }
                }
                
                // Extract delta currency from "mio XXX" pattern (e.g., "mio CNH", "mio USD")
                if (detectedDeltaCcy == null)
                {
                    var mioMatch = Regex.Match(line, @"mio\s+([A-Z]{3})", RegexOptions.IgnoreCase);
                    if (mioMatch.Success)
                    {
                        detectedDeltaCcy = mioMatch.Groups[1].Value.ToUpper();
                    }
                }
            }

            if (rowsParsed == 0)
            {
                MessageBox.Show("No valid data rows found. Expected format:\nSpot[TAB]Delta[TAB]Gamma or Spot[TAB]Gamma\n1.3248[TAB]16.9[TAB]18.20", 
                    "Invalid Format", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Set currency pair if detected and not already set
            if (!string.IsNullOrEmpty(detectedCcy) && string.IsNullOrEmpty(CurrencyPair))
            {
                // Add or switch to tab for this currency pair
                var tab = GetOrAddTab(detectedCcy);
                
                // Start spot monitoring
                StartSpotMonitoring();
            }
            else if (!string.IsNullOrEmpty(detectedCcy) && _activeTab != null)
            {
                // Update gamma ladder on active tab
            }

            // Set delta currency from ladder header (e.g., "mio CNH" -> "CNH")
            // This determines the currency for threshold comparison
            if (!string.IsNullOrEmpty(detectedDeltaCcy))
            {
                HedgeThresholdCcy = detectedDeltaCcy;
            }
            else if (!string.IsNullOrEmpty(detectedCcy))
            {
                // Default to base currency of pair (first 3 chars)
                HedgeThresholdCcy = detectedCcy.Substring(0, 3);
            }

            // Set trading limits based on ladder range
            var validRows = GammaLadder.Where(r => r.Spot.HasValue).ToList();
            if (validRows.Count > 0)
            {
                double minSpot = validRows.Min(r => r.Spot.Value);
                double maxSpot = validRows.Max(r => r.Spot.Value);
                LowerTradingLimit = minSpot;
                UpperTradingLimit = maxSpot;
            }

            // Sync gamma ladder to active tab
            if (_activeTab != null)
            {
                _activeTab.GammaLadder.Clear();
                foreach (var row in GammaLadder)
                {
                    _activeTab.GammaLadder.Add(new GammaLadderRow 
                    { 
                        Spot = row.Spot, 
                        Delta = row.Delta,
                        Gamma = row.Gamma 
                    });
                }
                
                // Set delta currency on tab
                if (!string.IsNullOrEmpty(detectedDeltaCcy))
                {
                    _activeTab.DeltaCurrency = detectedDeltaCcy;
                }
            }

            // Initialize delta from ladder if we have 3-column format
            double initialDelta = 0;
            if (hasThreeColumns && CurrentSpot > 0)
            {
                initialDelta = InterpolateDeltaFromLadder(CurrentSpot);
                LogActivity($"Initialized delta from ladder: {initialDelta:F2} mio {HedgeThresholdCcy} at spot {CurrentSpot:F4}", "INFO");
            }
            
            // Set accumulated delta (convert from millions to actual units)
            _accumulatedDelta = initialDelta * 1_000_000;
            _lastSpotForDelta = CurrentSpot;
            
            if (_activeTab != null)
            {
                _activeTab.AccumulatedDelta = _accumulatedDelta;
                _activeTab.LastSpotForDelta = CurrentSpot;
            }

            LogActivity($"Loaded gamma ladder: {rowsParsed} rows" + 
                (detectedCcy != null ? $" ({detectedCcy})" : "") +
                (hasThreeColumns ? " with delta column" : ""), "INFO");
            LogActivity($"Ladder range: {LowerTradingLimit:F4} - {UpperTradingLimit:F4}", "INFO");
            LogActivity($"Delta currency: {HedgeThresholdCcy}", "INFO");
            
            // Recalculate position with new ladder
            RecalculatePosition();
        }

        /// <summary>
        /// Try to parse a double from string, handling various formats.
        /// </summary>
        private bool TryParseDouble(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            // Remove spaces and handle negative values
            text = text.Trim().Replace(" ", "");
            
            // Try invariant culture first (1.234), then current culture
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return true;
            
            return false;
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

        #region Position Management UI

        private void RemovePosition_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string tradeId)
            {
                RemovePosition(tradeId);
            }
        }

        #endregion

        #region UI Helpers

        private void UpdateWindowTitle()
        {
            if (Tabs.Count == 0)
            {
                Title = "Gamma Hedger - No Positions";
            }
            else if (Tabs.Count == 1)
            {
                Title = $"Gamma Hedger - {CurrencyPair ?? "No Position"}";
            }
            else
            {
                // Show active pair and count
                Title = $"Gamma Hedger - {CurrencyPair} ({Tabs.Count} pairs)";
            }
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

        private void AddNewTab_Click(object sender, RoutedEventArgs e)
        {
            // Create a simple input dialog
            var dialog = new Window
            {
                Title = "Add Currency Pair",
                Width = 350,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59))
            };
            
            var panel = new StackPanel { Margin = new Thickness(20) };
            var label = new TextBlock 
            { 
                Text = "Enter currency pair (e.g., USDJPY, EURUSD):", 
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            };
            var textBox = new System.Windows.Controls.TextBox 
            { 
                Width = 200, 
                FontSize = 14, 
                Padding = new Thickness(5)
            };
            var buttonPanel = new StackPanel 
            { 
                Orientation = System.Windows.Controls.Orientation.Horizontal, 
                Margin = new Thickness(0, 15, 0, 0), 
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right 
            };
            var okButton = new System.Windows.Controls.Button { Content = "OK", Width = 70, Height = 28, Margin = new Thickness(0, 0, 10, 0) };
            var cancelButton = new System.Windows.Controls.Button { Content = "Cancel", Width = 70, Height = 28 };
            
            okButton.Click += (s, args) => { dialog.DialogResult = true; };
            cancelButton.Click += (s, args) => { dialog.DialogResult = false; };
            textBox.KeyDown += (s, args) => { if (args.Key == System.Windows.Input.Key.Enter) dialog.DialogResult = true; };
            
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            panel.Children.Add(label);
            panel.Children.Add(textBox);
            panel.Children.Add(buttonPanel);
            dialog.Content = panel;
            
            textBox.Focus();
            
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(textBox.Text))
                return;
                
            string pair = textBox.Text.Trim().ToUpperInvariant();
            
            // Validate format
            if (pair.Length < 6)
            {
                MessageBox.Show("Please enter a valid currency pair (e.g., USDJPY)", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Check if tab already exists
            var existingTab = Tabs.FirstOrDefault(t => string.Equals(t.CurrencyPair, pair, StringComparison.OrdinalIgnoreCase));
            if (existingTab != null)
            {
                SwitchToTab(existingTab);
                LogActivity($"Switched to existing tab: {pair}", "INFO");
                return;
            }
            
            // Add new tab
            AddTab(pair);
            LogActivity($"Added new currency pair tab: {pair}", "INFO");
        }

        private void Tab_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is GammaHedgerTab tab)
            {
                SwitchToTab(tab);
            }
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is GammaHedgerTab tab)
            {
                // Unsubscribe from Bloomberg
                tab.UnsubscribeFromBloomberg();
                
                Tabs.Remove(tab);
                LogActivity($"Closed tab: {tab.CurrencyPair}", "INFO");
                
                if (Tabs.Count == 0)
                {
                    StopSpotMonitoring();
                    Status = "Incomplete";
                    _positions.Clear();
                    Positions.Clear();
                    GammaLadder.Clear();
                    _accumulatedDelta = 0;
                    CurrentDelta = 0;
                    CurrentGamma = 0;
                    CurrencyPair = null;
                    _activeTab = null;
                }
                else if (tab.IsActive)
                {
                    // Switch to another tab
                    SwitchToTab(Tabs[0]);
                }
            }
        }

        /// <summary>
        /// Add a new currency pair tab and switch to it.
        /// </summary>
        private GammaHedgerTab AddTab(string currencyPair)
        {
            var tab = new GammaHedgerTab(_bloombergService)
            {
                CurrencyPair = currencyPair,
                IsActive = false
            };
            
            Tabs.Add(tab);
            
            // Subscribe to Bloomberg spot feed
            tab.SubscribeToBloomberg();
            
            // Switch to the new tab
            SwitchToTab(tab);
            
            return tab;
        }

        /// <summary>
        /// Switch to a different tab, updating the UI bindings.
        /// </summary>
        private void SwitchToTab(GammaHedgerTab tab)
        {
            if (tab == null) return;
            
            // Deactivate all tabs
            foreach (var t in Tabs)
                t.IsActive = false;
            
            // Activate selected tab
            tab.IsActive = true;
            _activeTab = tab;
            
            // Update window-level properties from tab
            CurrencyPair = tab.CurrencyPair;
            CurrentSpot = tab.CurrentSpot;
            CurrentDelta = tab.CurrentDelta;
            CurrentGamma = tab.CurrentGamma;
            _accumulatedDelta = tab.AccumulatedDelta;
            Status = tab.Status;
            IsHedgingActive = tab.IsHedgingActive;
            
            // Sync collections
            _positions.Clear();
            _positions.AddRange(tab.Positions);
            
            Positions.Clear();
            foreach (var p in tab.PositionRows)
                Positions.Add(p);
            
            GammaLadder.Clear();
            foreach (var r in tab.GammaLadder)
                GammaLadder.Add(r);
            
            SpotChartData.Clear();
            foreach (var d in tab.SpotChartData)
                SpotChartData.Add(d);
            
            // Set hedge threshold currency
            if (!string.IsNullOrEmpty(tab.CurrencyPair) && tab.CurrencyPair.Length >= 3)
            {
                HedgeThresholdCcy = tab.CurrencyPair.Substring(0, 3);
            }
            
            UpdateWindowTitle();
            LogActivity($"Switched to {tab.CurrencyPair}", "INFO");
        }

        /// <summary>
        /// Get or add a tab for a currency pair.
        /// </summary>
        public GammaHedgerTab GetOrAddTab(string currencyPair)
        {
            var existingTab = Tabs.FirstOrDefault(t => 
                string.Equals(t.CurrencyPair, currencyPair, StringComparison.OrdinalIgnoreCase));
            
            if (existingTab != null)
            {
                SwitchToTab(existingTab);
                return existingTab;
            }
            
            return AddTab(currencyPair);
        }

        #endregion

        #region Window Lifecycle

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe all tabs from Bloomberg
            foreach (var tab in Tabs)
            {
                tab.UnsubscribeFromBloomberg();
            }
            
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

    /// <summary>
    /// Represents a tab/currency pair in the Gamma Hedger with its own state.
    /// Each tab maintains its own positions, gamma ladder, spot data, and Bloomberg subscription.
    /// </summary>
    public class GammaHedgerTab : INotifyPropertyChanged
    {
        private readonly BloombergService _bloombergService;
        private bool _isBloombergSubscribed;
        
        public GammaHedgerTab(BloombergService bloombergService)
        {
            _bloombergService = bloombergService;
            Positions = new List<TradeStructure>();
            PositionRows = new ObservableCollection<PositionRow>();
            GammaLadder = new ObservableCollection<GammaLadderRow>();
            SpotChartData = new ObservableCollection<SpotDataPoint>();
        }

        private string _currencyPair;
        public string CurrencyPair
        {
            get => _currencyPair;
            set 
            { 
                var oldPair = _currencyPair;
                _currencyPair = value; 
                OnPropertyChanged();
                
                // Re-subscribe to Bloomberg if pair changed
                if (!string.Equals(oldPair, value, StringComparison.OrdinalIgnoreCase))
                {
                    UpdateBloombergSubscription(oldPair, value);
                }
            }
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

        // Per-tab state
        public List<TradeStructure> Positions { get; }
        public ObservableCollection<PositionRow> PositionRows { get; }
        public ObservableCollection<GammaLadderRow> GammaLadder { get; }
        public ObservableCollection<SpotDataPoint> SpotChartData { get; }

        private double _currentSpot;
        public double CurrentSpot
        {
            get => _currentSpot;
            set { _currentSpot = value; OnPropertyChanged(); }
        }

        private double _currentDelta;
        public double CurrentDelta
        {
            get => _currentDelta;
            set { _currentDelta = value; OnPropertyChanged(); }
        }

        private double _currentGamma;
        public double CurrentGamma
        {
            get => _currentGamma;
            set { _currentGamma = value; OnPropertyChanged(); }
        }

        private double _accumulatedDelta;
        public double AccumulatedDelta
        {
            get => _accumulatedDelta;
            set { _accumulatedDelta = value; OnPropertyChanged(); }
        }

        private double _lastSpotForDelta;
        public double LastSpotForDelta
        {
            get => _lastSpotForDelta;
            set { _lastSpotForDelta = value; OnPropertyChanged(); }
        }

        private bool _isHedgingActive;
        public bool IsHedgingActive
        {
            get => _isHedgingActive;
            set { _isHedgingActive = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// The currency for delta/gamma values (e.g., "CNH", "USD", "EUR").
        /// Extracted from ladder header "mio XXX" pattern.
        /// </summary>
        private string _deltaCurrency;
        public string DeltaCurrency
        {
            get => _deltaCurrency;
            set { _deltaCurrency = value; OnPropertyChanged(); }
        }

        private string _status = "Incomplete";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Subscribe to Bloomberg spot feed for this tab's currency pair.
        /// </summary>
        public void SubscribeToBloomberg()
        {
            if (_isBloombergSubscribed || string.IsNullOrEmpty(CurrencyPair)) return;
            
            try
            {
                if (_bloombergService.SubscribeToSpot(CurrencyPair))
                {
                    _isBloombergSubscribed = true;
                    Console.WriteLine($"[GammaHedgerTab] Bloomberg subscription started for {CurrencyPair}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GammaHedgerTab] Bloomberg subscription failed for {CurrencyPair}: {ex.Message}");
            }
        }

        /// <summary>
        /// Unsubscribe from Bloomberg spot feed.
        /// </summary>
        public void UnsubscribeFromBloomberg()
        {
            if (!_isBloombergSubscribed || string.IsNullOrEmpty(CurrencyPair)) return;
            
            try
            {
                _bloombergService.UnsubscribeFromSpot(CurrencyPair);
                _isBloombergSubscribed = false;
                Console.WriteLine($"[GammaHedgerTab] Bloomberg unsubscribed for {CurrencyPair}");
            }
            catch { }
        }

        private void UpdateBloombergSubscription(string oldPair, string newPair)
        {
            // Unsubscribe from old
            if (!string.IsNullOrEmpty(oldPair) && _isBloombergSubscribed)
            {
                try { _bloombergService.UnsubscribeFromSpot(oldPair); } catch { }
            }
            
            // Subscribe to new
            _isBloombergSubscribed = false;
            SubscribeToBloomberg();
        }

        /// <summary>
        /// Handle spot update from Bloomberg.
        /// </summary>
        public void OnSpotUpdate(double spotRate, DateTime timestamp)
        {
            // Track delta accumulation
            if (LastSpotForDelta > 0 && Math.Abs(LastSpotForDelta - spotRate) > 0.000001)
            {
                double spotMove = spotRate - LastSpotForDelta;
                double deltaChange = CurrentGamma * spotMove * 1_000_000;
                AccumulatedDelta += deltaChange;
            }
            
            CurrentSpot = spotRate;
            LastSpotForDelta = spotRate;
            
            // Update gamma from ladder if available
            if (GammaLadder.Count > 0)
            {
                CurrentGamma = InterpolateGammaFromLadder(spotRate);
            }
            
            // Add to chart data
            SpotChartData.Add(new SpotDataPoint { Time = timestamp, Spot = spotRate });
            
            // Keep chart data limited
            while (SpotChartData.Count > 300)
                SpotChartData.RemoveAt(0);
        }

        /// <summary>
        /// Interpolate gamma from the gamma ladder at the current spot level.
        /// </summary>
        private double InterpolateGammaFromLadder(double spot)
        {
            if (GammaLadder.Count == 0) return 0;

            var sortedLadder = GammaLadder
                .Where(r => r.Spot.HasValue && r.Gamma.HasValue)
                .OrderBy(r => r.Spot.Value)
                .ToList();

            if (sortedLadder.Count == 0) return 0;

            // If below lowest point
            if (spot <= sortedLadder[0].Spot.Value)
                return sortedLadder[0].Gamma.Value;

            // If above highest point
            if (spot >= sortedLadder[^1].Spot.Value)
                return sortedLadder[^1].Gamma.Value;

            // Find bracketing points and interpolate
            for (int i = 0; i < sortedLadder.Count - 1; i++)
            {
                if (spot >= sortedLadder[i].Spot.Value && spot <= sortedLadder[i + 1].Spot.Value)
                {
                    double x0 = sortedLadder[i].Spot.Value;
                    double x1 = sortedLadder[i + 1].Spot.Value;
                    double y0 = sortedLadder[i].Gamma.Value;
                    double y1 = sortedLadder[i + 1].Gamma.Value;

                    // Linear interpolation
                    double t = (spot - x0) / (x1 - x0);
                    return y0 + t * (y1 - y0);
                }
            }

            return 0;
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

        /// <summary>
        /// Delta at this spot level (in millions of delta currency).
        /// </summary>
        private double? _delta;
        public double? Delta
        {
            get => _delta;
            set { _delta = value; OnPropertyChanged(); }
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

    /// <summary>
    /// Represents a position row in the positions list.
    /// </summary>
    public class PositionRow : INotifyPropertyChanged
    {
        private string _tradeId;
        public string TradeId
        {
            get => _tradeId;
            set { _tradeId = value; OnPropertyChanged(); }
        }

        private string _description;
        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        private double _notionalMM;
        public double NotionalMM
        {
            get => _notionalMM;
            set { _notionalMM = value; OnPropertyChanged(); }
        }

        private string _direction;
        public string Direction
        {
            get => _direction;
            set { _direction = value; OnPropertyChanged(); }
        }

        private double _deltaMM;
        public double DeltaMM
        {
            get => _deltaMM;
            set { _deltaMM = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    #endregion
}
