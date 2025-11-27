using QLNet;
using System;
using System.Globalization;
using static QLNet.JointCalendar;
using QLCal = QLNet.Calendar;   // <-- disambiguate Calendar

public enum PairSpotLag { OneBD = 1, TwoBD = 2 }
public enum CalendarMode { PremiumCcyOnly, JointPairCurrencies }

// Make this a record so we can use 'with'
public sealed record FxDateRules
{
    public string Ccy1 { get; init; } = "EUR";
    public string Ccy2 { get; init; } = "USD";
    public PairSpotLag SpotLag { get; init; } = PairSpotLag.TwoBD;
    public BusinessDayConvention ExpiryConvention { get; init; } = BusinessDayConvention.ModifiedFollowing;
    public bool ExpiryEOM { get; init; } = true;
    public int PremiumSettleDays { get; init; } = 2;
    public CalendarMode PremiumCalMode { get; init; } = CalendarMode.PremiumCcyOnly;
    public BusinessDayConvention PremiumConvention { get; init; } = BusinessDayConvention.Following;
}

public static class FxDateService
{
    public static (DateTime tradeDate, DateTime spotDate, DateTime expiryDate, DateTime deliveryDate, DateTime premiumDate)
        ComputeDates(DateTime tradeDateUtc, string pair, string tenor, string premiumCcy, FxDateRules rules)
    {
        var (ccy1, ccy2) = ParsePair(pair);
        rules = rules with { Ccy1 = ccy1, Ccy2 = ccy2 }; // now valid because FxDateRules is a record

        // Get individual calendars for custom holiday checking
        var cal1 = CalendarFromCcy(ccy1);
        var cal2 = CalendarFromCcy(ccy2);
        var calPair = new JointCalendar(cal1, cal2, JointCalendarRule.JoinHolidays);

        var calPremium = rules.PremiumCalMode == CalendarMode.PremiumCcyOnly
            ? CalendarFromCcy(premiumCcy) ?? throw new ArgumentException($"Unsupported premium calendar for {premiumCcy}")
            : calPair;

        var trade = ToQl(tradeDateUtc.Date);
        if (!IsBusinessDay(trade, cal1, cal2)) trade = AdjustDate(trade, cal1, cal2, BusinessDayConvention.Following);

        var spot = AdvanceBusinessDaysCustom(trade, (int)rules.SpotLag, cal1, cal2, BusinessDayConvention.Following);
        var expiry = ComputeExpiryFromTenorCustom(tenor, trade, cal1, cal2, rules.ExpiryConvention, rules.ExpiryEOM);
        var delivery = AdvanceBusinessDaysCustom(expiry, (int)rules.SpotLag, cal1, cal2, BusinessDayConvention.Following);
        var premium = AdvanceBusinessDaysCustom(trade, rules.PremiumSettleDays, CalendarFromCcy(premiumCcy), null, rules.PremiumConvention);

        return (ToSys(trade), ToSys(spot), ToSys(expiry), ToSys(delivery), ToSys(premium));
    }

    // Check if a date is a business day in BOTH calendars (accounting for custom holidays)
    private static bool IsBusinessDay(Date d, QLCal cal1, QLCal cal2)
    {
        bool isBD1 = (cal1 is FXCalendars.UnitedStatesFX usFX) ? usFX.isBusinessDay(d) :
                     (cal1 is FXCalendars.TargetFX targetFX) ? targetFX.isBusinessDay(d) :
                     (cal1 is FXCalendars.SwedenFX sekFX) ? sekFX.isBusinessDay(d) :
                     cal1.isBusinessDay(d);

        if (cal2 == null) return isBD1;

        bool isBD2 = (cal2 is FXCalendars.UnitedStatesFX usFX2) ? usFX2.isBusinessDay(d) :
                     (cal2 is FXCalendars.TargetFX targetFX2) ? targetFX2.isBusinessDay(d) :
                     (cal2 is FXCalendars.SwedenFX sekFX2) ? sekFX2.isBusinessDay(d) :
                     cal2.isBusinessDay(d);

        return isBD1 && isBD2;
    }

    // Adjust date using custom calendar logic
    private static Date AdjustDate(Date d, QLCal cal1, QLCal cal2, BusinessDayConvention conv)
    {
        while (!IsBusinessDay(d, cal1, cal2))
        {
            if (conv == BusinessDayConvention.Following || conv == BusinessDayConvention.ModifiedFollowing)
                d = d + 1;
            else if (conv == BusinessDayConvention.Preceding || conv == BusinessDayConvention.ModifiedPreceding)
                d = d - 1;
            else
                break;
        }
        return d;
    }

