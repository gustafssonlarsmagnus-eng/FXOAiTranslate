using System;
using System.Configuration;
using System.Linq;
using Microsoft.Data.SqlClient;

namespace FX.Infrastructure.Calendars.Legacy
{
    /// <summary>
    /// Service for FX calendar operations using database-backed holiday calendars.
    /// Replaces QLNet-based FxDateService with reliable, database-driven business day calculations.
    /// </summary>
    public class FxCalendarService
    {
        private readonly HolidayCalendar _holidayCalendar;
      private readonly bool _isDatabaseAvailable;
        private static FxCalendarService _instance;
        private static readonly object _lock = new object();

        /// <summary>
    /// Whether the database connection is available.
        /// </summary>
  public bool IsDatabaseAvailable => _isDatabaseAvailable;

        /// <summary>
 /// Private constructor for singleton pattern.
        /// </summary>
        private FxCalendarService(string connectionString)
        {
            try
            {
                // Ensure connection string uses TCP/IP for remote access
    connectionString = EnsureRemoteCompatible(connectionString);
                
       Console.WriteLine($"[FX-CALENDAR] Testing connection: {MaskConnectionString(connectionString)}");

           // Parse connection string to check DNS
       var builder = new SqlConnectionStringBuilder(connectionString);
     Console.WriteLine($"[FX-CALENDAR] Data Source: {builder.DataSource}");
          Console.WriteLine($"[FX-CALENDAR] Connect Timeout: {builder.ConnectTimeout}s");
       Console.WriteLine($"[FX-CALENDAR] Encrypt: {builder.Encrypt}, TrustServerCertificate: {builder.TrustServerCertificate}");

    // Try DNS resolution first
 try
{
  var sw = System.Diagnostics.Stopwatch.StartNew();
       var serverName = builder.DataSource.Replace("tcp:", "").Split(',')[0];
   var addresses = System.Net.Dns.GetHostAddresses(serverName);
             sw.Stop();
         Console.WriteLine($"[FX-CALENDAR] DNS resolved in {sw.ElapsedMilliseconds}ms: {string.Join(", ", addresses.Select(a => a.ToString()))}");
   }
      catch (Exception dnsEx)
   {
           Console.WriteLine($"[FX-CALENDAR] DNS FAILED: {dnsEx.Message}");
      Console.WriteLine($"[FX-CALENDAR] Tip: If on VPN, ensure you're connected and can reach internal servers");
          throw new Exception($"DNS resolution failed for {builder.DataSource}", dnsEx);
  }

         _holidayCalendar = new HolidayCalendar(connectionString);

       // Test connection by trying to get a small date range
                var testDate = DateTime.UtcNow;
        Console.WriteLine($"[FX-CALENDAR] Attempting SQL connection via TCP/IP...");
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
                _holidayCalendar.GetHolidays(new[] { "USA" }, testDate, testDate.AddDays(1), timeoutSeconds: 30);
                sw2.Stop();

      _isDatabaseAvailable = true;
           Console.WriteLine($"[FX-CALENDAR] Database connection successful ({sw2.ElapsedMilliseconds}ms)");
         }
            catch (SqlException sqlEx)
          {
                _isDatabaseAvailable = false;
         Console.WriteLine($"[FX-CALENDAR] SQL ERROR ({sqlEx.Number}): {sqlEx.Message}");
        
            // Provide helpful troubleshooting tips based on error
   if (sqlEx.Number == 53 || sqlEx.Message.Contains("Named Pipes"))
       {
  Console.WriteLine($"[FX-CALENDAR] TIP: Named Pipes failed. This often happens when working remotely.");
              Console.WriteLine($"[FX-CALENDAR] TIP: Ensure connection uses 'tcp:servername,1433' format.");
          }
      else if (sqlEx.Number == 258 || sqlEx.Message.Contains("wait operation timed out"))
  {
              Console.WriteLine($"[FX-CALENDAR] TIP: Connection timeout. The server may be unreachable or blocked by firewall.");
          Console.WriteLine($"[FX-CALENDAR] TIP: Verify VPN is connected and you can ping the server.");
         Console.WriteLine($"[FX-CALENDAR] TIP: Check if port 1433 is open: Test-NetConnection -ComputerName AHSKvant-prod-db -Port 1433");
          Console.WriteLine($"[FX-CALENDAR] TIP: Try increasing timeout or check with IT if SQL Server allows remote connections.");
 }
                else if (sqlEx.Number == 18456)
        {
          Console.WriteLine($"[FX-CALENDAR] TIP: Login failed. Windows auth may not work over VPN - consider SQL auth.");
      }
   else if (sqlEx.Message.Contains("Access is denied"))
      {
          Console.WriteLine($"[FX-CALENDAR] TIP: Access denied - Windows credentials not passing over network.");
                  Console.WriteLine($"[FX-CALENDAR] TIP: Try using SQL Server authentication instead of Integrated Security.");
   }
       }
      catch (Exception ex)
            {
_isDatabaseAvailable = false;
  Console.WriteLine($"[FX-CALENDAR] WARNING: Database unavailable, using fallback calculations: {ex.Message}");
         if (ex.InnerException != null)
  {
         Console.WriteLine($"[FX-CALENDAR] Inner exception: {ex.InnerException.Message}");
      }
            }
        }

