# Example 3: Vanilla Put - With Delta and Hedge

This example demonstrates a complete FIX message flow for a EURUSD 1M vanilla **put** option with delta targeting and delta hedging.

## Message Flow

1. **Quote Request (35=R)** - Client requests quote from DEUT for PUT with delta=50% and hedge
2. **Quote Response (35=S)** - DEUT responds with **bid** quote (delta=-51%) including hedge details
3. **New Order Multileg (35=AB)** - Client executes (**sells** the put, hitting bid)

---

## 1. Quote Request (35=R)

### Raw Message
```
8=FIX.4.4|9=360|35=R|34=18981|49=WEBFENICS1|52=20251114-12:47:33.280|56=GFI|115=SWES|128=DEUT|75=20251114|131=FENICS.14899.0NDHUVT0DMCSNCKW0A000382|5475=S|5830=USD|8051=9-MQFBFNRH|9016=1|9126=2|9943=2|146=1|55=EURUSD|6258=2|537=1|555=1|600=EURUSD|6714=2|9125=1|6215=1M|611=20251216|743=20251218|5020=20251118|6035=50|9019=2|6351=1|9904=2|556=EUR|687=1.000000|7940=SL|9034=EUR|10=052|
```

### Decoded Fields

#### Header
- `8=FIX.4.4` - BeginString (FIX version 4.4)
- `9=360` - BodyLength
- `35=R` - MsgType (Quote Request)
- `34=18981` - MsgSeqNum
- `49=WEBFENICS1` - SenderCompID
- `52=20251114-12:47:33.280` - SendingTime
- `56=GFI` - TargetCompID
- `115=SWES` - OnBehalfOfCompID (Trader/Desk identifier) ✓ **CRITICAL**
- `128=DEUT` - DeliverToCompID (Route to Deutsche Bank)

#### Body - Trade Details
- `75=20251114` - TradeDate
- `131=FENICS.14899.0NDHUVT0DMCSNCKW0A000382` - QuoteReqID (Unique request ID)
- `5475=S` - PremDel
- `5830=USD` - PremiumCcy (Premium in USD)
- `8051=9-MQFBFNRH` - GroupID
- `9016=1` - HedgeTradeType (1 = With Hedge/Delta Hedge) ✓
- `9126=2` - Structure (2 = Put) ✓ **KEY DIFFERENCE FROM CALLS**
- `9943=2` - ProductQuoteType
- `146=1` - NoRelatedSym (Number of symbols)

#### Symbol Group
- `55=EURUSD` - Symbol
- `6258=2` - Strategy (2 = Put)
- `537=1` - QuoteType
- `555=1` - NoLegs (Single leg)

#### Leg Details
- `600=EURUSD` - LegSymbol
- `6714=2` - LegStrategy (2 = Put) ✓ **PUT OPTION**
- `9125=1` - Cutoff
- `6215=1M` - Tenor (1 Month)
- `611=20251216` - LegMaturityDate (Expiry)
- `743=20251218` - DeliveryDate (Settlement)
- `5020=20251118` - PremiumDelivery
- `6035=50` - LegDelta (Target delta: 50%) ✓ **DELTA TARGETING**
- `9019=2` - FXOptionStyle (2 = European)
- `6351=1` - Position
- `9904=2` - PriceIndicator
- `556=EUR` - LegCurrency
- `687=1.000000` - LegQty (1 million EUR notional) ✓ **CRITICAL**
- `7940=SL` - LegStrategyID
- `9034=EUR` - LegStrategyCcy

#### Trailer
- `10=052` - CheckSum

### Key Features
- **Put Option**: Structure=2, LegStrategy=2
- **Delta Targeting**: Tag 6035=50 in request (client wants 50% delta)
- **With Hedge**: HedgeTradeType=1

---

## 2. Quote Response - Bid (35=S)

### Raw Message
```
8=FIX.4.4|9=432|35=S|34=512799|49=GFI|52=20251114-12:47:46.572|56=WEBFENICS1|115=DEUT|54=1|55=EURUSD|60=20251114-12:47:45.093135|62=20251114-12:52:37|117=B_FENICS.14899.0NDHUVT0DMCSNCKW0A000382-24|131=FENICS.14899.0NDHUVT0DMCSNCKW0A000382|6289=A|6436=9064|9126=2|6120=1|7940=SL|5678=6.49|8515=0|5359=1|5235=1.16497|5191=22.205|9073=USD|5844=90.64|6035=-51|6354=1.1675|7464=1|9074=EUR|9016=1|6666=1|6036=0.513|9657=1.16497|9112=20251118|6426=EURUSD|10=134|
```

