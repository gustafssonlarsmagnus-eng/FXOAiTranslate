using QLNet;
using System;
using System.Collections.Generic;

/// <summary>
/// Custom FX market calendars that extend QLNet calendars with FX-specific holidays
/// </summary>
public static class FXCalendars
{
    /// <summary>
    /// US calendar with FX market holidays (Thanksgiving Friday, etc.)
    /// </summary>
    public class UnitedStatesFX : Calendar
    {
        private readonly UnitedStates _baseCalendar;
        private readonly HashSet<Date> _additionalHolidays;

        public UnitedStatesFX() : base()
        {
            _baseCalendar = new UnitedStates(UnitedStates.Market.Settlement);
            _additionalHolidays = new HashSet<Date>();

            // Add FX-specific holidays
            AddFXHolidays();
        }

        private void AddFXHolidays()
        {
            // Add ALL US Federal Reserve holidays explicitly (2025-2029)
            // Source: https://www.federalreserve.gov/aboutthefed/k8.htm
            AddUSFederalHolidays2025();
            AddUSFederalHolidays2026();
            AddUSFederalHolidays2027();
            AddUSFederalHolidays2028();
            AddUSFederalHolidays2029();

            // Christmas Eve when on weekday (Dec 24)
            AddChristmasEveHolidays();

            // New Year's Eve when on weekday (Dec 31) - early close, treat as holiday for settlement
            AddNewYearsEveHolidays();
        }

        private void AddUSFederalHolidays2025()
        {
            // Source: https://www.federalreserve.gov/aboutthefed/k8.htm
            _additionalHolidays.Add(new Date(1, Month.January, 2025));    // New Year's Day (Wed)
            _additionalHolidays.Add(new Date(20, Month.January, 2025));   // Martin Luther King Jr. Day (Mon)
            _additionalHolidays.Add(new Date(17, Month.February, 2025));  // Presidents' Day (Mon)
            _additionalHolidays.Add(new Date(26, Month.May, 2025));       // Memorial Day (Mon)
            _additionalHolidays.Add(new Date(19, Month.June, 2025));      // Juneteenth (Thu)
            _additionalHolidays.Add(new Date(4, Month.July, 2025));       // Independence Day (Fri)
            _additionalHolidays.Add(new Date(1, Month.September, 2025));  // Labor Day (Mon)
            _additionalHolidays.Add(new Date(13, Month.October, 2025));   // Columbus Day (Mon)
            _additionalHolidays.Add(new Date(11, Month.November, 2025));  // Veterans Day (Tue)
            _additionalHolidays.Add(new Date(27, Month.November, 2025));  // Thanksgiving (Thu)
            _additionalHolidays.Add(new Date(25, Month.December, 2025));  // Christmas (Thu)
        }

        private void AddUSFederalHolidays2026()
        {
            // Source: https://www.federalreserve.gov/aboutthefed/k8.htm
            _additionalHolidays.Add(new Date(1, Month.January, 2026));    // New Year's Day (Thu)
            _additionalHolidays.Add(new Date(19, Month.January, 2026));   // Martin Luther King Jr. Day (Mon)
            _additionalHolidays.Add(new Date(16, Month.February, 2026));  // Presidents' Day (Mon)
            _additionalHolidays.Add(new Date(25, Month.May, 2026));       // Memorial Day (Mon)
            _additionalHolidays.Add(new Date(19, Month.June, 2026));      // Juneteenth (Fri)
            _additionalHolidays.Add(new Date(3, Month.July, 2026));       // Independence Day observed (Fri, since July 4 is Sat)
            _additionalHolidays.Add(new Date(7, Month.September, 2026));  // Labor Day (Mon)
            _additionalHolidays.Add(new Date(12, Month.October, 2026));   // Columbus Day (Mon)
            _additionalHolidays.Add(new Date(11, Month.November, 2026));  // Veterans Day (Wed)
            _additionalHolidays.Add(new Date(26, Month.November, 2026));  // Thanksgiving (Thu)
            _additionalHolidays.Add(new Date(25, Month.December, 2026));  // Christmas (Fri)
        }

        private void AddUSFederalHolidays2027()
        {
            // Source: https://www.federalreserve.gov/aboutthefed/k8.htm
            _additionalHolidays.Add(new Date(1, Month.January, 2027));    // New Year's Day (Fri)
            _additionalHolidays.Add(new Date(18, Month.January, 2027));   // Martin Luther King Jr. Day (Mon)
            _additionalHolidays.Add(new Date(15, Month.February, 2027));  // Presidents' Day (Mon)
            _additionalHolidays.Add(new Date(31, Month.May, 2027));       // Memorial Day (Mon)
            _additionalHolidays.Add(new Date(18, Month.June, 2027));      // Juneteenth observed (Fri, since June 19 is Sat)
            _additionalHolidays.Add(new Date(5, Month.July, 2027));       // Independence Day observed (Mon, since July 4 is Sun)
            _additionalHolidays.Add(new Date(6, Month.September, 2027));  // Labor Day (Mon)
            _additionalHolidays.Add(new Date(11, Month.October, 2027));   // Columbus Day (Mon)
            _additionalHolidays.Add(new Date(11, Month.November, 2027));  // Veterans Day (Thu)
            _additionalHolidays.Add(new Date(25, Month.November, 2027));  // Thanksgiving (Thu)
            _additionalHolidays.Add(new Date(24, Month.December, 2027));  // Christmas observed (Fri, since Dec 25 is Sat)
        }