        /// <summary>
        /// Ensures connection string is compatible with remote/VPN access
        /// </summary>
    private static string EnsureRemoteCompatible(string connectionString)
        {
  var builder = new SqlConnectionStringBuilder(connectionString);
   
 // If server doesn't specify tcp:, add it for explicit TCP/IP connection
            if (!builder.DataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
 {
                // Check if port is specified
       if (!builder.DataSource.Contains(","))
        {
        // Add default SQL Server port
builder.DataSource = $"tcp:{builder.DataSource},1433";
        }
      else
             {
            builder.DataSource = $"tcp:{builder.DataSource}";
 }
         }

   // Increase timeout for VPN latency - 30 seconds minimum
            if (builder.ConnectTimeout < 30)
  {
            builder.ConnectTimeout = 30;
        }

  // Enable encryption with TrustServerCertificate for internal servers
       if (!builder.Encrypt)
            {
        builder.Encrypt = true;
        builder.TrustServerCertificate = true;
   }

  return builder.ConnectionString;
        }

  /// <summary>
        /// Mask sensitive parts of connection string for logging
        /// </summary>
        private static string MaskConnectionString(string connectionString)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (!string.IsNullOrEmpty(builder.Password))
      {
       builder.Password = "****";
       }
            return builder.ConnectionString;
        }

        /// <summary>
 /// Get singleton instance. Reads connection string from App.config.
        /// </summary>
        public static FxCalendarService Instance
        {
   get
        {
                if (_instance == null)
              {
    lock (_lock)
       {
    if (_instance == null)
     {
         // Try to get connection string from App.config
                 var connString = ConfigurationManager.ConnectionStrings["AHSKvant"]?.ConnectionString;

                if (string.IsNullOrWhiteSpace(connString))
                  {
                 // Fallback to appSettings if not in connectionStrings
   connString = ConfigurationManager.AppSettings["AHSKvantConnectionString"];
     }

      if (string.IsNullOrWhiteSpace(connString))
         {
     throw new InvalidOperationException(
     "FxCalendarService requires connection string 'AHSKvant' in App.config. " +
           "Add: <connectionStrings><add name=\"AHSKvant\" connectionString=\"...\"/></connectionStrings>");
          }

        _instance = new FxCalendarService(connString);
    }
          }
       }
       return _instance;
      }
    }

        /// <summary>
        /// Get the next business day from a given date.
  /// </summary>
 public DateTime GetNextBusinessDay(DateTime date, string currencyPair, bool includeStart = true)
        {
   if (!_isDatabaseAvailable)
   {
                return GetNextBusinessDayFallback(date, includeStart);
 }

      try
            {
                var markets = GetMarketsForPair(currencyPair);
   var startDate = includeStart ? date : date.AddDays(1);
    return _holidayCalendar.NextBusinessDay(startDate, markets);
            }
   catch (Exception ex)
       {
          Console.WriteLine($"[FX-CALENDAR] Error getting next business day: {ex.Message}");
    return GetNextBusinessDayFallback(date, includeStart);
  }
 }

