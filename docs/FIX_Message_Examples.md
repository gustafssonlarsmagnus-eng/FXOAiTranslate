# FIX Message Examples - BUY Order Debugging

## Example 1: Call with No Hedge

### Message 1 - Quote Request (35=R)
```
8=FIX.4.4|9=363|35=R|34=18747|49=WEBFENICS1|52=20251114-12:18:50.602|56=GFI|115=SWES|128=DEUT|75=20251114|131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|5475=S|5830=USD|8051=1-VTYXTDNK|9016=0|9126=1|9943=2|146=1|55=EURUSD|6258=1|537=1|555=1|600=EURUSD|6714=1|9125=1|6215=1M|611=20251216|743=20251218|5020=20251118|612=1.1643|9019=2|6351=1|9904=2|556=EUR|687=1.000000|7940=SL|9034=EUR|10=068|
```

**Key Fields:**
- `35=R` → Quote Request
- `55=EURUSD` → Symbol
- `131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371` → QuoteReqID
- `6258=1` → Strategy (1=Vanilla)
- `6351=1` → **Position=1** (Client wants to BUY)
- `9016=0` → Hedge OFF
- `612=1.1643` → Strike
- `611=20251216` → Expiry Date
- `687=1.000000` → Leg Quantity (1MM)

### Message 2 - Quote Response (35=S)
```
8=FIX.4.4|9=353|35=S|34=506864|49=GFI|52=20251114-12:18:53.934|56=WEBFENICS1|115=DEUT|54=2|55=EURUSD|60=20251114-12:18:53.890033|62=20251114-12:23:54|117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2|131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|6289=A|6436=-14960|9126=1|6120=1|7940=SL|5678=6.51|8515=0|5359=1|5235=1.16369|5191=22.185|9073=USD|5844=-149.6|6035=52|6354=1.1643|10=128|
```

**Key Fields:**
- `35=S` → Quote
- `115=DEUT` → LP (Deutsche Bank)
- `54=2` → **Side=2 (OFFER)** - Client can BUY from this quote
- `117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2` → QuoteID (starts with "O_" indicating OFFER)
- `131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371` → QuoteReqID (matches request)
- `62=20251114-12:23:54` → ValidUntilTime (5 minute validity)
- `5678=6.51` → Volatility
- `5844=-149.6` → Leg Premium Price
- `6436=-14960` → Total Premium (USD -149.60)
- `6035=52` → Leg Delta

**Analysis:**
✅ **Position=1 → GFI sent OFFER quote (Side=2)** - This is CORRECT for BUY

### Message 3 - Execution Order (35=AB)
```
8=FIX.4.4|9=392|35=AB|34=18758|49=WEBFENICS1|52=20251114-12:18:55.586|56=GFI|115=SWES|128=DEUT|11=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|40=1|54=1|55=EURUSD|59=3|60=20251114-12:18:55.586000|117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2|131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|5830=USD|6436=-14960|9126=1|453=1|448=swed.ui|447=D|452=11|555=1|600=EURUSD|7940=SL|5678=6.51|8518=10320|5359=1.000000|5844=-149.6|10=112|
```

**Key Fields:**
- `35=AB` → NewOrderMultileg
- `11=FENICS.14899.OV1SJOZFTS0WYNJOGB000371` → ClOrdID
- `117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2` → QuoteID (referencing OFFER quote from Message 2)
- `54=1` → **Execution Side=1** (Opposite of quote Side=2)
- `128=DEUT` → DeliverToCompID (Deutsche Bank)
- `40=1` → OrdType=MARKET
- `6436=-14960` → Total Premium (copied from quote)
- `5678=6.51` → Volatility (copied from quote)
- `5844=-149.6` → Leg Premium Price (copied from quote)

**Analysis:**
✅ Execution references OFFER quote (Side=2)
✅ Execution sends opposite Side=1
✅ All pricing fields copied correctly from quote

---

## Example 2: Call with Hedge ON - **SHOWS THE BUG!**

### Message 1 - Quote Request (35=R)
```
8=FIX.4.4|9=363|35=R|34=18796|49=WEBFENICS1|52=20251114-12:25:10.964|56=GFI|115=SWES|128=DEUT|75=20251114|131=FENICS.14899.VEFMY2BSA75MMGNY5U000372|5475=S|5830=USD|8051=1-JYUORTEW|9016=1|9126=1|9943=2|146=1|55=EURUSD|6258=1|537=1|555=1|600=EURUSD|6714=1|9125=1|6215=1M|611=20251216|743=20251218|5020=20251118|612=1.1643|9019=2|6351=1|9904=2|556=EUR|687=1.000000|7940=SL|9034=EUR|10=000|
```

