using System.ComponentModel;
using System.Windows.Media;

// Aliases to resolve WinForms/WPF type conflicts
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace FXOAiTranslate.WPF.Views
{
    /// <summary>
    /// View model for LP quote rows in the ladder (WPF version)
    /// </summary>
    public class LPQuoteRow : INotifyPropertyChanged
    {
        private string _lpName;
        private string _bidVol;
        private string _bidSecondary;
        private string _offerVol;
  private string _offerSecondary;
      private double _opacity;
        private bool _isBestBid;
        private bool _isBestOffer;
        private bool _isEnabled;
        private Brush _bidForeground;
        private Brush _offerForeground;
   private Brush _bidBackground;
        private Brush _offerBackground;
        private Brush _bidBorderBrush;
        private Brush _offerBorderBrush;
        private Brush _lpForeground;

        public LPQuoteRow(string lpName, string bidVol = "-", string offerVol = "-")
        {
        _lpName = lpName;
            _bidVol = bidVol;
          _offerVol = offerVol;
   _opacity = 1.0;
  _isEnabled = true;
      UpdateColors();
        }

      public string LPName
        {
       get => _lpName;
     set
       {
        if (_lpName != value)
         {
        _lpName = value;
   OnPropertyChanged(nameof(LPName));
     }
  }
        }

    public string BidVol
        {
            get => _bidVol;
set
   {
         if (_bidVol != value)
      {
     _bidVol = value;
         OnPropertyChanged(nameof(BidVol));
      }
          }
 }

     public string BidSecondary
      {
   get => _bidSecondary;
            set
            {
     if (_bidSecondary != value)
                {
        _bidSecondary = value;
        OnPropertyChanged(nameof(BidSecondary));
                }
   }
      }

  public string OfferVol
        {
  get => _offerVol;
          set
        {
            if (_offerVol != value)
   {
                 _offerVol = value;
                    OnPropertyChanged(nameof(OfferVol));
   }
     }
   }

        public string OfferSecondary
        {
 get => _offerSecondary;
      set
            {
    if (_offerSecondary != value)
 {
      _offerSecondary = value;
  OnPropertyChanged(nameof(OfferSecondary));
    }
       }
      }

  public double Opacity
     {
            get => _opacity;
            set
         {
        if (_opacity != value)
{
          _opacity = value;
             OnPropertyChanged(nameof(Opacity));
     }
         }
        }

        public bool IsBestBid
        {
    get => _isBestBid;
    set
   {
              if (_isBestBid != value)
   {
         _isBestBid = value;
   OnPropertyChanged(nameof(IsBestBid));
         UpdateColors();
 }
     }
        }

     public bool IsBestOffer
        {
            get => _isBestOffer;
     set
       {
      if (_isBestOffer != value)
     {
          _isBestOffer = value;
         OnPropertyChanged(nameof(IsBestOffer));
         UpdateColors();
          }
}
        }

        public bool IsEnabled
      {
     get => _isEnabled;
            set
            {
         if (_isEnabled != value)
 {
         _isEnabled = value;
      OnPropertyChanged(nameof(IsEnabled));
        UpdateColors();
        }
    }
        }

        public Brush BidForeground
        {
     get => _bidForeground;
      private set
       {
         if (_bidForeground != value)
 {
        _bidForeground = value;
             OnPropertyChanged(nameof(BidForeground));
    }
     }
        }

        public Brush OfferForeground
        {
       get => _offerForeground;
            private set
    {
     if (_offerForeground != value)
    {
       _offerForeground = value;
          OnPropertyChanged(nameof(OfferForeground));
       }
        }
  }

   public Brush BidBackground
        {
  get => _bidBackground;
          private set
        {
    if (_bidBackground != value)
        {
 _bidBackground = value;
 OnPropertyChanged(nameof(BidBackground));
    }
    }
        }

        public Brush OfferBackground
        {
        get => _offerBackground;
          private set
    {
 if (_offerBackground != value)
    {
        _offerBackground = value;
    OnPropertyChanged(nameof(OfferBackground));
                }
            }
        }

    public Brush BidBorderBrush
        {
        get => _bidBorderBrush;
       private set
     {
             if (_bidBorderBrush != value)
     {
    _bidBorderBrush = value;
      OnPropertyChanged(nameof(BidBorderBrush));
      }
          }
        }

        public Brush OfferBorderBrush
 {
            get => _offerBorderBrush;
            private set
          {
 if (_offerBorderBrush != value)
      {
   _offerBorderBrush = value;
        OnPropertyChanged(nameof(OfferBorderBrush));
   }
  }
 }

    public Brush LPForeground
  {
            get => _lpForeground;
         private set
            {
    if (_lpForeground != value)
    {
      _lpForeground = value;
               OnPropertyChanged(nameof(LPForeground));
 }
        }
        }

        private void UpdateColors()
        {
   // Best bid/offer highlighting
         BidForeground = _isBestBid ? new SolidColorBrush(Color.FromRgb(34, 197, 94)) : Brushes.White;
    OfferForeground = _isBestOffer ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) : Brushes.White;

            BidBackground = _isBestBid ? new SolidColorBrush(Color.FromArgb(26, 34, 197, 94)) : Brushes.Transparent;
    OfferBackground = _isBestOffer ? new SolidColorBrush(Color.FromArgb(26, 239, 68, 68)) : Brushes.Transparent;

            BidBorderBrush = _isBestBid ? new SolidColorBrush(Color.FromRgb(34, 197, 94)) : Brushes.Transparent;
 OfferBorderBrush = _isBestOffer ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) : Brushes.Transparent;

            // LP name color based on enabled state
 LPForeground = _isEnabled ? Brushes.White : new SolidColorBrush(Color.FromRgb(100, 116, 139));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
         PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
     }
    }
}
