// Trade Details Initialization for FX Aggregator
// Populates HTML elements with actual trade data from C#

function initializeTradeDetailsUI(trade) {
    console.log(`[Trade Init] Initializing: ${trade.underlying} ${trade.legs.length} leg(s)`);

  // Update page title
    document.title = `FX Aggregator - ${trade.underlying}`;

    // Determine structure type name
    const structureTypes = {
      '1': 'Vanilla Option',
        '5': 'Risk Reversal',
        '8': 'Call Spread',
        '9': 'Put Spread',
        '10': 'Seagull'
    };
    const structureName = structureTypes[trade.structureType] || 'Multi-Leg Option';

    // Get base and term currencies
    const baseCcy = trade.underlying.substring(0, 3);
    const termCcy = trade.underlying.substring(3, 6);

    // Update LP count label
    const lpCountLabel = document.querySelector('#ladder-summary .text-\\[10px\\]');
    if (lpCountLabel) {
        lpCountLabel.textContent = '4 LPs';
    }

    // Populate Leg 1 details
    if (trade.legs.length >= 1) {
        const leg = trade.legs[0];

        // Find and update fields by their label text
        updateFieldByLabel('Type', structureName);
        updateFieldByLabel('Quantity', `${baseCcy}`, `${formatNotional(leg.notionalMM * 1000000)}`);
        updateFieldByLabel('Option Type', formatOptionType(leg.optionType, baseCcy, termCcy));
        updateFieldByLabel('Expiry Date', formatExpiryDate(leg.expiryDate), leg.tenor);
        updateFieldByLabel('Strike', formatStrike(leg.strike, trade.underlying));
  
        // Store trade data globally
        window.tradeData = trade;
        
        console.log(`[Trade Init] Leg 1: ${leg.direction} ${leg.notionalMM}M ${baseCcy} ${leg.optionType} @ ${leg.strike} (${leg.tenor})`);
    }

    // Update spot rate in Market Data section if present
    if (trade.spot) {
        updateFieldByLabel('Spot Rate', formatStrike(trade.spot, trade.underlying));
    }
}

// Helper: Update a field by finding its label
function updateFieldByLabel(labelText, value, secondValue = null) {
    const labels = document.querySelectorAll('#leg1-content .bg-\\[\\#14161b\\]');
    
    for (const label of labels) {
        if (label.textContent.trim() === labelText || label.textContent.includes(labelText)) {
   const row = label.parentElement;
  const valueCell = row.querySelector(':scope > div:last-child');
            
    if (valueCell) {
      if (labelText === 'Quantity') {
     // Special handling for quantity (currency + amount)
       valueCell.innerHTML = `
  <span class="font-medium">${value}</span>
     <span class="font-bold">${secondValue}</span>
         `;
         } else if (labelText === 'Expiry Date') {
     // Special handling for expiry (date + tenor)
        valueCell.innerHTML = `
            <span class="font-medium">${value}</span>
      <span class="text-blue-400 font-medium ml-2">${secondValue}</span>
        `;
 } else if (labelText === 'Strike') {
          // Yellow highlight for strike
           valueCell.innerHTML = `<span class="font-bold text-yellow-400">${value}</span>`;
      } else {
   // Default: just set text content
        const span = valueCell.querySelector('span');
         if (span) {
      span.textContent = value;
          } else {
            valueCell.textContent = value;
   }
        }
  console.log(`[Trade Init] Updated ${labelText}: ${value}${secondValue ? ' / ' + secondValue : ''}`);
            }
            break;
     }
    }
}

// Format notional with commas (e.g., 10000000 -> "10,000,000")
function formatNotional(amount) {
    return amount.toLocaleString('en-US');
}

// Format option type (e.g., "CALL" -> "EUR Call / USD Put")
function formatOptionType(type, baseCcy, termCcy) {
    if (type === 'CALL' || type === 'Call' || type === 'C') {
   return `${baseCcy} Call / ${termCcy} Put`;
    } else {
        return `${baseCcy} Put / ${termCcy} Call`;
    }
}

// Format expiry date (ISO to display format)
function formatExpiryDate(isoDate) {
    if (!isoDate) return '--';
    try {
        const date = new Date(isoDate);
        const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
 return `${date.getDate()} ${months[date.getMonth()]} ${String(date.getFullYear()).slice(-2)}`;
    } catch {
        return isoDate;
    }
}

// Format strike based on currency pair
function formatStrike(strike, underlying) {
if (!strike) return '--';
    
    // Determine decimal places based on pair
    const jpy = underlying.includes('JPY');
    const decimals = jpy ? 2 : 4;
    
    return parseFloat(strike).toFixed(decimals);
}

// Hook into the original initializeTradeDetails function
function enhanceInitializeTradeDetails() {
    if (typeof window.initializeTradeDetails === 'function') {
        const original = window.initializeTradeDetails;
        window.initializeTradeDetails = function(trade) {
 original(trade);
      initializeTradeDetailsUI(trade);
        };
        console.log('[Trade Init] Enhanced initializeTradeDetails');
    } else {
     // If original doesn't exist, create it
      window.initializeTradeDetails = initializeTradeDetailsUI;
 console.log('[Trade Init] Created initializeTradeDetails');
    }
}

// Initialize when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', enhanceInitializeTradeDetails);
} else {
enhanceInitializeTradeDetails();
}

console.log('[Trade Init] Trade details initialization module loaded');
