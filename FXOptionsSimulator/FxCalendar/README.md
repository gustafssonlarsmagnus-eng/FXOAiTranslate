# FX Calendar Service

Database-backed holiday calendar and business day calculation service for FX options trading.

## Overview

This service replaces QLNet-based calendar logic with reliable, database-driven business day calculations using production holiday calendars.

## Features

- ✅ **Database-backed holiday calendars** - Real holiday data from `AHSKvant-prod-db`
- ✅ **Modified Following convention** - Adjusts to next business day, but uses previous if crosses month boundary
- ✅ **Tenor calculation** - Supports 1M, 3M, 6M, 1Y, 2W, 10D formats
- ✅ **Multi-currency support** - USD, EUR, GBP, SEK, NOK, CHF, CAD, AUD, JPY, RUB
- ✅ **Retry logic** - Exponential backoff for database transient errors
- ✅ **Performance** - Holiday caching reduces database calls

## Components

### 1. **HolidayCalendar.cs**
Core database access class with retry logic.

**Key methods:**
- `GetHolidays(markets, from, to)` - Fetch holidays for specific markets/date range
- `IsHoliday(date, markets)` - Check if date is a holiday
- `NextBusinessDay(startDate, markets)` - Get next business day

### 2. **CurrencyCalendarMapper.cs**
Maps currency codes to calendar names and provides business day logic.

**Currency mapping:**
- EUR → TARGET
- USD → USA
- SEK → SWEDEN
- NOK → NORWAY
- GBP → ENGLAND
- CAD → CANADA
- CHF → SWITZERLAND
- AUD → AUSTRALIA
- JPY → JAPAN
- RUB → RUSSIA

**Key methods:**
- `CalculateExpiryFromTenor(ccyPair, tradeDate, tenor, holidayCal, useModifiedFollowing)` - **NEW:** Calculate expiry with business day adjustment
- `IsBusinessDay(ccyPair, date, holidayCal)` - Check if date is business day for currency pair
- `NextBusinessDay(ccyPair, startDate, holidayCal)` - Get next business day
- `PreviousBusinessDay(ccyPair, startDate, holidayCal)` - Get previous business day

### 3. **FxCalendarService.cs**
Singleton wrapper for easy integration.

**Usage:**
```csharp
// Calculate expiry from tenor
var expiry = FxCalendarService.Instance.CalculateExpiry(
    DateTime.UtcNow,
    "1M",
    "EURUSD"
);

// Check if business day
bool isBizDay = FxCalendarService.Instance.IsBusinessDay(
    new DateTime(2025, 12, 31),
    "EURUSD"
);

// Format for display
string displayText = FxCalendarService.Instance.FormatExpiryForDisplay(
    expiry,
    "1M"
);
// Output: "30-Dec-25, Tue (1M)"
```

## Configuration

Add connection string to `App.config`:

```xml
<configuration>
  <connectionStrings>
    <add name="AHSKvant"
         connectionString="Server=AHSKvant-prod-db;Database=YourDatabase;Integrated Security=true;"
         providerName="System.Data.SqlClient" />
  </connectionStrings>
</configuration>
```

Or use appSettings:

```xml
<configuration>
  <appSettings>
    <add key="AHSKvantConnectionString"
         value="Server=AHSKvant-prod-db;Database=YourDatabase;Integrated Security=true;" />
  </appSettings>
</configuration>
```

## Database Schema

**Table:** `Holiday`

| Column | Type | Description |
|--------|------|-------------|
| MARKET | string | Market code (USA, TARGET, SWEDEN, etc.) |
| HOLIDAY_DATE | DateTime | Holiday date |

**Example data:**
```
MARKET    | HOLIDAY_DATE
----------|-------------
USA       | 2025-12-25
USA       | 2026-01-01
TARGET    | 2025-12-25
SWEDEN    | 2025-12-24
```

## Business Day Conventions

### Modified Following
Default convention used for FX expiry dates:
1. If date falls on weekend/holiday, move to **next** business day
2. **UNLESS** that crosses into next month, then use **previous** business day instead

**Example (Dec 31, 2025 issue):**
- Input: Nov 27, 2025 + 1M = Dec 27, 2025 (Saturday)
- Next business day: Dec 29, 2025 (Monday)
- But if Dec 29 or 30 are holidays, and Dec 31 is also a holiday...
- Then: Use **Dec 30** (last business day of December)

### Preceding
Always moves backward to previous business day (not currently default).

## Migration from FxDateService (QLNet)

**Before:**
```csharp
var rules = new FxDateRules
{
    SpotLag = PairSpotLag.TwoBD,
    ExpiryConvention = QLNet.BusinessDayConvention.ModifiedFollowing,
    ExpiryEOM = true
};

var result = FxDateService.ComputeDates(
    DateTime.UtcNow,
    currencyPair,
    tenor,
    premiumCcy: "USD",
    rules
);

return result.expiryDate;
```

**After:**
```csharp
var expiryDate = FxCalendarService.Instance.CalculateExpiry(
    DateTime.UtcNow,
    tenor,
    currencyPair
);

return expiryDate;
```

## Troubleshooting

### Error: "FxCalendarService requires connection string 'AHSKvant' in App.config"
**Solution:** Add connection string to App.config (see Configuration section above)

### Error: "Ingen kalender mappad för XXX"
**Solution:** Currency not supported. Add mapping to `CurrencyToCalendar` dictionary in `CurrencyCalendarMapper.cs`

### Wrong expiry date calculated
**Solution:**
1. Check console logs for `[FX-CALENDAR]` messages showing adjustment logic
2. Verify holidays exist in database for the currency pair's markets
3. Check if Dec 31 is marked as holiday in database:
   ```sql
   SELECT * FROM Holiday
   WHERE HOLIDAY_DATE = '2025-12-31'
   AND MARKET IN ('USA', 'TARGET');
   ```

## Console Logging

The service provides detailed logging for debugging:

```
[FX-CALENDAR] Tenor 1M from 2025-11-27 -> Unadjusted: 2025-12-27 (Sat)
[FX-CALENDAR] Modified Following: 2025-12-27 (weekend/holiday) -> 2025-12-29 crosses month -> 2025-12-30 (final)
```

## Future Enhancements

- [ ] Add support for more currencies (TRY, MXN, ZAR, etc.)
- [ ] Cache holiday data on startup for better performance
- [ ] Support for custom business day conventions per currency pair
- [ ] Add spot lag calculation (T+0, T+1, T+2)
- [ ] Premium settlement date calculation

## License

Internal use - shared across FX trading applications.

## Contributors

- Original calendar infrastructure: [Colleague's name]
- Tenor calculation enhancement: Claude Code (2025-11-27)

---

**Last Updated:** 2025-11-27
