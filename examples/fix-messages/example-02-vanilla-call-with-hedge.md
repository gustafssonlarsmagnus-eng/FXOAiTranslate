# Example 2: Vanilla Call - With Hedge

This example demonstrates a complete FIX message flow for a EURUSD 1M vanilla call option **with hedge** (delta hedging).

## Message Flow

1. **Quote Request (35=R)** - Client requests quote from DEUT with hedge
2. **Quote Response (35=S)** - DEUT responds with **bid** quote including hedge details
3. **New Order Multileg (35=AB)** - Client executes (**sells** the call, hitting the bid)

---

## 1. Quote Request (35=R)

### Raw Message
```
8=FIX.4.4|9=363|35=R|34=18796|49=WEBFENICS1|52=20251114-12:25:10.964|56=GFI|115=SWES|128=DEUT|75=20251114|131=FENICS.14899.VEFMY2BSA75MMGNY5U000372|5475=S|5830=USD|8051=1-JYUORTEW|9016=1|9126=1|9943=2|146=1|55=EURUSD|6258=1|537=1|555=1|600=EURUSD|6714=1|9125=1|6215=1M|611=20251216|743=20251218|5020=20251118|612=1.1643|9019=2|6351=1|9904=2|556=EUR|687=1.000000|7940=SL|9034=EUR|10=000|
```

### Decoded Fields

#### Header
- `8=FIX.4.4` - BeginString (FIX version 4.4)
- `9=363` - BodyLength
- `35=R` - MsgType (Quote Request)
- `34=18796` - MsgSeqNum
- `49=WEBFENICS1` - SenderCompID
- `52=20251114-12:25:10.964` - SendingTime
- `56=GFI` - TargetCompID
- `115=SWES` - OnBehalfOfCompID (Trader/Desk identifier) ✓ **CRITICAL**
- `128=DEUT` - DeliverToCompID (Route to Deutsche Bank)

#### Body - Trade Details
- `75=20251114` - TradeDate
- `131=FENICS.14899.VEFMY2BSA75MMGNY5U000372` - QuoteReqID (Unique request ID)
- `5475=S` - PremDel
- `5830=USD` - PremiumCcy (Premium in USD)
- `8051=1-JYUORTEW` - GroupID
- `9016=1` - HedgeTradeType (1 = With Hedge/Delta Hedge) ✓ **KEY DIFFERENCE**
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
- `10=000` - CheckSum

---

## 2. Quote Response - Bid (35=S)

### Raw Message
```
8=FIX.4.4|9=433|35=S|34=508247|49=GFI|52=20251114-12:25:19.793|56=WEBFENICS1|115=DEUT|54=1|55=EURUSD|60=20251114-12:25:19.761089|62=20251114-12:30:15|117=B_FENICS.14899.VEFMY2BSA75MMGNY5U000372-12|131=FENICS.14899.VEFMY2BSA75MMGNY5U000372|6289=A|6436=10473|9126=1|6120=1|7940=SL|5678=6.39|8515=0|5359=1|5235=1.16532|5191=22.215|9073=USD|5844=104.73|6035=56|6354=1.1643|7464=1|9074=EUR|9016=1|6666=2|6036=0.558|9657=1.16532|9112=20251118|6426=EURUSD|10=173|
```

### Decoded Fields

#### Header
- `8=FIX.4.4` - BeginString
- `9=433` - BodyLength (longer due to hedge fields)
- `35=S` - MsgType (Quote)
- `34=508247` - MsgSeqNum
- `49=GFI` - SenderCompID
- `52=20251114-12:25:19.793` - SendingTime
- `56=WEBFENICS1` - TargetCompID
- `115=DEUT` - OnBehalfOfCompID (LP identifier) ✓ **CRITICAL**

#### Quote Details
- `54=1` - Side (1 = Bid/Buy - LP is buying from client, client sells) ✓ **KEY DIFFERENCE**
- `55=EURUSD` - Symbol
- `60=20251114-12:25:19.761089` - TransactTime
- `62=20251114-12:30:15` - ValidUntilTime (Quote expires in ~5 minutes) ✓
- `117=B_FENICS.14899.VEFMY2BSA75MMGNY5U000372-12` - QuoteID (Note: B_ prefix for BID)
- `131=FENICS.14899.VEFMY2BSA75MMGNY5U000372` - QuoteReqID (Matches request)
- `6289=A` - QuoteResponseLevel
- `6436=10473` - PremiumAmount (in cents: +$104.73 - CLIENT RECEIVES) ✓
- `9126=1` - Structure (Vanilla)

