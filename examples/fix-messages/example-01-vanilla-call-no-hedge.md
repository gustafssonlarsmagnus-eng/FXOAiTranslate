# Example 1: Vanilla CALL with No Hedge

**Status**: ✅ WORKS CORRECTLY

**Configuration**:
- Option Type: CALL
- Pricing: Strike-based (1.1643)
- Hedge: OFF (9016=0)
- Action: BUY

---

## Message 1 - Quote Request (35=R)

```
8=FIX.4.4|9=363|35=R|34=18747|49=WEBFENICS1|52=20251114-12:18:50.602|56=GFI|115=SWES|128=DEUT|75=20251114|131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|5475=S|5830=USD|8051=1-VTYXTDNK|9016=0|9126=1|9943=2|146=1|55=EURUSD|6258=1|537=1|555=1|600=EURUSD|6714=1|9125=1|6215=1M|611=20251216|743=20251218|5020=20251118|612=1.1643|9019=2|6351=1|9904=2|556=EUR|687=1.000000|7940=SL|9034=EUR|10=068|
```

### Key Fields:
- `35=R` → Quote Request
- `55=EURUSD` → Symbol
- `131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371` → QuoteReqID
- `6258=1` → Strategy (1=Vanilla)
- `6351=1` → **Position=1** (Client wants to BUY)
- `9016=0` → **Hedge OFF**
- `612=1.1643` → Strike
- `611=20251216` → Expiry Date
- `687=1.000000` → Leg Quantity (1MM)
- `6714=1` → Call option

---

## Message 2 - Quote Response (35=S)

```
8=FIX.4.4|9=353|35=S|34=506864|49=GFI|52=20251114-12:18:53.934|56=WEBFENICS1|115=DEUT|54=2|55=EURUSD|60=20251114-12:18:53.890033|62=20251114-12:23:54|117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2|131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|6289=A|6436=-14960|9126=1|6120=1|7940=SL|5678=6.51|8515=0|5359=1|5235=1.16369|5191=22.185|9073=USD|5844=-149.6|6035=52|6354=1.1643|10=128|
```

### Key Fields:
- `35=S` → Quote
- `115=DEUT` → LP (Deutsche Bank)
- `54=2` → **Side=2 (OFFER)** ✅ Client can BUY from this quote
- `117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2` → QuoteID (starts with "O_" = OFFER)
- `131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371` → QuoteReqID (matches request)
- `62=20251114-12:23:54` → ValidUntilTime (5 minute validity)
- `5678=6.51` → Volatility (6.51%)
- `5844=-149.6` → Leg Premium Price (NEGATIVE - client pays)
- `6436=-14960` → Total Premium (USD -149.60, client pays)
- `6035=52` → Leg Delta (52%)

---

## Message 3 - Execution Order (35=AB)

```
8=FIX.4.4|9=392|35=AB|34=18758|49=WEBFENICS1|52=20251114-12:18:55.586|56=GFI|115=SWES|128=DEUT|11=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|40=1|54=1|55=EURUSD|59=3|60=20251114-12:18:55.586000|117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2|131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|5830=USD|6436=-14960|9126=1|453=1|448=swed.ui|447=D|452=11|555=1|600=EURUSD|7940=SL|5678=6.51|8518=10320|5359=1.000000|5844=-149.6|10=112|
```

### Key Fields:
- `35=AB` → NewOrderMultileg
- `11=FENICS.14899.OV1SJOZFTS0WYNJOGB000371` → ClOrdID
- `117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2` → QuoteID (OFFER quote from Message 2)
- `54=1` → **Execution Side=1** (Opposite of quote Side=2) ✅
- `128=DEUT` → DeliverToCompID (Deutsche Bank)
- `40=1` → OrdType=MARKET
- `6436=-14960` → Total Premium (copied from quote)
- `5678=6.51` → Volatility (copied from quote)
- `5844=-149.6` → Leg Premium Price (copied from quote)

---

## Analysis

✅ **This example shows CORRECT behavior when Hedge=OFF:**

1. **Quote Request**: Position=1 sent for BUY
2. **Quote Response**: GFI sent OFFER quote (Side=2) - correct for BUY
3. **Execution**: Used OFFER quote with opposite Side=1 - correct

**Premium Flow**: Client pays -149.60 USD (negative premium, typical for buying an ATM call)

**Result**: ✅ FILLED successfully
