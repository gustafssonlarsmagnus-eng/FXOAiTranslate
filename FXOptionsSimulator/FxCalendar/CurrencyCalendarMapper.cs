using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace FX.Infrastructure.Calendars.Legacy
{
    public static class CurrencyCalendarMapper
    {
        // Currency -> Calendar name
        private static readonly Dictionary<string, string> CurrencyToCalendar =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "EUR", "TARGET" },
            { "USD", "USA" },
            { "SEK", "SWEDEN" },
            { "NOK", "NORWAY" },
            { "GBP", "ENGLAND" },
            { "CAD", "CANADA" },
            { "CHF", "SWITZERLAND" },
            { "AUD", "AUSTRALIA" },
            { "RUB", "RUSSIA" },
            { "JPY", "JAPAN" },
            { "TRY", "TURKEY" },
            { "PHP", "PHILIPPINES" }
        };

        /// <summary>
        /// Spot lag configuration by currency pair.
        /// Most FX pairs use T+2, but some exceptions use T+1 or T+0.
        /// </summary>
        private static readonly Dictionary<string, int> SpotLagOverrides =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "USDCAD", 1 },  // T+1
            { "USDTRY", 1 },  // T+1
            { "USDRUB", 1 },  // T+1
            { "USDPHP", 1 }   // T+1
            // All other pairs default to T+2
        };

        /// <summary>
        /// Get spot lag (in business days) for a currency pair.
        /// Default is T+2, with exceptions for specific pairs.
        /// </summary>
        public static int GetSpotLag(string ccyPair)
        {
            var pair = ccyPair.Replace("/", "").Trim().ToUpperInvariant();

            if (SpotLagOverrides.TryGetValue(pair, out int lag))
                return lag;

            return 2; // Default T+2
        }

        public static string[] GetCalendarsForPair(string ccyPair)
        {
            if (string.IsNullOrWhiteSpace(ccyPair))
                throw new ArgumentException("Valutapar saknas.", nameof(ccyPair));

            var pair = ccyPair.Replace("/", "").Trim().ToUpperInvariant();
            if (pair.Length != 6)
                throw new ArgumentException("Valutapar måste vara 6 tecken, t.ex. EURSEK.", nameof(ccyPair));

            var ccy1 = pair.Substring(0, 3);
            var ccy2 = pair.Substring(3, 3);

            string cal1, cal2;
            if (!CurrencyToCalendar.TryGetValue(ccy1, out cal1))
                throw new KeyNotFoundException("Ingen kalender mappad för " + ccy1);
            if (!CurrencyToCalendar.TryGetValue(ccy2, out cal2))
                throw new KeyNotFoundException("Ingen kalender mappad för " + ccy2);

            return new[] { cal1, cal2 };
        }

        public static bool IsBusinessDay(string ccyPair, DateTime date, HolidayCalendar holidayCal)
        {
            var d = date.Date;
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday)
                return false;

            var calendars = GetCalendarsForPair(ccyPair);
            // hämta bara denna dag
            var dt = holidayCal.GetHolidays(calendars, d, d);
            return dt == null || dt.Rows.Count == 0;
        }

        public static DateTime NextBusinessDay(string ccyPair, DateTime startDate, HolidayCalendar holidayCal,
                                               bool includeStart = true, int lookaheadDays = 370)
        {
            var calendars = GetCalendarsForPair(ccyPair);
            DateTime start = startDate.Date;
            DateTime from = start;
            DateTime to = start.AddDays(Math.Max(lookaheadDays, 1));

            var holidays = holidayCal.GetHolidays(calendars, from, to);
            var holidaySet = BuildHolidaySet(holidays);

            DateTime d = includeStart ? start : start.AddDays(1);
            int safety = 0;
            while (safety++ < lookaheadDays + 2)
            {
                if (!IsWeekend(d) && !holidaySet.Contains(d))
                    return d;
                d = d.AddDays(1);
            }
            throw new InvalidOperationException("NextBusinessDay: ingen dag hittades inom lookahead.");
        }

        public static DateTime PreviousBusinessDay(string ccyPair, DateTime startDate, HolidayCalendar holidayCal,
                                                   bool includeStart = true, int lookbackDays = 370)
        {
            var calendars = GetCalendarsForPair(ccyPair);
            DateTime start = startDate.Date;
            DateTime from = start.AddDays(-Math.Max(lookbackDays, 1));
            DateTime to = start;

            var holidays = holidayCal.GetHolidays(calendars, from, to);
            var holidaySet = BuildHolidaySet(holidays);

            DateTime d = includeStart ? start : start.AddDays(-1);
            int safety = 0;
            while (safety++ < lookbackDays + 2)
            {
                if (!IsWeekend(d) && !holidaySet.Contains(d))
                    return d;
                d = d.AddDays(-1);
            }
            throw new InvalidOperationException("PreviousBusinessDay: ingen dag hittades inom lookback.");
        }

        /// <summary>
        /// Calculate FX options expiry date from tenor using correct market convention:
        /// 1. Spot Date = Trade Date + Spot Lag (T+2 for most pairs, T+1 for USD/CAD, USD/TRY, etc.)
        /// 2. Delivery Date = Spot Date + Tenor
        /// 3. Expiry Date = Delivery Date - Spot Lag
        ///
        /// This ensures expiry is always 2 business days (or 1 for T+1 pairs) before delivery.
        /// </summary>
        /// <param name="ccyPair">Currency pair (e.g., "EURUSD")</param>
        /// <param name="tradeDate">Trade date (starting point)</param>
        /// <param name="tenor">Tenor string (e.g., "1M", "3M", "6M", "1Y", "2W", "10D")</param>
        /// <param name="holidayCal">Holiday calendar instance</param>
        /// <param name="useModifiedFollowing">If true, use Modified Following convention; if false, use Preceding</param>
        /// <returns>FX options expiry date</returns>
        public static DateTime CalculateExpiryFromTenor(
            string ccyPair,
            DateTime tradeDate,
            string tenor,
            HolidayCalendar holidayCal,
            bool useModifiedFollowing = true)
        {
            if (string.IsNullOrWhiteSpace(tenor))
                throw new ArgumentException("Tenor cannot be empty.", nameof(tenor));

            int spotLag = GetSpotLag(ccyPair);

            // Step 1: Calculate Spot Date = Trade Date + Spot Lag business days
            DateTime spotDate = AddBusinessDays(ccyPair, tradeDate.Date, spotLag, holidayCal);

            // Step 2: Calculate Delivery Date = Spot Date + Tenor
            DateTime deliveryUnadjusted = ParseTenorToDate(spotDate, tenor);

            // Adjust delivery date if it falls on weekend/holiday
            DateTime deliveryDate = AdjustBusinessDay(ccyPair, deliveryUnadjusted, holidayCal, useModifiedFollowing);

            // Step 3: Calculate Expiry Date = Delivery Date - Spot Lag business days
            DateTime expiryDate = SubtractBusinessDays(ccyPair, deliveryDate, spotLag, holidayCal);

            // Step 4: FX Market Rule - January 1st can NEVER be an expiry or settlement date
            // (New Year's Day - global holiday)
            // If expiry falls on Jan 1st, move BACK to last business day of December
            if (expiryDate.Month == 1 && expiryDate.Day == 1)
            {
                expiryDate = PreviousBusinessDay(ccyPair, expiryDate, holidayCal, includeStart: false);
            }

            return expiryDate;
        }

        /// <summary>
        /// Add N business days to a date, skipping weekends and holidays.
        /// </summary>
        private static DateTime AddBusinessDays(string ccyPair, DateTime startDate, int businessDays, HolidayCalendar holidayCal)
        {
            DateTime current = startDate.Date;
            int daysAdded = 0;

            while (daysAdded < businessDays)
            {
                current = current.AddDays(1);
                if (IsBusinessDay(ccyPair, current, holidayCal))
                {
                    daysAdded++;
                }
            }

            return current;
        }

        /// <summary>
        /// Subtract N business days from a date, skipping weekends and holidays.
        /// </summary>
        private static DateTime SubtractBusinessDays(string ccyPair, DateTime startDate, int businessDays, HolidayCalendar holidayCal)
        {
            DateTime current = startDate.Date;
            int daysSubtracted = 0;

            while (daysSubtracted < businessDays)
            {
                current = current.AddDays(-1);
                if (IsBusinessDay(ccyPair, current, holidayCal))
                {
                    daysSubtracted++;
                }
            }

            return current;
        }

        /// <summary>
        /// Adjust a date to the nearest business day using Modified Following or Preceding convention.
        /// </summary>
        private static DateTime AdjustBusinessDay(string ccyPair, DateTime date, HolidayCalendar holidayCal, bool useModifiedFollowing)
        {
            if (IsBusinessDay(ccyPair, date, holidayCal))
                return date;

            if (useModifiedFollowing)
            {
                // Try next business day
                var next = NextBusinessDay(ccyPair, date, holidayCal, includeStart: false);

                // If crosses month boundary, use previous business day instead
                if (next.Month != date.Month || next.Year != date.Year)
                {
                    return PreviousBusinessDay(ccyPair, date, holidayCal, includeStart: false);
                }

                return next;
            }
            else
            {
                // Preceding convention: always go backward
                return PreviousBusinessDay(ccyPair, date, holidayCal, includeStart: false);
            }
        }

        /// <summary>
        /// Parse tenor string to a date (without business day adjustment).
        /// Supports: 1D, 2W, 1M, 3M, 6M, 1Y, etc.
        /// </summary>
        private static DateTime ParseTenorToDate(DateTime start, string tenor)
        {
            tenor = tenor.Trim().ToUpperInvariant();

            if (tenor.Length < 2)
                throw new ArgumentException($"Invalid tenor format: {tenor}");

            char unit = tenor[tenor.Length - 1];
            string numberPart = tenor.Substring(0, tenor.Length - 1);

            if (!int.TryParse(numberPart, out int amount))
                throw new ArgumentException($"Invalid tenor number: {numberPart} in {tenor}");

            return unit switch
            {
                'D' => start.AddDays(amount),
                'W' => start.AddDays(amount * 7),
                'M' => start.AddMonths(amount),
                'Y' => start.AddYears(amount),
                _ => throw new ArgumentException($"Invalid tenor unit: {unit} in {tenor}. Supported: D, W, M, Y")
            };
        }

        // ---- helpers ----
        private static bool IsWeekend(DateTime d)
        {
            var w = d.DayOfWeek;
            return w == DayOfWeek.Saturday || w == DayOfWeek.Sunday;
        }

        private static HashSet<DateTime> BuildHolidaySet(DataTable holidays)
        {
            var set = new HashSet<DateTime>();
            if (holidays == null) return set;

            foreach (DataRow r in holidays.Rows)
            {
                object val = r["HolidayDate"]; // kolumnnamn enligt HolidayCalendar.GetHolidays
                if (val is DateTime)
                    set.Add(((DateTime)val).Date);
            }
            return set;
        }
    }
}
