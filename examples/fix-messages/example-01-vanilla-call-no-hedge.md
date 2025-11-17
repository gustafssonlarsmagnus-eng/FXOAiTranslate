# Example 1: Vanilla Call - No Hedge

This example demonstrates a complete FIX message flow for a EURUSD 1M vanilla call option with no hedge.

## Message Flow

1. **Quote Request (35=R)** - Client requests quote from DEUT
2. **Quote Response (35=S)** - DEUT responds with offer quote
3. **New Order Multileg (35=AB)** - Client executes (buys) the offer

---

## 1. Quote Request (35=R)

### Raw Message
```
8=FIX.4.4|9=363|35=R|34=18747|49=WEBFENICS1|52=20251114-12:18:50.602|56=GFI|115=SWES|128=DEUT|75=20251114|131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|5475=S|5830=USD|8051=1-VTYXTDNK|9016=0|9126=1|9943=2|146=1|55=EURUSD|6258=1|537=1|555=1|600=EURUSD|6714=1|9125=1|6215=1M|611=20251216|743=20251218|5020=20251118|612=1.1643|9019=2|6351=1|9904=2|556=EUR|687=1.000000|7940=SL|9034=EUR|10=068|
```

### Decoded Fields

#### Header
- `8=FIX.4.4` - BeginString (FIX version 4.4)
- `9=363` - BodyLength
- `35=R` - MsgType (Quote Request)
- `34=18747` - MsgSeqNum
- `49=WEBFENICS1` - SenderCompID
- `52=20251114-12:18:50.602` - SendingTime
- `56=GFI` - TargetCompID
- `115=SWES` - OnBehalfOfCompID (Trader/Desk identifier) ✓ **CRITICAL**
- `128=DEUT` - DeliverToCompID (Route to Deutsche Bank)

#### Body - Trade Details
- `75=20251114` - TradeDate
- `131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371` - QuoteReqID (Unique request ID)
- `5475=S` - PremDel
- `5830=USD` - PremiumCcy (Premium in USD)
- `8051=1-VTYXTDNK` - GroupID
- `9016=0` - HedgeTradeType (0 = No Hedge) ✓
- `9126=1` - Structure (1 = Vanilla)
- `9943=2` - ProductQuoteType
- `146=1` - NoRelatedSym (Number of symbols)

#### Symbol Group
- `55=EURUSD` - Symbol
- `6258=1` - Strategy
- `537=1` - QuoteType
- `555=1` - NoLegs (Single leg)

#### Leg Details
- `600=EURUSD` - LegSymbol
- `6714=1` - LegStrategy (1 = Call)
- `9125=1` - Cutoff
- `6215=1M` - Tenor (1 Month)
- `611=20251216` - LegMaturityDate (Expiry)
- `743=20251218` - DeliveryDate (Settlement)
- `5020=20251118` - PremiumDelivery
- `612=1.1643` - LegStrikePrice
- `9019=2` - FXOptionStyle (2 = European)
- `6351=1` - Position
- `9904=2` - PriceIndicator
- `556=EUR` - LegCurrency
- `687=1.000000` - LegQty (1 million EUR notional) ✓ **CRITICAL**
- `7940=SL` - LegStrategyID
- `9034=EUR` - LegStrategyCcy

#### Trailer
- `10=068` - CheckSum

---

## 2. Quote Response - Offer (35=S)

### Raw Message
```
8=FIX.4.4|9=353|35=S|34=506864|49=GFI|52=20251114-12:18:53.934|56=WEBFENICS1|115=DEUT|54=2|55=EURUSD|60=20251114-12:18:53.890033|62=20251114-12:23:54|117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2|131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|6289=A|6436=-14960|9126=1|6120=1|7940=SL|5678=6.51|8515=0|5359=1|5235=1.16369|5191=22.185|9073=USD|5844=-149.6|6035=52|6354=1.1643|10=128|
```

### Decoded Fields

#### Header
- `8=FIX.4.4` - BeginString
- `9=353` - BodyLength
- `35=S` - MsgType (Quote)
- `34=506864` - MsgSeqNum
- `49=GFI` - SenderCompID
- `52=20251114-12:18:53.934` - SendingTime
- `56=WEBFENICS1` - TargetCompID
- `115=DEUT` - OnBehalfOfCompID (LP identifier) ✓ **CRITICAL**

#### Quote Details
- `54=2` - Side (2 = Offer/Sell - LP is selling to client, client buys)
- `55=EURUSD` - Symbol
- `60=20251114-12:18:53.890033` - TransactTime
- `62=20251114-12:23:54` - ValidUntilTime (Quote expires in 5 minutes) ✓
- `117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2` - QuoteID
- `131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371` - QuoteReqID (Matches request)
- `6289=A` - QuoteResponseLevel
- `6436=-14960` - PremiumAmount (in cents: -$149.60)
- `9126=1` - Structure (Vanilla)

