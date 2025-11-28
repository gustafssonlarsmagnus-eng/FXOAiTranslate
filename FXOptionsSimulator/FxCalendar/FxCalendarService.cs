using System;
using System.Configuration;

namespace FX.Infrastructure.Calendars.Legacy
{
    /// <summary>
    /// Service for FX calendar operations using database-backed holiday calendars.
    /// Replaces QLNet-based FxDateService with reliable, database-driven business day calculations.
    /// </summary>
    public class FxCalendarService
    {
        private readonly HolidayCalendar _holidayCalendar;
        private static FxCalendarService _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// Private constructor for singleton pattern.
        /// </summary>
        private FxCalendarService(string connectionString)
        {
            _holidayCalendar = new HolidayCalendar(connectionString);
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
                            Console.WriteLine("[FX-CALENDAR] Service initialized with database connection.");

                            // Run diagnostic to check December 2025 holidays
                            _instance._holidayCalendar.DiagnoseDecember2025Holidays();
                        }
                    }
                }
                return _instance;
            }
        }

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
                return CurrencyCalendarMapper.CalculateExpiryFromTenor(
                    currencyPair,
                    tradeDate,
                    tenor,
                    _holidayCalendar,
                    useModifiedFollowing: true
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FX-CALENDAR] ERROR calculating expiry for {tenor}/{currencyPair}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Check if a date is a business day for a currency pair.
        /// </summary>
        public bool IsBusinessDay(DateTime date, string currencyPair)
        {
            return CurrencyCalendarMapper.IsBusinessDay(currencyPair, date, _holidayCalendar);
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