        /// <summary>
        /// Calculate spot date (T+2 for most pairs, T+1 for some).
        /// </summary>
        public DateTime CalculateSpotDate(DateTime tradeDate, string currencyPair)
        {
         if (!_isDatabaseAvailable)
  {
             // Fallback: simple T+2 calculation
 return AddBusinessDaysFallback(tradeDate, 2);
 }

         try
            {
        var markets = GetMarketsForPair(currencyPair);

                // T+1 pairs (USD/CAD, USD/MXN, etc.)
    int spotLag = IsT1Pair(currencyPair) ? 1 : 2;
       
    var result = tradeDate;
            for (int i = 0; i < spotLag; i++)
            {
           result = _holidayCalendar.NextBusinessDay(result.AddDays(1), markets);
 }
        return result;
}
      catch (Exception ex)
    {
        Console.WriteLine($"[FX-CALENDAR] Error calculating spot date: {ex.Message}");
             return AddBusinessDaysFallback(tradeDate, 2);
   }
    }

        /// <summary>
        /// Calculate expiry date from trade date and tenor.
        /// FX Options Convention: Delivery = Spot + Tenor (Modified Following), Expiry = Delivery - 2 business days
        /// </summary>
        public DateTime CalculateExpiry(DateTime tradeDate, string currencyPair, string tenor)
        {
            if (!_isDatabaseAvailable)
            {
                // Fallback: simple tenor calculation
                return CalculateExpiryFallback(tradeDate, tenor);
            }

            try
            {
                var spotDate = CalculateSpotDate(tradeDate, currencyPair);
                var markets = GetMarketsForPair(currencyPair);
          
                // FX Options: Standard market convention (per ISDA)
                // 1. Calculate DELIVERY date = Spot + Tenor (adjusted using Modified Following)
                // 2. Calculate EXPIRY date = Delivery - SpotLag business days
                //
                // Example EURUSD 1M from Jan 19 (Mon, trade date):
                //   Spot = Jan 21 (T+2, Wed)
                //   Raw Delivery = Feb 21 (Sat) 
                //   Adjusted Delivery = Feb 23 (Mon) using Modified Following
                //   Expiry = Feb 23 - 2BD = Feb 19 (Thu)
         
                var rawDelivery = AddTenor(spotDate, tenor);
         
                // Adjust delivery for holidays/weekends using MODIFIED FOLLOWING convention
                // FX Options: If delivery falls on weekend/holiday, roll FORWARD to next business day
                // (unless it crosses into next month, then roll back - but this is rare for standard tenors)
                var deliveryDate = GetNextBusinessDayForDelivery(rawDelivery, markets);
         
                // Calculate expiry = Delivery - SpotLag business days
                int spotLag = IsT1Pair(currencyPair) ? 1 : 2;
                var expiryDate = RetreatBusinessDays(deliveryDate, spotLag, markets);
         
                Console.WriteLine($"[FX-CALENDAR] {currencyPair} {tenor}: Spot={spotDate:dd-MMM-yy}, RawDelivery={rawDelivery:dd-MMM-yy}, Delivery={deliveryDate:dd-MMM-yy}, Expiry={expiryDate:dd-MMM-yy}");
         
                return expiryDate;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FX-CALENDAR] Error calculating expiry: {ex.Message}");
                return CalculateExpiryFallback(tradeDate, tenor);
            }
        }

 /// <summary>
        /// Calculate delivery date from expiry date.
        /// </summary>
        public DateTime CalculateDeliveryDate(DateTime expiryDate, string currencyPair)
        {
            if (!_isDatabaseAvailable)
            {
   return AddBusinessDaysFallback(expiryDate, 2);
     }

            try
    {
         var markets = GetMarketsForPair(currencyPair);
                int spotLag = IsT1Pair(currencyPair) ? 1 : 2;
 
                var result = expiryDate;
       for (int i = 0; i < spotLag; i++)
      {
       result = _holidayCalendar.NextBusinessDay(result.AddDays(1), markets);
          }
                return result;
            }
     catch (Exception ex)
         {
  Console.WriteLine($"[FX-CALENDAR] Error calculating delivery date: {ex.Message}");
     return AddBusinessDaysFallback(expiryDate, 2);
 }
     }

