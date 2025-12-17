using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace FXOAiTranslate.WPF.ViewModels
{
    /// <summary>
/// ViewModel for a single LP quote in the ladder
    /// </summary>
    public class LPQuoteViewModel : INotifyPropertyChanged
    {
        private string _lpName;
        private double _bidPremium;
        private double _offerPremium;
        private double _bidVol;
  private double _offerVol;
        private double _notional;
        private double _delta;
 private bool _isEnabled = true;
  private bool _isBestBid;
  private bool _isBestOffer;
    private int _remainingSeconds;
private string _quoteId;
        private DateTime _quoteTime;
  private DispatcherTimer _timer;

        public string LPName
        {
      get => _lpName;
      set { _lpName = value; OnPropertyChanged(); }
    }

      public double BidPremium
        {
   get => _bidPremium;
            set { _bidPremium = value; OnPropertyChanged(); OnPropertyChanged(nameof(BidDisplay)); }
        }

   public double OfferPremium
        {
   get => _offerPremium;
            set { _offerPremium = value; OnPropertyChanged(); OnPropertyChanged(nameof(OfferDisplay)); }
        }

public double BidVol
        {
  get => _bidVol;
            set { _bidVol = value; OnPropertyChanged(); }
        }

        public double OfferVol
        {
            get => _offerVol;
            set { _offerVol = value; OnPropertyChanged(); }
        }

        public double Notional
  {
            get => _notional;
       set { _notional = value; OnPropertyChanged(); OnPropertyChanged(nameof(NotionalDisplay)); }
        }

        public double Delta
 {
            get => _delta;
    set { _delta = value; OnPropertyChanged(); OnPropertyChanged(nameof(DeltaDisplay)); }
   }

        public bool IsEnabled
        {
            get => _isEnabled;
      set { _isEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(RowOpacity)); }
  }

        public bool IsBestBid
        {
            get => _isBestBid;
      set { _isBestBid = value; OnPropertyChanged(); }
        }

        public bool IsBestOffer
        {
  get => _isBestOffer;
         set { _isBestOffer = value; OnPropertyChanged(); }
    }

public int RemainingSeconds
        {
  get => _remainingSeconds;
    set 
      { 
     _remainingSeconds = value; 
   OnPropertyChanged(); 
        OnPropertyChanged(nameof(TimerDisplay));
     OnPropertyChanged(nameof(TimerState));
   OnPropertyChanged(nameof(RowOpacity));
  }
        }

        public string QuoteId
        {
          get => _quoteId;
        set { _quoteId = value; OnPropertyChanged(); }
        }

        public DateTime QuoteTime
        {
            get => _quoteTime;
      set { _quoteTime = value; OnPropertyChanged(); }
        }

        // Display properties
        public string BidDisplay => BidPremium > 0 ? BidPremium.ToString("F2") : "--";
        public string OfferDisplay => OfferPremium > 0 ? OfferPremium.ToString("F2") : "--";
        public string NotionalDisplay => Notional > 0 ? $"{(Notional / 1000):F0}k" : "--";
   public string DeltaDisplay => Delta > 0 ? $"{Delta:F0}p" : "--";
      
   public string TimerDisplay
        {
          get
         {
             if (RemainingSeconds <= 0) return "EXPIRED";
     int mins = RemainingSeconds / 60;
             int secs = RemainingSeconds % 60;
         return $"{mins}:{secs:D2}";
  }
        }

   public string TimerState
        {
    get
 {
          if (RemainingSeconds <= 0) return "Expired";
     if (RemainingSeconds <= 10) return "Urgent";
  if (RemainingSeconds <= 30) return "Warning";
      if (RemainingSeconds <= 90) return "Aging";
       return "Fresh";
   }
        }

        public double RowOpacity
        {
            get
            {
                if (!IsEnabled) return 0.2;
       if (RemainingSeconds <= 0) return 0.35;
  if (RemainingSeconds <= 10) return 0.6;
                if (RemainingSeconds <= 30) return 0.85;
           return 1.0;
            }
}

        /// <summary>
        /// Start the countdown timer
        /// </summary>
      public void StartTimer(int validitySeconds)
    {
      StopTimer();
            RemainingSeconds = validitySeconds;

  _timer = new DispatcherTimer
 {
     Interval = TimeSpan.FromSeconds(1)
   };
            _timer.Tick += (s, e) =>
            {
        if (RemainingSeconds > 0)
                {
           RemainingSeconds--;
      }
   else
       {
     StopTimer();
  }
          };
            _timer.Start();
        }

        public void StopTimer()
    {
            _timer?.Stop();
            _timer = null;
      }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// ViewModel for the entire FX Aggregator window
    /// </summary>
    public class FXAggregatorViewModel : INotifyPropertyChanged
    {
        private string _underlying;
private string _structureType;
        private double _spotRate;
    private string _expiry;
        private string _tenor;
      private double _strike;
        private double _notionalMM;
        private string _optionType;
        private string _direction;
        private bool _isRfqActive;

    // Best prices
        private double _bestBidPremium;
        private double _bestOfferPremium;
        private string _bestBidLP;
private string _bestOfferLP;
        private int _bestBidTimer;
        private int _bestOfferTimer;

public ObservableCollection<LPQuoteViewModel> LPQuotes { get; } = new ObservableCollection<LPQuoteViewModel>();

 public string Underlying
        {
            get => _underlying;
        set { _underlying = value; OnPropertyChanged(); OnPropertyChanged(nameof(BaseCurrency)); OnPropertyChanged(nameof(TermCurrency)); }
        }

        public string BaseCurrency => Underlying?.Length >= 3 ? Underlying.Substring(0, 3) : "EUR";
        public string TermCurrency => Underlying?.Length >= 6 ? Underlying.Substring(3, 3) : "USD";

        public string StructureType
     {
        get => _structureType;
       set { _structureType = value; OnPropertyChanged(); OnPropertyChanged(nameof(StructureName)); }
        }

    public string StructureName
        {
    get
  {
          return StructureType switch
        {
     "1" => "Vanilla Option",
 "5" => "Risk Reversal",
           "8" => "Call Spread",
    "9" => "Put Spread",
    "10" => "Seagull",
           _ => "Multi-Leg Option"
  };
          }
        }

        public double SpotRate
        {
get => _spotRate;
    set { _spotRate = value; OnPropertyChanged(); OnPropertyChanged(nameof(SpotDisplay)); }
  }

        public string SpotDisplay => SpotRate > 0 ? SpotRate.ToString(Underlying?.Contains("JPY") == true ? "F2" : "F4") : "--";

      public string Expiry
        {
            get => _expiry;
            set { _expiry = value; OnPropertyChanged(); }
    }

        public string Tenor
{
 get => _tenor;
            set { _tenor = value; OnPropertyChanged(); }
        }

        public double Strike
        {
          get => _strike;
     set { _strike = value; OnPropertyChanged(); OnPropertyChanged(nameof(StrikeDisplay)); }
    }

        public string StrikeDisplay => Strike > 0 ? Strike.ToString(Underlying?.Contains("JPY") == true ? "F2" : "F4") : "--";

  public double NotionalMM
        {
    get => _notionalMM;
 set { _notionalMM = value; OnPropertyChanged(); OnPropertyChanged(nameof(NotionalDisplay)); }
        }

        public string NotionalDisplay => NotionalMM > 0 ? $"{NotionalMM:F0},000,000" : "--";

        public string OptionType
        {
     get => _optionType;
   set { _optionType = value; OnPropertyChanged(); OnPropertyChanged(nameof(OptionTypeDisplay)); }
        }

        public string OptionTypeDisplay
{
            get
{
          if (string.IsNullOrEmpty(OptionType)) return "--";
        bool isCall = OptionType.ToUpper().StartsWith("C");
     return isCall ? $"{BaseCurrency} Call / {TermCurrency} Put" : $"{BaseCurrency} Put / {TermCurrency} Call";
  }
        }

        public string Direction
        {
        get => _direction;
      set { _direction = value; OnPropertyChanged(); }
        }

        public bool IsRfqActive
        {
          get => _isRfqActive;
  set { _isRfqActive = value; OnPropertyChanged(); }
}

        // Best prices for hero tiles
    public double BestBidPremium
        {
      get => _bestBidPremium;
  set { _bestBidPremium = value; OnPropertyChanged(); OnPropertyChanged(nameof(BestBidDisplay)); }
        }

        public double BestOfferPremium
        {
     get => _bestOfferPremium;
    set { _bestOfferPremium = value; OnPropertyChanged(); OnPropertyChanged(nameof(BestOfferDisplay)); }
        }

    public string BestBidDisplay => BestBidPremium > 0 ? BestBidPremium.ToString("F2") : "RFQ";
      public string BestOfferDisplay => BestOfferPremium > 0 ? BestOfferPremium.ToString("F2") : "RFQ";

        public string BestBidLP
        {
          get => _bestBidLP;
         set { _bestBidLP = value; OnPropertyChanged(); }
        }

        public string BestOfferLP
   {
      get => _bestOfferLP;
      set { _bestOfferLP = value; OnPropertyChanged(); }
        }

        public int BestBidTimer
        {
  get => _bestBidTimer;
      set { _bestBidTimer = value; OnPropertyChanged(); OnPropertyChanged(nameof(BestBidTimerDisplay)); }
        }

        public int BestOfferTimer
  {
     get => _bestOfferTimer;
   set { _bestOfferTimer = value; OnPropertyChanged(); OnPropertyChanged(nameof(BestOfferTimerDisplay)); }
        }

    public string BestBidTimerDisplay => BestBidTimer > 0 ? $"{BestBidTimer / 60}:{BestBidTimer % 60:D2}" : "--";
 public string BestOfferTimerDisplay => BestOfferTimer > 0 ? $"{BestOfferTimer / 60}:{BestOfferTimer % 60:D2}" : "--";

     public int EnabledLPCount => LPQuotes.Count; // TODO: filter by IsEnabled

        /// <summary>
/// Recalculate best BID/OFFER from enabled LPs
        /// </summary>
        public void RecalculateBestPrices()
   {
            double bestBid = 0;
         double bestOffer = double.MaxValue;
            string bestBidLP = null;
    string bestOfferLP = null;
       int bestBidTimer = 0;
 int bestOfferTimer = 0;

            foreach (var lp in LPQuotes)
       {
                if (!lp.IsEnabled || lp.RemainingSeconds <= 0) continue;

         // Best BID = highest
       if (lp.BidPremium > bestBid)
      {
         bestBid = lp.BidPremium;
              bestBidLP = lp.LPName;
          bestBidTimer = lp.RemainingSeconds;
                }

             // Best OFFER = lowest
if (lp.OfferPremium > 0 && lp.OfferPremium < bestOffer)
                {
          bestOffer = lp.OfferPremium;
   bestOfferLP = lp.LPName;
        bestOfferTimer = lp.RemainingSeconds;
    }
    }

 // Update best price indicators on each LP
   foreach (var lp in LPQuotes)
          {
        lp.IsBestBid = lp.LPName == bestBidLP;
   lp.IsBestOffer = lp.LPName == bestOfferLP;
            }

            BestBidPremium = bestBid;
            BestOfferPremium = bestOffer == double.MaxValue ? 0 : bestOffer;
            BestBidLP = bestBidLP ?? "--";
  BestOfferLP = bestOfferLP ?? "--";
            BestBidTimer = bestBidTimer;
        BestOfferTimer = bestOfferTimer;
        }

        public event PropertyChangedEventHandler PropertyChanged;

 protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
