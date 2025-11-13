using QLNet;

public sealed record DatePolicy
{
    // Expiry
    public BusinessDayConvention ExpiryConvention { get; init; } = BusinessDayConvention.ModifiedFollowing;
    public bool ExpiryEOM { get; init; } = true;

    // Premium - Use premium currency calendar only (not joint) for settlement
    // HSBC/GFI appears to expect T+1 for premium settlement
    public CalendarMode PremiumCalendarMode { get; init; } = CalendarMode.PremiumCcyOnly;
    public BusinessDayConvention PremiumConvention { get; init; } = BusinessDayConvention.Following;
    public int PremiumSettleDays { get; init; } = 1;

    // Spot lag resolver (same for everyone)
    public Func<string, PairSpotLag> SpotLagForPair { get; init; } = pair =>
    {
        switch ((pair ?? "").ToUpperInvariant())
        {
            case "USDCAD": return PairSpotLag.OneBD;
            case "USDTRY": return PairSpotLag.OneBD;
            default: return PairSpotLag.TwoBD; // most majors
        }
    };
}

public static class GlobalDatePolicy
{
    public static readonly DatePolicy Policy = new DatePolicy
    {
        ExpiryConvention = BusinessDayConvention.ModifiedFollowing,
        ExpiryEOM = true,
        PremiumCalendarMode = CalendarMode.PremiumCcyOnly,
        PremiumConvention = BusinessDayConvention.Following,
        PremiumSettleDays = 1  // HSBC/GFI expects T+1
    };
}