#### Leg Pricing (NoMQEntries)
- `6120=1` - NoMQEntries (Number of pricing legs)
- `7940=SL` - LegStrategyID ✓
- `5678=6.39` - Volatility (6.39%) ✓ **CRITICAL**
- `8515=0` - Reserved
- `5359=1` - MQSize (1 million notional) ✓ **CRITICAL**
- `5235=1.16532` - LegSpotRate
- `5191=22.215` - LegForwardPoints
- `9073=USD` - DepoRateCcy
- `5844=104.73` - LegPremPrice (Premium in USD) ✓ **CRITICAL**
- `6035=56` - LegDelta (56%)
- `6354=1.1643` - MQStrikePrice

#### Hedge Details ✓ **NEW FIELDS**
- `7464=1` - Hedge indicator/flag
- `9074=EUR` - Hedge currency (EUR - the base currency)
- `9016=1` - HedgeTradeType confirmation (1 = With Hedge)
- `6666=2` - Hedge execution method/type
- `6036=0.558` - Hedge notional or rate (558,000 EUR)
- `9657=1.16532` - Hedge spot rate
- `9112=20251118` - Hedge settlement/delivery date
- `6426=EURUSD` - Hedge symbol

#### Trailer
- `10=173` - CheckSum

### Pricing Summary
- **Volatility**: 6.39%
- **Premium**: +$104.73 (client receives premium for selling the call)
- **Spot**: 1.16532
- **Strike**: 1.1643
- **Delta**: 56%
- **Hedge Notional**: ~558,000 EUR (0.558 million)
- **Hedge Rate**: 1.16532

---

## 3. New Order Multileg (35=AB)

### Raw Message
```
8=FIX.4.4|9=392|35=AB|34=18815|49=WEBFENICS1|52=20251114-12:25:20.645|56=GFI|115=SWES|128=DEUT|11=FENICS.14899.VEFMY2BSA75MMGNY5U000372|40=1|54=2|55=EURUSD|59=3|60=20251114-12:25:20.645000|117=B_FENICS.14899.VEFMY2BSA75MMGNY5U000372-12|131=FENICS.14899.VEFMY2BSA75MMGNY5U000372|5830=USD|6436=10473|9126=1|453=1|448=swed.ui|447=D|452=11|555=1|600=EURUSD|7940=SL|5678=6.39|8518=10562|5359=1.000000|5844=104.73|10=108|
```

### Decoded Fields

#### Header
- `8=FIX.4.4` - BeginString
- `9=392` - BodyLength
- `35=AB` - MsgType (NewOrderMultileg)
- `34=18815` - MsgSeqNum
- `49=WEBFENICS1` - SenderCompID
- `52=20251114-12:25:20.645` - SendingTime
- `56=GFI` - TargetCompID
- `115=SWES` - OnBehalfOfCompID (Trader/Desk) ✓ **CRITICAL**
- `128=DEUT` - DeliverToCompID (Route to DEUT) ✓ **CRITICAL**

#### Order Details
- `11=FENICS.14899.VEFMY2BSA75MMGNY5U000372` - ClOrdID (Client Order ID)
- `40=1` - OrdType (1 = Market, though should be D for PREVIOUSLY_QUOTED)
- `54=2` - Side (2 = Sell - Client is selling/hitting the bid) ✓ **KEY DIFFERENCE**
- `55=EURUSD` - Symbol
- `59=3` - TimeInForce (3 = IOC - Immediate or Cancel)
- `60=20251114-12:25:20.645000` - TransactTime
- `117=B_FENICS.14899.VEFMY2BSA75MMGNY5U000372-12` - QuoteID (References bid quote)
- `131=FENICS.14899.VEFMY2BSA75MMGNY5U000372` - QuoteReqID
- `5830=USD` - PremiumCcy
- `6436=10473` - PremiumAmount (+$104.73 received)
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
- `5678=6.39` - Volatility (From quote) ✓ **CRITICAL**
- `8518=10562` - Reserved/internal field
- `5359=1.000000` - MQSize ✓ **CRITICAL**
- `5844=104.73` - LegPremPrice (From quote) ✓ **CRITICAL**

#### Trailer
- `10=108` - CheckSum