### Decoded Fields

#### Header
- `8=FIX.4.4` - BeginString
- `9=432` - BodyLength
- `35=S` - MsgType (Quote)
- `34=512799` - MsgSeqNum
- `49=GFI` - SenderCompID
- `52=20251114-12:47:46.572` - SendingTime
- `56=WEBFENICS1` - TargetCompID
- `115=DEUT` - OnBehalfOfCompID (LP identifier) ✓ **CRITICAL**

#### Quote Details
- `54=1` - Side (1 = Bid/Buy - LP is buying from client, client sells) ✓
- `55=EURUSD` - Symbol
- `60=20251114-12:47:45.093135` - TransactTime
- `62=20251114-12:52:37` - ValidUntilTime (Quote expires in ~5 minutes) ✓
- `117=B_FENICS.14899.0NDHUVT0DMCSNCKW0A000382-24` - QuoteID (B_ prefix for BID)
- `131=FENICS.14899.0NDHUVT0DMCSNCKW0A000382` - QuoteReqID (Matches request)
- `6289=A` - QuoteResponseLevel
- `6436=9064` - PremiumAmount (in cents: +$90.64 - CLIENT RECEIVES) ✓
- `9126=2` - Structure (2 = Put) ✓

#### Leg Pricing (NoMQEntries)
- `6120=1` - NoMQEntries (Number of pricing legs)
- `7940=SL` - LegStrategyID ✓
- `5678=6.49` - Volatility (6.49%) ✓ **CRITICAL**
- `8515=0` - Reserved
- `5359=1` - MQSize (1 million notional) ✓ **CRITICAL**
- `5235=1.16497` - LegSpotRate
- `5191=22.205` - LegForwardPoints
- `9073=USD` - DepoRateCcy
- `5844=90.64` - LegPremPrice (Premium in USD) ✓ **CRITICAL**
- `6035=-51` - LegDelta (-51%) ✓ **NEGATIVE DELTA FOR PUT**
- `6354=1.1675` - MQStrikePrice

#### Hedge Details ✓
- `7464=1` - Hedge indicator/flag
- `9074=EUR` - Hedge currency (EUR - the base currency)
- `9016=1` - HedgeTradeType confirmation (1 = With Hedge)
- `6666=1` - Hedge execution method/type
- `6036=0.513` - Hedge notional (513,000 EUR)
- `9657=1.16497` - Hedge spot rate
- `9112=20251118` - Hedge settlement/delivery date
- `6426=EURUSD` - Hedge symbol

#### Trailer
- `10=134` - CheckSum

### Pricing Summary
- **Option Type**: Put
- **Volatility**: 6.49%
- **Premium**: +$90.64 (client receives premium for selling the put)
- **Spot**: 1.16497
- **Strike**: 1.1675 (out-of-the-money put)
- **Delta**: -51% (negative for puts) ✓ **CLOSE TO REQUESTED 50%**
- **Hedge Notional**: ~513,000 EUR (0.513 million)
- **Hedge Rate**: 1.16497

### Put Delta Mechanics
- **Requested Delta**: 50% (absolute value)
- **Quoted Delta**: -51% (negative sign indicates put)
- **Delta Sign**: Puts always have negative delta (when long put, lose money as spot rises)
- **Hedge Direction**: With delta -51%, LP needs to sell EUR (short EUR) to hedge

---

## 3. New Order Multileg (35=AB)

### Raw Message
```
8=FIX.4.4|9=389|35=AB|34=19019|49=WEBFENICS1|52=20251114-12:47:48.412|56=GFI|115=SWES|128=DEUT|11=FENICS.14899.0NDHUVT0DMCSNCKW0A000382|40=1|54=2|55=EURUSD|59=3|60=20251114-12:47:48.412001|117=B_FENICS.14899.0NDHUVT0DMCSNCKW0A000382-24|131=FENICS.14899.0NDHUVT0DMCSNCKW0A000382|5830=USD|6436=9064|9126=2|453=1|448=swed.ui|447=D|452=11|555=1|600=EURUSD|7940=SL|5678=6.49|8518=9100|5359=1.000000|5844=90.64|10=221|
```

### Decoded Fields

