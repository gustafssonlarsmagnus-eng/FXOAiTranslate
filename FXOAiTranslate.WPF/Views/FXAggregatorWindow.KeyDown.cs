using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;

namespace FXOAiTranslate.WPF.Views
{
    public partial class FXAggregatorWindow
    {
        private async void txtTradeInput_KeyDown(object sender, KeyEventArgs e)
        {
            // Trigger parsing when Enter key is pressed
            if (e.Key == Key.Enter)
            {
                // Allow Shift+Enter to insert a newline (in case the TextBox is multiline styled later)
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    return;
                }

                e.Handled = true; // Prevent default behavior

                // Get the parse button - try direct field access first, then FindName as fallback
                Button parseButton = btnParse ?? FindName("btnParse") as Button;

                if (parseButton == null)
                {
                    System.Console.WriteLine("[WPF] WARNING: btnParse button not found, falling back to parse without UI feedback");
                    await ParseTradeInputAndUpdate();
                    return;
                }

                // Reuse the same button feedback UI as a click
                await ParseTradeWithButtonFeedbackAsync(parseButton);
            }
        }
    }
}
