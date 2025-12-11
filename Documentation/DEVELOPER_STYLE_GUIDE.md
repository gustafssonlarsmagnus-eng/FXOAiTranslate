# 📘 Developer Style Guide: FX Option Aggregator UI

**Project:** FXOAiTranslate
**Target Framework:** .NET 6 / .NET 8 (WPF)
**Theme:** Dark Mode (Bloomberg/Institutional Style)
**Visual Style:** Hybrid Aggregator (Massive Top Buttons + Data Grid)

---

## 1. Color Palette (Resource Dictionary)

Define these in `App.xaml` or a separate `Colors.xaml` resource dictionary.
*Note: WPF Hex codes use **#AARRGGBB** (Alpha, Red, Green, Blue).*

### **Backgrounds**
| Resource Key | Hex Code | Usage |
| :--- | :--- | :--- |
| `Brush.AppBackground` | `#FF0E0E0E` | Main Window background (Deep Black) |
| `Brush.PanelBackground` | `#FF1E1E1E` | Card/Panel backgrounds |
| `Brush.HeaderBackground` | `#FF111111` | Grid headers and title bars |
| `Brush.Border` | `#FF333333` | Thin dividers between panels |

### **Accents & Data**
| Resource Key | Hex Code | Usage |
| :--- | :--- | :--- |
| `Brush.TealMain` | `#FF00876C` | **Brand Color**. Used for "Buy" side and Top of Book. |
| `Brush.TealDark` | `#FF004D3D` | Darker end of the gradient for Buy buttons. |
| `Brush.RedMain` | `#FF870020` | "Sell" side active state. |
| `Brush.Gold` | `#FFFFCC00` | **Winner Highlight**. Used for Bank Name when they have best price. |
| `Brush.WinnerHighlight` | `#1900876C` | **10% Opacity Teal**. Background for the winning row in the grid. |

### **Typography Colors**
| Resource Key | Hex Code | Usage |
| :--- | :--- | :--- |
| `Brush.TextMain` | `#FFE0E0E0` | Primary Data (Premium prices). |
| `Brush.TextSub` | `#FF888888` | Labels (Vol, Delta, Spot Ref). |
| `Brush.TextDim` | `#FF555555` | Watermarks or inactive elements. |

---

## 2. Typography & Formatting

**Font Family:** `Segoe UI` (Standard) for UI, `Consolas` or `Roboto Mono` for Grid Numbers.

### **The "Big Price" Logic (Top Panels)**
Option prices must display the **Premium** (Cash) primarily, and **Volatility** secondarily.

*   **Premium:** `FontWeight="ExtraBold"`, `FontSize="36"`
*   **Volatility:** `FontWeight="SemiBold"`, `FontSize="16"`, Color=`Brush.TextSub`
*   **Bank Tag:** Small, All-Caps, Semi-Transparent background (`#40000000`).

### **The Grid Numbers**
*   **Alignment:** Right-Aligned.
*   **Monospace:** Use a fixed-width font so decimal points align vertically across rows.

---

## 3. UI Components & XAML Implementation

### A. The "Best Execution" Button (Gradient Style)
Do not use a standard Button. Use a `Border` with an `InputBinding` (MouseLeftButtonUp).

**Visual Specs:**
*   **Buy Button:** Linear Gradient from `TealDark` (Top-Left) to `TealMain` (Bottom-Right).
*   **Sell Button:** Dark Gray Gradient -> Red on Hover.
*   **Shadow:** Deep drop shadow to make it pop.

