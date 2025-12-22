# Inter Font Installation

## Quick Setup

1. **Download Inter Font**
   - Visit: https://fonts.google.com/specimen/Inter
   - Click "Download family" button
   - OR download from: https://github.com/rsms/inter/releases/latest

2. **Extract Font Files**
   - Unzip the downloaded file
   - Navigate to the `/static` or `/ttf` folder
   - You need: `Inter-Bold.ttf` (minimum)
   - Optional: `Inter-Regular.ttf`, `Inter-Medium.ttf`, `Inter-SemiBold.ttf`

3. **Copy to Project**
   - Copy `Inter-Bold.ttf` to this folder (`Assets/Fonts/`)
   - The file should be at: `FXOAiTranslate.WPF/Assets/Fonts/Inter-Bold.ttf`

4. **Build Action (Important!)**
   - Right-click `Inter-Bold.ttf` in Visual Studio Solution Explorer
   - Properties → Build Action → **Resource**
   - Copy to Output Directory → **Do not copy**

## Verification

After adding the font file, rebuild the project. The hero numbers should display in Inter font, matching the HTML design.

## Font Reference in XAML

The font is referenced as:
```xaml
FontFamily="./Assets/Fonts/#Inter"
```

## Fallback

If the font file is not found, it will fall back to Segoe UI.
