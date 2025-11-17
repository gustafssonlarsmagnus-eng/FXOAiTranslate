# Example 2: Vanilla CALL with Hedge ON

**Status**: ❌ FAILED (Before Fix) / ✅ WORKS (After Fix)

**Configuration**:
- Option Type: CALL
- Pricing: Strike-based (1.1643)
- Hedge: ON (9016=1) ⚠️
- Action: BUY

**This example revealed the bug!**

---

## Message 1 - Quote Request (35=R)

```
8=FIX.4.4|9=363|35=R|34=18796|49=WEBFENICS1|52=20251114-12:25:10.964|56=GFI|115=SWES|128=DEUT|75=20251114|131=FENICS.14899.VEFMY2BSA75MMGNY5U000372|5475=S|5830=USD|8051=1-JYUORTEW|9016=1|9126=1|9943=2|146=1|55=EURUSD|6258=1|537=1|555=1|600=EURUSD|6714=1|9125=1|6215=1M|611=20251216|743=20251218|5020=20251118|612=1.1643|9019=2|6351=1|9904=2|556=EUR|687=1.000000|7940=SL|9034=EUR|10=000|
```

### Key Fields:
- `6351=1` → **Position=1** (Client wants to BUY)
- `9016=1` → **Hedge ON** ⚠️ (DIFFERENT FROM EXAMPLE 1!)
- Other fields same as Example 1

### Difference from Example 1:
**ONLY** the Hedge flag changed: `9016=0` → `9016=1`

---

## Message 2 - Quote Response (35=S)

```
8=FIX.4.4|9=433|35=S|34=508247|49=GFI|52=20251114-12:25:19.793|56=WEBFENICS1|115=DEUT|54=1|55=EURUSD|60=20251114-12:25:19.761089|62=20251114-12:30:15|117=B_FENICS.14899.VEFMY2BSA75MMGNY5U000372-12|131=FENICS.14899.VEFMY2BSA75MMGNY5U000372|6289=A|6436=10473|9126=1|6120=1|7940=SL|5678=6.39|8515=0|5359=1|5235=1.16532|5191=22.215|9073=USD|5844=104.73|6035=56|6354=1.1643|7464=1|9074=EUR|9016=1|6666=2|6036=0.558|9657=1.16532|9112=20251118|6426=EURUSD|10=173|
```

### Key Fields:
- `54=1` → **Side=1 (BID)** ❌❌❌ WRONG! Should be Side=2 (OFFER) for BUY!
- `117=B_FENICS.14899.VEFMY2BSA75MMGNY5U000372-12` → QuoteID starts with "B_" (BID)
- `6436=10473` → Total Premium USD +104.73 (POSITIVE - client receives premium!)
- `5844=104.73` → Leg Premium Price (POSITIVE)
- `9016=1` → Hedge ON in quote response
- `6666=2` → Hedge details included

### Analysis of Quote:
❌ **Position=1 with Hedge=1 → GFI sent BID quote (Side=1) instead of OFFER quote (Side=2)**

**Compare to Example 1:**
- Example 1 (Hedge OFF): Position=1 → OFFER (Side=2) ✅
- Example 2 (Hedge ON): Position=1 → BID (Side=1) ❌

**Premium Direction Reveals the Error:**
- BID quote shows POSITIVE premium (+104.73) = client receives money
- But client wants to BUY a call = should PAY premium
- This confirms we received the WRONG side!

---

## Message 3 - Execution Order (35=AB)

```
8=FIX.4.4|9=392|35=AB|34=18815|49=WEBFENICS1|52=20251114-12:25:20.645|56=GFI|115=SWES|128=DEUT|11=FENICS.14899.VEFMY2BSA75MMGNY5U000372|40=1|54=2|55=EURUSD|59=3|60=20251114-12:25:20.645000|117=B_FENICS.14899.VEFMY2BSA75MMGNY5U000372-12|131=FENICS.14899.VEFMY2BSA75MMGNY5U000372|5830=USD|6436=10473|9126=1|453=1|448=swed.ui|447=D|452=11|555=1|600=EURUSD|7940=SL|5678=6.39|8518=10562|5359=1.000000|5844=104.73|10=108|
```

### Key Fields:
- `117=B_FENICS.14899.VEFMY2BSA75MMGNY5U000372-12` → QuoteID (BID quote)
- `54=2` → **Execution Side=2** (Opposite of quote Side=1)

### Why This Failed:
❌ **Execution is trying to BUY using a BID quote (Side=1)!**
- BID quotes are for SELLING into
- You can only BUY from OFFER quotes
- GFI correctly rejects this with: "quote is no longer available"

---

## Root Cause

**When Hedge=1 (ON), GFI REVERSES the Position field interpretation:**

| Hedge | Position=1 Sends | Position=2 Sends |
|-------|------------------|------------------|
| OFF (0) | OFFER (Side=2) ✅ | BID (Side=1) ✅ |
| ON (1) | BID (Side=1) ❌ | OFFER (Side=2) ❌ |

---

## The Fix

**Before Fix:**
```csharp
string positionValue = leg.Direction == "BUY" ? "1" : "2";
// Always sent Position=1 for BUY, regardless of hedge flag
```

**After Fix:**
```csharp
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

**Now sends:**
- BUY + Hedge OFF → Position=1 → Gets OFFER quotes ✅
- BUY + Hedge ON → Position=2 → Gets OFFER quotes ✅
- SELL + Hedge OFF → Position=2 → Gets BID quotes ✅
- SELL + Hedge ON → Position=1 → Gets BID quotes ✅
