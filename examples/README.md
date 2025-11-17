# FIX 4.4 Message Examples - GFI Fenics Integration

This directory contains real FIX 4.4 message examples that were instrumental in identifying and fixing a critical bug in BUY order execution when Hedge=ON.

## Quick Navigation

- **[Example 1](fix-messages/example-01-vanilla-call-no-hedge.md)** - Vanilla CALL, No Hedge ✅ Works
- **[Example 2](fix-messages/example-02-vanilla-call-with-hedge.md)** - Vanilla CALL, Hedge ON ❌ Bug Revealed
- **[Example 3](fix-messages/example-03-vanilla-put-with-delta-and-hedge.md)** - PUT with Delta, Hedge ON ❌ Bug Confirmed

---

## Summary Table

| Example | Type | Pricing | Hedge | Position | Quote Received | Expected | Result |
|---------|------|---------|-------|----------|----------------|----------|--------|
| 1 | CALL | Strike | OFF | 1 (BUY) | OFFER (Side=2) ✅ | OFFER | ✅ FILLED |
| 2 | CALL | Strike | ON | 1 (BUY) | BID (Side=1) ❌ | OFFER | ❌ REJECTED |
| 3 | PUT | Delta | ON | 1 (BUY) | BID (Side=1) ❌ | OFFER | ❌ REJECTED |

---

## Root Cause: Position Field Reversal with Hedge=ON

### The Problem

**GFI reverses the Position field (tag 6351) interpretation when Hedge=ON (tag 9016=1):**

| Hedge Setting | Position=1 Sends | Position=2 Sends |
|--------------|------------------|------------------|
| **OFF (9016=0)** | OFFER quotes (Side=2) | BID quotes (Side=1) |
| **ON (9016=1)** | BID quotes (Side=1) ⚠️ | OFFER quotes (Side=2) ⚠️ |

### Why This Caused Failures

When requesting to **BUY** an option with **Hedge=ON**:
1. Code sent `Position=1` (expecting OFFER quotes)
2. GFI interpreted this as request for BID quotes (reversed!)
3. Client received BID quote (Side=1)
4. Client tried to execute BUY against BID quote → **REJECTED**
5. Error: "quote is no longer available"

**Market Convention:**
- **BID quotes (Side=1)**: Client can SELL into these
- **OFFER quotes (Side=2)**: Client can BUY from these

---

## The Fix

### Code Location
`/FXOptionsSimulator/FIX/RawFIXMessageBuilder.cs` (lines 188-199)

### Before Fix
```csharp
// WRONG: Did not account for hedge flag
string positionValue = leg.Direction == "BUY" ? "1" : "2";
```

### After Fix
```csharp
// CORRECT: Position field is hedge-aware
string positionValue;
if (hedge)
{
    // Hedge ON: Position field is REVERSED by GFI
    positionValue = leg.Direction == "BUY" ? "2" : "1";
}
else
{
    // Hedge OFF: Normal mapping
    positionValue = leg.Direction == "BUY" ? "1" : "2";
}
```

### Result Matrix

| User Action | Hedge | Position Sent | Quote Received | Execution |
|-------------|-------|---------------|----------------|-----------|
| BUY | OFF | 1 | OFFER (Side=2) | ✅ Works |
| BUY | ON | 2 | OFFER (Side=2) | ✅ Works |
| SELL | OFF | 2 | BID (Side=1) | ✅ Works |
| SELL | ON | 1 | BID (Side=1) | ✅ Works |

---

## Evidence Analysis

### Example 2 vs Example 1 (Same trade, different hedge flag)

**Identical Parameters:**
- Symbol: EURUSD
- Option: CALL
- Strike: 1.1643
- Expiry: 2025-12-16
- Notional: 1MM EUR
- Action: BUY
- LP: Deutsche Bank

**Only Difference:**
```diff
- Example 1: 9016=0 (Hedge OFF)  → Got OFFER quote ✅
+ Example 2: 9016=1 (Hedge ON)   → Got BID quote ❌
```

**Premium Direction Reveals the Error:**
- Example 1: Premium = -149.60 (client pays) ✅ Correct for buying
- Example 2: Premium = +104.73 (client receives) ❌ Wrong for buying

### Universal Pattern (Example 3 Confirms)

Example 3 tested with:
- **Different option type**: PUT (not CALL)
- **Different pricing method**: Delta-based (not Strike)
- **Hedge=ON**: Same reversal occurred

**Conclusion**: Reversal is purely `Hedge flag + Position value` dependent, universal across all option characteristics.

---

## FIX Tag Reference

### Core Message Tags

| Tag | Name | Description | Example Values |
|-----|------|-------------|----------------|
| 8 | BeginString | FIX version | FIX.4.4 |
| 9 | BodyLength | Message length | 363 |
| 35 | MsgType | Message type | R (Quote Request), S (Quote), AB (New Order) |
| 49 | SenderCompID | Sender ID | WEBFENICS1 |
| 56 | TargetCompID | Target ID | GFI |
| 115 | OnBehalfOfCompID | LP identifier | DEUT, BNP, etc. |
| 128 | DeliverToCompID | Route to specific LP | DEUT |

