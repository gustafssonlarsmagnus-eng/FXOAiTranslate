# FX Options Simulator - Examples

This directory contains reference examples for FIX message flows used in the FX Options trading system.

## FIX Message Examples

### [Example 1: Vanilla Call - No Hedge](fix-messages/example-01-vanilla-call-no-hedge.md)

A complete message flow demonstrating:
- Quote Request for EURUSD 1M vanilla call option (HedgeTradeType = 0)
- Quote Response from Deutsche Bank (DEUT) - **OFFER** side
- Order Execution (**buying** the offer, lifting offer)

**Key Learning Points:**
- Critical fields required for successful execution (tags 115, 687, 5678, 5844, 7940)
- Proper routing using OnBehalfOfCompID and DeliverToCompID
- Complete leg pricing structure
- Message timing and quote validity windows
- Client pays premium (negative premium flow)

### [Example 2: Vanilla Call - With Hedge](fix-messages/example-02-vanilla-call-with-hedge.md)

A complete message flow demonstrating:
- Quote Request for EURUSD 1M vanilla call option (HedgeTradeType = 1)
- Quote Response from Deutsche Bank (DEUT) - **BID** side with hedge details
- Order Execution (**selling** the call, hitting bid)

**Key Learning Points:**
- Delta hedging mechanics and hedge fields (tags 7464, 9074, 6036, 9657, 9112, 6426)
- Bid vs Offer differences (quote side, order side, premium sign)
- Client receives premium (positive premium flow)
- LP executes spot FX hedge alongside option
- Hedge notional calculation (delta × option notional)
- Comparison table with no-hedge example

## Message Structure Reference

All FIX messages in these examples use:
- **Protocol**: FIX 4.4
- **Delimiter**: `|` (pipe symbol, representing SOH `\x01` in actual messages)
- **Format**: `tag=value|tag=value|...`

## How to Use These Examples

1. **For Development**: Use as reference when implementing new message handlers
2. **For Testing**: Validate your message builder outputs match these structures
3. **For Debugging**: Compare failed messages against these working examples
4. **For Documentation**: Understand the complete workflow and field requirements

## Common FIX Tags Reference

### Core Message Tags
| Tag | Field Name | Description |
|-----|------------|-------------|
| 35 | MsgType | R=QuoteRequest, S=Quote, AB=NewOrderMultileg, 8=ExecutionReport |
| 115 | OnBehalfOfCompID | Trader/Desk or LP identifier (CRITICAL) |
| 128 | DeliverToCompID | Route to specific LP |
| 131 | QuoteReqID | Unique quote request identifier |
| 117 | QuoteID | Quote identifier from LP |
| 54 | Side | 1=Buy/Bid, 2=Sell/Offer |
| 555 | NoLegs | Number of option legs |

### Pricing Tags (CRITICAL)
| Tag | Field Name | Description |
|-----|------------|-------------|
| 687 | LegQty | Notional quantity per leg |
| 5678 | Volatility | Implied volatility % |
| 5844 | LegPremPrice | Premium price per leg |
| 7940 | LegStrategyID | Leg strategy identifier |
| 5359 | MQSize | Market quote size |
| 5235 | LegSpotRate | Spot FX rate |
| 6035 | LegDelta | Option delta % |

### Hedge Tags (When HedgeTradeType = 1)
| Tag | Field Name | Description |
|-----|------------|-------------|
| 9016 | HedgeTradeType | 0=No Hedge, 1=With Hedge |
| 7464 | Hedge Indicator | Hedge present flag |
| 9074 | Hedge Currency | Currency being hedged |
| 6666 | Hedge Execution Type | Method of hedge execution |
| 6036 | Hedge Notional | Hedge amount (in millions) |
| 9657 | Hedge Spot Rate | FX rate for hedge |
| 9112 | Hedge Settlement Date | Delivery date for hedge (YYYYMMDD) |
| 6426 | Hedge Symbol | Symbol for hedge transaction |

## Additional Resources

- [FIX 4.4 Specification](https://www.fixtrading.org/standards/)
- Project Documentation: See main README.md
- Implementation: See `FXOptionsSimulator/FIX/` directory

## Contributing Examples

To add new examples:
1. Create a new markdown file in `fix-messages/`
2. Follow the format: `example-##-description.md`
3. Include: Raw messages, decoded fields, explanations, key takeaways
4. Update this README with a link and description