        /// <summary>
        /// Check if a date is a business day.
      /// </summary>
        public bool IsBusinessDay(DateTime date, string currencyPair)
        {
  if (!_isDatabaseAvailable)
          {
   return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
            }

            try
            {
      var markets = GetMarketsForPair(currencyPair);
                // A date is a business day if it's not a weekend and not a holiday
        if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
     return false;
     
         return !_holidayCalendar.IsHoliday(date, markets);
            }
  catch
            {
          return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
     }
        }

        #region Helper Methods

  /// <summary>
        /// Get market codes for a currency pair.
    /// </summary>
        private static string[] GetMarketsForPair(string currencyPair)
        {
          if (string.IsNullOrEmpty(currencyPair) || currencyPair.Length < 6)
            return new[] { "USA" };
                
            var ccy1 = currencyPair.Substring(0, 3).ToUpperInvariant();
   var ccy2 = currencyPair.Substring(3, 3).ToUpperInvariant();
     
            // Map currency codes to market codes
      return new[] { CurrencyToMarket(ccy1), CurrencyToMarket(ccy2) }.Distinct().ToArray();
     }

        /// <summary>
   /// Map currency code to market code.
        /// </summary>
        private static string CurrencyToMarket(string currency)
  {
            return currency switch
    {
           "USD" => "USA",
          "EUR" => "EUR",  // TARGET2 calendar
          "GBP" => "GBR",
     "JPY" => "JPN",
           "CHF" => "CHE",
                "CAD" => "CAN",
       "AUD" => "AUS",
    "NZD" => "NZL",
        "SEK" => "SWE",
      "NOK" => "NOR",
      "DKK" => "DNK",
      "SGD" => "SGP",
     "HKD" => "HKG",
                "CNY" => "CHN",
     "MXN" => "MEX",
    "ZAR" => "ZAF",
   _ => currency  // Use currency code as market code if no mapping
      };
     }

private static bool IsT1Pair(string pair)
        {
            // T+1 pairs
var t1Pairs = new[] { "USDCAD", "CADUSD", "USDMXN", "MXNUSD", "USDRUB", "RUBUSD" };
            return t1Pairs.Contains(pair.ToUpperInvariant());
  }

      private static DateTime AddTenor(DateTime date, string tenor)
        {
       tenor = tenor.ToUpperInvariant().Trim();
            
   if (tenor.EndsWith("D"))
      {
         int days = int.Parse(tenor.TrimEnd('D'));
           return date.AddDays(days);
     }
 else if (tenor.EndsWith("W"))
            {
      int weeks = int.Parse(tenor.TrimEnd('W'));
    return date.AddDays(weeks * 7);
     }
            else if (tenor.EndsWith("M"))
         {
     int months = int.Parse(tenor.TrimEnd('M'));
        return date.AddMonths(months);
        }
            else if (tenor.EndsWith("Y"))
         {
   int years = int.Parse(tenor.TrimEnd('Y'));
         return date.AddYears(years);
   }
    
            throw new ArgumentException($"Invalid tenor format: {tenor}");
      }

        private static DateTime AddBusinessDaysFallback(DateTime date, int days)
        {
            var result = date;
int added = 0;
          
       while (added < days)
            {
 result = result.AddDays(1);
          if (result.DayOfWeek != DayOfWeek.Saturday && result.DayOfWeek != DayOfWeek.Sunday)
         {
  added++;
          }
   }

            return result;
 }

        /// <summary>
        /// Go backwards by the specified number of business days.
        /// Used to calculate expiry from delivery: Expiry = Delivery - N business days
        /// </summary>
        private DateTime RetreatBusinessDays(DateTime date, int days, string[] markets)
        {
            var result = date;
            int retreated = 0;
            
            while (retreated < days)
            {
                result = result.AddDays(-1);
                
                bool isWeekend = result.DayOfWeek == DayOfWeek.Saturday || result.DayOfWeek == DayOfWeek.Sunday;
                bool isHoliday = !isWeekend && _holidayCalendar.IsHoliday(result, markets);
                bool isBusinessDay = !isWeekend && !isHoliday;
                
                if (isBusinessDay)
                {
                    retreated++;
                }
            }
            
            return result;
        }

