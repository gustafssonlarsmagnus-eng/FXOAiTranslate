using System;

namespace FXOptionsSimulator
{
    /// <summary>
    /// Test/demo class for Trade Capture Report parsing
    /// </summary>
    public class TradeCaptureTest
    {
        public static void TestSampleMessage()
        {
            // Sample AE message from GFI documentation
            string sampleAE = "8=FIX.4.4|9=1906|35=AE|34=28893|49=GFI|52=20251104-16:12:56.430|56=GFI_BFXO_SWED_TC1|" +
                "17=120272240.3255849707|31=0|32=0|55=EURUSD|60=20251104-16:12:56.340400|75=20251104|" +
                "117=O_FENICS.5016859.32e1f49b-0b23-49f8-92c2-d32b8ff0ac97-5|" +
                "131=FENICS.5016859.32e1f49b-0b23-49f8-92c2-d32b8ff0ac97|" +
                "150=0|461=KFXXXX|487=3|570=N|571=2445050_3_GFI_BFXO_SWED_TC1|855=65|" +
                "6030=Auto  Clerk|6139=1|7705=GFSM|8504=GUNTJCA81C7IHNBGI392|8505=3|" +
                "8506=GBR2445051|8507=Y|8520=GUNTJCA81C7IHNBGI392|9126=9|9536=RFQ_bfxo_swed_taker|" +
                "552=1|54=B|37=GFI_BFXO_SWED_TC1_3255849708|" +
                "11=FENICS.5016859.32e1f49b-0b23-49f8-92c2-d32b8ff0ac97_1|" +
                "1=7LTWFZYICNSX8D621K86|DBLN|WORLD|7LTWFZYICNSX8D621K86|" +
                "8510=1|6810=M312WZV08Y7LYUC71685|SEKO|WORLD|M312WZV08Y7LYUC71685|" +
                "8502=1|453=1|448=5016859|447=D|452=11|" +
                "555=2|" + // 2 option legs
                "600=EURUSD|8500=GUNTJCA81C7IHNBGI392|8524=tolong530327GBL2445050L0|8516=N|" +
                "604=1|605=EZHKRNZZX4S4|606=4|608=HFTDVP|8521=N|" +
                "654=PutSpread-Leg-0-1210844388474103|624=1|1057=Y|687=10|556=EUR|" +
                "6714=2|9034=EUR|6215=OD|611=20251204|743=20251208|9125=1|9019=2|" +
                "612=1.15|6035=-45|5235=1.14918|5191=23.66|9115=0|9073=USD|" +
                "5678=5.67|8515=0|8518=5.67|9904=2|566=66.93|5830=USD|5020=20251106|" +
                "6034=66930|10670=7|" +
                "600=EURUSD|8500=GUNTJCA81C7IHNBGI392|8524=tolong530327GBL2445050L1|8516=N|" +
                "604=1|605=EZ0FGC27FYD5|606=4|608=HFTDVP|8521=N|" +
                "654=PutSpread-Leg-1-1210844388613053|624=2|1057=Y|687=20|556=EUR|" +
                "6714=2|9034=EUR|6215=OD|611=20260102|743=20260106|9125=1|9019=2|" +
                "612=1.14|6035=-28|5235=1.14918|5191=45.8|9115=0|9073=USD|" +
                "5678=5.95|8515=0|8518=6.075|9904=2|566=53.73|5830=USD|5020=20251106|" +
                "6034=107460|10670=7|" +
                "768=1|769=20251104-16:12:53.826232|770=1|" +
                "7464=2|" + // 2 hedge legs
                "9074=EUR|8508=GUNTJCA81C7IHNBGI392|8526=tolong530327GBL2445050H0|8517=N|" +
                "10604=1|10605=EZP1RLKH5ZQ4|10606=4|10608=JFTXFP|6666=1|6036=4.48|" +
                "9657=1.151546|9112=20251208|6426=EURUSD|10671=7|" +
                "9074=EUR|8508=GUNTJCA81C7IHNBGI392|8526=tolong530327GBL2445050H1|8517=N|" +
                "10604=1|10605=EZ3WHSKR67T4|10606=4|10608=JFTXFP|6666=2|6036=5.55|" +
                "9657=1.15376|9112=20260106|6426=EURUSD|10671=7|" +
                "10=172|";

            Console.WriteLine("========================================");
            Console.WriteLine("TESTING TRADE CAPTURE REPORT PARSER");
            Console.WriteLine("========================================\n");

            try
            {
                var trade = TradeCaptureParser.ParseAE(sampleAE);

                Console.WriteLine("✓ Successfully parsed AE message\n");
                Console.WriteLine(TradeCaptureParser.FormatTrade(trade));

                Console.WriteLine("\n--- Detailed Fields ---");
                Console.WriteLine($"ExecID:           {trade.ExecID}");
                Console.WriteLine($"QuoteID:          {trade.QuoteID}");
                Console.WriteLine($"QuoteReqID:       {trade.QuoteReqID}");
                Console.WriteLine($"OrderID:          {trade.OrderID}");
                Console.WriteLine($"ClOrdID:          {trade.ClOrdID}");
                Console.WriteLine($"Symbol:           {trade.Symbol}");
                Console.WriteLine($"Side:             {trade.Side}");
                Console.WriteLine($"TransactTime:     {trade.TransactTime:yyyy-MM-dd HH:mm:ss.ffffff}");
                Console.WriteLine($"TradeDate:        {trade.TradeDate:yyyy-MM-dd}");
                Console.WriteLine($"Counterparty LEI: {trade.CounterpartyLEI}");
                Console.WriteLine($"Counterparty:     {trade.CounterpartyName}");
                Console.WriteLine($"Reuters Code:     {trade.CounterpartyReuters}");

                Console.WriteLine($"\n--- Structure ---");
                Console.WriteLine($"Option Legs:      {trade.OptionLegs.Count}");
                Console.WriteLine($"Hedge Legs:       {trade.HedgeLegs.Count}");

                Console.WriteLine("\n✓ Test completed successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Test failed: {ex.Message}");
                Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
            }

            Console.WriteLine("\n========================================\n");
        }

        /// <summary>
        /// Test parsing with a custom LEI mapping
        /// </summary>
        public static void TestWithCustomMapping()
        {
            var customMapping = new System.Collections.Generic.Dictionary<string, string>
            {
                { "7LTWFZYICNSX8D621K86", "Deutsche Bank AG London" },
                { "DGQCSV2PHVF7I2743539", "Nomura International PLC" },
                { "M312WZV08Y7LYUC71685", "Morgan Stanley & Co. International PLC" },
                { "GUNTJCA81C7IHNBGI392", "UBS AG London Branch" }
            };

            TradeCaptureParser.UpdateLEIMapping(customMapping);

            Console.WriteLine("Updated LEI mapping with custom values");
            TestSampleMessage();
        }
    }
}