### Quote Request Tags (35=R)

| Tag | Name | Description | Example | Notes |
|-----|------|-------------|---------|-------|
| 75 | TradeDate | Trade date | 20251114 | Format: YYYYMMDD |
| 131 | QuoteReqID | Unique request ID | FENICS.14899.xxx | Tracks quote lifecycle |
| 55 | Symbol | Currency pair | EURUSD | |
| 146 | NoRelatedSym | Number of symbols | 1 | Usually 1 for single symbol |
| 537 | QuoteType | Quote type | 1 | 1=Indicative |
| 555 | NoLegs | Number of legs | 1 | 1 for vanilla, >1 for strategies |
| 5475 | PremDel | Premium delivery | S | S=Separate settlement |
| 5830 | PremiumCcy | Premium currency | USD | |
| 6258 | Strategy | Structure code | 1 (Vanilla), 2 (Delta) | |
| **9016** | **HedgeTradeType** | **Hedge flag** | **0 (OFF), 1 (ON)** | **CRITICAL for Position interpretation!** |
| 9126 | Structure | Structure type | 1, 2 | |
| 9943 | ProductQuoteType | Product type | 2 | |

### Leg-Specific Tags

| Tag | Name | Description | Example | Notes |
|-----|------|-------------|---------|-------|
| 600 | LegSymbol | Leg symbol | EURUSD | |
| 611 | LegMaturityDate | Expiry date | 20251216 | Format: YYYYMMDD |
| 612 | LegStrikePrice | Strike price | 1.1643 | For strike-based pricing |
| 687 | LegQty | Notional amount | 1.000000 | In millions |
| 556 | LegCurrency | Leg currency | EUR | |
| **6035** | **LegDelta** | **Target/Actual delta** | **50, -51** | **Negative for PUTs** |
| **6351** | **Position** | **Client position** | **1, 2** | **REVERSES with Hedge=ON!** |
| 6714 | OptionType | Call or Put | 1 (CALL), 2 (PUT) | |
| 7940 | LegStrategyID | Strategy ID | SL | |
| 9019 | FXOptionStyle | Option style | 2 | European |
| 9034 | LegStrategyCcy | Strategy currency | EUR | Base currency |
| 9125 | NotionalType | Notional type | 1 | |
| 9904 | PriceIndicator | Price indicator | 2 | |

### Quote Response Tags (35=S)

| Tag | Name | Description | Example | Notes |
|-----|------|-------------|---------|-------|
| **54** | **Side** | **Quote side** | **1 (BID), 2 (OFFER)** | **Determines if client can BUY or SELL** |
| 117 | QuoteID | Unique quote ID | B_xxx, O_xxx | Prefix indicates BID/OFFER |
| 60 | TransactTime | Quote timestamp | 20251114-12:18:53.890033 | |
| 62 | ValidUntilTime | Quote expiry | 20251114-12:23:54 | Typically 5 min validity |
| 5235 | LegSpotRate | Spot rate | 1.16369 | Current market spot |
| 5359 | MQSize | Quote size | 1 | Multiplier |
| **5678** | **Volatility** | **Implied volatility** | **6.51** | **In percentage** |
| **5844** | **LegPremPrice** | **Leg premium** | **-149.6, 104.73** | **Negative=pay, Positive=receive** |
| **6035** | **LegDelta** | **Actual delta** | **52, -51** | **From LP's pricing** |
| 6289 | QuoteStatus | Quote status | A (Active) | |
| **6436** | **TotalPremium** | **Total premium** | **-14960, 10473** | **In smallest currency unit (cents)** |
| 6666 | HedgeInfo | Hedge details | 1, 2 | Present when Hedge=ON |

### Execution Tags (35=AB)

| Tag | Name | Description | Example | Notes |
|-----|------|-------------|---------|-------|
| 11 | ClOrdID | Client order ID | FENICS.14899.xxx | Unique order identifier |
| 40 | OrdType | Order type | 1 (MARKET) | Per GFI spec |
| **54** | **Side** | **Execution side** | **1, 2** | **OPPOSITE of quote Side** |
| 59 | TimeInForce | TIF | 3 (Immediate or Cancel) | |
| 117 | QuoteID | Referenced quote | From quote response | Must match received quote |
| 131 | QuoteReqID | Original request | FENICS.14899.xxx | Tracks to original request |
| 448 | PartyID | User identifier | swed.ui | |
| 452 | PartyRole | Party role | 11 | |

---

## Premium Sign Convention

### Understanding Premium Direction

| Premium Sign | Meaning | Client Action | Example |
|--------------|---------|---------------|---------|
| **Negative** | Client pays | Buying option premium | -149.60 USD |
| **Positive** | Client receives | Selling option premium | +104.73 USD |

### In Our Examples:

**Example 1 (Correct OFFER quote for BUY):**
- LegPremPrice: `-149.6`
- TotalPremium: `-14960` (in cents)
- Client wants to BUY → Should pay → Negative ✅

**Example 2 (Wrong BID quote for BUY):**
- LegPremPrice: `104.73`
- TotalPremium: `10473` (in cents)
- Client wants to BUY → Should pay → But quote shows receive ❌
- **This mismatch revealed the bug!**

