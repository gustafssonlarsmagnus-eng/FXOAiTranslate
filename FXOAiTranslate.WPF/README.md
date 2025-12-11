# FXOAiTranslate.WPF

**Modern WPF UI for Multi-LP FX Options Quote Aggregator**

## Overview

This is a WPF (Windows Presentation Foundation) project that provides a professional Bloomberg-style dark mode interface for the FX Options Multi-LP quote aggregator.

## Architecture

- **MVVM Pattern**: Clean separation of View (XAML) and ViewModel (C#)
- **Data Binding**: Two-way binding for real-time updates
- **Resource Dictionaries**: Centralized styles and colors
- **Integration**: Called from Windows Forms MainForm via interop

## Project Structure

```
FXOAiTranslate.WPF/
├── Views/
│   ├── MultiLPQuoteWindow.xaml       # Main quote aggregator window
│   └── MultiLPQuoteWindow.xaml.cs    # Code-behind
├── ViewModels/
│   ├── MultiLPQuoteViewModel.cs      # Main window ViewModel
│   └── QuoteRowViewModel.cs          # Grid row ViewModel
├── Styles/
│   ├── Colors.xaml                   # Color palette (dark theme)
│   ├── ButtonStyles.xaml             # Gradient execution buttons
│   └── GridStyles.xaml               # DataGrid styling
└── README.md
```

## Features

### 🎨 Visual Design
- **Dark Mode Theme**: Bloomberg/institutional style
- **Gradient Buttons**: Teal for BUY, Red for SELL
- **Drop Shadows**: Professional depth effect
- **Winner Highlighting**: Gold bank names, teal row backgrounds
- **Monospace Numbers**: Perfect decimal alignment

### ⚡ Functionality
- **Best Execution Tiles**: Large prominent buttons showing best BID/OFFER
- **Quote Grid**: Real-time comparison of all LP quotes
- **Winner Detection**: Automatically highlights best price
- **Quote Refresh**: Request fresh quotes from all LPs
- **Execution Commands**: Click-to-execute from top panels

## Color Palette

| Element | Color | Usage |
|---------|-------|-------|
| Background | `#0E0E0E` | App background |
| Panel | `#1E1E1E` | Cards/panels |
| Teal | `#00876C` | Buy side, brand color |
| Red | `#870020` | Sell side |
| Gold | `#FFCC00` | Winner bank name |
| Text Main | `#E0E0E0` | Primary text |
| Text Sub | `#888888` | Labels/metadata |

## Integration with Windows Forms

The WPF window is launched from `MainForm.cs`:

```csharp
var tradeStructure = OVMLBridge.ConvertToTradeStructure(ovmlResult);
var wpfWindow = new FXOAiTranslate.WPF.Views.MultiLPQuoteWindow(tradeStructure);
wpfWindow.ShowDialog();
```

## Data Flow

1. User selects trade in Windows Forms blotter
2. Right-click → "Send to GFI Fenics"
3. OVML converted to `TradeStructure`
4. WPF window opened with `TradeStructure` passed to ViewModel
5. ViewModel populates sample quotes (6 LPs)
6. User clicks BUY or SELL to execute
7. Command executes trade logic

## Future Enhancements

- [ ] Real quote integration (currently simulated data)
- [ ] Live quote streaming via FIX protocol
- [ ] Price flash animations on updates
- [ ] More sophisticated winner detection
- [ ] Historical quote comparison
- [ ] Export to Excel functionality

## Style Guide Reference

See `/Documentation/DEVELOPER_STYLE_GUIDE.md` for detailed styling guidelines.

## Requirements

- .NET 8.0 Windows
- Visual Studio 2022
- WPF enabled in project settings

## Dependencies

- `FXOptionsSimulator` - Trade structure definitions
- `CommunityToolkit.Mvvm` - MVVM helpers (optional, using manual INPC)

---

**Version**: 1.0.0
**Created**: 2025-12-11
**Style**: Bloomberg/Institutional Dark Mode
