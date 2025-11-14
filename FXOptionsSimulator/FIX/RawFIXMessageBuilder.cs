using QLNet;
using QuickFix;
using System;
using System.Globalization;
using System.Text;
using static QLNet.JointCalendar;

namespace FXOptionsSimulator.FIX
{
    /// <summary>
    /// Builds raw FIX messages with complete control over field order
    /// </summary>
    public class RawFIXMessageBuilder
    {
        private readonly StringBuilder _body;
        private readonly string _beginString;
        private readonly string _senderCompID;
        private readonly string _targetCompID;
        private int _msgSeqNum;

        public RawFIXMessageBuilder(string beginString, string senderCompID, string targetCompID)
        {
            _beginString = beginString;
            _senderCompID = senderCompID;
            _targetCompID = targetCompID;
            _body = new StringBuilder();
            _msgSeqNum = 1;
        }

        public void SetMsgSeqNum(int seqNum)
        {
            _msgSeqNum = seqNum;
        }

        public string BuildQuoteRequest(
            TradeStructure trade,
            string lpName,
            string quoteReqID,
            string groupId,
            string tag75Override = null,
            string tag5020Override = null)
        {
            _body.Clear();

            // Standard header fields (in body)
            AddField(35, "R"); // MsgType
            AddField(34, _msgSeqNum.ToString()); // MsgSeqNum
            AddField(49, _senderCompID); // SenderCompID
            AddField(52, GetUTCTimestamp()); // SendingTime
            AddField(56, _targetCompID); // TargetCompID
            // AddField(115, "SWES"); // OnBehalfOfCompID - COMMENTED OUT FOR TESTING

            // DeliverToCompID in header
            AddField(128, lpName);
            Console.WriteLine($"[DEBUG] Tag 128 - lpName value: '{lpName}'");

            // Use a single captured trade date for consistency
            var tradeDate = DateTime.UtcNow.Date;

            // ✅ canonical override for 75 & 5020
            var tag75 = tag75Override ?? tradeDate.ToString("yyyyMMdd");
            // Note: tag5020Override is always provided by caller, this fallback should never be used
            // But if it is, use T+1 from today as a safe default
            var tag5020 = tag5020Override ?? tradeDate.AddDays(1).ToString("yyyyMMdd");

            // === QLNet policy & calendars for expiry/delivery ===
            var pair = trade.Underlying;               // e.g. EURUSD
            var ccy1 = pair.Substring(0, 3);
            var ccy2 = pair.Substring(3, 3);
            var policy = GlobalDatePolicy.Policy;

            var jointCal = new JointCalendar(
                FxDateService.CalendarFromCcy(ccy1),
                FxDateService.CalendarFromCcy(ccy2),
                JointCalendarRule.JoinHolidays);

            var spotLag = policy.SpotLagForPair(pair);   // OneBD / TwoBD etc.


            // Body fields in EXACT GFI order
            AddField(75, tag75); // TradeDate  ✅ uses override if provided
            AddField(131, quoteReqID); // QuoteReqID
            AddField(5475, "S"); // PremDel
            AddField(5830, trade.PremiumCurrency); // PremiumCcy
            AddField(9016, "1"); // HedgeTradeType

            int structureCode = TradeStructure.GetStructureCode(trade.StructureType);
            AddField(9126, structureCode.ToString()); // Structure
            AddField(9943, "2"); // ProductQuoteType
            AddField(8051, groupId);
            AddField(146, "1"); // NoRelatedSym

            // NoRelatedSym group - EXACT order from GFI sample
            AddField(55, trade.Underlying); // Symbol
            AddField(6258, structureCode.ToString()); // Strategy - MOVED HERE!
            AddField(537, "1"); // QuoteType
            AddField(555, trade.Legs.Count.ToString()); // NoLegs

            // DIAGNOSTIC: Show all leg directions before building
            Console.WriteLine($"[QUOTE REQUEST] Building {trade.Legs.Count}-leg structure:");
            for (int i = 0; i < trade.Legs.Count; i++)
            {
                Console.WriteLine($"  Leg {i+1}: Direction={trade.Legs[i].Direction} → Position={(trade.Legs[i].Direction == "SELL" ? "1" : "2")}");
            }

            // Legs in EXACT GFI order from their sample
            for (int i = 0; i < trade.Legs.Count; i++)
            {
                var leg = trade.Legs[i];

                // EXACT order from GFI sample
                AddField(600, trade.Underlying); // LegSymbol
                AddField(6714, leg.OptionType == "CALL" ? "1" : "2"); // LegStrategy
                AddField(9125, "1"); // Cutoff
                // GFI requires BOTH tenor AND maturity date (even though docs say "either")
                AddField(6215, leg.Tenor); // Tenor (e.g., "1M")

                // === QLNet-based expiry (611) and delivery (743) ===
                DateTime expiryDt;
                DateTime deliveryDt;

                if (leg.ExpiryDate != default)
                {
                    // explicit expiry supplied by the user
                    var e = leg.ExpiryDate.Date;
                    var qExp = new QLNet.Date(e.Day, (QLNet.Month)e.Month, e.Year);

                    // Adjust expiry using policy convention on joint calendar
                    var qAdjExp = jointCal.adjust(qExp, policy.ExpiryConvention);

                    // Compute delivery = expiry + spotLag BUSINESS days on the joint calendar
                    var qDel = qAdjExp;
                    int moved = 0;
                    while (moved < (int)spotLag)
                    {
                        qDel = qDel + 1;
                        if (jointCal.isBusinessDay(qDel)) moved++;
                    }

                    expiryDt = new DateTime(qAdjExp.Year, (int)qAdjExp.Month, qAdjExp.Day);
                    deliveryDt = new DateTime(qDel.Year, (int)qDel.Month, qDel.Day);
                }
                else
                {
                    // tenor-driven dates via your FxDateService (QLNet under the hood)
                    var nowUtc = DateTime.UtcNow;
                    var (_, _, expiry, delivery, _) =
                        FxDateService.ComputeDates(nowUtc, pair, leg.Tenor, trade.PremiumCurrency, new FxDateRules
                        {
                            Ccy1 = ccy1,
                            Ccy2 = ccy2,
                            SpotLag = spotLag,
                            ExpiryConvention = policy.ExpiryConvention,
                            ExpiryEOM = policy.ExpiryEOM,
                            PremiumSettleDays = policy.PremiumSettleDays,
                            PremiumCalMode = policy.PremiumCalendarMode,
                            PremiumConvention = policy.PremiumConvention
                        });

                    expiryDt = expiry;
                    deliveryDt = delivery;
                }

                // Tag 611/743 with policy-computed dates
                AddField(611, expiryDt.ToString("yyyyMMdd"));   // LegMaturityDate
                AddField(743, deliveryDt.ToString("yyyyMMdd")); // DeliveryDate


                // ❌ old (ignored override)
                // var premiumDate = GetNextBusinessDay(tradeDate, 2);
                // AddField(5020, premiumDate.ToString("yyyyMMdd")); // PremiumDelivery

                // ✅ use the canonical 5020 for ALL legs / ALL LPs
                AddField(5020, tag5020); // PremiumDelivery

                AddField(612, leg.Strike.ToString("F4", CultureInfo.InvariantCulture)); // LegStrikePrice

                AddField(9019, "2"); // FXOptionStyle
                // Position field - GFI's actual behavior (CORRECTED after diagnostic testing):
                // Position=1 → GFI sends OFFER quote (Side=1) for client to BUY from
                // Position=2 → GFI sends BID quote (Side=2) for client to SELL into
                string positionValue = leg.Direction == "BUY" ? "1" : "2";
                AddField(6351, positionValue); // Position
                Console.WriteLine($"[QUOTE REQUEST] Direction={leg.Direction} → Position(6351)={positionValue}");
                AddField(9904, "2"); // PriceIndicator

                if (trade.SpotReference > 0)
                {
                    AddField(5235, trade.SpotReference.ToString("F4", CultureInfo.InvariantCulture)); // LegSpotRate
                }

                AddField(556, trade.PremiumCurrency); // LegCurrency
                AddField(687, leg.NotionalMM.ToString(CultureInfo.InvariantCulture)); // LegQty
                AddField(7940, leg.LegID); // LegStrategyID
                AddField(9034, ccy1); // LegStrategyCcy - always Ccy1 (base currency) per GFI spec
            }

            // Build complete message with header and trailer
            return BuildCompleteMessage(_body.ToString());
        }

