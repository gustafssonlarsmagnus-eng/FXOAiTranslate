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
            // Thanksgiving (4th Thursday) + Friday - many FX desks closed both days
            AddThanksgivingHolidays();

            // Christmas Eve when on weekday (Dec 24)
            AddChristmasEveHolidays();

            // New Year's Eve when on weekday (Dec 31) - early close, treat as holiday for settlement
            AddNewYearsEveHolidays();
        }

        private void AddThanksgivingHolidays()
        {
            // Thanksgiving is 4th Thursday of November (federal holiday)
            // Day after (Friday) is often observed as a holiday in FX markets

            // 2024: Thanksgiving = Nov 28 (Thu), Friday = Nov 29
            _additionalHolidays.Add(new Date(28, Month.November, 2024));  // Thanksgiving Thu
            _additionalHolidays.Add(new Date(29, Month.November, 2024));  // Day after Fri

            // 2025: Thanksgiving = Nov 27 (Thu), Friday = Nov 28
            _additionalHolidays.Add(new Date(27, Month.November, 2025));  // Thanksgiving Thu
            _additionalHolidays.Add(new Date(28, Month.November, 2025));  // Day after Fri

            // 2026: Thanksgiving = Nov 26 (Thu), Friday = Nov 27
            _additionalHolidays.Add(new Date(26, Month.November, 2026));  // Thanksgiving Thu
            _additionalHolidays.Add(new Date(27, Month.November, 2026));  // Day after Fri

            // 2027: Thanksgiving = Nov 25 (Thu), Friday = Nov 26
            _additionalHolidays.Add(new Date(25, Month.November, 2027));  // Thanksgiving Thu
            _additionalHolidays.Add(new Date(26, Month.November, 2027));  // Day after Fri

            // 2028: Thanksgiving = Nov 23 (Thu), Friday = Nov 24
            _additionalHolidays.Add(new Date(23, Month.November, 2028));  // Thanksgiving Thu
            _additionalHolidays.Add(new Date(24, Month.November, 2028));  // Day after Fri

            // 2029: Thanksgiving = Nov 22 (Thu), Friday = Nov 23
            _additionalHolidays.Add(new Date(22, Month.November, 2029));  // Thanksgiving Thu
            _additionalHolidays.Add(new Date(23, Month.November, 2029));  // Day after Fri

            // 2030: Thanksgiving = Nov 28 (Thu), Friday = Nov 29
            _additionalHolidays.Add(new Date(28, Month.November, 2030));  // Thanksgiving Thu
            _additionalHolidays.Add(new Date(29, Month.November, 2030));  // Day after Fri
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
