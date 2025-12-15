
FX Options Aggregator: GUI Design Specification
Version: 1.5 (Final Refinement)
Theme: Institutional Dark (Midnight Blue / Gunmetal)
Focus: Volatility Trading
1. Visual Style Guide
This interface uses a high-contrast, low-saturation palette designed for long trading sessions.
Backgrounds: Deep Gunmetal (#0b0d10) to Slate (#16181d).
Interaction Color: "Midnight Blue" (#1e2536) for hover states (simulating a depressed button).
Typography:
UI Labels: Inter (Clean, proportional).
Financial Data: JetBrains Mono (Fixed width, technical).
Highlights:
Bid: Desaturated Red (text-red-900 bg, text-red-500 text).
Offer: Desaturated Blue (text-blue-900 bg, text-blue-500 text).
Best Price: Highlighted with a vertical bar and subtle gradient.
2. Interface States
State A: Idle (Start RFQ)
The initial state before a stream is active. It is designed to look like a dormant version of the active tile.
code
Html
<!-- RFQ WIDGET -->
<div id="rfq-panel" class="h-48 w-full mb-2 bg-[#14161b] rounded-lg overflow-hidden flex flex-col cursor-pointer border border-[#1e293b] group select-none transition-all hover:border-slate-500">
    <!-- Header -->
    <div class="bg-[#1e2229] px-3 py-1.5 flex gap-3 items-center border-b border-[#252933]">
        <span class="text-[10px] font-bold text-slate-600 uppercase tracking-widest">Option</span>
        <span class="text-sm font-bold text-slate-200 tracking-tight">EURUSD</span>
    </div>
    <!-- Body -->
    <div class="flex-1 flex">
        <!-- Left: Info -->
        <div class="flex-1 bg-[#0f1114] p-5 flex flex-col justify-between border-r border-[#1e293b]">
            <span class="text-[10px] text-slate-500 font-bold tracking-wider">CLICK TO RFQ (F9)</span>
            <span class="text-xl text-slate-700 font-bold font-mono tracking-tighter">NO PRICE</span>
        </div>
        <!-- Right: Button Area (Midnight Blue) -->
        <div class="w-1/2 bg-[#172033] flex flex-col justify-center items-center p-4 hover:bg-[#1e2536] transition shadow-inner">
             <span class="text-xs text-blue-400/50 font-bold mb-0.5 tracking-wider">START</span>
             <span class="text-6xl font-bold text-white tracking-tighter opacity-80 group-hover:opacity-100 transition-opacity">RFQ</span>
        </div>
    </div>
    <!-- Footer -->
    <div class="bg-[#14161b] py-1 text-center border-t border-[#1e293b]">
        <span class="text-[11px] font-bold text-amber-700 tracking-wide">No Hedge - Live</span>
    </div>
</div>
State B: Active Trading (Aggregated View)
The primary trading view. Features:
Vol-First Hierarchy: Large Vol numbers, Premium/Pips as subtext.
Spread Indicator: Floating badge in the center.
Ladder: Highlights the Best Bid/Offer source.
code
Html
<!-- MAIN EXECUTION PANEL -->
<div class="flex flex-col h-full">
    
    <!-- TILES CONTAINER -->
    <div class="flex h-48 w-full mb-2 gap-0.5 relative">
        
        <!-- SPREAD INDICATOR (Center) -->
        <div class="absolute left-1/2 top-1/2 transform -translate-x-1/2 -translate-y-1/2 z-20 pointer-events-none">
            <div class="bg-[#0f1114] border border-slate-700 shadow-xl rounded px-2.5 py-1.5 text-center min-w-[44px] flex items-center justify-center">
                <span class="block text-sm font-bold text-white font-mono leading-none">0.35</span>
            </div>
        </div>

        <!-- BID SIDE (Left Aligned Label) -->
        <div class="flex-1 bg-tile-base hover-dark-mode rounded-l-lg p-4 flex flex-col relative overflow-hidden group cursor-pointer">
            <div class="flex justify-start items-start w-full absolute top-4 left-0 px-4">
                <span class="text-xs font-bold text-red-900 transition-colors group-hover:text-red-300/80 tracking-widest">BID (VOL)</span>
            </div>
            <div class="flex-1 flex flex-col justify-center items-center mt-2">
                <div class="text-red-500/90 group-hover:text-white transition-colors duration-200 text-7xl font-bold tracking-tighter mono">5.47</div>
                <div class="text-sm font-bold text-slate-600 mt-2 font-mono tracking-wide group-hover:text-slate-400 transition-colors">68,778 USD <span class="opacity-50 mx-1">|</span> -43p</div>
            </div>
            <div class="absolute bottom-4 left-4"><span class="text-[10px] bg-red-900/10 text-red-800 px-1.5 py-0.5 rounded group-hover:bg-blue-900/30 group-hover:text-blue-300 transition-all">JPM</span></div>
        </div>
        
        <!-- OFFER SIDE (Right Aligned Label) -->
        <div class="flex-1 bg-tile-base hover-dark-mode rounded-r-lg p-4 flex flex-col relative overflow-hidden group cursor-pointer">
            <div class="flex justify-end items-start w-full absolute top-4 left-0 px-4">
                <span class="text-xs font-bold text-blue-900 transition-colors group-hover:text-blue-300/80 tracking-widest">OFFER (VOL)</span>
            </div>
            <div class="flex-1 flex flex-col justify-center items-center mt-2">
                <div class="text-blue-500/90 group-hover:text-white transition-colors duration-200 text-7xl font-bold tracking-tighter mono">5.82</div>
                <div class="text-sm font-bold text-slate-600 mt-2 font-mono tracking-wide group-hover:text-slate-400 transition-colors">71,699 USD <span class="opacity-50 mx-1">|</span> +44p</div>
            </div>
            <div class="absolute bottom-4 right-4"><span class="text-[10px] bg-blue-900/10 text-blue-800 px-1.5 py-0.5 rounded group-hover:bg-blue-900/30 group-hover:text-blue-300 transition-all">DEUT</span></div>
        </div>
    </div>

    <!-- LADDER (Venue Stack) -->
    <div class="flex-1 bg-[#14161b] border border-[#1e293b] rounded-lg flex flex-col overflow-hidden">
        <div class="grid grid-cols-[1fr_80px_1fr] bg-[#1a1d23] border-b border-[#1e293b] items-center py-1.5">
            <div class="text-center text-[10px] text-slate-600 font-bold uppercase tracking-wider">Bid Vol</div>
            <div class="text-center text-[10px] text-slate-600 font-bold uppercase tracking-wider border-l border-r border-[#1e293b] h-full flex items-center justify-center">Venue</div>
            <div class="text-center text-[10px] text-slate-600 font-bold uppercase tracking-wider">Offer Vol</div>
        </div>
        <div class="overflow-y-auto flex-1">
            <!-- Row 1: Best Bid (JPM) -->
            <div class="ladder-row grid grid-cols-[1fr_80px_1fr] border-b border-[#1e293b]/50 items-stretch h-14 group">
                <div class="flex flex-col justify-center items-center best-bid-cell">
                    <div class="text-white font-bold mono text-lg group-hover:text-white transition-colors">5.47</div>
                    <div class="text-slate-500 font-medium mono text-[10px] mt-0.5">68k | -43p</div>
                </div>
                <div class="flex items-center justify-center font-bold text-yellow-600/90 text-xs border-l border-r border-[#1e293b] ladder-col-mid">JPM</div>
                <div class="flex flex-col justify-center items-center opacity-50">
                    <div class="text-blue-900/70 font-bold mono text-lg">5.85</div>
                    <div class="text-slate-700 font-medium mono text-[10px] mt-0.5">71k | -46p</div>
                </div>
            </div>
             <!-- Row 2: Best Offer (DEUT) -->
             <div class="ladder-row grid grid-cols-[1fr_80px_1fr] border-b border-[#1e293b]/50 items-stretch h-14 group">
                <div class="flex flex-col justify-center items-center opacity-50">
                    <div class="text-red-900/70 font-medium mono text-lg">5.44</div>
                    <div class="text-slate-700 font-medium mono text-[10px] mt-0.5">68k | -40p</div>
                </div>
                <div class="flex items-center justify-center font-semibold text-slate-500 text-xs border-l border-r border-[#1e293b] ladder-col-mid">DEUT</div>
                <div class="flex flex-col justify-center items-center best-offer-cell">
                    <div class="text-white font-bold mono text-lg group-hover:text-white transition-colors">5.82</div>
                    <div class="text-slate-500 font-medium mono text-[10px] mt-0.5">71k | +44p</div>
                </div>
            </div>
        </div>
    </div>
</div>
3. Right Panel: Structuring Ticket
This uses a "Property Grid" layout for high data density and quick entry. Includes custom dropdowns for "Delta Exchange" and "Premium Due".
code
Html
<div class="w-[380px] bg-[#14161b] border border-[#1e293b] rounded-lg flex flex-col h-full shadow-2xl text-sm">
    <!-- Header -->
    <div class="flex justify-between items-center bg-[#1e293b] px-3 py-1.5 border-b border-black">
        <div class="flex items-center gap-1 opacity-50">
            <span class="text-[10px]">≫ Global</span>
        </div>
        <div class="flex items-center gap-2">
            <span class="text-xs font-bold text-white">Leg 1</span>
            <span class="text-slate-400 hover:text-white cursor-pointer">+</span>
            <span class="text-slate-400 hover:text-white cursor-pointer">✕</span>
        </div>
    </div>

    <!-- Properties -->
    <div class="overflow-y-auto flex-1 bg-[#0f1114]">
        
        <!-- Editable Row Example -->
        <div class="ticket-row">
            <div class="ticket-label">Quantity</div>
            <div class="ticket-value flex-nowrap w-full">
                <div class="flex items-center gap-2 cursor-pointer mr-2 flex-shrink-0">
                    <span class="font-bold text-white">EUR</span>
                    <span class="text-slate-500 text-xs">⇄</span>
                </div>
                <input type="text" class="flex-1 bg-transparent text-right outline-none font-bold mono text-white min-w-0" value="10,000,000">
            </div>
        </div>

        <!-- Dropdown Example: Price Display -->
        <div class="ticket-row z-50">
            <div class="ticket-label">Price Display</div>
            <div class="ticket-value justify-between dropdown-value group relative">
                <span class="font-bold text-blue-400 group-hover:text-blue-300">VOL</span>
                <span class="text-slate-500 text-[10px]">▼</span>
                <!-- Hover Menu -->
                <div class="hidden group-hover:block absolute top-full left-0 w-full bg-[#16181d] border border-[#1e293b] shadow-xl z-50">
                    <div class="px-2 py-1.5 hover:bg-[#1e293b] text-white flex justify-between bg-blue-500/10 border-l-2 border-blue-500">
                        <span>VOL</span><span>✓</span>
                    </div>
                    <div class="px-2 py-1.5 hover:bg-[#1e293b] text-slate-400 border-l-2 border-transparent">EUR Pips</div>
                </div>
            </div>
        </div>

        <!-- Read Only Section Example -->
        <div class="section-header">
            <div class="arrow-down transform -rotate-90"></div>
            Market Data
        </div>
        <div class="ticket-row">
            <div class="ticket-label">Spot</div>
            <div class="ticket-value justify-end font-mono text-slate-300">1.17257 / 1.17261</div>
        </div>
    </div>
</div>
4. Implementation Guide (Visual Studio 2022)
To get this pixel-perfect look in a Windows Desktop App, use .NET MAUI Blazor Hybrid.
Step 1: Project Setup
Open Visual Studio 2022.
Create a new project: .NET MAUI Blazor Hybrid App.
Target Framework: .NET 8.0.
Step 2: Install Fonts & Tailwind
Download Inter and JetBrains Mono fonts.
Place them in wwwroot/css/fonts/.
In wwwroot/index.html, add the Tailwind CDN (for dev) inside <head>:
code
Html
<script src="https://cdn.tailwindcss.com"></script>
Step 3: Add the CSS
Create a file wwwroot/css/app.css (or append to the existing one) with these core styles:
code
CSS
/* Custom Scrollbar */
::-webkit-scrollbar { width: 4px; }
::-webkit-scrollbar-track { background: #0f1115; }
::-webkit-scrollbar-thumb { background: #334155; border-radius: 2px; }

/* Tile Styling */
.bg-tile-base { 
    background: linear-gradient(to bottom, #16181d, #0f1114); 
    transition: background 0.2s ease; 
}
.hover-dark-mode:hover { 
    background: linear-gradient(to bottom, #1e2536, #11151f) !important; 
}

/* Grid Ticket Styling */
.ticket-row {
    display: grid;
    grid-template-columns: 110px 1fr;
    border-bottom: 1px solid #1e293b;
    font-size: 11px;
    height: 28px;
    position: relative;
}
.ticket-label {
    background-color: #16181d;
    color: #64748b; 
    padding: 0 8px;
    text-align: right;
    display: flex;
    align-items: center;
    justify-content: flex-end;
    font-weight: 500;
}
.ticket-value {
    background-color: #0f1114;
    color: #e2e8f0;
    padding: 0 8px;
    display: flex;
    align-items: center;
    white-space: nowrap;
}
.ticket-value:hover { background-color: #1a1d23; }

/* Best Price Markers */
.best-bid-cell { 
    background: linear-gradient(to right, rgba(127, 29, 29, 0.15), transparent); 
    border-left: 2px solid #ef4444; 
}
.best-offer-cell { 
    background: linear-gradient(to left, rgba(30, 58, 138, 0.2), transparent); 
    border-right: 2px solid #3b82f6; 
}

/* Accordion Arrow */
.arrow-down {
    width: 0; height: 0; 
    border-left: 4px solid transparent;
    border-right: 4px solid transparent;
    border-top: 5px solid #cbd5e1;
}
Step 4: Create the Component
Go to Components/Pages/Home.razor.
Paste the HTML structure from the sections above.
Use C# @code blocks to handle logic (switching between RFQ/Active states, updating values).
code
C#
@code {
    private bool IsActive = false; // Toggles between State A and State B
    private string PriceDisplay = "VOL";
    
    private void StartRFQ() {
        IsActive = true;
    }
}

5. Responsive & Layout Rules
Since this is a desktop application (Blazor Hybrid), windows will be resized frequently to fit alongside charts or other aggregators.
Global Constraints
Minimum Window Dimensions: 820px (Width) x 600px (Height).
Scroll Behavior:
Vertical: Only the Ladder (Left Panel) and the Properties Grid (Right Panel) should scroll internally. The Header and Execution Tiles must remain fixed/visible at all times.
Horizontal: Never scroll. If the window gets too narrow, the Right Panel (Ticket) should collapse or stack (see below).
Layout Logic (CSS Grid)
Use a flexible grid that prioritizes the Execution Panel.
code
CSS
.main-layout {
    display: grid;
    /* Left Panel takes remaining space, Right Panel fixed width */
    grid-template-columns: 1fr 380px; 
    gap: 8px;
    height: 100vh;
}

/* BREAKPOINT: Narrow Mode (< 900px) */
@media (max-width: 900px) {
    .main-layout {
        /* Stack vertically or hide ticket via toggle */
        grid-template-columns: 1fr; 
    }
    .right-panel {
        display: none; /* Or create a toggle button in header to slide it in */
    }
}
6. Error & State Handling
Markets disconnect, prices go stale, and banks reject orders. The UI must communicate this without breaking the layout.
A. Stale Pricing (No Data > 3s)
If a venue stops streaming, do not hide it. "Greying it out" implies it is disabled. Instead, use a Warning State.
Visual: Dim opacity to 50% and change the border to Amber.
Label: Append a [STALE] tag to the venue name.
code
Html
<!-- Stale Venue Row -->
<div class="ladder-row ... opacity-60 border-l-2 border-amber-600">
    <div class="text-amber-500/50 ...">5.47</div> <!-- Dimmed values -->
    <div class="text-amber-500 font-bold ...">JPM (STALE)</div>
    <div class="text-amber-500/50 ...">5.82</div>
</div>
B. Connection Loss (Global)
If the application loses connection to the pricing engine.
Visual: A thin, non-dismissible banner immediately below the Header.
Color: Red background (bg-red-900/80), White text.
Action: Disable the "Execute" click events on the main tiles.
code
Html
<div class="w-full bg-red-900/90 text-white text-[10px] font-bold text-center py-1 tracking-widest uppercase animate-pulse">
    Connection Lost - Attempting Reconnect...
</div>
C. Order Rejection
If a trade is rejected after clicking.
Visual: The specific tile flashes Red, and the inner text changes temporarily.
Duration: 3 seconds, then reverts to live price.
code
Html
<!-- Rejected Tile State -->
<div class="bg-red-950 border border-red-600 ...">
    <div class="text-red-500 font-bold text-xl">REJECTED</div>
    <div class="text-red-400 text-xs">Credit Limit Exceeded</div>
</div>
7. Loading States
What happens between clicking "Start RFQ" and seeing numbers?
The "Shimmer" Effect
Do not use a spinning circle (it looks like a web page). Use a Skeleton Shimmer to imply data is populating.
Logic: When IsActive = true but Price == null.
Animation: A gradient moving left-to-right.
code
Html
<!-- Skeleton Tile -->
<div class="flex-1 bg-[#16181d] rounded-lg p-4 relative overflow-hidden">
    <!-- Shimmer Overlay -->
    <div class="absolute inset-0 bg-gradient-to-r from-transparent via-white/5 to-transparent skew-x-12 animate-shimmer"></div>
    
    <!-- Fake Data Blocks -->
    <div class="h-3 w-12 bg-slate-800 rounded mb-4"></div> <!-- Header -->
    <div class="h-12 w-32 bg-slate-800 rounded mx-auto"></div> <!-- Price -->
</div>

<style>
@keyframes shimmer {
    0% { transform: translateX(-100%); }
    100% { transform: translateX(100%); }
}
.animate-shimmer {
    animation: shimmer 1.5s infinite linear;
}
</style>
8. Real-Time Update Patterns (C# Logic)
Since this is Blazor Hybrid, we need to manage how often the UI repaints to prevent freezing.
A. Throttling (UI Rate Limit)
Markets can tick 100 times a second. The human eye cannot see that.
Rule: Limit UI updates to 30fps (approx. every 33ms).
Implementation: Use a System.Threading.Timer or a Throttle method in your ViewModel to batch incoming tick updates before pushing them to the LiquidityVenues list.
B. Debouncing Inputs
For the Quantity and Strike inputs in the Right Panel:
Problem: If the user types "10,000,000", you don't want to re-calculate Greeks on every keystroke (1, 10, 100...).
Rule: Wait 300ms after the user stops typing before triggering the Re-Calc event.
C. Flash Updates
When a new price arrives that is different from the old price:
Price Up: Flash text Green (text-emerald-400) for 200ms.
Price Down: Flash text Red (text-rose-400) for 200ms.
Return: Revert to standard White (text-white).
This creates the "living" feel of a professional terminal.