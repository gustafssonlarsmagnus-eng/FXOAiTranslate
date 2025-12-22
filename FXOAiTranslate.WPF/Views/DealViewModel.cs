using System;
using System.ComponentModel;
using System.Windows.Media;

namespace FXOAiTranslate.WPF.Views
{
    /// <summary>
 /// View model for displaying trade deals in the blotter
    /// </summary>
    public class DealViewModel : INotifyPropertyChanged
 {
        private bool _isExpanded;

        public string Time { get; set; }
      public string Instrument { get; set; }
  public string LP { get; set; }
        public string Side { get; set; }
  public Brush SideColor { get; set; }
        public string Status { get; set; }
        public Brush StatusBackground { get; set; }
        public Brush StatusForeground { get; set; }

   // Pricing details
   public string Volatility { get; set; }
     public string EurPips { get; set; }
        public string PremiumLabel { get; set; }  // "RCV" or "PAY"
        public string PremiumDisplay { get; set; }
   public Brush PremiumColor { get; set; }
 public string SpotRate { get; set; }

  // Trade details
     public string Strike { get; set; }
        public string ExpiryDate { get; set; }
     public string Notional { get; set; }
    public string ExpiryCut { get; set; }
        public string OrderId { get; set; }

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