        private void AddUSFederalHolidays2028()
        {
            // Source: https://www.federalreserve.gov/aboutthefed/k8.htm
            _additionalHolidays.Add(new Date(31, Month.December, 2027));  // New Year's Day observed (Fri, since Jan 1 2028 is Sat)
            _additionalHolidays.Add(new Date(17, Month.January, 2028));   // Martin Luther King Jr. Day (Mon)
            _additionalHolidays.Add(new Date(21, Month.February, 2028));  // Presidents' Day (Mon)
            _additionalHolidays.Add(new Date(29, Month.May, 2028));       // Memorial Day (Mon)
            _additionalHolidays.Add(new Date(19, Month.June, 2028));      // Juneteenth (Mon)
            _additionalHolidays.Add(new Date(4, Month.July, 2028));       // Independence Day (Tue)
            _additionalHolidays.Add(new Date(4, Month.September, 2028));  // Labor Day (Mon)
            _additionalHolidays.Add(new Date(9, Month.October, 2028));    // Columbus Day (Mon)
            _additionalHolidays.Add(new Date(10, Month.November, 2028));  // Veterans Day observed (Fri, since Nov 11 is Sat)
            _additionalHolidays.Add(new Date(23, Month.November, 2028));  // Thanksgiving (Thu)
            _additionalHolidays.Add(new Date(25, Month.December, 2028));  // Christmas (Mon)
        }

        private void AddUSFederalHolidays2029()
        {
            // Source: https://www.federalreserve.gov/aboutthefed/k8.htm
            _additionalHolidays.Add(new Date(1, Month.January, 2029));    // New Year's Day (Mon)
            _additionalHolidays.Add(new Date(15, Month.January, 2029));   // Martin Luther King Jr. Day (Mon)
            _additionalHolidays.Add(new Date(19, Month.February, 2029));  // Presidents' Day (Mon)
            _additionalHolidays.Add(new Date(28, Month.May, 2029));       // Memorial Day (Mon)
            _additionalHolidays.Add(new Date(19, Month.June, 2029));      // Juneteenth (Tue)
            _additionalHolidays.Add(new Date(4, Month.July, 2029));       // Independence Day (Wed)
            _additionalHolidays.Add(new Date(3, Month.September, 2029));  // Labor Day (Mon)
            _additionalHolidays.Add(new Date(8, Month.October, 2029));    // Columbus Day (Mon)
            _additionalHolidays.Add(new Date(12, Month.November, 2029));  // Veterans Day observed (Mon, since Nov 11 is Sun)
            _additionalHolidays.Add(new Date(22, Month.November, 2029));  // Thanksgiving (Thu)
            _additionalHolidays.Add(new Date(25, Month.December, 2029));  // Christmas (Tue)
        }

        private void AddChristmasEveHolidays()
        {
            // Christmas Eve when it falls on a weekday (Mon-Fri)
            // 2024: Dec 24 (Tue) - Holiday
            // 2025: Dec 24 (Wed) - Holiday
            // 2026: Dec 24 (Thu) - Holiday
            // 2027: Dec 24 (Fri) - Holiday

            var christmasEves = new[]
            {
                new Date(24, Month.December, 2024),
                new Date(24, Month.December, 2025),
                new Date(24, Month.December, 2026),
                new Date(24, Month.December, 2027),
                new Date(24, Month.December, 2028),  // Sun - skip
                new Date(24, Month.December, 2029),  // Mon - add
                new Date(24, Month.December, 2030)   // Tue - add
            };

            foreach (var date in christmasEves)
            {
                var dayOfWeek = date.DayOfWeek;
                if (dayOfWeek != DayOfWeek.Saturday && dayOfWeek != DayOfWeek.Sunday)
                {
                    _additionalHolidays.Add(date);
                }
            }
        }

        private void AddNewYearsEveHolidays()
        {
            // New Year's Eve when it falls on a weekday (early close)
            // Only add if it's a weekday (Mon-Fri)

            var newYearsEves = new[]
            {
                new Date(31, Month.December, 2024),  // Tue - add
                new Date(31, Month.December, 2025),  // Wed - add
                new Date(31, Month.December, 2026),  // Thu - add
                new Date(31, Month.December, 2027),  // Fri - add
                new Date(31, Month.December, 2028),  // Sun - skip
                new Date(31, Month.December, 2029),  // Mon - add
                new Date(31, Month.December, 2030)   // Tue - add
            };

            foreach (var date in newYearsEves)
            {
                var dayOfWeek = date.DayOfWeek;
                if (dayOfWeek != DayOfWeek.Saturday && dayOfWeek != DayOfWeek.Sunday)
                {
                    _additionalHolidays.Add(date);
                }
            }
        }

        public override bool isBusinessDay(Date d)
        {
            // Check base calendar first
            if (!_baseCalendar.isBusinessDay(d))
                return false;

            // Check additional FX holidays
            if (_additionalHolidays.Contains(d))
                return false;

            return true;
        }

        public override string name()
        {
            return "US FX Settlement";
        }
    }

    /// <summary>
    /// EUR calendar with FX market holidays
    /// </summary>
    public class TargetFX : Calendar
    {
        private readonly TARGET _baseCalendar;
        private readonly HashSet<Date> _additionalHolidays;

        public TargetFX() : base()
        {
            _baseCalendar = new TARGET();
            _additionalHolidays = new HashSet<Date>();

            // TARGET already has Dec 24-26, Dec 31 - Jan 1 as holidays
            // No additional FX-specific holidays needed for EUR
        }

        public override bool isBusinessDay(Date d)
        {
            return _baseCalendar.isBusinessDay(d);
        }

        public override string name()
        {
            return "EUR FX Settlement";
        }
    }
}
