using System;
using System.Configuration;
using QLNet;

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

        // Fallback calendars when database is unavailable
        private static readonly QLNet.Calendar _usdCalendar = new FXCalendars.UnitedStatesFX();
        private static readonly QLNet.Calendar _eurCalendar = new QLNet.TARGET();
        private static readonly QLNet.Calendar _sekCalendar = new QLNet.Sweden();
        private static readonly QLNet.Calendar _nokCalendar = new QLNet.Norway();
        private static readonly QLNet.Calendar _gbpCalendar = new QLNet.UnitedKingdom(QLNet.UnitedKingdom.Market.Exchange);
        private static readonly QLNet.Calendar _jpyCalendar = new QLNet.Japan();

        /// <summary>
        /// Private constructor for singleton pattern.
        /// </summary>
        private FxCalendarService(string connectionString)
        {
            try
            {
                // Log current Windows user for debugging auth issues
                var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent();
                Console.WriteLine($"[FX-CALENDAR] Running as: {currentUser.Name} (Auth: {currentUser.AuthenticationType})");
                Console.WriteLine($"[FX-CALENDAR] Testing connection: {connectionString}");

                _holidayCalendar = new HolidayCalendar(connectionString);

                // Test connection by trying to get a small date range
                var testDate = DateTime.UtcNow;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                _holidayCalendar.GetHolidays(new[] { "USA" }, testDate, testDate.AddDays(1), timeoutSeconds: 5);
                sw.Stop();

                _isDatabaseAvailable = true;
                Console.WriteLine($"[FX-CALENDAR] ✓ Database connected in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                _isDatabaseAvailable = false;
                Console.WriteLine($"[FX-CALENDAR] Database unavailable: {ex.Message}");
            }
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
        /// Gets whether the database is available and connected.
        /// </summary>
        public bool IsDatabaseAvailable => _isDatabaseAvailable;

        /// <summary>
        /// Create instance with explicit connection string (for testing or custom scenarios).
        /// </summary>
        public static FxCalendarService CreateWithConnectionString(string connectionString)
        {
            return new FxCalendarService(connectionString);
        }

        /// <summary>
        /// Calculate expiry date from tenor (e.g., "1M", "3M", "6M", "1Y").
        /// Uses Modified Following convention: adjusts to next business day, but if that
        /// crosses month boundary, uses previous business day instead.
        /// </summary>
        /// <param name="tradeDate">Starting date (usually today)</param>
        /// <param name="tenor">Tenor string (1M, 3M, 6M, 1Y, etc.)</param>
        /// <param name="currencyPair">Currency pair (e.g., "EURUSD")</param>
        /// <returns>Business day adjusted expiry date</returns>
        public DateTime CalculateExpiry(DateTime tradeDate, string tenor, string currencyPair)
        {
            try
            {
                // Use database-backed calculation if available
                if (_isDatabaseAvailable && _holidayCalendar != null)
                {
                    return CurrencyCalendarMapper.CalculateExpiryFromTenor(
                        currencyPair,
                        tradeDate,
                        tenor,
                        _holidayCalendar,
                        useModifiedFollowing: true
                    );
                }
                else
                {
                    // Fallback: use QLNet FXCalendars
                    return CalculateExpiryFallback(tradeDate, tenor, currencyPair);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FX-CALENDAR] ERROR calculating expiry for {tenor}/{currencyPair}: {ex.Message}");
                // Use fallback instead of throwing
                return CalculateExpiryFallback(tradeDate, tenor, currencyPair);
            }
        }

        /// <summary>
        /// Get QLNet calendar for a currency (fallback when database unavailable).
        /// </summary>
        private static QLNet.Calendar GetFallbackCalendar(string currency)
        {
            return currency?.ToUpper() switch
            {
                "USD" => _usdCalendar,
                "EUR" => _eurCalendar,
                "SEK" => _sekCalendar,
                "NOK" => _nokCalendar,
                "GBP" => _gbpCalendar,
                "JPY" => _jpyCalendar,
                _ => _usdCalendar  // Default to USD calendar
            };
        }

        /// <summary>
        /// Check if a date is a business day using fallback calendars.
        /// </summary>
        private static bool IsBusinessDayFallback(DateTime date, string currencyPair)
        {
            if (string.IsNullOrWhiteSpace(currencyPair) || currencyPair.Length < 6)
                return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;

            string ccy1 = currencyPair.Substring(0, 3);
            string ccy2 = currencyPair.Substring(3, 3);

            var cal1 = GetFallbackCalendar(ccy1);
            var cal2 = GetFallbackCalendar(ccy2);

            var qlDate = new QLNet.Date(date.Day, (QLNet.Month)(date.Month), date.Year);

            // FX options can only expire on a day that is a business day in BOTH currencies
            return cal1.isBusinessDay(qlDate) && cal2.isBusinessDay(qlDate);
        }

        /// <summary>
        /// Fallback expiry calculation when database is unavailable.
        /// Uses QLNet FXCalendars for accurate business day calculations.
        /// </summary>
        private DateTime CalculateExpiryFallback(DateTime tradeDate, string tenor, string currencyPair = "EURUSD")
        {
            DateTime result = tradeDate;

            // Parse tenor
            if (string.IsNullOrWhiteSpace(tenor) || tenor.Length < 2)
                return result;

            char unit = char.ToUpper(tenor[tenor.Length - 1]);
            if (!int.TryParse(tenor.Substring(0, tenor.Length - 1), out int amount))
                return result;

            // Add tenor
            switch (unit)
            {
                case 'D':
                    result = result.AddDays(amount);
                    break;
                case 'W':
                    result = result.AddDays(amount * 7);
                    break;
                case 'M':
                    result = result.AddMonths(amount);
                    break;
                case 'Y':
                    result = result.AddYears(amount);
                    break;
            }

            // Adjust to next business day using fallback calendars
            while (!IsBusinessDayFallback(result, currencyPair))
            {
                result = result.AddDays(1);
            }

            return result;
        }

        /// <summary>
        /// Check if a date is a business day for a currency pair.
        /// </summary>
        public bool IsBusinessDay(DateTime date, string currencyPair)
        {
            if (_isDatabaseAvailable && _holidayCalendar != null)
            {
                try
                {
                    return CurrencyCalendarMapper.IsBusinessDay(currencyPair, date, _holidayCalendar);
                }
                catch
                {
                    // Fallback to QLNet calendars
                    return IsBusinessDayFallback(date, currencyPair);
                }
            }
            else
            {
                // Use QLNet FXCalendars fallback
                return IsBusinessDayFallback(date, currencyPair);
            }
        }

        /// <summary>
        /// Get next business day for a currency pair.
        /// </summary>
        public DateTime GetNextBusinessDay(DateTime date, string currencyPair, bool includeStart = true)
        {
            return CurrencyCalendarMapper.NextBusinessDay(currencyPair, date, _holidayCalendar, includeStart);
        }

        /// <summary>
        /// Get previous business day for a currency pair.
        /// </summary>
        public DateTime GetPreviousBusinessDay(DateTime date, string currencyPair, bool includeStart = true)
        {
            return CurrencyCalendarMapper.PreviousBusinessDay(currencyPair, date, _holidayCalendar, includeStart);
        }

        /// <summary>
        /// Format expiry date for display (matches existing format: "30-Dec-25, Tue (1M)").
        /// </summary>
        public string FormatExpiryForDisplay(DateTime expiryDate, string tenor = null)
        {
            var enUS = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            string dateStr = expiryDate.ToString("dd-MMM-yy, ddd", enUS);

            if (!string.IsNullOrEmpty(tenor))
            {
                return $"{dateStr} ({tenor})";
            }

            return dateStr;
        }
    }
}
