using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FXOAiTranslate.WPF.Controls
{
    /// <summary>
    /// Interaction logic for ExecutionTile.xaml
    /// </summary>
    public partial class ExecutionTile : UserControl
    {
        public ExecutionTile()
        {
            InitializeComponent();
        }

        #region Dependency Properties

        public static readonly DependencyProperty SideLabelProperty =
            DependencyProperty.Register("SideLabel", typeof(string), typeof(ExecutionTile), new PropertyMetadata("SIDE"));

        public string SideLabel
        {
            get { return (string)GetValue(SideLabelProperty); }
            set { SetValue(SideLabelProperty, value); }
        }

        public static readonly DependencyProperty PriceProperty =
            DependencyProperty.Register("Price", typeof(double), typeof(ExecutionTile), new PropertyMetadata(0.0, OnPriceChanged));

        public double Price
        {
            get { return (double)GetValue(PriceProperty); }
            set { SetValue(PriceProperty, value); }
        }

        public static readonly DependencyProperty BigFigureProperty =
            DependencyProperty.Register("BigFigure", typeof(string), typeof(ExecutionTile), new PropertyMetadata(""));

        public string BigFigure
        {
            get { return (string)GetValue(BigFigureProperty); }
            set { SetValue(BigFigureProperty, value); }
        }

        public static readonly DependencyProperty PipsProperty =
            DependencyProperty.Register("Pips", typeof(string), typeof(ExecutionTile), new PropertyMetadata("--"));

        public string Pips
        {
            get { return (string)GetValue(PipsProperty); }
            set { SetValue(PipsProperty, value); }
        }

        public static readonly DependencyProperty TenthsProperty =
            DependencyProperty.Register("Tenths", typeof(string), typeof(ExecutionTile), new PropertyMetadata(""));

        public string Tenths
        {
            get { return (string)GetValue(TenthsProperty); }
            set { SetValue(TenthsProperty, value); }
        }

        public static readonly DependencyProperty IsRfqStateProperty =
            DependencyProperty.Register("IsRfqState", typeof(bool), typeof(ExecutionTile), new PropertyMetadata(true));

        public bool IsRfqState
        {
            get { return (bool)GetValue(IsRfqStateProperty); }
            set { SetValue(IsRfqStateProperty, value); }
        }

        public static readonly DependencyProperty FooterTextProperty =
            DependencyProperty.Register("FooterText", typeof(string), typeof(ExecutionTile), new PropertyMetadata(""));

        public string FooterText
        {
            get { return (string)GetValue(FooterTextProperty); }
            set { SetValue(FooterTextProperty, value); }
        }

        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register("Command", typeof(ICommand), typeof(ExecutionTile), new PropertyMetadata(null));

        public ICommand Command
        {
            get { return (ICommand)GetValue(CommandProperty); }
            set { SetValue(CommandProperty, value); }
        }

        public static readonly DependencyProperty CommandParameterProperty =
           DependencyProperty.Register("CommandParameter", typeof(object), typeof(ExecutionTile), new PropertyMetadata(null));

        public object CommandParameter
        {
            get { return GetValue(CommandParameterProperty); }
            set { SetValue(CommandParameterProperty, value); }
        }

        public static readonly DependencyProperty PriceBrushProperty =
            DependencyProperty.Register("PriceBrush", typeof(Brush), typeof(ExecutionTile), new PropertyMetadata(Brushes.White));

        public Brush PriceBrush
        {
            get { return (Brush)GetValue(PriceBrushProperty); }
            set { SetValue(PriceBrushProperty, value); }
        }

        public static readonly DependencyProperty SideLabelBrushProperty =
            DependencyProperty.Register("SideLabelBrush", typeof(Brush), typeof(ExecutionTile), new PropertyMetadata(Brushes.Gray));

        public Brush SideLabelBrush
        {
            get { return (Brush)GetValue(SideLabelBrushProperty); }
            set { SetValue(SideLabelBrushProperty, value); }
        }

        // ===== NEW PROPERTIES FOR HTML-MATCHING LAYOUT =====

        public static readonly DependencyProperty DisplayPriceProperty =
            DependencyProperty.Register("DisplayPrice", typeof(string), typeof(ExecutionTile), new PropertyMetadata("5.47"));

        public string DisplayPrice
        {
            get { return (string)GetValue(DisplayPriceProperty); }
            set { SetValue(DisplayPriceProperty, value); }
        }

        public static readonly DependencyProperty SubInfoProperty =
            DependencyProperty.Register("SubInfo", typeof(string), typeof(ExecutionTile), new PropertyMetadata("68,778 USD | 43p"));

        public string SubInfo
        {
            get { return (string)GetValue(SubInfoProperty); }
            set { SetValue(SubInfoProperty, value); }
        }

        public static readonly DependencyProperty LPNameProperty =
            DependencyProperty.Register("LPName", typeof(string), typeof(ExecutionTile), new PropertyMetadata("JPM"));

        public string LPName
        {
            get { return (string)GetValue(LPNameProperty); }
            set { SetValue(LPNameProperty, value); }
        }

        public static readonly DependencyProperty TimerTextProperty =
            DependencyProperty.Register("TimerText", typeof(string), typeof(ExecutionTile), new PropertyMetadata("0:08"));

        public string TimerText
        {
            get { return (string)GetValue(TimerTextProperty); }
            set { SetValue(TimerTextProperty, value); }
        }

        public static readonly DependencyProperty LPAlignmentProperty =
            DependencyProperty.Register("LPAlignment", typeof(HorizontalAlignment), typeof(ExecutionTile), new PropertyMetadata(HorizontalAlignment.Left));

        public HorizontalAlignment LPAlignment
        {
            get { return (HorizontalAlignment)GetValue(LPAlignmentProperty); }
            set { SetValue(LPAlignmentProperty, value); }
        }

        public static readonly DependencyProperty TimerAlignmentProperty =
            DependencyProperty.Register("TimerAlignment", typeof(HorizontalAlignment), typeof(ExecutionTile), new PropertyMetadata(HorizontalAlignment.Right));

        public HorizontalAlignment TimerAlignment
        {
            get { return (HorizontalAlignment)GetValue(TimerAlignmentProperty); }
            set { SetValue(TimerAlignmentProperty, value); }
        }

        #endregion

        private static void OnPriceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ExecutionTile tile && e.NewValue is double newPrice)
            {
                tile.FormatPrice(newPrice);
            }
        }

        private void FormatPrice(double price)
        {
            if (price <= 0)
            {
                BigFigure = "";
                Pips = "--";
                Tenths = "";
                return;
            }

            // Simple formatting logic - can be enhanced for different precisions
            // Assuming 5 decimal places for now (standard FX)
            string priceStr = price.ToString("F5", System.Globalization.CultureInfo.InvariantCulture);
            
            // Logic to split 1.08505 -> 1.08 | 50 | 5
            int dotIndex = priceStr.IndexOf('.');
            if (dotIndex > 0 && priceStr.Length >= dotIndex + 5)
            {
                // Standard 4/5 split
                BigFigure = priceStr.Substring(0, dotIndex + 3); // "1.08"
                Pips = priceStr.Substring(dotIndex + 3, 2);      // "50"
                Tenths = priceStr.Substring(dotIndex + 5);       // "5"
            }
            else
            {
                // Fallback
                BigFigure = priceStr;
                Pips = "";
                Tenths = "";
            }
        }

        private void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (Command != null && Command.CanExecute(CommandParameter))
            {
                Command.Execute(CommandParameter);
            }
        }
    }
}
