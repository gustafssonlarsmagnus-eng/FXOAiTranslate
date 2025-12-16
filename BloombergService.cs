using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms; // For Clipboard

namespace FXOAiTranslator
{
    public class BloombergService
    {
        public bool IsConnected { get; private set; } = true; // Mock connected

        public BloombergService()
        {
            Console.WriteLine("[Bloomberg Mock] Service Initialized");
        }

        public void TryConnect()
        {
            IsConnected = true;
            Console.WriteLine("[Bloomberg Mock] Connected");
        }

        public void SendOVML(string ovmlCommand)
        {
            Console.WriteLine($"[Bloomberg Mock] Sending OVML: {ovmlCommand}");
            Clipboard.SetText(ovmlCommand);
        }

        public async Task<double?> GetSpotRate(string underlying)
        {
            Console.WriteLine($"[Bloomberg Mock] Requesting spot for {underlying}");
            await Task.Delay(100);
            return 1.1234; // Dummy spot
        }

        public string DetermineCallOrPut(double strikePrice, string currencyPair)
        {
            return strikePrice > 1.0 ? "CALL" : "PUT";
        }

        public string DetermineCallOrPut(string tradeRequest)
        {
            var lower = tradeRequest.ToLower();
            if (lower.Contains("put") || lower.Contains("sell option")) return "P";
            return "C";
        }
    }
}