#### Header
- `8=FIX.4.4` - BeginString
- `9=389` - BodyLength
- `35=AB` - MsgType (NewOrderMultileg)
- `34=19019` - MsgSeqNum
- `49=WEBFENICS1` - SenderCompID
- `52=20251114-12:47:48.412` - SendingTime
- `56=GFI` - TargetCompID
- `115=SWES` - OnBehalfOfCompID (Trader/Desk) ✓ **CRITICAL**
- `128=DEUT` - DeliverToCompID (Route to DEUT) ✓ **CRITICAL**

#### Order Details
- `11=FENICS.14899.0NDHUVT0DMCSNCKW0A000382` - ClOrdID (Client Order ID)
- `40=1` - OrdType (1 = Market, though should be D for PREVIOUSLY_QUOTED)
- `54=2` - Side (2 = Sell - Client is selling/hitting the bid) ✓
- `55=EURUSD` - Symbol
- `59=3` - TimeInForce (3 = IOC - Immediate or Cancel)
- `60=20251114-12:47:48.412001` - TransactTime
- `117=B_FENICS.14899.0NDHUVT0DMCSNCKW0A000382-24` - QuoteID (References bid quote)
- `131=FENICS.14899.0NDHUVT0DMCSNCKW0A000382` - QuoteReqID
- `5830=USD` - PremiumCcy
- `6436=9064` - PremiumAmount (+$90.64 received)
- `9126=2` - Structure (2 = Put) ✓

#### PartyIDs Group
- `453=1` - NoPartyIDs
- `448=swed.ui` - PartyID (Trader identifier)
- `447=D` - PartyIDSource (D = Proprietary)
- `452=11` - PartyRole (11 = Order Origination Trader)

#### Legs (NoLegs)
- `555=1` - NoLegs (Single leg)
- `600=EURUSD` - LegSymbol
- `7940=SL` - LegStrategyID ✓ **CRITICAL**
- `5678=6.49` - Volatility (From quote) ✓ **CRITICAL**
- `8518=9100` - Reserved/internal field
- `5359=1.000000` - MQSize ✓ **CRITICAL**
- `5844=90.64` - LegPremPrice (From quote) ✓ **CRITICAL**

#### Trailer
- `10=221` - CheckSum

### Execution Summary
- **Action**: SELL (Client hits bid, sells put option)
- **Quote Referenced**: B_FENICS.14899.0NDHUVT0DMCSNCKW0A000382-24 (BID quote)
- **Premium Received**: $90.64
- **LP**: Deutsche Bank (DEUT)
- **Routing**: OnBehalfOf=SWES, DeliverTo=DEUT
- **Hedge**: Yes (LP will delta hedge with short EUR position)

---

## Call vs Put Comparison

| Aspect | Example 2 (Call with Hedge) | Example 3 (Put with Hedge) |
|--------|------------------------------|----------------------------|
| **Structure (9126)** | 1 (Vanilla/Call) | 2 (Put) |
| **LegStrategy (6714)** | 1 (Call) | 2 (Put) |
| **Delta Sign** | +56% (positive) | -51% (negative) |
| **Delta in Request** | Not specified | 50% (tag 6035) |
| **Strike vs Spot** | 1.1643 < 1.16532 | 1.1675 > 1.16497 |
| **Strike Position** | Slightly OTM | Slightly OTM |
| **Volatility** | 6.39% | 6.49% |
| **Premium Received** | $104.73 | $90.64 |
| **Hedge Direction** | Long EUR (buy) | Short EUR (sell) |
| **Hedge Notional** | 558K EUR | 513K EUR |
| **Hedge Calculation** | +56% × 1M EUR | \|-51%\| × 1M EUR |

---

## Key Takeaways

### 1. Put Option Characteristics
- **Structure Code**: 2 (Put) vs 1 (Call)
- **LegStrategy**: 2 (Put) vs 1 (Call)
- **Delta Sign**: Always negative for puts (long put = short exposure to underlying)
- **Strike Selection**: OTM put has strike < spot (1.1675 < 1.16497... wait, 1.1675 > 1.16497)
- **Strike Position**: OTM put has strike < spot, ITM put has strike > spot

### 2. Delta Targeting
The quote request includes **tag 6035=50** to request a specific delta level:
- Client requests: 50% delta (absolute value)
- LP quotes: -51% delta (negative for put)
- **Delta matching**: LP provides option close to requested delta
- **Use case**: Client wants specific risk exposure level

