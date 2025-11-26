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

        var calPair = JointCalendarForPair(rules.Ccy1, rules.Ccy2)
                      ?? throw new ArgumentException($"Unsupported pair calendars: {rules.Ccy1}/{rules.Ccy2}");
        var calPremium = rules.PremiumCalMode == CalendarMode.PremiumCcyOnly
            ? CalendarFromCcy(premiumCcy) ?? throw new ArgumentException($"Unsupported premium calendar for {premiumCcy}")
            : calPair;

        var trade = ToQl(tradeDateUtc.Date);
        if (!calPair.isBusinessDay(trade)) trade = calPair.adjust(trade, BusinessDayConvention.Following);

        var spot = AdvanceBusinessDays(calPair, trade, (int)rules.SpotLag, BusinessDayConvention.Following);
        var expiry = ComputeExpiryFromTenor(tenor, trade, calPair, rules.ExpiryConvention, rules.ExpiryEOM);
        var delivery = AdvanceBusinessDays(calPair, expiry, (int)rules.SpotLag, BusinessDayConvention.Following);
        var premium = AdvanceBusinessDays(calPremium, trade, rules.PremiumSettleDays, rules.PremiumConvention);

        return (ToSys(trade), ToSys(spot), ToSys(expiry), ToSys(delivery), ToSys(premium));
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
        "USD" => new UnitedStates(UnitedStates.Market.Settlement),
        "EUR" => new TARGET(),
        "GBP" => new UnitedKingdom(UnitedKingdom.Market.Settlement),
        "JPY" => new Japan(),
        "CHF" => new Switzerland(),
        "CAD" => new Canada(),
        "AUD" => new Australia(),
        "NZD" => new NewZealand(),
        "SEK" => new Sweden(),
        "NOK" => new Norway(),
        "DKK" => new Denmark(),
        _ => null
    };

    private static Date ToQl(DateTime dt) => new Date(dt.Day, (Month)dt.Month, dt.Year);

    // Use capitalized properties on QLNet.Date
    private static DateTime ToSys(Date d) => new DateTime(d.Year, (int)d.Month, d.Day);
}
