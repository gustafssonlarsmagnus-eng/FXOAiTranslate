# FX Options Date Calculation Logic

**Last Updated:** 2025-12-02
**Purpose:** Documentation of date calculation conventions used in FXO AI Translator

---

## Table of Contents

1. [Overview](#overview)
2. [Key Terminology](#key-terminology)
3. [Calculation Flow](#calculation-flow)
4. [Business Day Rules](#business-day-rules)
5. [Examples](#examples)
6. [Implementation Details](#implementation-details)

---

## Overview

FX options date calculations follow standard FX market conventions to determine:
- **Spot Date:** When an FX spot transaction would settle
- **Delivery Date:** When the option's underlying FX transaction settles
- **Expiry Date:** When the option expires (typically 2 business days before delivery)

The calculations must account for:
- Spot lag (T+1 or T+2 depending on currency pair)
- Weekends and holidays
- Special rules for January 1st
- Modified Following business day convention

---

## Key Terminology

### Trade Date
The date when the option trade is executed (today).

### Spot Date
The standard settlement date for an FX spot transaction:
- **T+2** for most currency pairs (EURUSD, EURSEK, GBPUSD, etc.)
- **T+1** for exceptions: USD/CAD, USD/TRY, USD/PHP, USD/RUB

Where "T" is the trade date and "+2" means 2 **business days** forward.

### Tenor
The time period from spot date to delivery:
- **D** = Days (e.g., "5D" = 5 days)
- **W** = Weeks (e.g., "2W" = 2 weeks)
- **M** = Months (e.g., "1M" = 1 month)
- **Y** = Years (e.g., "1Y" = 1 year)

### Delivery Date (Settlement Date)
The date when the underlying FX spot transaction settles:
```
Delivery Date = Spot Date + Tenor
```

Adjusted for weekends and holidays using Modified Following convention.

### Expiry Date
The date when the option expires (last day to exercise):
```
Expiry Date = Delivery Date - Spot Lag
```

For T+2 pairs, expiry is 2 business days before delivery.

---

## Calculation Flow

### Step-by-Step Process

#### 1. Calculate Spot Date
```
Spot Date = Trade Date + Spot Lag (in business days)
```

**Example (EURUSD, T+2):**
- Trade Date: Monday, Dec 2, 2025
- +1 BD: Tuesday, Dec 3, 2025
- +2 BD: Wednesday, Dec 4, 2025
- **Spot Date = Dec 4, 2025**

#### 2. Calculate Unadjusted Delivery Date
```
Unadjusted Delivery = Spot Date + Tenor (calendar days/months/years)
```

**Example (1M tenor):**
- Spot Date: Dec 4, 2025
- Add 1 month: Jan 4, 2026
- **Unadjusted Delivery = Jan 4, 2026**

#### 3. Adjust Delivery Date for Business Days

**FX Market Convention:**
> "For a trade with a time to expiry of v days, the [delivery] date is the day v days ahead of the horizon date (unless it is a **weekend or 1 January**, in which case the date is **rolled forward** to a weekday)"

If the unadjusted delivery falls on:
- **Weekend (Sat/Sun):** Roll forward to next business day
- **January 1st:** Roll forward to next business day
- **Other holiday:** Roll forward to next business day

**Modified Following Convention:**
- Roll forward to next business day
- UNLESS that crosses a month boundary, then use previous business day instead

**Example (Jan 4 = Sunday):**
- Unadjusted: Jan 4, 2026 (Sunday)
- Next business day: Jan 5, 2026 (Monday)
- Does not cross month boundary → use Jan 5
- **Adjusted Delivery = Jan 5, 2026**

#### 4. Calculate Expiry Date
```
Expiry Date = Delivery Date - Spot Lag (in business days)
```

Count backwards, skipping weekends and holidays (including Jan 1).

**Example (from Jan 5, go back 2 BD):**
- Start: Jan 5, 2026 (Monday)
- -1 BD: Jan 2, 2026 (Friday) [skip Jan 3-4 weekend]
- -2 BD: Skip Jan 1 (Thursday, holiday) → Dec 31, 2025 (Wednesday)
- **Expiry Date = Dec 31, 2025**

---

## Business Day Rules

### Weekend Rule
**Saturday and Sunday are NEVER business days.**

### January 1st Rule (Global Holiday)
**January 1st is ALWAYS a non-business day, regardless of which calendar is used.**

This is hardcoded in the logic because New Year's Day is a global holiday.

### Other Holidays
Checked against currency-specific holiday calendars:
- **EURUSD:** TARGET (EUR) + USA (USD) calendars
- **EURSEK:** TARGET (EUR) + SWEDEN (SEK) calendars
- **USDNOK:** USA (USD) + NORWAY (NOK) calendars

### Business Day Check Logic
```
IsBusinessDay(date):
    1. If Saturday or Sunday → false
    2. If January 1st → false
    3. If in holiday calendar for either currency → false
    4. Otherwise → true
```

---

## Examples

### Example 1: 1M EURUSD (Dec 2, 2025)

**Given:**
- Trade Date: Monday, Dec 2, 2025
- Tenor: 1M
- Currency Pair: EURUSD (T+2)

**Calculation:**

1. **Spot Date:** Trade + 2 BD
   - Dec 2 (Mon) + 2 BD = Dec 4, 2025 (Wed)

2. **Unadjusted Delivery:** Spot + 1M
   - Dec 4, 2025 + 1 month = Jan 4, 2026

3. **Day of Week Check:** Jan 4, 2026 = Sunday
   - Weekend! Roll forward to Jan 5, 2026 (Monday)
   - **Delivery Date = Jan 5, 2026**

4. **Expiry Date:** Delivery - 2 BD
   - Jan 5 (Mon) → Jan 2 (Fri) [−1 BD, skip weekend]
   - Jan 2 (Fri) → Dec 31 (Wed) [−1 BD, skip Jan 1 holiday]
   - **Expiry Date = Dec 31, 2025**

**Result:**
- **Expiry:** Dec 31, 2025 (Wednesday)
- **Settlement:** Jan 5, 2026 (Monday)
- **OVML Date:** `12/31/25`
- **Display:** `31-Dec-25, Wed (1M)`

---

### Example 2: 3M EURUSD (Dec 2, 2025)

**Given:**
- Trade Date: Monday, Dec 2, 2025
- Tenor: 3M
- Currency Pair: EURUSD (T+2)

**Calculation:**

1. **Spot Date:** Dec 4, 2025 (Wed)

2. **Unadjusted Delivery:** Dec 4 + 3M = Apr 4, 2026 (Saturday)

3. **Adjusted Delivery:** Apr 4 (Sat) → Apr 6, 2026 (Mon)

4. **Expiry Date:** Apr 6 - 2 BD = Apr 2, 2026 (Thu)

**Result:**
- **Expiry:** Apr 2, 2026 (Thursday)
- **Settlement:** Apr 6, 2026 (Monday)

---

### Example 3: 1M USDCAD (Dec 2, 2025)

**Given:**
- Trade Date: Monday, Dec 2, 2025
- Tenor: 1M
- Currency Pair: USDCAD (T+1)

**Calculation:**

1. **Spot Date:** Trade + 1 BD = Dec 3, 2025 (Tue)

2. **Unadjusted Delivery:** Dec 3 + 1M = Jan 3, 2026 (Sat)

3. **Adjusted Delivery:** Jan 3 (Sat) → Jan 5, 2026 (Mon)

4. **Expiry Date:** Jan 5 - 1 BD = Jan 2, 2026 (Fri)

**Result:**
- **Expiry:** Jan 2, 2026 (Friday)
- **Settlement:** Jan 5, 2026 (Monday)

---

## Implementation Details

### Code Location

#### Main Calculation
**File:** `FXOptionsSimulator/FxCalendar/CurrencyCalendarMapper.cs`

**Method:** `CalculateExpiryFromTenor()`

```csharp
public static DateTime CalculateExpiryFromTenor(
    string ccyPair,
    DateTime tradeDate,
    string tenor,
    HolidayCalendar holidayCal,
    bool useModifiedFollowing = true)
{
    // 1. Calculate Spot Date
    int spotLag = GetSpotLag(ccyPair);
    DateTime spotDate = AddBusinessDays(ccyPair, tradeDate.Date, spotLag, holidayCal);

    // 2. Calculate Delivery Date
    DateTime deliveryUnadjusted = ParseTenorToDate(spotDate, tenor);
    DateTime deliveryDate = AdjustBusinessDay(ccyPair, deliveryUnadjusted, holidayCal, useModifiedFollowing);

    // 3. Calculate Expiry Date
    DateTime expiryDate = SubtractBusinessDays(ccyPair, deliveryDate, spotLag, holidayCal);

    return expiryDate;
}
```

#### Business Day Check
**File:** `FXOptionsSimulator/FxCalendar/CurrencyCalendarMapper.cs`

**Method:** `IsBusinessDay()`

```csharp
public static bool IsBusinessDay(string ccyPair, DateTime date, HolidayCalendar holidayCal)
{
    var d = date.Date;

    // Weekends are never business days
    if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday)
        return false;

    // January 1st is ALWAYS a holiday (New Year's Day) regardless of calendar
    if (d.Month == 1 && d.Day == 1)
        return false;

    // Check calendar for other holidays
    var calendars = GetCalendarsForPair(ccyPair);
    var dt = holidayCal.GetHolidays(calendars, d, d);
    return dt == null || dt.Rows.Count == 0;
}
```

### Spot Lag Configuration

**T+2 Pairs (Default):**
- EURUSD, GBPUSD, EURGBP, EURSEK, EURNOK, USDJPY, etc.

**T+1 Pairs (Exceptions):**
```csharp
private static readonly Dictionary<string, int> SpotLagOverrides = new()
{
    { "USDCAD", 1 },
    { "USDTRY", 1 },
    { "USDRUB", 1 },
    { "USDPHP", 1 }
};
```

### Calendar Mappings

```csharp
private static readonly Dictionary<string, string> CurrencyToCalendar = new()
{
    { "EUR", "TARGET" },
    { "USD", "USA" },
    { "SEK", "SWEDEN" },
    { "NOK", "NORWAY" },
    { "GBP", "ENGLAND" },
    { "CAD", "CANADA" },
    { "CHF", "SWITZERLAND" },
    { "AUD", "AUSTRALIA" },
    { "JPY", "JAPAN" },
    // ... etc.
};
```

---

## Key Insights

### Why January 1st is Special

January 1st is **hardcoded** as a non-business day because:

1. **Global Holiday:** New Year's Day is recognized worldwide
2. **Market Convention:** FX markets are closed globally on Jan 1
3. **Database Independence:** Works even if holiday calendar is unavailable
4. **Consistency:** Ensures uniform treatment across all currency pairs

### Why Delivery Rolls Forward (Not Backward)

From FX market conventions:
> "unless it is a weekend or 1 January, in which case the date is **rolled forward** to a weekday"

This means:
- ✅ Delivery on weekend/Jan 1 → next business day
- ❌ NOT previous business day

The expiry date then naturally falls 2 BD before the adjusted delivery.

### Modified Following vs Following

**Following:** Always roll forward to next business day

**Modified Following:** Roll forward, BUT:
- If that crosses a month boundary → use previous business day instead
- Prevents delivery dates from "jumping" to the next month unexpectedly

**Example:**
- Unadjusted: Jan 31, 2026 (Saturday)
- Following would give: Feb 2, 2026 (Monday)
- Modified Following gives: Jan 30, 2026 (Friday) [stays in January]

---

## Testing Scenarios

### Scenario 1: Delivery on Weekend
- **Unadjusted:** Saturday or Sunday
- **Expected:** Roll forward to Monday (or Tuesday if Monday is holiday)

### Scenario 2: Delivery on January 1st
- **Unadjusted:** January 1st (any year)
- **Expected:** Roll forward to Jan 2 (or later if Jan 2 is weekend)

### Scenario 3: Expiry Crosses January 1st
- **Delivery:** Jan 5, 2026 (Monday)
- **Expiry Calculation:** Jan 5 - 2 BD
  - Skip Jan 4 (Sun), Jan 3 (Sat) → Jan 2 (Fri) [−1 BD]
  - Skip Jan 1 (Thu, holiday) → Dec 31 (Wed) [−1 BD]
- **Expected Expiry:** Dec 31, 2025

### Scenario 4: Month-End with Modified Following
- **Spot:** Jan 29, 2026 (Thursday)
- **Tenor:** 1M
- **Unadjusted Delivery:** Feb 28, 2026 (Saturday)
- **Adjusted:** Mar 2, 2026 (Monday) - crosses month boundary
- **Modified Following:** Feb 27, 2026 (Friday) - stays in February
- **Expiry:** Feb 25, 2026 (Wednesday)

---

## References

- **FX Date Conventions:** [Wikipedia - Foreign Exchange Date Conventions](https://en.wikipedia.org/wiki/Foreign_exchange_date_conventions)
- **Market Practice:** Standard FX options market conventions
- **Implementation:** QLNet, ObjectLab Kit for reference implementations

---

## Revision History

| Date | Version | Changes |
|------|---------|---------|
| 2025-12-02 | 1.0 | Initial documentation of FX options date logic |

---

**End of Document**