### Execution Summary
- **Action**: SELL (Client hits bid, sells call option)
- **Quote Referenced**: B_FENICS.14899.VEFMY2BSA75MMGNY5U000372-12 (BID quote)
- **Premium Received**: $104.73
- **LP**: Deutsche Bank (DEUT)
- **Routing**: OnBehalfOf=SWES, DeliverTo=DEUT
- **Hedge**: Yes (LP will delta hedge the position)

---

## Comparison with Example 1 (No Hedge)

| Aspect | Example 1 (No Hedge) | Example 2 (With Hedge) |
|--------|---------------------|------------------------|
| **HedgeTradeType (9016)** | 0 (No Hedge) | 1 (With Hedge) |
| **Quote Side (54)** | 2 (Offer/Sell) | 1 (Bid/Buy) |
| **Order Side (54)** | 1 (Buy - Lift Offer) | 2 (Sell - Hit Bid) |
| **Premium Flow** | Client pays $149.60 | Client receives $104.73 |
| **Premium Sign** | Negative (-149.6) | Positive (+104.73) |
| **Quote Prefix** | O_ (Offer) | B_ (Bid) |
| **Volatility** | 6.51% | 6.39% |
| **Delta** | 52% | 56% |
| **Spot** | 1.16369 | 1.16532 |
| **Hedge Fields** | None | Tags 7464, 9074, 6666, 6036, 9657, 9112, 6426 |

---

## Key Takeaways

### 1. Hedge Trade Flow
When **HedgeTradeType (9016) = 1**:
- LP provides delta hedge along with the option quote
- Additional hedge fields appear in the quote response
- LP will execute a spot FX hedge to offset the option delta
- Hedge details include: currency, notional, rate, settlement date

### 2. Bid vs Offer Mechanics
**Bid (LP buys, Client sells):**
- Quote Side (54) = 1
- Order Side (54) = 2
- Premium is positive (client receives money)
- Quote ID prefix: `B_`

**Offer (LP sells, Client buys):**
- Quote Side (54) = 2
- Order Side (54) = 1
- Premium is negative (client pays money)
- Quote ID prefix: `O_`

### 3. Delta Hedging Details
- **Option Delta**: 56% (tag 6035)
- **Hedge Notional**: 558,000 EUR (tag 6036 = 0.558)
- **Hedge Calculation**: 1,000,000 EUR × 56% ≈ 560,000 EUR
- **Hedge Currency**: EUR (tag 9074) - the base currency
- **Hedge Settlement**: 20251118 (tag 9112)

### 4. Critical Fields (Same as No Hedge)
The same critical fields are required for execution:
- OnBehalfOfCompID (115) - In all three messages
- DeliverToCompID (128) - For routing
- LegStrategyID (7940) - Leg identification
- Volatility (5678) OR LegPremPrice (5844) - Pricing
- MQSize (5359) - Quote size
- LegQty (687) - Should be in order execution

### 5. Message Timing
- Quote Request sent: 12:25:10.964
- Quote Response received: 12:25:19.793 (8.8 seconds later)
- Order Execution sent: 12:25:20.645 (0.85 seconds after quote)
- Quote valid until: 12:30:15 (~5 minutes from quote time)
- **Total execution time**: 10 seconds from request to order

### 6. Premium Sign Convention
- **Negative premium** = Client pays (buying options, buying protection)
- **Positive premium** = Client receives (selling options, earning income)

---

## Hedge Fields Reference

| Tag | Field Name | Value | Description |
|-----|------------|-------|-------------|
| 9016 | HedgeTradeType | 1 | 1 = With Hedge, 0 = No Hedge |
| 7464 | Hedge Indicator | 1 | Hedge present flag |
| 9074 | Hedge Currency | EUR | Currency being hedged |
| 6666 | Hedge Execution Type | 2 | Method of hedge execution |
| 6036 | Hedge Notional | 0.558 | Hedge amount in millions |
| 9657 | Hedge Spot Rate | 1.16532 | FX rate for hedge |
| 9112 | Hedge Settlement Date | 20251118 | Delivery date for hedge |
| 6426 | Hedge Symbol | EURUSD | Symbol for hedge transaction |

---

## Use Cases

**When to use HedgeTradeType = 1:**
- Client wants immediate delta hedge execution
- Client lacks spot FX execution capability
- All-in pricing including hedge costs
- Simplified execution (single RFQ instead of two trades)

**When to use HedgeTradeType = 0:**
- Client manages own delta hedging
- Client has specific hedge timing requirements
- Client wants to separate option and hedge pricing
- Option-only views (no hedge costs embedded)