---

## Quote Side Interpretation

### FIX Standard (Tag 54)

| Side Value | Name | LP's Perspective | Client Can | QuoteID Prefix |
|------------|------|------------------|------------|----------------|
| **1** | BID | LP wants to BUY | **SELL** into this | B_ |
| **2** | OFFER (ASK) | LP wants to SELL | **BUY** from this | O_ |

### Market Convention

```
BID ←─── Client can SELL ───→ LP BUYS
 ↑                               ↑
Side=1                       LP pays premium

OFFER ←─── Client can BUY ───→ LP SELLS
  ↑                               ↑
Side=2                      Client pays premium
```

---

## Execution Side Logic

### Critical Rule
**Execution Side (tag 54) must be OPPOSITE of Quote Side**

| Quote Side | Quote Type | Execution Side | Why |
|------------|------------|----------------|-----|
| 1 (BID) | Client sells into LP's bid | 2 | Client is on offer side of trade |
| 2 (OFFER) | Client buys from LP's offer | 1 | Client is on bid side of trade |

### In Code
```csharp
// RawFIXMessageBuilder.cs line 217-218
string quoteSide = quote.Get("54");
string executionSide = quoteSide == "1" ? "2" : "1";  // Always opposite
```

---

## Quote Lifecycle

### Typical Flow

```
1. QUOTE REQUEST (35=R)
   ├─ Position=1 or 2 (hedge-dependent)
   ├─ Hedge=0 or 1
   └─ Trade details (symbol, strike/delta, expiry, etc.)

2. QUOTE RESPONSE (35=S)
   ├─ Side=1 (BID) or Side=2 (OFFER)
   ├─ QuoteID (B_xxx or O_xxx)
   ├─ Volatility, Premium, Delta
   └─ ValidUntilTime (typically 5 minutes)

3. (Optional) QUOTE CANCEL (35=Z)
   └─ LP cancels and may send replacement

4. EXECUTION ORDER (35=AB)
   ├─ QuoteID from step 2
   ├─ Side = OPPOSITE of quote Side
   └─ Premium/volatility from quote

5. EXECUTION REPORT (35=8)
   ├─ OrdStatus=2 (FILLED) ✅
   └─ OrdStatus=8 (REJECTED) ❌
```

### Quote Expiry

- **ValidUntilTime (tag 62)**: Typical 5-minute window
- **Quote Cancel (35=Z)**: LP can cancel anytime
- **Market movement**: Quotes updated/canceled as market moves

---

## Testing Checklist

After implementing the fix, test all combinations:

### Basic Scenarios
- [ ] BUY CALL, Hedge OFF, Strike-based
- [ ] BUY CALL, Hedge ON, Strike-based
- [ ] SELL CALL, Hedge OFF, Strike-based
- [ ] SELL CALL, Hedge ON, Strike-based
- [ ] BUY PUT, Hedge OFF, Strike-based
- [ ] BUY PUT, Hedge ON, Strike-based
- [ ] SELL PUT, Hedge OFF, Strike-based
- [ ] SELL PUT, Hedge ON, Strike-based

### Delta-Based Pricing
- [ ] BUY CALL, Hedge ON, Delta 25
- [ ] BUY CALL, Hedge ON, Delta 50
- [ ] BUY PUT, Hedge ON, Delta 25
- [ ] BUY PUT, Hedge ON, Delta 50

### Multi-Leg Strategies
- [ ] Risk Reversal, Hedge ON
- [ ] Straddle, Hedge ON
- [ ] Strangle, Hedge ON

---

## Debug Output

The fix includes enhanced debug summaries for easy troubleshooting:

```
========== QUOTE REQUEST DEBUG ==========
Building 1-leg structure for EURUSD:
Hedge (9016): 1 (ON)
  Leg 1: BUY 1MM CALL @ 1.1643
         → Position(6351)=2 → Expecting OFFER (Side=2)
=========================================
```

```
========== QUOTE RECEIVED SUMMARY ==========
LP: DEUT
QuoteReqID: FENICS.14899.xxx
QuoteID: O_FENICS.14899.xxx-2
Type: OFFER (FIX Side=2)
Client Action: Can BUY from this quote
Legs: 1
  Leg 1: Vol=6.51 Premium=-149.6
Current State for DEUT:
  - BidQuote: NULL
  - OfferQuote: AVAILABLE (QuoteID=O_xxx)
============================================
```

---

## Related Documentation

- [GFI Fenics FIX 4.4 Specification](../docs/gfi-fix-spec.pdf) (if available)
- [FIX 4.4 Protocol Documentation](https://www.fixtrading.org/standards/fix-4-4/)
- [Code Implementation](../FXOptionsSimulator/FIX/RawFIXMessageBuilder.cs)

---

## Contact & Support

For questions about this integration:
1. Check the example messages in `fix-messages/`
2. Review the debug output format above
3. Verify Position field logic for your hedge setting

**Key Takeaway**: Always check the Hedge flag (9016) when debugging Position field (6351) issues!
