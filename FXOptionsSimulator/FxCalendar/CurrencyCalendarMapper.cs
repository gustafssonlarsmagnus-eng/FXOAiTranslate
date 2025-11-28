using System;
using System.Collections.Generic;
using System.Data;

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
            { "JPY", "JAPAN" }
        };

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
        /// NEW: Calculate expiry date from tenor (1M, 3M, 6M, 1Y, etc.) with business day adjustment.
        /// Uses Modified Following convention by default: adjust to next business day, but if that
        /// crosses into next month, use previous business day instead.
        /// </summary>
        /// <param name="ccyPair">Currency pair (e.g., "EURUSD")</param>
        /// <param name="tradeDate">Trade date (starting point)</param>
        /// <param name="tenor">Tenor string (e.g., "1M", "3M", "6M", "1Y", "2W", "10D")</param>
        /// <param name="holidayCal">Holiday calendar instance</param>
        /// <param name="useModifiedFollowing">If true, use Modified Following convention; if false, use Preceding</param>
        /// <returns>Business day adjusted expiry date</returns>
        public static DateTime CalculateExpiryFromTenor(
            string ccyPair,
            DateTime tradeDate,
            string tenor,
            HolidayCalendar holidayCal,
            bool useModifiedFollowing = true)
        {
            if (string.IsNullOrWhiteSpace(tenor))
                throw new ArgumentException("Tenor cannot be empty.", nameof(tenor));

            // Parse tenor to get unadjusted date
            DateTime unadjusted = ParseTenorToDate(tradeDate.Date, tenor);

            Console.WriteLine($"[FX-CALENDAR] Tenor {tenor} from {tradeDate:yyyy-MM-dd} -> Unadjusted: {unadjusted:yyyy-MM-dd (ddd)}");

            // Check if already a business day
            if (IsBusinessDay(ccyPair, unadjusted, holidayCal))
            {
                Console.WriteLine($"[FX-CALENDAR] {unadjusted:yyyy-MM-dd} is already a business day. No adjustment needed.");
                return unadjusted;
            }

            // Need to adjust
            if (useModifiedFollowing)
            {
                // Try next business day
                var next = NextBusinessDay(ccyPair, unadjusted, holidayCal, includeStart: false);

                // If crosses month boundary, use previous business day instead
                if (next.Month != unadjusted.Month || next.Year != unadjusted.Year)
                {
                    var prev = PreviousBusinessDay(ccyPair, unadjusted, holidayCal, includeStart: false);
                    Console.WriteLine($"[FX-CALENDAR] Modified Following: {unadjusted:yyyy-MM-dd} (weekend/holiday) -> {next:yyyy-MM-dd} crosses month -> {prev:yyyy-MM-dd} (final)");
                    return prev;
                }

                Console.WriteLine($"[FX-CALENDAR] Modified Following: {unadjusted:yyyy-MM-dd} (weekend/holiday) -> {next:yyyy-MM-dd} (next business day)");
                return next;
            }
            else
            {
                // Preceding convention: always go backward
                var prev = PreviousBusinessDay(ccyPair, unadjusted, holidayCal, includeStart: false);
                Console.WriteLine($"[FX-CALENDAR] Preceding: {unadjusted:yyyy-MM-dd} (weekend/holiday) -> {prev:yyyy-MM-dd} (previous business day)");
                return prev;
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