**XAML Snippet:**
```xml
<Border CornerRadius="3" BorderThickness="1" BorderBrush="{StaticResource Brush.TealMain}" Margin="5">
    <Border.Background>
        <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
            <GradientStop Color="#FF004D3D" Offset="0.0" />
            <GradientStop Color="#FF00876C" Offset="1.0" />
        </LinearGradientBrush>
    </Border.Background>
    <Border.Effect>
        <DropShadowEffect Color="Black" Direction="270" ShadowDepth="4" Opacity="0.4" BlurRadius="10"/>
    </Border.Effect>

    <!-- Content Grid -->
    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/> <!-- Header: Bank Name + Direction -->
            <RowDefinition Height="*"/>    <!-- Big Premium -->
            <RowDefinition Height="Auto"/> <!-- Volatility -->
        </Grid.RowDefinitions>

        <!-- Bank Tag -->
        <Border Background="#40000000" HorizontalAlignment="Right" CornerRadius="2" Padding="4,1">
            <TextBlock Text="JPM" FontSize="10" FontWeight="Bold" Foreground="White"/>
        </Border>

        <TextBlock Text="YOU BUY" Foreground="#AAFFFFFF" FontSize="10" FontWeight="Bold" VerticalAlignment="Center"/>

        <!-- Big Premium -->
        <TextBlock Grid.Row="1" Text="$67,420" Foreground="White" FontSize="36" FontWeight="ExtraBold"
                   VerticalAlignment="Center" HorizontalAlignment="Center" LetterSpacing="-1"/>

        <!-- Volatility -->
        <TextBlock Grid.Row="2" Text="5.25 vol" Foreground="#CCFFFFFF" FontSize="14" HorizontalAlignment="Center"/>
    </Grid>
</Border>
```

### B. Grid Row Highlighting
Winner detection requires the **lowest absolute premium** (most negative = best for client buying protection).

**XAML DataTrigger:**
```xml
<Style TargetType="DataGridRow">
    <Style.Triggers>
        <!-- Best Price Highlight -->
        <DataTrigger Binding="{Binding IsBestPrice}" Value="True">
            <Setter Property="Background" Value="{StaticResource Brush.WinnerHighlight}"/>
            <Setter Property="FontWeight" Value="Bold"/>
        </DataTrigger>
    </Style.Triggers>
</Style>

<!-- Bank Name Highlight -->
<Style TargetType="TextBlock" x:Key="BankNameStyle">
    <Style.Triggers>
        <DataTrigger Binding="{Binding IsBestPrice}" Value="True">
            <Setter Property="Foreground" Value="{StaticResource Brush.Gold}"/>
            <Setter Property="FontWeight" Value="Bold"/>
        </DataTrigger>
    </Style.Triggers>
</Style>
```

---

## 4. Layout Structure

### **Top Panel: Best Execution Tiles**
*   **2 Large Buttons Side-by-Side:**
    *   **Left:** BUY (Client perspective - receiving premium)
    *   **Right:** SELL (Client perspective - paying premium)
*   **Height:** At least 120-140px to accommodate 36pt font.
*   **Spacing:** 10px gap between buttons.

### **Middle Panel: Data Grid**
*   **Columns (Left to Right):**
    1. Bank Name (Left-aligned)
    2. Bid Vol (%)
    3. Bid Premium (Currency, Bold)
    4. Mid Vol (%)
    5. Offer Vol (%)
    6. Offer Premium (Currency, Bold)
    7. Delta (%)
    8. Spot Reference

*   **Styling:**
    *   Alternating row background: `#FF1A1A1A` / `#FF1E1E1E`
    *   Column headers: Bold, ALL CAPS, 11pt, `Brush.TextSub`
    *   Grid lines: `Brush.Border`

### **Bottom Panel: Trade Parameters**
*   Trade ID, Structure Type, Expiry, Notional
*   **Layout:** Horizontal StackPanel or Grid with equal columns
*   **Font:** 10pt, `Brush.TextSub`

---

## 5. Hover & Active States

### **Button Hover (Top Panels)**
*   **Brightness Increase:** Multiply gradient colors by 1.15
*   **Glow Effect:** Add a subtle `OuterGlowBitmapEffect` in Teal
*   **Cursor:** `Hand` pointer

**XAML Trigger:**
```xml
<Border.Style>
    <Style TargetType="Border">
        <Style.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Effect">
                    <Setter.Value>
                        <DropShadowEffect Color="#FF00876C" ShadowDepth="0" BlurRadius="15" Opacity="0.6"/>
                    </Setter.Value>
                </Setter>
            </Trigger>
        </Style.Triggers>
    </Style>
</Border.Style>
```

### **Grid Row Hover**
*   Background becomes 10% lighter: `#FF252525`
*   Subtle left border pulse: 3px Teal accent

---

## 6. Number Formatting Rules

