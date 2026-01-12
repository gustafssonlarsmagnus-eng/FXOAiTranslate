using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FXOptionsSimulator;

namespace FXOAiTranslate.WPF.Views
{
    /// <summary>
    /// Simple DTO for passing OVML parse results to OVMLBridge.
    /// Matches the interface expected by OVMLBridge.ConvertToTradeStructure (dynamic).
    /// </summary>
    public class OVMLParseResult
    {
        public string OVML { get; set; }
        public string Underlying { get; set; }
        public string Expiry { get; set; }
        public int LegCount { get; set; }
    }
}
