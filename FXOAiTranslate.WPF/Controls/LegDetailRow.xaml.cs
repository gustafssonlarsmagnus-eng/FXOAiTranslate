using System.Windows;
using System.Windows.Controls;

namespace FXOAiTranslate.WPF.Controls
{
    /// <summary>
    /// Interaction logic for LegDetailRow.xaml
    /// </summary>
    public partial class LegDetailRow : UserControl
    {
        public LegDetailRow()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label", typeof(string), typeof(LegDetailRow), new PropertyMetadata("Label"));

        public string Label
        {
            get { return (string)GetValue(LabelProperty); }
            set { SetValue(LabelProperty, value); }
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(string), typeof(LegDetailRow), new PropertyMetadata(""));

        public string Value
        {
            get { return (string)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }
    }
}
