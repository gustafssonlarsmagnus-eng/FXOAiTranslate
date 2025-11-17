# FX Options Simulator - Examples

This directory contains reference examples for FIX message flows used in the FX Options trading system.

## FIX Message Examples

### [Example 1: Vanilla Call - No Hedge](fix-messages/example-01-vanilla-call-no-hedge.md)

A complete message flow demonstrating:
- Quote Request for EURUSD 1M vanilla call option
- Quote Response from Deutsche Bank (DEUT)
- Order Execution (buying the offer)

**Key Learning Points:**
- Critical fields required for successful execution (tags 115, 687, 5678, 5844, 7940)
- Proper routing using OnBehalfOfCompID and DeliverToCompID
- Complete leg pricing structure
- Message timing and quote validity windows

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

| Tag | Field Name | Description |
|-----|------------|-------------|
| 35 | MsgType | R=QuoteRequest, S=Quote, AB=NewOrderMultileg, 8=ExecutionReport |
| 115 | OnBehalfOfCompID | Trader/Desk or LP identifier (CRITICAL) |
| 128 | DeliverToCompID | Route to specific LP |
| 131 | QuoteReqID | Unique quote request identifier |
| 117 | QuoteID | Quote identifier from LP |
| 54 | Side | 1=Buy, 2=Sell |
| 555 | NoLegs | Number of option legs |
| 687 | LegQty | Notional quantity per leg (CRITICAL) |
| 5678 | Volatility | Implied volatility % (CRITICAL) |
| 5844 | LegPremPrice | Premium price per leg (CRITICAL) |
| 7940 | LegStrategyID | Leg strategy identifier (CRITICAL) |
| 5359 | MQSize | Market quote size |

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
