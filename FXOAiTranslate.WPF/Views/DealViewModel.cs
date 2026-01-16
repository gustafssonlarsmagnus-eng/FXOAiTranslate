using System;
using System.ComponentModel;
using System.Windows.Media;

// Alias to resolve WinForms/WPF type conflicts
using Brush = System.Windows.Media.Brush;

namespace FXOAiTranslate.WPF.Views
{
    /// <summary>
 /// View model for displaying trade deals in the blotter
    /// </summary>
    public class DealViewModel : INotifyPropertyChanged
 {
        private bool _isExpanded;
        private string _status;
        private Brush _statusBackground;
        private Brush _statusForeground;

        public string Time { get; set; }
      public string Instrument { get; set; }
  public string LP { get; set; }
        public string Side { get; set; }
  public Brush SideColor { get; set; }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        public Brush StatusBackground
        {
            get => _statusBackground;
            set
            {
                if (_statusBackground != value)
                {
                    _statusBackground = value;
                    OnPropertyChanged(nameof(StatusBackground));
                }
            }
        }

        public Brush StatusForeground
        {
            get => _statusForeground;
            set
            {
                if (_statusForeground != value)
                {
                    _statusForeground = value;
                    OnPropertyChanged(nameof(StatusForeground));
                }
            }
        }

   // Pricing details
   public string Volatility { get; set; }
     public string EurPips { get; set; }
        public string PremiumLabel { get; set; }  // "RCV" or "PAY"
        public string PremiumDisplay { get; set; }
        public string PremiumCurrency { get; set; }
   public Brush PremiumColor { get; set; }
 public string SpotRate { get; set; }

  // Trade details
     public string Strike { get; set; }
        public string ExpiryDate { get; set; }
     public string Notional { get; set; }
    public string ExpiryCut { get; set; }
        public string OrderId { get; set; }

        // Delta Hedge details
        public string HedgeType { get; set; }   // "No Hedge", "Spot Hedge", "Forward Hedge"
 public string HedgeSide { get; set; }       // "BUY" or "SELL"
        public string HedgeAmount { get; set; }     // e.g., "8,250,000 USD"
     public string HedgeRate { get; set; }       // e.g., "1.1665"
        public string HedgeValueDate { get; set; } // e.g., "22-Jan-26"
        public string Delta { get; set; }         // e.g., "55%"
    public bool HasHedge => !string.IsNullOrEmpty(HedgeType) && HedgeType != "No Hedge" && HedgeType != "Live";
   
        // Expansion state
        public bool IsExpanded
 {
            get => _isExpanded;
       set
      {
       if (_isExpanded != value)
      {
      _isExpanded = value;
 OnPropertyChanged(nameof(IsExpanded));
            }
 }
     }

 public event PropertyChangedEventHandler PropertyChanged;

protected virtual void OnPropertyChanged(string propertyName)
        {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
