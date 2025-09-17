using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FXOAiTranslator
{
    public partial class Form1 : Form
    {
        private readonly TradeParser _tradeParser;
        private readonly BloombergService _bloombergService;

        public Form1()
        {
            InitializeComponent();

            // Initialize Bloomberg service
            _bloombergService = new BloombergService();
            _bloombergService.TryConnect();

            // Supply API key if you want AI enabled
            string openAIApiKey = ""; // ?? put your key here (or leave blank for regex only)

            _tradeParser = new TradeParser(_bloombergService, openAIApiKey);
            _tradeParser.DebugCallback = AppendDebug;

            AppendDebug("Bloomberg Terminal detected - Connected");
            AppendDebug("[AI] " + (string.IsNullOrEmpty(openAIApiKey) ? "Disabled" : "Enabled"));
        }

        // --- Parse button click ---
        private async void btnParse_Click(object sender, EventArgs e)
        {
            string input = txtInput.Text.Trim();
            if (string.IsNullOrEmpty(input)) return;

            AppendDebug($"=== PROCESSING TRADE ===\nInput: {input}");

            try
            {
                TradeParseResult result = await _tradeParser.ParseTradeAsync(input);

                if (!string.IsNullOrEmpty(result.OVML))
                {
                    lstBlotter.Items.Add(result.OVML);

                    AppendDebug($"Parse result: SUCCESS");
                    AppendDebug($"Generated OVML: {result.OVML}");

                    // Send to Bloomberg
                    if (_bloombergService.IsConnected)
                    {
                        _bloombergService.SendOVML(result.OVML);
                        AppendDebug($"Sent to Bloomberg (new tab): {result.OVML}");
                    }
                }
                else
                {
                    AppendDebug("Parse result: FAILED (no OVML generated)");
                }
            }
            catch (Exception ex)
            {
                AppendDebug("Error: " + ex.Message);
            }
        }

        // --- Append to debug text box safely ---
        private void AppendDebug(string msg)
        {
            if (txtDebug.InvokeRequired)
            {
                txtDebug.Invoke(new Action(() => AppendDebug(msg)));
            }
            else
            {
                txtDebug.AppendText(msg + Environment.NewLine);
            }
        }

        // --- Optional: handle Enter key in input box ---
        private async void txtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                btnParse_Click(sender, e);
            }
        }
    }
}
