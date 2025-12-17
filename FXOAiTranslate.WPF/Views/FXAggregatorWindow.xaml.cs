using System.Windows;
using FXOAiTranslate.WPF.ViewModels;
using FXOptionsSimulator;

namespace FXOAiTranslate.WPF.Views
{
    /// <summary>
    /// Interaction logic for FXAggregatorWindow.xaml
    /// </summary>
    public partial class FXAggregatorWindow : Window
    {
        public FXAggregatorWindow()
        {
            InitializeComponent();
            // Initialize with sample data for design verification
            this.DataContext = new MultiLPQuoteViewModel(new TradeStructure());
        }
    }
}