#### Leg Pricing (NoMQEntries)
- `6120=1` - NoMQEntries (Number of pricing legs)
- `7940=SL` - LegStrategyID ✓
- `5678=6.51` - Volatility (6.51%) ✓ **CRITICAL**
- `8515=0` - Reserved
- `5359=1` - MQSize (1 million notional) ✓ **CRITICAL**
- `5235=1.16369` - LegSpotRate
- `5191=22.185` - LegForwardPoints
- `9073=USD` - DepoRateCcy
- `5844=-149.6` - LegPremPrice (Premium in USD) ✓ **CRITICAL**
- `6035=52` - LegDelta (52%)
- `6354=1.1643` - MQStrikePrice

#### Trailer
- `10=128` - CheckSum

### Pricing Summary
- **Volatility**: 6.51%
- **Premium**: -$149.60 (client pays $149.60 to buy the call)
- **Spot**: 1.16369
- **Strike**: 1.1643
- **Delta**: 52%

---

## 3. New Order Multileg (35=AB)

### Raw Message
```
8=FIX.4.4|9=392|35=AB|34=18758|49=WEBFENICS1|52=20251114-12:18:55.586|56=GFI|115=SWES|128=DEUT|11=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|40=1|54=1|55=EURUSD|59=3|60=20251114-12:18:55.586000|117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2|131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371|5830=USD|6436=-14960|9126=1|453=1|448=swed.ui|447=D|452=11|555=1|600=EURUSD|7940=SL|5678=6.51|8518=10320|5359=1.000000|5844=-149.6|10=112|
```

### Decoded Fields

#### Header
- `8=FIX.4.4` - BeginString
- `9=392` - BodyLength
- `35=AB` - MsgType (NewOrderMultileg)
- `34=18758` - MsgSeqNum
- `49=WEBFENICS1` - SenderCompID
- `52=20251114-12:18:55.586` - SendingTime
- `56=GFI` - TargetCompID
- `115=SWES` - OnBehalfOfCompID (Trader/Desk) ✓ **CRITICAL**
- `128=DEUT` - DeliverToCompID (Route to DEUT) ✓ **CRITICAL**

#### Order Details
- `11=FENICS.14899.OV1SJOZFTS0WYNJOGB000371` - ClOrdID (Client Order ID)
- `40=1` - OrdType (1 = Market, though should be D for PREVIOUSLY_QUOTED)
- `54=1` - Side (1 = Buy - Client is buying/lifting the offer) ✓
- `55=EURUSD` - Symbol
- `59=3` - TimeInForce (3 = IOC - Immediate or Cancel)
- `60=20251114-12:18:55.586000` - TransactTime
- `117=O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2` - QuoteID (References quote)
- `131=FENICS.14899.OV1SJOZFTS0WYNJOGB000371` - QuoteReqID
- `5830=USD` - PremiumCcy
- `6436=-14960` - PremiumAmount
- `9126=1` - Structure

#### PartyIDs Group
- `453=1` - NoPartyIDs
- `448=swed.ui` - PartyID (Trader identifier)
- `447=D` - PartyIDSource (D = Proprietary)
- `452=11` - PartyRole (11 = Order Origination Trader)

#### Legs (NoLegs)
- `555=1` - NoLegs (Single leg)
- `600=EURUSD` - LegSymbol
- `7940=SL` - LegStrategyID ✓ **CRITICAL**
- `5678=6.51` - Volatility (From quote) ✓ **CRITICAL**
- `8518=10320` - Reserved
- `5359=1.000000` - MQSize ✓ **CRITICAL**
- `5844=-149.6` - LegPremPrice (From quote) ✓ **CRITICAL**

#### Trailer
- `10=112` - CheckSum

### Execution Summary
- **Action**: BUY (Client lifts offer)
- **Quote Referenced**: O_FENICS.14899.OV1SJOZFTS0WYNJOGB000371-2
- **Premium Paid**: $149.60
- **LP**: Deutsche Bank (DEUT)
- **Routing**: OnBehalfOf=SWES, DeliverTo=DEUT

---

## Key Takeaways

### Critical Fields for Successful Execution

1. **OnBehalfOfCompID (115)** - Must be present in:
   - Quote Request (identifies trader/desk)
   - Quote Response (identifies LP)
   - NewOrderMultileg (identifies trader and routes to LP via tag 128)

2. **LegQty (687)** - Required in:
   - Quote Request (specifies notional)
   - NewOrderMultileg would benefit from this field

3. **Leg Pricing Fields** - Quote must contain:
   - LegStrategyID (7940)
   - Volatility (5678) OR LegPremPrice (5844)
   - MQSize (5359)

4. **Routing** - NewOrderMultileg must have:
   - Tag 115 (OnBehalfOfCompID) = Trader identifier
   - Tag 128 (DeliverToCompID) = LP from quote (tag 115)

### Message Timing
- Quote Request sent: 12:18:50.602
- Quote Response received: 12:18:53.934 (3.3 seconds later)
- Order Execution sent: 12:18:55.586 (1.6 seconds after quote)
- Quote valid until: 12:23:54 (5 minutes from quote time)
- **Total execution time**: 5 seconds from request to order

### Trade Economics
- **Spot Rate**: 1.16369
- **Strike**: 1.1643 (slightly out-of-the-money)
- **Volatility**: 6.51%
- **Delta**: 52% (near at-the-money)
- **Premium**: $149.60 for 1M EUR notional
- **Premium %**: 0.01496% of notional
