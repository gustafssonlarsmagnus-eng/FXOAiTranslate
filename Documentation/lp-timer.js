// LP Timer Management System for FX Aggregator
// Provides countdown timers for LP quote validity

const lpTimers = new Map();
const QUOTE_VALIDITY_DEFAULT = 120;
const TIMER_FRESH = 90, TIMER_AGING = 30, TIMER_URGENT = 10;

function startLPTimer(lp, validitySeconds = QUOTE_VALIDITY_DEFAULT, quoteId = null) {
    stopLPTimer(lp);
    const expiryTime = Date.now() + (validitySeconds * 1000);
    const intervalId = setInterval(() => updateLPTimer(lp), 100);
    lpTimers.set(lp, { intervalId, expiryTime, quoteId });
    updateLPTimer(lp);
    console.log(`[Timer] Started for ${lp}: ${validitySeconds}s`);
}

function stopLPTimer(lp) {
    const timer = lpTimers.get(lp);
  if (timer) {
        clearInterval(timer.intervalId);
     lpTimers.delete(lp);
    }
}

function updateLPTimer(lp) {
    const timer = lpTimers.get(lp);
    if (!timer) return;

    const remainingSeconds = Math.max(0, Math.floor((timer.expiryTime - Date.now()) / 1000));
    const checkbox = document.querySelector(`.lp-checkbox[data-lp="${lp}"]`);
    if (!checkbox) return;

    const lpRow = checkbox.closest('.ladder-row');
    const midCell = lpRow?.querySelector(':scope > div:nth-child(2)');
    if (!midCell) return;

    // Find or create timer element
    let timerElement = midCell.querySelector('.lp-timer');
    if (!timerElement) {
        timerElement = document.createElement('div');
        timerElement.className = 'lp-timer';
  const lpName = midCell.querySelector('span');
        if (lpName) lpName.after(timerElement);
  }

 // Format time display
    const minutes = Math.floor(remainingSeconds / 60);
    const seconds = remainingSeconds % 60;
    timerElement.textContent = `${minutes}:${seconds.toString().padStart(2, '0')}`;

    // Clear previous classes
    timerElement.classList.remove('timer-fresh', 'timer-warning', 'timer-urgent', 'timer-expired');
    lpRow.classList.remove('quote-fresh', 'quote-aging', 'quote-stale', 'quote-expired');
    lpRow.style.opacity = '';

    // Apply state-based styling
    if (remainingSeconds <= 0) {
timerElement.classList.add('timer-expired');
        lpRow.classList.add('quote-expired');
        timerElement.textContent = 'EXPIRED';
        stopLPTimer(lp);
    } else if (remainingSeconds <= TIMER_URGENT) {
        timerElement.classList.add('timer-urgent');
        lpRow.classList.add('quote-stale');
    } else if (remainingSeconds <= TIMER_AGING) {
        timerElement.classList.add('timer-warning');
        lpRow.classList.add('quote-aging');
    } else {
        timerElement.classList.add('timer-fresh');
    lpRow.classList.add('quote-fresh');
    }

    // Keep disabled LP rows dimmed
    if (!checkbox.checked) {
        lpRow.querySelectorAll(':scope > div:not(:nth-child(2))').forEach(col => col.style.opacity = '0.2');
    }
}

// Hook into updateLPQuote to start timers when quotes arrive
function enhanceUpdateLPQuote() {
    if (typeof window.updateLPQuote === 'function') {
     const originalUpdateLPQuote = window.updateLPQuote;
     window.updateLPQuote = function(quote) {
         originalUpdateLPQuote(quote);
 const validitySeconds = quote.validitySeconds || QUOTE_VALIDITY_DEFAULT;
            startLPTimer(quote.lp, validitySeconds, quote.quoteId);
        };
   console.log('[Timer] Enhanced updateLPQuote with timer support');
    }
}

// Cleanup on page unload
window.addEventListener('beforeunload', () => {
    lpTimers.forEach((timer) => clearInterval(timer.intervalId));
    lpTimers.clear();
});

// Initialize when DOM is ready
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', enhanceUpdateLPQuote);
} else {
    enhanceUpdateLPQuote();
}

console.log('[Timer] LP Timer system loaded');