### **Premium (Currency Values)**
*   **Format:** `{0:N0}` (e.g., `67,420`)
*   **Currency Symbol:** Prefix with `$`, `€`, or appropriate currency
*   **Color:** `Brush.TextMain` (White)
*   **Negative Values:** Show in **Red** (`#FFFF4444`) with minus sign

### **Volatility (%)**
*   **Format:** `{0:F2}` (e.g., `5.25`)
*   **Suffix:** ` vol` or `%`
*   **Color:** `Brush.TextSub` (Gray)

### **Delta (%)**
*   **Format:** `{0:F1}` (e.g., `25.3`)
*   **Suffix:** `Δ` symbol
*   **Color:** Green (`#FF28A745`) if > 0, Red if < 0

### **Spot Reference**
*   **Format:** `{0:F4}` (e.g., `1.0895`)
*   **Color:** `Brush.TextSub`

---

## 7. Animation & Transitions

### **Price Updates (Flashing)**
When a price changes, briefly flash the cell background:
*   **Color:** Teal (`#FF00876C`)
*   **Duration:** 300ms fade-out
*   **Easing:** `QuadraticEase EaseOut`

**XAML Storyboard:**
```xml
<Storyboard x:Key="FlashAnimation">
    <ColorAnimation Storyboard.TargetProperty="Background.Color"
                    From="#FF00876C" To="#FF1E1E1E" Duration="0:0:0.3">
        <ColorAnimation.EasingFunction>
            <QuadraticEase EasingMode="EaseOut"/>
        </ColorAnimation.EasingFunction>
    </ColorAnimation>
</Storyboard>
```

### **Panel Slide-In (On Load)**
*   **Top Panels:** Slide from left (-200px → 0px), 400ms
*   **Grid:** Fade in (Opacity 0 → 1), 300ms delay

---

## 8. Accessibility & Usability

### **Contrast Ratios**
*   Text on Dark Background: **Minimum 7:1** (WCAG AAA)
*   Use `#FFE0E0E0` for primary text, `#FF888888` for secondary

### **Keyboard Navigation**
*   Top buttons: `Tab` to focus, `Enter` to execute
*   Grid: Arrow keys to navigate, `Space` to select row

### **Screen Reader Support**
*   All interactive elements must have `AutomationProperties.Name`
*   Example: `AutomationProperties.Name="Execute Buy at 67,420 dollars"`

---

## 9. Code Quality Standards

### **Data Binding**
*   Use `INotifyPropertyChanged` for all ViewModel properties
*   Bind to **Premiums first**, then Volatility
*   Never hard-code prices in XAML

### **Winner Detection Logic**
```csharp
public class QuoteViewModel : INotifyPropertyChanged
{
    private bool _isBestPrice;

    public bool IsBestPrice
    {
        get => _isBestPrice;
        set
        {
            if (_isBestPrice != value)
            {
                _isBestPrice = value;
                OnPropertyChanged(nameof(IsBestPrice));
            }
        }
    }

    // In the aggregator view model:
    public void UpdateBestPrice()
    {
        var bestQuote = Quotes
            .Where(q => q.BidPremium < 0) // Only negative premiums (client receives)
            .OrderBy(q => Math.Abs(q.BidPremium)) // Closest to zero = best
            .FirstOrDefault();

        foreach (var quote in Quotes)
        {
            quote.IsBestPrice = (quote == bestQuote);
        }
    }
}
```

### **Performance Considerations**
*   Use `VirtualizingStackPanel` for grids with >20 rows
*   Freeze `SolidColorBrush` resources: `brush.Freeze();`
*   Debounce price updates to max 10 updates/second

---

## 10. File Organization

```
/Styles
    ├── Colors.xaml          (Brush resources)
    ├── Typography.xaml      (Font styles)
    ├── ButtonStyles.xaml    (Gradient buttons)
    └── GridStyles.xaml      (DataGrid row/column styles)

/Views
    ├── MultiLPQuoteWindow.xaml
    └── (Other views)

/ViewModels
    ├── MultiLPQuoteViewModel.cs
    ├── QuoteRowViewModel.cs
    └── (Other VMs)
```