    // Advance business days using custom calendar logic
    private static Date AdvanceBusinessDaysCustom(Date start, int bd, QLCal cal1, QLCal cal2, BusinessDayConvention conv)
    {
        var d = start;
        var moved = 0;
        while (moved < bd)
        {
            d = d + 1;
            if (IsBusinessDay(d, cal1, cal2)) moved++;
        }
        return AdjustDate(d, cal1, cal2, conv);
    }

    // Compute expiry using custom calendar logic
    private static Date ComputeExpiryFromTenorCustom(string tenor, Date trade, QLCal cal1, QLCal cal2, BusinessDayConvention conv, bool eom)
    {
        tenor = (tenor ?? "").Trim().ToUpperInvariant();
        if (tenor.EndsWith("D")) return AdvanceBusinessDaysCustom(trade, int.Parse(tenor[..^1]), cal1, cal2, conv);
        if (tenor.EndsWith("W")) return AdvanceBusinessDaysCustom(trade, int.Parse(tenor[..^1]) * 5, cal1, cal2, conv);

        // For month/year tenors, use QLNet's advance but then adjust with our custom logic
        Date result;
        if (tenor.EndsWith("M"))
            result = cal1.advance(trade, new Period(int.Parse(tenor[..^1]), TimeUnit.Months), conv, eom);
        else if (tenor.EndsWith("Y"))
            result = cal1.advance(trade, new Period(int.Parse(tenor[..^1]), TimeUnit.Years), conv, eom);
        else if (int.TryParse(tenor, out var d))
            return AdvanceBusinessDaysCustom(trade, d, cal1, cal2, conv);
        else
            throw new ArgumentException($"Unsupported tenor: {tenor}");

        // Adjust result using our custom calendar logic
        return AdjustDate(result, cal1, cal2, conv);
    }

    public static string Ymd(DateTime dt) => dt.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static (string, string) ParsePair(string s)
    {
        s = (s ?? "").Trim().ToUpperInvariant().Replace("/", "");
        if (s.Length == 6) return (s[..3], s[3..]);
        throw new ArgumentException($"Unrecognized pair: {s}");
    }

    // Note the QLCal type to avoid ambiguity
    private static Date ComputeExpiryFromTenor(string tenor, Date trade, QLCal cal, BusinessDayConvention conv, bool eom)
    {
        tenor = (tenor ?? "").Trim().ToUpperInvariant();
        if (tenor.EndsWith("D")) return AdvanceBusinessDays(cal, trade, int.Parse(tenor[..^1]), conv);
        if (tenor.EndsWith("W")) return AdvanceBusinessDays(cal, trade, int.Parse(tenor[..^1]) * 5, conv);
        if (tenor.EndsWith("M")) return cal.advance(trade, new Period(int.Parse(tenor[..^1]), TimeUnit.Months), conv, eom);
        if (tenor.EndsWith("Y")) return cal.advance(trade, new Period(int.Parse(tenor[..^1]), TimeUnit.Years), conv, eom);
        if (int.TryParse(tenor, out var d)) return AdvanceBusinessDays(cal, trade, d, conv);
        throw new ArgumentException($"Unsupported tenor: {tenor}");
    }

    private static Date AdvanceBusinessDays(QLCal cal, Date start, int bd, BusinessDayConvention conv)
    {
        var d = start; var moved = 0;
        while (moved < bd) { d = d + 1; if (cal.isBusinessDay(d)) moved++; }
        return cal.adjust(d, conv);
    }

    private static QLCal JointCalendarForPair(string c1, string c2)
    {
        var cal1 = CalendarFromCcy(c1);
        var cal2 = CalendarFromCcy(c2);
        return (cal1 == null || cal2 == null) ? null : new JointCalendar(cal1, cal2, JointCalendarRule.JoinHolidays);
    }

    private static QLCal CalendarFromCcy(string ccy) => (ccy ?? "").ToUpperInvariant() switch
    {
        "USD" => new FXCalendars.UnitedStatesFX(),      // Use custom FX calendar
        "EUR" => new FXCalendars.TargetFX(),            // Use custom FX calendar (same as TARGET for now)
        "SEK" => new FXCalendars.SwedenFX(),            // Use custom FX calendar with Midsummer, etc.
        "GBP" => new UnitedKingdom(UnitedKingdom.Market.Settlement),
        "JPY" => new Japan(),
        "CHF" => new Switzerland(),
        "CAD" => new Canada(),
        "AUD" => new Australia(),
        "NZD" => new NewZealand(),
        "NOK" => new Norway(),
        "DKK" => new Denmark(),
        _ => null
    };

    private static Date ToQl(DateTime dt) => new Date(dt.Day, (Month)dt.Month, dt.Year);

    // Use capitalized properties on QLNet.Date
    private static DateTime ToSys(Date d) => new DateTime(d.Year, (int)d.Month, d.Day);
}
