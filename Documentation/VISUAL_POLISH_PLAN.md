# Implementation Plan - Port FX Aggregator UI to WPF

Recreating the modern, dark-themed HTML design (`fx_aggregator_fixed_380.html`) using native WPF controls to ensure better integration, performance, and maintainability.

## User Review Required
> [!IMPORTANT]
> This plan involves creating a significant number of new XAML controls. The design will closely mimic the HTML/Tailwind look but may differ slightly in exact pixel rendering due to font rendering differences between browsers and WPF.

## Proposed Changes

### 1. Foundation Resources
Define the specific color palette/styles to match the "Slate" and "Midnight" theme from the HTML.
#### [MODIFY] [Styles.xaml](file:///c:/Users/mgu74/.gemini/antigravity/scratch/FXOAiTranslate/FXOAiTranslate.WPF/Styles/Styles.xaml)
- **DataGrid**: Retemplate to remove headers, disable selection highlight, and mimic HTML table (48px rows, slate text).
- **ScrollBar**: Custom `ControlTemplate` for thin, dark scrollbars.
- **ComboBox**: Custom non-native template.

#### [MODIFY] [Colors.xaml](file:///c:/Users/mgu74/.gemini/antigravity/scratch/FXOAiTranslate/FXOAiTranslate.WPF/Styles/Colors.xaml)
- Update brushes:
    - `BrushBackgroundMain`: `#0b0d10` (was `#1e1e1e`)
    - `BrushBackgroundPanel`: `#14161b` (was `#2d2d30`)
    - `BrushTextPrimary`: `#94a3b8` (Slate-400)
    
### 2. New User Controls
Create modular components to build the UI.

#### [NEW] [ExecutionTile.xaml](file:///c:/Users/mgu74/.gemini/antigravity/scratch/FXOAiTranslate/FXOAiTranslate.WPF/Controls/ExecutionTile.xaml)
- A highly interactive button with "RFQ" state, countdown timer, and price display.
- A highly interactive button with "RFQ" state, countdown timer, and price display.
- **Behavior**:
    - **State 1 (Idle/Stale)**: displaying "RFQ". Click triggers `SendQuoteRequest`.
    - **State 2 (Streaming)**: displaying Price. Click triggers `SendExecution`.
- Visual States for Idle, RFQ Active, Executed.

#### [NEW] [LegDetailRow.xaml](file:///c:/Users/mgu74/.gemini/antigravity/scratch/FXOAiTranslate/FXOAiTranslate.WPF/Controls/LegDetailRow.xaml)
- Simple grid layout: `[Label (Gray background)] [Value (Black background)]`.

#### [NEW] [LadderView.xaml](file:///c:/Users/mgu74/.gemini/antigravity/scratch/FXOAiTranslate/FXOAiTranslate.WPF/Controls/LadderView.xaml)
- A `DataGrid` or `ItemsControl` mimicking the 3-column ladder (You Rec | Venue | You Pay).
- **Columns**:
    1.  **Rec (Bid)**: Volatility (Tag 5678), Premium (Tag 5844/Calc), Pips.
    2.  **Venue**: LP Name (Tag 1/128), Response Time (Now - SendingTime), Checkbox.
    3.  **Pay (Offer)**: Volatility (Tag 5678), Premium (Tag 5844/Calc), Pips.
- **Data Source**: Bind to `FenicsConfig.LiquidityProviders` keys to generate rows dynamically. Use `SimulatedLP` logic for prices.

#### [MODIFY] [LegPanel.xaml](file:///c:/Users/mgu74/.gemini/antigravity/scratch/FXOAiTranslate/FXOAiTranslate.WPF/Controls/LegPanel.xaml)
- **Reorder**:
    1.  **NLP Input**.
    2.  **Execution Tiles** (Prominent, Top).
    3.  **Price Display Toggle**.
    4.  **Option Details** (Expander, "Option 1 >> Global").
    5.  **Market Data** (Expander, New).
    6.  **Risk** (Expander, New).
    7.  **Liquidity Ladder** (Expander).
- **Styling**:
    - **Expanders**: Custom ControlTemplate for "Bar with Arrow" look (Dark bg #212529, Chevron right/down).
    - **Inputs**: Update TextBox style to be slimmer (Height 20-22) with darker background (`BrushBackgroundInput`) and lighter border.
    - **Section Headers**: Use distinct background for expanded sections.

#### [NEW] [DealsPanel.xaml](file:///c:/Users/mgu74/.gemini/antigravity/scratch/FXOAiTranslate/FXOAiTranslate.WPF/Controls/DealsPanel.xaml)
- **Styling**: Update "Trade Blotter" header to uppercase "CONFIRMED DEALS" with distinct background.

### 3. Main Window & Integration
#### [NEW] [FXAggregatorWindow.xaml](file:///c:/Users/mgu74/.gemini/antigravity/scratch/FXOAiTranslate/FXOAiTranslate.WPF/Views/FXAggregatorWindow.xaml)
- **Top Bar**: Search/NLP Input, Account User Display.
- **Main Layout**: Two Columns (Left: Trade/Quote, Right: Deals Panel).
- **Initialization**: Accepts `TradeStructure` (optional) in constructor.
- Horizontal `StackPanel` or `ItemsControl` to hold multiple `LegPanel` instances.
- Docked `DealsPanel` on the right.
- **Logic Porting**:
    - Port `_fixSession` subscription from `GFIQuoteDialog.cs`.
    - Port `InitializeHedgeRate` and `PopulateLegGrid` logic to ViewModel.

#### [MODIFY] [MainForm.cs](file:///c:/Users/mgu74/.gemini/antigravity/scratch/FXOAiTranslate/MainForm.cs)
- **Goal**: Restore "Send to GFI" to existing dialog, redirect "Test Tool" to new WPF Window.
- **Changes**:
    - Update `SendToGFI_Click`: Ensure it opens `GFIQuoteDialog` (restore functionality).
    - Update `BtnTestGFI_Click`: Redirect to open `FXAggregatorWindow` with sample trade.

## Verification Plan

### Manual Verification
1.  **Visual Check**: Build the solution and open `FXAggregatorWindow`.
2.  **Compare**: Place the WPF window side-by-side with the HTML file open in a browser.
3.  **Interaction**:
    - Click "RFQ" -> Verify text changes and timer starts.
    - Click "Execute" -> Verify deal appears in the Deals Panel.
    - Toggle "Ladder" -> Verify animation/visibility.