        public string BuildNewOrderMultileg(
            string clOrdID,
            string quoteID,
            string side, // "SELL" or "BUY"
            string symbol,
            int structureCode,
            FIXMessage quote)
        {
            Console.WriteLine($"  [DEBUG IN BuildNewOrderMultileg] side parameter: '{side}'");

            // GFI requires execution Side to be OPPOSITE of quote Side (confirmed by GFI examples):
            // - Quote Side=1 (BID) → Execution sends Side=2
            // - Quote Side=2 (OFFER) → Execution sends Side=1
            // Get the quote Side and flip it
            string quoteSide = quote.Get("54"); // Side field from quote
            string executionSide = quoteSide == "1" ? "2" : "1";
            Console.WriteLine($"  [DEBUG] Quote Side={quoteSide} → Execution Side={executionSide} (opposite of quote)");

            _body.Clear();

            // Standard header fields (in body) - in FIX order
            AddField(35, "AB"); // MsgType = NewOrderMultileg
            AddField(34, _msgSeqNum.ToString()); // MsgSeqNum
            AddField(49, _senderCompID); // SenderCompID
            AddField(52, GetUTCTimestamp()); // SendingTime
            AddField(56, _targetCompID); // TargetCompID

            // Get LP from quote and set DeliverToCompID only
            // OnBehalfOfCompID (115) appears to be auto-populated by GFI, not sent by client
            string lpName = quote.Get("115"); // OnBehalfOfCompID from quote
            if (!string.IsNullOrEmpty(lpName))
            {
                AddField(128, lpName); // DeliverToCompID - routes to specific LP
            }

            // Body fields in EXACT order from GFI sample
            AddField(11, clOrdID); // ClOrdID
            AddField(40, "1"); // OrdType = MARKET (per GFI spec for New Order - Multileg)
            AddField(54, executionSide); // Side - must match quote Side being hit
            AddField(55, symbol); // Symbol
            AddField(59, "3"); // TimeInForce = IMMEDIATE_OR_CANCEL
            AddField(60, GetUTCTimestamp()); // TransactTime
            AddField(117, quoteID); // QuoteID

            // Get QuoteReqID from quote
            string quoteReqID = quote.Get("131");
            if (!string.IsNullOrEmpty(quoteReqID))
            {
                AddField(131, quoteReqID); // QuoteReqID
            }

            AddField(9126, structureCode.ToString()); // Structure

            // Per GFI: Must send aggregate Premium and PremiumCcy at message level
            // Calculate total premium from all legs (preserve sign!)
            if (quote.LegPricing != null && quote.LegPricing.Count > 0)
            {
                double totalPremium = 0;
                foreach (var leg in quote.LegPricing)
                {
                    if (!string.IsNullOrEmpty(leg.LegPremPrice) && double.TryParse(leg.LegPremPrice, out double legPrem))
                    {
                        totalPremium += legPrem;
                    }
                }

                // Get PremiumCcy from quote (should be same as quote currency)
                string premiumCcy = quote.Get("5830"); // Try to get from quote first
                if (string.IsNullOrEmpty(premiumCcy))
                {
                    // Fallback: for EURUSD, premium is typically in USD (term currency)
                    premiumCcy = symbol.Length >= 6 ? symbol.Substring(3, 3) : "USD";
                }

                AddField(5830, premiumCcy); // PremiumCcy
                AddField(6436, totalPremium.ToString("F2")); // Premium - aggregate, preserve sign
            }

            // PartyIDs group - per GFI spreadsheet example
            AddField(453, "1"); // NoPartyIDs
            AddField(448, _senderCompID); // PartyID
            AddField(447, "D"); // PartyIDSource = PROPRIETARY_CUSTOM_CODE
            AddField(452, "11"); // PartyRole = OrderOriginationTrader

            // NoLegs and leg repeating groups
            // Per GFI spreadsheet example: Must send ALL fields including LegSymbol and pricing
            if (quote.LegPricing != null && quote.LegPricing.Count > 0)
            {
                AddField(555, quote.LegPricing.Count.ToString()); // NoLegs

                foreach (var legPricing in quote.LegPricing)
                {
                    // Send leg fields in EXACT order from GFI spreadsheet example:
                    // 1. LegSymbol (600) - YES, it's in the example!
                    // 2. LegStrategyID (7940)
                    // 3. Volatility (5678)
                    // 4. MQSize (5359)
                    // 5. LegPremPrice (5844)

                    AddField(600, legPricing.LegSymbol ?? symbol); // LegSymbol - per GFI example

                    if (!string.IsNullOrEmpty(legPricing.LegStrategyID))
                        AddField(7940, legPricing.LegStrategyID); // LegStrategyID

                    if (!string.IsNullOrEmpty(legPricing.Volatility))
                        AddField(5678, legPricing.Volatility); // Volatility - exact from quote

                    if (!string.IsNullOrEmpty(legPricing.MQSize))
                        AddField(5359, legPricing.MQSize); // MQSize - exact from quote

                    if (!string.IsNullOrEmpty(legPricing.LegPremPrice))
                        AddField(5844, legPricing.LegPremPrice); // LegPremPrice - exact from quote, keep sign
                }
            }

            // Build complete message with header and trailer
            return BuildCompleteMessage(_body.ToString());
        }

