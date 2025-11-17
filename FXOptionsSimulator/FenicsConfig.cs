using System;
using System.Collections.Generic;

namespace FXOptionsSimulator.FIX  // ← CHANGED: Added .FIX namespace
{
    /// <summary>
    /// Configuration matching GFI Fenics requirements
    /// These will be provided by GFI when you onboard
    /// </summary>
    public class FenicsConfig
    {
        // ========== ENVIRONMENT ==========
        public string Environment { get; set; } = "UAT";  // ← ADDED: Was missing

        // ========== UAT CREDENTIALS (RECEIVED FROM GFI) ==========
        public string SenderCompID { get; set; } = "WEBFENICS55";
        public string OnBehalfOfCompID { get; set; } = "SWES";
        public string Username { get; set; } = "swed.obo.stg.api";
        public string Password { get; set; } = "ZQcZokEOLjb9";

        public bool IsTestMode { get; set; } = false;

        // ========== CONNECTION SETTINGS ==========
        public string Host { get; set; } = "quotes.stage2.gfifx.com";
        public int Port { get; set; } = 443;
        public int HeartbeatInterval { get; set; } = 10;

        // ========== QUOTE REQUEST PREFIX ==========
        public string QuoteReqPrefix { get; set; } = "SWES.";

        // ========== LIQUIDITY PROVIDERS (UAT VALUES) ==========
        public Dictionary<string, string> LiquidityProviders { get; set; } = new Dictionary<string, string>
        {
            // Ask GFI for actual UAT CompIDs - these are likely correct for UAT
            ["MS"] = "MS",           // Morgan Stanley
            ["UBS"] = "UBS",         // UBS
            ["CITI"] = "CITI",       // Citibank
            ["JPM"] = "JPM",         // JP Morgan
            ["GS"] = "GS",           // Goldman Sachs
            ["BARC"] = "BARC",       // Barclays
            ["DB"] = "DB",           // Deutsche Bank
            ["NATWEST"] = "NATWEST", // NatWest Markets
            ["NOMU"] = "NOMU",       // Nomura
            ["SG"] = "SG"            // Societe Generale
        };

        // ========== AUTHORIZED CURRENCY PAIRS ==========
        public List<string> AuthorizedPairs { get; set; } = new List<string>
        {
            "EURUSD",
            "USDSEK",
            "EURSEK",
            "GBPUSD",
            "USDJPY",
            "USDNOK",
            "EURNOK",
            "GBPSEK",
            "AUDNOK",
            "NOKSEK"
        };

        // ========== CUTOFF MAPPINGS ==========
        public Dictionary<string, string> CutoffCodes { get; set; } = new Dictionary<string, string>
        {
            ["NY"] = "1",      // New York 10:00
            ["TK"] = "2",      // Tokyo 15:00
            ["LON"] = "157",   // London WMR 1pm
        };

        // ========== VALIDATION ==========
        public void Validate()
        {
            var errors = new List<string>();

            // Check if still using placeholder values
            if (SenderCompID == "YOUR_COMPID_HERE" || string.IsNullOrEmpty(SenderCompID))
                errors.Add("  ! SenderCompID not configured");

            if (Username == "your.username" || string.IsNullOrEmpty(Username))
                errors.Add("  ! Username not configured");

            if (Password == "your.password" || string.IsNullOrEmpty(Password))
                errors.Add("  ! Password not configured");

            // These are now configured, so no warnings!
            // Just validate reasonable values
            if (HeartbeatInterval > 10)
                errors.Add("  ! Heartbeat must be <= 10 seconds per GFI spec");

            if (Port != 443 && Port != 80)
                errors.Add("  ! Port should be 443 (SSL) or 80 for UAT");

            if (errors.Count > 0)
            {
                Console.WriteLine("\n=== CONFIGURATION WARNINGS ===");
                foreach (var error in errors)
                {
                    Console.WriteLine(error);
                }
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine("\n✅ Configuration validated - all credentials present!");
            }
        }

        public void PrintConfig()
        {
            Console.WriteLine("\n=== FENICS CONNECTION CONFIG ===");
            Console.WriteLine($"Environment:     {Environment}");
            Console.WriteLine($"Host:            {Host}");
            Console.WriteLine($"Port:            {Port}");
            Console.WriteLine($"SenderCompID:    {SenderCompID}");
            Console.WriteLine($"OnBehalfOfCompID: {OnBehalfOfCompID}");
            Console.WriteLine($"Username:        {Username}");
            Console.WriteLine($"Password:        {new string('*', Password?.Length ?? 0)}");  // ← CHANGED: Mask password
            Console.WriteLine($"QuoteReqPrefix:  {QuoteReqPrefix}");
            Console.WriteLine($"Heartbeat:       {HeartbeatInterval}s");
            Console.WriteLine($"Available LPs:   {string.Join(", ", LiquidityProviders.Keys)}");
            Console.WriteLine($"Authorized Pairs: {string.Join(", ", AuthorizedPairs)}");

            Validate();
        }
    }

    /// <summary>
    /// Realistic market data for testing
    /// Based on actual levels as of Oct 2024
    /// </summary>
    public class MarketData
    {
        public string Symbol { get; set; }
        public double SpotRate { get; set; }
        public Dictionary<string, double> ForwardPoints { get; set; }
        public Dictionary<string, double> ImpliedVols { get; set; }

        public static MarketData GetEURUSD()
        {
            return new MarketData
            {
                Symbol = "EURUSD",
                SpotRate = 1.0850,
                ForwardPoints = new Dictionary<string, double>
                {
                    ["1M"] = 12.5,
                    ["3M"] = 35.2,
                    ["6M"] = 68.4,
                    ["1Y"] = 125.7,
                    ["2Y"] = 235.1,
                },
                ImpliedVols = new Dictionary<string, double>
                {
                    ["1M_ATM"] = 6.5,
                    ["3M_ATM"] = 7.2,
                    ["6M_ATM"] = 7.8,
                    ["1Y_ATM"] = 8.3,
                    ["2Y_ATM"] = 8.9,
                }
            };
        }

        public static MarketData GetUSDSEK()
        {
            return new MarketData
            {
                Symbol = "USDSEK",
                SpotRate = 10.4560,
                ForwardPoints = new Dictionary<string, double>
                {
                    ["1M"] = -85.5,
                    ["3M"] = -245.2,
                    ["6M"] = -482.1,
                    ["1Y"] = -895.3,
                },
                ImpliedVols = new Dictionary<string, double>
                {
                    ["1M_ATM"] = 8.5,
                    ["3M_ATM"] = 9.2,
                    ["6M_ATM"] = 9.8,
                    ["1Y_ATM"] = 10.5,
                }
            };
        }

        public double GetForwardRate(string tenor)
        {
            if (ForwardPoints.TryGetValue(tenor, out var points))
            {
                var divisor = 10000.0;
                return SpotRate + (points / divisor);
            }
            return SpotRate;
        }

        public double GetVolatility(string tenor, double delta = 50)
        {
            var key = $"{tenor}_ATM";
            if (ImpliedVols.TryGetValue(key, out var vol))
            {
                if (delta != 50)
                {
                    var skew = 0.05 * Math.Abs(delta - 50) / 25.0;
                    vol += skew;
                }
                return vol;
            }
            return 8.0;
        }
    }
}