### **Resource Dictionary Merging (App.xaml)**
```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Styles/Colors.xaml"/>
            <ResourceDictionary Source="Styles/Typography.xaml"/>
            <ResourceDictionary Source="Styles/ButtonStyles.xaml"/>
            <ResourceDictionary Source="Styles/GridStyles.xaml"/>
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

---

## 11. Testing Checklist

Before committing UI changes, verify:
- [ ] All colors reference `StaticResource` (no hard-coded hex)
- [ ] Premium values display with correct sign (negative = good for client)
- [ ] Winner highlighting works with simulated quote updates
- [ ] Hover states work on all interactive elements
- [ ] Font sizes are consistent (no random `FontSize="13"`)
- [ ] Grid scrolls smoothly with 100+ rows
- [ ] Dark mode looks good on both 100% and 150% DPI scaling

---

## 12. Example: Complete Top Panel XAML

```xml
<Grid Grid.Row="0" Height="140" Margin="10">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="10"/> <!-- Spacer -->
        <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>

    <!-- BUY PANEL -->
    <Border Grid.Column="0" CornerRadius="3" BorderThickness="1"
            BorderBrush="{StaticResource Brush.TealMain}" Cursor="Hand">
        <Border.Background>
            <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                <GradientStop Color="#FF004D3D" Offset="0.0"/>
                <GradientStop Color="#FF00876C" Offset="1.0"/>
            </LinearGradientBrush>
        </Border.Background>
        <Border.Effect>
            <DropShadowEffect Color="Black" Direction="270" ShadowDepth="4" Opacity="0.4" BlurRadius="10"/>
        </Border.Effect>
        <Border.InputBindings>
            <MouseBinding Gesture="LeftClick" Command="{Binding ExecuteBuyCommand}"/>
        </Border.InputBindings>

        <Grid Margin="12">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <!-- Header -->
            <StackPanel Orientation="Horizontal" HorizontalAlignment="SpaceBetween">
                <TextBlock Text="YOU BUY" Foreground="#AAFFFFFF" FontSize="10" FontWeight="Bold"/>
                <Border Background="#40000000" CornerRadius="2" Padding="4,1">
                    <TextBlock Text="{Binding BestBuyBank}" FontSize="10" FontWeight="Bold" Foreground="White"/>
                </Border>
            </StackPanel>

            <!-- Big Premium -->
            <TextBlock Grid.Row="1" Text="{Binding BestBuyPremium, StringFormat='${0:N0}'}"
                       Foreground="White" FontSize="36" FontWeight="ExtraBold"
                       VerticalAlignment="Center" HorizontalAlignment="Center" LetterSpacing="-1"/>

            <!-- Volatility -->
            <TextBlock Grid.Row="2" Text="{Binding BestBuyVolatility, StringFormat='{}{0:F2} vol'}"
                       Foreground="#CCFFFFFF" FontSize="14" HorizontalAlignment="Center"/>
        </Grid>
    </Border>

    <!-- SELL PANEL (Mirror with Red Gradient) -->
    <Border Grid.Column="2" CornerRadius="3" BorderThickness="1"
            BorderBrush="{StaticResource Brush.RedMain}" Cursor="Hand">
        <!-- (Similar structure with Red gradient) -->
    </Border>
</Grid>
```

---

## 13. Common Pitfalls to Avoid

❌ **DO NOT:**
*   Use `Button` controls for the top execution panels (they don't support gradients well)
*   Hard-code hex colors in XAML (`Background="#FF00876C"`)
*   Display volatility larger than premium
*   Forget to update `IsBestPrice` when new quotes arrive
*   Use proportional fonts for grid numbers (decimals won't align)

✅ **DO:**
*   Use `Border` + `InputBinding` for custom-styled buttons
*   Reference all colors via `StaticResource`
*   Show premium prominently, volatility as metadata
*   Recalculate winner on every quote update
*   Use `Consolas` or `Courier New` for grid numbers

---

## 14. Version History

| Version | Date | Changes |
| :--- | :--- | :--- |
| 1.0 | 2025-12-11 | Initial style guide for Multi-LP Aggregator UI |

---

**Questions?** Contact the UI team or refer to the Figma mockups in `/Documentation/Designs/`.
