# Task: Port FX Aggregator HTML UI to WPF

## Foundation
- [ ] Create/Update `Colors.xaml` with custom palette from HTML (Slate, Midnight, Blue, Green, Red)
- [x] Detailed analysis of `fx_aggregator_fixed_380.html` and `GFIQuoteDialog.cs` <!-- id: 0 -->
    - [x] Identify all data fields (Option Details, Market Data, Risk, LPs). <!-- id: 1 -->
    - [x] Map fields to FIX tags in `GFIFIXApplication.cs`. <!-- id: 2 -->
    - [x] Confirm data sourcing strategy (USER/FIX only). <!-- id: 3 -->
- [x] **Phase 1: Styles & Shared Controls** <!-- id: 4 -->
- [x] **Phase 2: Core Components** <!-- id: 7 -->
    - [x] Create `ExecutionTile.xaml` (Bid/Offer interactive tiles). <!-- id: 8 -->
    - [x] Create `LegDetailRow.xaml` (Expandable row for options). <!-- id: 9 -->
- [x] **Phase 3: Panels & Ladder** <!-- id: 10 -->
    - [x] Create `LadderView.xaml` (Collapsible LP grid). <!-- id: 11 -->
    - [x] Create `LegPanel.xaml` (Container for tiles, stats, ladder, and details). <!-- id: 12 -->
    - [x] Create `DealsPanel.xaml` (Trade Blotter). <!-- id: 13 -->
- [ ] **Phase 4: Main Window & Integration** <!-- id: 14 -->
    - [x] Create `FXAggregatorWindow.xaml` (Main Layout). <!-- id: 15 -->
    - [x] Update `MainForm.cs` menu handlers to open new window. <!-- id: 16 -->
    - [x] **Phase 4.5: Stabilization (Runtime Debugging)**
        - [x] Fix `XamlParseException` (Window Background Scope).
        - [x] Fix Missing Resources (`BrushAccentBlue`, `ExecutionButtonBorder`).
        - [x] Fix Type Mismatch (`Color` vs `Brush` for Foreground).
        - [x] Fix Style TargetType Mismatches (`Border` vs `Button`).
        - [x] Verify `InverseBooleanToVisibilityConverter` fix.
    - [x] **Phase 4.6: Visual Polish & Layout**
        - [x] **Reorder LegPanel**: Priority: Input -> Tiles -> Ladder -> Details.
        - [x] **Styling**: "YOU REC"/"YOU PAY" colored headers on tiles.
        - [x] **Gradients**: Implement Best Bid/Offer blue gradients in Ladder.
        - [x] **Market Data/Risk**: Wrap in Expanders and collapse by default.
        - [x] **Refine Inputs**: Slimmer, darker styling for "Option Details" fields.
    - [x] **Phase 4.7: Verification**
        - [x] Smoke Test: `FXOAiTranslate.Tests` passes (4/4 tests).
        - [x] Runtime Check: `FXAggregatorWindow` instantiates without XAML exceptions.
    - [/] **Phase 4.8: HTML Fidelity Polish (In Progress)**
        - [x] **Palette**: Updated Colors.xaml to match HTML Tailwind/Slate hex codes.
        - [x] **Fonts**: Embedded Inter font (static TTF files), exact sizes from HTML.
        - [x] **Gradients**: Subtle navy rgba(30,58,138,0.2) for best bid/offer.
        - [x] **ExecutionTile**: Redesigned with 192px height, 72px price font.
        - [ ] **DataGrid**: Need to further refine LadderView row styling.
        - [ ] **ScrollBars**: Custom thin dark scrollbar (defined but needs testing).
        - [ ] **Spread Indicator**: Missing center badge between tiles.
        - [ ] **ComboBox/Dropdown**: Needs custom dark theme template.
        - [ ] **Overall Layout**: May need spacing/margin adjustments.
    - [ ] **Phase 5: Logic & Binding** <!-- id: 17 -->
        - [ ] Create `FXAggregatorViewModel` and sub-viewmodels. <!-- id: 18 -->
        - [ ] Port `OVMLBridge` and FIX logic. <!-- id: 19 -->
    - **Logic**: Use `FenicsConfig.LiquidityProviders` to populate the `LadderView` dynamically.
- [ ] Create `DealsPanel.xaml` (Collapsible right panel)

## Main Window
- [ ] Create `FXAggregatorWindow.xaml` (Main layout container)
- [ ] Implement `FXAggregatorViewModel` to orchestrate leg panels and deals (Port logic from `GFIQuoteDialog.cs`)

## Integration
- [ ] Bind `ExecutionTile` to RFQ/Execute commands
- [ ] Connect `LadderView` to live market data (Reference `GFIQuoteDialog.OnQuoteReceivedFromFIX`)
- [ ] Connect `DealsPanel` to trade execution events