**Key Fields:**
- `6351=1` → **Position=1** (Client wants to BUY)
- `9016=1` → **Hedge ON** (⚠️ DIFFERENT FROM EXAMPLE 1!)
- Other fields same as Example 1

### Message 2 - Quote Response (35=S)
```
8=FIX.4.4|9=433|35=S|34=508247|49=GFI|52=20251114-12:25:19.793|56=WEBFENICS1|115=DEUT|54=1|55=EURUSD|60=20251114-12:25:19.761089|62=20251114-12:30:15|117=B_FENICS.14899.VEFMY2BSA75MMGNY5U000372-12|131=FENICS.14899.VEFMY2BSA75MMGNY5U000372|6289=A|6436=10473|9126=1|6120=1|7940=SL|5678=6.39|8515=0|5359=1|5235=1.16532|5191=22.215|9073=USD|5844=104.73|6035=56|6354=1.1643|7464=1|9074=EUR|9016=1|6666=2|6036=0.558|9657=1.16532|9112=20251118|6426=EURUSD|10=173|
```

**Key Fields:**
- `54=1` → **Side=1 (BID)** ❌❌❌ WRONG! Should be Side=2 (OFFER) for BUY!
- `117=B_FENICS.14899.VEFMY2BSA75MMGNY5U000372-12` → QuoteID starts with "B_" confirming BID
- `6436=10473` → Total Premium USD +104.73 (POSITIVE - client receives premium)
- `5844=104.73` → Leg Premium Price (POSITIVE)
- `9016=1` → Hedge ON in quote response

**Analysis:**
❌ **Position=1 with Hedge=1 → GFI sent BID quote (Side=1) instead of OFFER quote (Side=2)**
This is WRONG for a BUY request!

### Message 3 - Execution Order (35=AB)
```
8=FIX.4.4|9=392|35=AB|34=18815|49=WEBFENICS1|52=20251114-12:25:20.645|56=GFI|115=SWES|128=DEUT|11=FENICS.14899.VEFMY2BSA75MMGNY5U000372|40=1|54=2|55=EURUSD|59=3|60=20251114-12:25:20.645000|117=B_FENICS.14899.VEFMY2BSA75MMGNY5U000372-12|131=FENICS.14899.VEFMY2BSA75MMGNY5U000372|5830=USD|6436=10473|9126=1|453=1|448=swed.ui|447=D|452=11|555=1|600=EURUSD|7940=SL|5678=6.39|8518=10562|5359=1.000000|5844=104.73|10=108|
```

**Key Fields:**
- `117=B_FENICS.14899.VEFMY2BSA75MMGNY5U000372-12` → QuoteID (BID quote)
- `54=2` → **Execution Side=2** (Opposite of quote Side=1)

**Analysis:**
❌ Execution is trying to BUY using a BID quote (Side=1)!
❌ This will be REJECTED because you can only SELL into a BID quote!

---

## Summary & Root Cause Analysis

### Comparison:

| Aspect | Example 1 (Hedge OFF) | Example 2 (Hedge ON) |
|--------|----------------------|---------------------|
| Position sent | `6351=1` (BUY) | `6351=1` (BUY) |
| Hedge flag | `9016=0` | `9016=1` ⚠️ |
| Quote received | Side=2 (OFFER) ✅ | Side=1 (BID) ❌ |
| Premium | -149.60 (client pays) | +104.73 (client receives) |
| QuoteID prefix | "O_" (Offer) | "B_" (Bid) |
| Execution works? | YES ✅ | NO ❌ |

### ROOT CAUSE IDENTIFIED:

**When Hedge=1 (ON), GFI interprets the Position field REVERSED:**
- Position=1 → GFI sends BID quote (Side=1) instead of OFFER
- Position=2 → GFI sends OFFER quote (Side=2) instead of BID

**Current code assumes:**
```csharp
Position=1 → OFFER quotes (for BUY)  // Only works when Hedge=0
Position=2 → BID quotes (for SELL)    // Only works when Hedge=0
```

**Actual GFI behavior:**
```
When Hedge=0 (OFF):
  Position=1 → OFFER quotes (Side=2) ✅
  Position=2 → BID quotes (Side=1) ✅

When Hedge=1 (ON):
  Position=1 → BID quotes (Side=1) ❌ REVERSED!
  Position=2 → OFFER quotes (Side=2) ❌ REVERSED!
```

### FIX REQUIRED:

```csharp
string positionValue;
if (hedge)
{
    // When hedge is ON, Position field is REVERSED
    positionValue = leg.Direction == "BUY" ? "2" : "1";
}
else
{
    // When hedge is OFF, use normal mapping
    positionValue = leg.Direction == "BUY" ? "1" : "2";
}
```

This explains why:
- ✅ SELL orders work (if tested without hedge)
- ❌ BUY orders fail consistently (if tested with hedge ON)
