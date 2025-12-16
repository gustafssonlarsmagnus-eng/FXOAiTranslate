using System;
using System.ComponentModel;
using FXOptionsSimulator;

namespace FXOAiTranslate.WPF.ViewModels
{
    public class MultiLPQuoteViewModel : INotifyPropertyChanged
    {
        public MultiLPQuoteViewModel(TradeStructure trade)
        {
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