        private void AddField(int tag, string value)
        {
            _body.Append(tag);
            _body.Append('=');
            _body.Append(value);
            _body.Append('\x01'); // SOH delimiter
        }

        private string BuildCompleteMessage(string body)
        {
            // Calculate body length (without BeginString and BodyLength fields)
            int bodyLength = Encoding.ASCII.GetByteCount(body);

            // Build message without checksum
            var msgBuilder = new StringBuilder();
            msgBuilder.Append("8=");
            msgBuilder.Append(_beginString);
            msgBuilder.Append('\x01');
            msgBuilder.Append("9=");
            msgBuilder.Append(bodyLength);
            msgBuilder.Append('\x01');
            msgBuilder.Append(body);

            // Calculate checksum
            string msgWithoutChecksum = msgBuilder.ToString();
            string checksum = CalculateChecksum(msgWithoutChecksum);

            // Add checksum
            msgBuilder.Append("10=");
            msgBuilder.Append(checksum);
            msgBuilder.Append('\x01');

            return msgBuilder.ToString();
        }

        private string CalculateChecksum(string message)
        {
            int sum = 0;
            foreach (char c in message)
            {
                sum += c;
            }
            int checksum = sum % 256;
            return checksum.ToString("D3");
        }

        private string GetUTCTimestamp()
        {
            return DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss.fff");
        }
    }
}
