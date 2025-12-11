using System.Windows;
using FXOAiTranslate.WPF.ViewModels;
using FXOptionsSimulator;

namespace FXOAiTranslate.WPF.Views
{
    /// <summary>
    /// Interaction logic for MultiLPQuoteWindow.xaml
    /// </summary>
    public partial class MultiLPQuoteWindow : Window
    {
        public MultiLPQuoteWindow(TradeStructure trade)
        {
            InitializeComponent();
            DataContext = new MultiLPQuoteViewModel(trade);
        }
    }
}