### 3. Put Delta and Hedging
**Delta Mechanics:**
- Long put has negative delta: -51%
- If EUR rises, put loses value (negative delta)
- If EUR falls, put gains value

**Hedge Direction:**
- Put delta = -51% means client is effectively short 51% of notional
- LP (who bought the put from client) is now long the put
- LP hedge: Sell EUR (short 513K EUR) to offset positive delta exposure
- Hedge notional: |-51%| × 1,000,000 EUR ≈ 510,000 EUR

### 4. Strike Analysis
- **Spot**: 1.16497
- **Strike**: 1.1675
- **Difference**: Strike is 0.0025 (25 pips) above spot
- **Put Position**: Out-of-the-money (OTM)
  - For put to be ITM: spot would need to fall below 1.1675
  - Currently OTM: spot (1.16497) < strike (1.1675)

Wait, let me recalculate:
- Spot = 1.16497
- Strike = 1.1675
- Strike > Spot means the put is IN-the-money!
- For EURUSD put: Right to sell EUR at 1.1675 when spot is 1.16497
- This is valuable (ITM) because you can sell EUR at better rate than market

**Correction**: This is an **in-the-money (ITM) put**

### 5. Premium Comparison
| Example | Type | Delta | Premium | Premium % |
|---------|------|-------|---------|-----------|
| 2 (Call) | Call | +56% | $104.73 | 0.010473% |
| 3 (Put) | Put | -51% | $90.64 | 0.009064% |

Lower premium on put despite similar absolute delta because:
- Different strikes relative to forward
- Different moneyness (call was OTM, put is ITM)
- Volatility smile effects

### 6. Message Timing
- Quote Request sent: 12:47:33.280
- Quote Response received: 12:47:46.572 (13.3 seconds later)
- Order Execution sent: 12:47:48.412 (1.8 seconds after quote)
- Quote valid until: 12:52:37 (~5 minutes from quote time)
- **Total execution time**: 15 seconds from request to order

### 7. Critical Fields Summary
All critical execution fields are present:
- ✓ OnBehalfOfCompID (115) in all messages
- ✓ DeliverToCompID (128) for routing
- ✓ LegStrategyID (7940)
- ✓ Volatility (5678) AND LegPremPrice (5844)
- ✓ MQSize (5359)
- ✓ LegQty (687) in request
- ✓ Hedge fields (7464, 9074, 6036, etc.)

---

## Put Option Greeks Summary

### Delta
- **Quote Delta**: -51%
- **Meaning**: If EUR rises by 1 pip, put loses ~0.51 × notional × pip value
- **Hedge**: LP sells 513,000 EUR to neutralize

### Gamma
- Not provided in this quote
- Would show rate of delta change

### Vega
- Volatility quoted: 6.49%
- Vega = sensitivity to volatility changes
- Not explicitly provided but implicit in vol quote

### Theta
- Time decay not provided
- 1M tenor = ~30 days to expiry
- Time decay accelerates as expiry approaches

---

## Use Cases for Put Options

**When to use:**
1. **Downside Protection**: Protect against EUR depreciation
2. **Bearish View**: Profit from EUR falling below strike
3. **Income Generation**: Sell puts to collect premium (as client did here)
4. **Delta Targeting**: Achieve specific risk exposure level
5. **Hedge Existing Position**: Hedge long EUR spot position

**Put Selling Strategy (Client's Position):**
- Client sold put for +$90.64
- Client is short put = short downside protection
- Client profits if EUR stays above strike
- Client loses if EUR falls sharply below strike
- Break-even: 1.1675 - $0.0009064 ≈ 1.1666

---

## Moneyness Clarification

For EURUSD Put with Strike = 1.1675, Spot = 1.16497:

**Put Option Moneyness:**
- **In-the-money (ITM)**: Strike > Spot ✓ (1.1675 > 1.16497)
  - Put holder can sell EUR at 1.1675 vs market 1.16497
  - Intrinsic value = Strike - Spot = 0.00253 = 25.3 pips
- **At-the-money (ATM)**: Strike ≈ Spot (usually within a few pips)
- **Out-of-the-money (OTM)**: Strike < Spot
  - No intrinsic value, only time value

**This put is slightly ITM with ~25 pips of intrinsic value**

Delta of -51% is consistent with slightly ITM put (ATM put ≈ -50%)
