using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace FX.Infrastructure.Calendars.Legacy
{
    public sealed class HolidayCalendar
    {
        private readonly string _connectionString;

        // Standardkolumnnamn i DB
        private const string ColMarket = "MARKET";
        private const string ColHoliday = "HOLIDAY_DATE";

        public HolidayCalendar(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Missing connection string.", nameof(connectionString));

            _connectionString = connectionString;
        }

        /// <summary>
        /// Hämtar helgdagar för angivna markets (t.ex. "SE","US","GB") inom intervallet [from, to].
        /// Lämna markets == null/empty för alla markets.
        /// </summary>
        public DataTable GetHolidays(IEnumerable<string> markets, DateTime from, DateTime to, int timeoutSeconds = 15)
        {
            // Normalisera inputs
            var marketList = (markets ?? Enumerable.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();

            if (to < from)
                throw new ArgumentException("Parameter 'to' must be >= 'from'.");

            // Bygg tom tabell med rätt schema (marknad + datum)
            var table = new DataTable("Holidays");
            table.Locale = CultureInfo.InvariantCulture;
            table.Columns.Add("Market", typeof(string));
            table.Columns.Add("HolidayDate", typeof(DateTime));

            // Dynamiskt IN-villkor (parametriserat)
            string whereMarkets = "";
            var parameters = new List<SqlParameter>();
            if (marketList.Count > 0)
            {
                var placeholders = new List<string>(marketList.Count);
                for (int i = 0; i < marketList.Count; i++)
                {
                    string pname = "@m" + i.ToString();
                    placeholders.Add(pname);
                    parameters.Add(new SqlParameter(pname, marketList[i]));
                }
                whereMarkets = " AND " + ColMarket + " IN (" + string.Join(",", placeholders) + ")";
            }

            string sql =
                "SELECT DISTINCT " + ColMarket + ", " + ColHoliday +
                " FROM Holiday WITH (NOLOCK) " +
                " WHERE " + ColHoliday + " >= @from AND " + ColHoliday + " <= @to " +
                whereMarkets +
                " ORDER BY " + ColMarket + ", " + ColHoliday + ";";

            parameters.Add(new SqlParameter("@from", SqlDbType.DateTime) { Value = from });
            parameters.Add(new SqlParameter("@to", SqlDbType.DateTime) { Value = to });

            ExecuteWithRetry(conn =>
            {
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.CommandTimeout = timeoutSeconds;
                    cmd.Parameters.AddRange(parameters.ToArray());

                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string market = rdr.GetString(0);
                            DateTime d = rdr.GetDateTime(1);
                            table.Rows.Add(market, d);
                        }
                    }
                }
            });

            return table;
        }

        /// <summary>
        /// True om datumet är en helgdag i någon av de angivna markets.
        /// </summary>
        public bool IsHoliday(DateTime date, IEnumerable<string> markets)
        {
            var dt = GetHolidays(markets, date.Date, date.Date);
            return dt.Rows.Count > 0;
        }

        /// <summary>
        /// Nästa bankdag >= startDate, givet en uppsättning markets (union av helgdagar).
        /// </summary>
        public DateTime NextBusinessDay(DateTime startDate, IEnumerable<string> markets)
        {
            // Hämta ett "rimligt" fönster (t.ex. 1 år fram)
            DateTime from = startDate.Date;
            DateTime to = from.AddYears(1);

            var holidays = GetHolidays(markets, from, to);
            var holidaySet = new HashSet<DateTime>(
                holidays.AsEnumerable().Select(r => ((DateTime)r["HolidayDate"]).Date));

            var d = from;
            while (true)
            {
                if (IsWeekend(d) || holidaySet.Contains(d))
                {
                    d = d.AddDays(1);
                    continue;
                }
                return d;
            }
        }

        private static bool IsWeekend(DateTime d)
        {
            var day = d.DayOfWeek;
            return day == DayOfWeek.Saturday || day == DayOfWeek.Sunday;
        }

        /// <summary>
        /// Kör en DB-aktion med enkel retry + exponential backoff.
        /// </summary>
        private void ExecuteWithRetry(Action<SqlConnection> action, int maxAttempts = 5, int initialDelayMs = 100)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            int attempt = 0;
            int delay = initialDelayMs;

            while (true)
            {
                try
                {
                    using (var conn = new SqlConnection(_connectionString))
                    {
                        conn.Open();
                        action(conn);
                        return;
                    }
                }
                catch (SqlException)
                {
                    attempt++;
                    if (attempt >= maxAttempts) throw;
                    Thread.Sleep(delay);
                    // Exponential backoff med cap
                    delay = Math.Min(delay * 2, 4000);
                }
            }
        }

        /// <summary>
        /// Diagnostic method: Print all holidays in December 2025 for debugging
        /// </summary>
        public void DiagnoseDecember2025Holidays()
        {
            try
            {
                Console.WriteLine("\n========== DATABASE DIAGNOSTIC: December 2025 Holidays ==========");

                var from = new DateTime(2025, 12, 1);
                var to = new DateTime(2025, 12, 31);

                // Query all markets for December 2025
                var allHolidays = GetHolidays(null, from, to); // null = all markets

                Console.WriteLine($"Total holidays found in December 2025: {allHolidays.Rows.Count}");

                if (allHolidays.Rows.Count == 0)
                {
                    Console.WriteLine("⚠️ WARNING: No holidays found for December 2025 in the database!");
                    Console.WriteLine("   The Holiday table may be missing 2025 data.");
                }
                else
                {
                    Console.WriteLine("\nHolidays by date:");
                    foreach (DataRow row in allHolidays.Rows)
                    {
                        string market = row["Market"].ToString();
                        DateTime date = (DateTime)row["HolidayDate"];
                        Console.WriteLine($"  {date:yyyy-MM-dd (ddd)} - Market: {market}");
                    }
                }

                // Check specific markets
                Console.WriteLine("\nChecking specific markets (USA, TARGET, ENGLAND, NORWAY, SWEDEN):");
                var specificMarkets = new[] { "USA", "TARGET", "ENGLAND", "NORWAY", "SWEDEN" };
                var specificHolidays = GetHolidays(specificMarkets, from, to);

                Console.WriteLine($"Holidays for USA/TARGET/ENGLAND/NORWAY/SWEDEN: {specificHolidays.Rows.Count}");
                foreach (DataRow row in specificHolidays.Rows)
                {
                    string market = row["Market"].ToString();
                    DateTime date = (DateTime)row["HolidayDate"];
                    Console.WriteLine($"  {date:yyyy-MM-dd (ddd)} - {market}");
                }

                Console.WriteLine("================================================================\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR during database diagnostic: {ex.Message}");
            }
        }
    }
}