        /// <summary>
        /// Get the previous (or same) business day. If date is a weekend/holiday, go backward.
        /// Used for expiry date adjustment if needed.
        /// </summary>
        private DateTime GetPreviousBusinessDay(DateTime date, string[] markets)
        {
            var result = date;
            
            while (result.DayOfWeek == DayOfWeek.Saturday || 
                   result.DayOfWeek == DayOfWeek.Sunday ||
                   _holidayCalendar.IsHoliday(result, markets))
            {
                result = result.AddDays(-1);
            }
            
            return result;
        }

        /// <summary>
        /// Get the next business day for delivery date adjustment using Modified Following.
        /// If date is a weekend/holiday, roll FORWARD to next business day.
        /// If rolling forward crosses into next month, roll BACKWARD instead.
        /// </summary>
        private DateTime GetNextBusinessDayForDelivery(DateTime date, string[] markets)
        {
            var result = date;
            int originalMonth = date.Month;
            
            // First try rolling forward
            while (result.DayOfWeek == DayOfWeek.Saturday || 
                   result.DayOfWeek == DayOfWeek.Sunday ||
                   _holidayCalendar.IsHoliday(result, markets))
            {
                result = result.AddDays(1);
            }
            
            // Modified Following: If we crossed into a new month, roll backward instead
            if (result.Month != originalMonth)
            {
                result = date;
                while (result.DayOfWeek == DayOfWeek.Saturday || 
                       result.DayOfWeek == DayOfWeek.Sunday ||
                       _holidayCalendar.IsHoliday(result, markets))
                {
                    result = result.AddDays(-1);
                }
            }
            
            return result;
        }

    private static DateTime GetNextBusinessDayFallback(DateTime date, bool includeStart)
        {
   var result = includeStart ? date : date.AddDays(1);
  while (result.DayOfWeek == DayOfWeek.Saturday || result.DayOfWeek == DayOfWeek.Sunday)
 {
                result = result.AddDays(1);
      }
            return result;
        }

        private static DateTime CalculateExpiryFallback(DateTime tradeDate, string tenor)
        {
            // FX Options Convention (per ISDA): Expiry = Delivery - SpotLag BD
            // 1. Calculate spot (T+2 for most pairs)
            int spotLag = 2; // Fallback assumes T+2
            var spotDate = AddBusinessDaysFallback(tradeDate, spotLag);
         
            // 2. Calculate delivery (Spot + Tenor)
            var rawDelivery = AddTenor(spotDate, tenor);
            int originalMonth = rawDelivery.Month;
          
            // Ensure delivery is a business day - use Modified Following convention
            // Roll FORWARD to next business day, unless it crosses into next month
            var deliveryDate = rawDelivery;
            while (deliveryDate.DayOfWeek == DayOfWeek.Saturday || deliveryDate.DayOfWeek == DayOfWeek.Sunday)
            {
                deliveryDate = deliveryDate.AddDays(1);
            }
            
            // Modified Following: If crossed into new month, roll backward instead
            if (deliveryDate.Month != originalMonth)
            {
                deliveryDate = rawDelivery;
                while (deliveryDate.DayOfWeek == DayOfWeek.Saturday || deliveryDate.DayOfWeek == DayOfWeek.Sunday)
                {
                    deliveryDate = deliveryDate.AddDays(-1);
                }
            }
         
            // 3. Calculate expiry = Delivery - SpotLag business days
            var expiryDate = RetreatBusinessDaysFallback(deliveryDate, spotLag);
  
            Console.WriteLine($"[FX-CALENDAR-FALLBACK] {tenor}: Spot={spotDate:dd-MMM-yy}, RawDelivery={rawDelivery:dd-MMM-yy}, Delivery={deliveryDate:dd-MMM-yy}, Expiry={expiryDate:dd-MMM-yy}");
  
            return expiryDate;
        }

        /// <summary>
        /// Go backwards by the specified number of business days (fallback without database).
        /// </summary>
        private static DateTime RetreatBusinessDaysFallback(DateTime date, int days)
        {
            var result = date;
            int retreated = 0;
            
            while (retreated < days)
            {
                result = result.AddDays(-1);
                
                // Check if it's a business day (not weekend)
                if (result.DayOfWeek != DayOfWeek.Saturday && 
                    result.DayOfWeek != DayOfWeek.Sunday)
                {
                    retreated++;
                }
            }
            
            return result;
        }

    #endregion
    }
}
