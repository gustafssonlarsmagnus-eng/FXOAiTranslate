# GFI FIX Message Samples

This document contains real GFI FIX message examples for reference during development and debugging.

## Message Flow

Each trade follows this sequence:
1. **Quote Request (35=R)** - Client → GFI
2. **Quote Response (35=S)** - GFI → Client
3. **Execution (35=AB)** - Client → GFI
4. **Execution Report (35=8)** - GFI → Client

---

## Example 1: Vanilla Call - No Hedge - BUY

### Quote Request (35=R)
**Client → GFI**

```
8=FIX.4.4|9=363|35=R|34=18747|49=WEBFENICS1|52=20251114-12:18:50.602|56=GFI|115=SWES|128=DEUT|75=20251114|131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|5475=S|5830=USD|8051=1-VTYXTDNK|9016=0|9126=1|9943=2|146=1|55=EURUSD|6258=1|537=1|555=1|600=EURUSD|6714=1|9125=1|6215=1M|611=20251216|743=20251218|5020=20251118|612=1.1643|9019=2|6351=1|9904=2|556=EUR|687=1.000000|7940=SL|9034=EUR|10=068|
```

**Key Fields:**
- `9016=0` - HedgeTradeType = 0 (No Hedge)
- `9126=1` - Structure = 1 (Vanilla)
- `6714=1` - LegStrategy = 1 (CALL)
- `6351=1` - Position = 1 (BUY)
- `612=1.1643` - Strike
- `6215=1M` - Tenor
- `687=1.000000` - Notional (1MM)
- **No 5235 (LegSpotRate)** - Not sent when Hedge=OFF

### Quote Response (35=S)
**GFI → Client**

```
8=FIX.4.4|9=353|35=S|34=506864|49=GFI|52=20251114-12:18:53.934|56=WEBFENICS1|115=DEUT|54=2|55=EURUSD|60=20251114-12:18:53.890033|62=20251114-12:23:54|117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2|131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|6289=A|6436=-14960|9126=1|6120=1|7940=SL|5678=6.51|8515=0|5359=1|5235=1.16369|5191=22.185|9073=USD|5844=-149.6|6035=52|6354=1.1643|10=128|
```

**Key Fields:**
- `54=2` - Side = 2 (OFFER) - LP is offering to sell (client buys)
- `117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2` - QuoteID (note "-2" suffix)
- `6436=-14960` - Premium = -14960 USD (negative = client pays)
- `5678=6.51` - Volatility = 6.51%
- `5359=1` - MQSize = 1MM
- `5844=-149.6` - LegPremPrice = -149.6 USD per MM
- `5235=1.16369` - LegSpotRate (GFI provides even though Hedge=OFF)
- `62=20251114-12:23:54` - ValidUntilTime (quote expires ~5 minutes after creation)

### Execution (35=AB)
**Client → GFI**

```
8=FIX.4.4|9=392|35=AB|34=18758|49=WEBFENICS1|52=20251114-12:18:55.586|56=GFI|115=SWES|128=DEUT|11=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|40=1|54=1|55=EURUSD|59=3|60=20251114-12:18:55.586000|117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2|131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|5830=USD|6436=-14960|9126=1|453=1|448=swed.ui|447=D|452=11|555=1|600=EURUSD|7940=SL|5678=6.51|8518=10320|5359=1.000000|5844=-149.6|10=112|
```

**Key Fields:**
- `40=1` - OrdType = 1 (MARKET)
- `54=1` - Side = 1 (opposite of quote Side=2)
- `59=3` - TimeInForce = 3 (IMMEDIATE_OR_CANCEL)
- `117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2` - QuoteID (exact match from quote)
- `5830=USD` - PremiumCcy
- `6436=-14960` - Premium (exact match from quote)
- `453=1|448=swed.ui|447=D|452=11` - PartyIDs group
- Leg pricing fields: `7940=SL|5678=6.51|5359=1.000000|5844=-149.6` (exact from quote)

**Field Order in Execution:**
1. ClOrdID (11)
2. OrdType (40)
3. Side (54)
4. Symbol (55)
5. TimeInForce (59)
6. TransactTime (60)
7. QuoteID (117)
8. QuoteReqID (131)
9. PremiumCcy (5830)
10. Premium (6436)
11. Structure (9126)
12. **PartyIDs group (453, 448, 447, 452)**
13. NoLegs (555)
14. Leg repeating group

---

## Field Reference

### Common Fields

| Tag | Field Name | Values | Description |
|-----|------------|--------|-------------|
| 35 | MsgType | R=Quote Request, S=Quote, AB=NewOrderMultileg, 8=ExecutionReport | Message type |
| 54 | Side | 1=BUY, 2=SELL | From LP perspective in quote |
| 40 | OrdType | 1=MARKET, D=PREVIOUSLY_QUOTED | Order type for execution |
| 59 | TimeInForce | 3=IMMEDIATE_OR_CANCEL | |
| 115 | OnBehalfOfCompID | SWES, etc. | Trading firm |
| 128 | DeliverToCompID | DEUT, BNP, etc. | LP name |

### Option-Specific Fields

| Tag | Field Name | Values | Description |
|-----|------------|--------|-------------|
| 6351 | Position | 1=BUY, 2=SELL | Client position |
| 6714 | LegStrategy | 1=CALL, 2=PUT | Option type |
| 9016 | HedgeTradeType | 0=No Hedge, 1=Hedge | |
| 9126 | Structure | 1=Vanilla, 5=RR, 8=CallSpread, 9=PutSpread, 10=Seagull | |
| 5235 | LegSpotRate | decimal | Spot reference (only with Hedge=ON in request) |
| 5678 | Volatility | decimal | Vol % |
| 5844 | LegPremPrice | decimal | Premium per MM (negative=client pays) |
| 6436 | Premium | integer | Total premium in premium currency |

### Quote ID Patterns

Quote IDs from GFI follow this pattern:
- `O_<QuoteReqID>-<number>` for OFFER quotes (client buys)
- `B_<QuoteReqID>-<number>` for BID quotes (client sells)

The suffix number may indicate which quote entry in a multi-quote response.

---

## Notes

1. **Premium Sign Convention:**
   - Negative premium = Client pays (buying protection)
   - Positive premium = Client receives (selling protection)

2. **Side Field Logic:**
   - Quote Side from LP perspective
   - Execution Side is OPPOSITE of Quote Side
   - To BUY option: Quote shows Side=2 (OFFER), Execution sends Side=1
   - To SELL option: Quote shows Side=1 (BID), Execution sends Side=2

3. **Hedge Behavior:**
   - Hedge=OFF (9016=0): Do NOT send LegSpotRate (5235) in quote request
   - Hedge=ON (9016=1): Must send LegSpotRate (5235) in quote request
   - GFI may return spot rate in quote regardless of hedge setting

4. **Quote Expiry:**
   - Field 62 (ValidUntilTime) shows when quote expires
   - Typically 5 minutes from creation
   - Must execute before expiry or will be rejected

