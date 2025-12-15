# CLAUDE.md - AI Assistant Guide for FXO AI Translator

**Last Updated**: 2025-11-27
**Target Audience**: AI assistants working on this codebase
**Project**: FX Options AI Translator with Bloomberg & GFI Integration

---

## Table of Contents

1. [Project Overview](#project-overview)
2. [Architecture & Design Patterns](#architecture--design-patterns)
3. [Project Structure](#project-structure)
4. [Key Components](#key-components)
5. [Development Workflows](#development-workflows)
6. [Coding Conventions](#coding-conventions)
7. [Integration Points](#integration-points)
8. [Common Tasks](#common-tasks)
9. [Critical Knowledge](#critical-knowledge)
10. [Testing & Debugging](#testing--debugging)
11. [Git Workflow](#git-workflow)

---

## Project Overview

### Purpose
FXO AI Translator is a sophisticated Windows Forms application that converts natural language FX options trade requests into structured formats (OVML for Bloomberg Terminal and UBS format) and integrates with GFI Fenics via FIX protocol for trade execution.

### Technology Stack
- **.NET 8.0** (Windows Forms)
- **Bloomberg Desktop API** (`Bloomberglp.Blpapi.dll`)
- **QuickFix.Net** (FIX 4.4 protocol)
- **OpenAI API** (GPT-4o-mini for NLP)
- **QLNet** (Quantitative finance library)

### Key Capabilities
- **Natural Language Parsing**: English, Swedish, Norwegian
- **3-Tier Parsing Strategy**: Regex → Learned Patterns → AI Fallback
- **Live Market Data**: Bloomberg API spot rates
- **Trade Automation**: Auto-send OVML to Bloomberg Terminal
- **FIX Integration**: Quote requests and execution via GFI Fenics
- **Pattern Learning**: System learns from successful AI parses

---

## Architecture & Design Patterns

### Overall Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     User Interface                          │
│  MainForm.cs - Trade Blotter, Speech Input, Clipboard       │
└──────────────────────┬──────────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────┐
│                  Parsing Engine                             │
│  TradeParser.cs (Orchestrator)                              │
│  ├─ RegexTradePatterns.cs (20+ patterns)                    │
│  ├─ OpenAIService.cs (AI + Pattern Learning)                │
│  └─ TradeSanityChecker.cs (Validation)                      │
└───────┬─────────────────────────────────┬──────────────────┘
        │                                 │
┌───────▼──────────┐          ┌──────────▼─────────────────┐
│  Bloomberg API   │          │  FXOptionsSimulator        │
│  (Market Data &  │          │  (GFI FIX Integration)     │
│  OVML Execution) │          │  - TradeStructure          │
└──────────────────┘          │  - GFIFIXSessionManager    │
                              │  - RawFIXMessageBuilder    │
                              │  - TradeBlotter            │
                              └────────────────────────────┘
```

### Design Patterns Used

1. **Strategy Pattern**: Parsing pipeline (Regex → Learned → AI)
2. **Singleton Pattern**: `TradeBlotter`, `GlobalFIXSession`
3. **Builder Pattern**: `RawFIXMessageBuilder` for FIX messages
4. **Observer Pattern**: FIX message handlers, UI events
5. **Factory Pattern**: Pattern matching in `RegexTradePatterns`

### Key Architectural Decisions

1. **Raw FIX Messages**: GFI requires specific field ordering; QuickFix sorts by tag number, so we use raw message construction
2. **Hybrid Intelligence**: Regex for speed, AI for flexibility, learned patterns for optimization
3. **Dual Output**: Both OVML (Bloomberg) and UBS formats generated simultaneously
4. **Date Policy Canonicalization**: Tag 75 = TODAY, Tag 5020 = T+1 premium settlement
5. **Premium-Based Execution**: Recent commits show preference for premium (Tag 6436) over volatility (Tag 5678)

---

## Project Structure

```
FXOAiTranslate/
├── Core Application (Windows Forms)
│   ├── MainForm.cs              (1,471 lines) - Main UI with trade blotter
│   ├── Program.cs               - Entry point with debug console
│   ├── App.config               - OpenAI API key configuration
│   └── quickfix.cfg             - FIX session configuration
│
├── Parsing Engine
│   ├── TradeParser.cs           (1,386 lines) - Main parsing orchestrator
│   ├── RegexTradePatterns.cs    (302 lines) - 20+ pattern definitions
│   ├── TradeSanityChecker.cs    (394 lines) - OVML validation logic
│   └── OpenAIService.cs         (709 lines) - AI integration + pattern learning
│
├── Market Data Integration
│   └── BloombergService.cs      (293 lines) - Bloomberg Terminal integration
│
├── FXOptionsSimulator/ (Separate Project)
│   ├── TradeStructure.cs        - Trade object models
│   ├── TradeBlotter.cs          - Singleton trade tracker
│   ├── TradeBlotterForm.cs      - Trade blotter UI
│   ├── GFIQuoteDialog.cs        - Quote request/response UI
│   ├── FenicsConfig.cs          - GFI LP configurations
│   ├── OVMLBridge.cs            - OVML ↔ TradeStructure converter
│   ├── FxDateService.cs         - FX date calculations
│   ├── DatePolicy.cs            - Business day conventions
│   └── FIX/
│       ├── GFIFIXSessionManager.cs    - FIX session lifecycle
│       ├── GFIFIXApplication.cs       - FIX message handlers
│       └── RawFIXMessageBuilder.cs    - Raw FIX message construction
│
└── Documentation/
    ├── README.md
    └── BGC__GFI_FIX_Specification_for_FX_v1_4_4.pdf
```

---

## Key Components

### 1. MainForm.cs (1,471 lines)

**Primary Responsibilities:**
- UI management (DataGridView trade blotter)
- Trade processing pipeline with async/await
- Speech recognition integration
- Clipboard monitoring (Ctrl+V paste-anywhere)
- Trade persistence (JSON in AppData)
- Bloomberg auto-send functionality

**Key Methods:**
- `ProcessTradeAsync()` - Main entry point for trade processing
- `LoadTrades()` / `SaveTrades()` - JSON persistence
- `SendToBloomberg()` - Automated OVML command execution
- `UpdateRowColors()` - Color-coded parse method indicators

**UI Features:**
- Color coding: Green=Regex, Yellow=Learned, Blue=AI, Orange=Warning, Red=Error
- Context menu: Copy OVML/UBS, Re-parse with AI, Delete, Send to GFI
- Filter toggle: Today's trades vs All
- Status bar: Bloomberg connection, processing indicator

### 2. TradeParser.cs (1,386 lines)

**Primary Responsibilities:**
- Orchestrates 3-tier parsing strategy
- Date and tenor extraction (multi-language)
- Currency pair detection (24 pairs)
- Spot rate integration
- OVML and UBS format generation
- Result caching

**Parsing Pipeline:**
```
Input → Preprocess → Check Cache → Regex → Learned → AI → Validate → Generate → Cache
```

**Key Methods:**
- `ParseTradeAsync()` - Main orchestrator
- `ExtractCurrencyPair()` - Recognizes 24 CCY pairs
- `ExtractExpiry()` - Multi-format date parsing
- `TryParseWithRegex()` - Pattern matching (20+ patterns)
- `TryParseWithLearnedPatterns()` - Similarity matching (60% threshold)
- `ParseWithAI()` - OpenAI fallback
- `GenerateUBSFormat()` - Convert OVML → UBS

**Multi-Language Support:**
- English: buy, sell, call, put
- Swedish: köpa, sälja
- Norwegian: kjøp, selg

### 3. BloombergService.cs (293 lines)

**Primary Responsibilities:**
- Bloomberg Terminal window detection
- OVML command automation (keyboard simulation)
- Live spot rate fetching via Bloomberg API
- Connection status monitoring

**Key Methods:**
- `SendOVMLAsync()` - Send OVML to Terminal
- `FetchSpotRateAsync()` - Get live spot via API
- `IsBloombergConnected()` - Check bplus.exe process

**Bloomberg Automation:**
1. Find OVML window (exclude browser windows)
2. Activate window
3. Copy OVML to clipboard
4. Send: Ctrl+T (clear) → Ctrl+V (paste) → Enter

### 4. OpenAIService.cs (709 lines)

**Dual Purpose: AI Parser + Pattern Learner**

**AI Integration:**
- Model: GPT-4o-mini (cost-effective)
- Temperature: 0.1 (deterministic)
- Max Tokens: 500
- Comprehensive 314-line system prompt with OVML rules

**Pattern Learning:**
- Stores successful AI parses as learned patterns
- Similarity matching: 60% token overlap threshold
- JSON persistence: `learned_patterns.json`
- Cache for repeated inputs

**Key Methods:**
- `ParseTradeWithAIAsync()` - OpenAI API call
- `TryGetLearnedPattern()` - Similarity search
- `AddLearnedPattern()` - Store successful parse
- `CalculateSimilarity()` - Token-based matching

### 5. TradeSanityChecker.cs (394 lines)

**Validation Logic:**
- OVML structure validation
- Multi-leg consistency checks
- Spread direction validation
- Expiry date validation (no past dates)
- Confidence scoring (0.9 perfect, 0.75 warnings, 0.1 errors)

**Key Methods:**
- `ValidateTrade()` - Main entry point
- `ValidateBasicStructure()` - Format checks
- `ValidateMultiLeg()` - Leg count matching
- `ValidateSpreadDirection()` - Put/Call spread rules
- `CheckExpiryDate()` - Date validation

### 6. RegexTradePatterns.cs (302 lines)

**20+ Regex Patterns:**
1. `Vanilla_CurrencyLed` - "EURNOK 17Dec25 9.85 call 25M"
2. `Collar_BuyCallSellCallSellPut` - 3-leg structure
3. `Seagull_BuyPutSellPutSellCall` - 3-leg protection
4. `PutSpread_DifferentNotionals` - Unequal notionals
5. `CallSpread_DifferentNotionals` - Unequal notionals
6. `RiskReversal_PutCall` / `RiskReversal_CallPut`
7. `Straddle` / `Strangle` patterns
8. `Simple_Vanilla` - Fallback pattern

**Pattern Structure:**
```csharp
public static (string Pattern, string Name, int Priority) PatternName()
{
    return (@"regex_pattern", "PatternName", priority);
}
```

### 7. GFIFIXSessionManager.cs

**FIX Session Lifecycle:**
- Session creation and configuration
- Logon/Logout handling
- Connection monitoring
- Message routing

**Key Methods:**
- `StartSession()` - Initialize FIX connection
- `SendQuoteRequest()` - Request quote from LP
- `SendNewOrderMultileg()` - Execute trade
- `CancelQuote()` - Cancel pending quote

### 8. RawFIXMessageBuilder.cs

**Critical Component for GFI Compliance:**

GFI requires specific field ordering that QuickFix doesn't preserve. This builder constructs raw FIX messages with exact field order.

**Key Methods:**
- `BuildQuoteRequest()` - Construct 35=R message
- `BuildNewOrderMultileg()` - Construct 35=AB message
- `BuildQuoteCancel()` - Construct 35=Z message

**Field Ordering Example:**
```
Tag 75 (TradeDate) MUST come before Tag 5020 (PremiumDelivery)
Tag 6120 (NoMQEntries) MUST precede repeating groups
Repeating groups must maintain strict field order within each block
```

**Date Policy Canonicalization:**
- Tag 75: TODAY (yyyyMMdd format)
- Tag 5020: T+1 premium delivery

### 9. TradeStructure.cs

**Trade Object Model:**
```csharp
public class TradeStructure
{
    public string Underlying { get; set; }           // "EURNOK"
    public DateTime Expiry { get; set; }             // 2025-12-17
    public List<TradeLeg> Legs { get; set; }         // 1-3 legs
    public string StructureType { get; set; }        // "1" (Vanilla), "5" (RR), etc.
    public double SpotReference { get; set; }        // 10.9518
}

public class TradeLeg
{
    public string Direction { get; set; }            // "BUY" / "SELL"
    public string OptionType { get; set; }           // "CALL" / "PUT"
    public double Strike { get; set; }               // 9.8500
    public double NotionalMM { get; set; }           // 25.0
}
```

### 10. OVMLBridge.cs

**OVML ↔ TradeStructure Converter:**
- Parses OVML strings into TradeStructure objects
- Extracts legs, strikes, notionals
- Handles multi-leg structures
- Detects structure types

**Key Methods:**
- `ConvertToTradeStructure()` - Parse OVML → TradeStructure
- `ConvertToOVML()` - TradeStructure → OVML string

---

## Development Workflows

### Adding a New Trade Pattern

1. **Define Pattern in RegexTradePatterns.cs:**
```csharp
public static (string Pattern, string Name, int Priority) MyNewPattern()
{
    string pattern = @"(?<action>buy|sell)\s+(?<notional>[\d.]+)M\s+(?<ccy>[A-Z]{6})\s+(?<strike>[\d.]+)\s+(?<type>call|put)";
    return (pattern, "MyNewPattern", 100); // Higher priority = checked first
}
```

2. **Add to Pattern List in TradeParser.cs:**
```csharp
patterns.Add(RegexTradePatterns.MyNewPattern());
```

3. **Test with Sample Inputs:**
```csharp
var result = await _tradeParser.ParseTradeAsync("buy 10M EURUSD 1.10 call");
Assert.AreEqual("Regex", result.ParseMethod);
```

4. **Add Validation Logic (if needed) in TradeSanityChecker.cs**

### Modifying FIX Message Structure

**⚠️ CRITICAL: Field order matters for GFI**

1. **Read Specification:** `Documentation/BGC__GFI_FIX_Specification_for_FX_v1_4_4.pdf`
2. **Modify RawFIXMessageBuilder.cs:** Add fields in exact order
3. **Test with GFI Test Environment:** Verify message acceptance
4. **Log Full Messages:** Use `Console.WriteLine(rawMessage)` to debug
5. **Verify Tag Values:** Check GFI documentation for valid values

**Example - Adding a New Field:**
```csharp
// WRONG - QuickFix sorts by tag number
message.SetField(new QuickFix.Fields.CustomTag(9999, "value"));

// CORRECT - Raw message with explicit ordering
sb.Append("8=FIX.4.4|9=XXX|35=R|...|9999=value|...|10=XXX|");
```

### Adding Bloomberg Fields

1. **Check Bloomberg API Documentation** for field names
2. **Modify BloombergService.cs:**
```csharp
Element securities = request.GetElement("securities");
securities.AppendValue("EURUSD Curncy");
request.Set("fields", new string[] { "PX_LAST", "NEW_FIELD" });
```
3. **Handle Response:**
```csharp
if (fieldData.HasElement("NEW_FIELD"))
{
    double value = fieldData.GetElementAsFloat64("NEW_FIELD");
}
```

### Extending AI Prompt

1. **Modify OpenAIService.cs** - `BuildSystemPrompt()` method
2. **Add Examples:** Include input/output pairs
3. **Add Rules:** Specify new parsing rules
4. **Test with Edge Cases:** Verify AI understands new requirements
5. **Monitor Token Usage:** Keep prompt under 1000 tokens

---

## Coding Conventions

### Naming Conventions

```csharp
// Classes: PascalCase
public class TradeParser { }

// Methods: PascalCase
public async Task<TradeResult> ParseTradeAsync() { }

// Private fields: _camelCase with underscore
private readonly BloombergService _bloombergService;

// UI controls: Type prefix
private DataGridView dgvTradeBlotter;
private TextBox txtManualInput;
private CheckBox chkAutoSend;

// Constants: UPPER_SNAKE_CASE
private const int MAX_RETRY_ATTEMPTS = 3;

// Local variables: camelCase
string currencyPair = "EURUSD";
```

### Code Organization

```csharp
// Class member order:
1. Constants
2. Static fields
3. Private fields
4. Properties
5. Constructors
6. Public methods
7. Private methods
8. Event handlers
9. Nested classes

// File organization:
- One class per file (except nested classes)
- Filename matches class name
- Using statements at top, grouped and sorted
```

### Async/Await Patterns

```csharp
// Good - Proper async naming and usage
public async Task<TradeResult> ParseTradeAsync(string input)
{
    var result = await _openAIService.ParseTradeWithAIAsync(input);
    return result;
}

// Good - Avoid async void except event handlers
private async void btnParse_Click(object sender, EventArgs e)
{
    try
    {
        await ProcessTradeAsync();
    }
    catch (Exception ex)
    {
        MessageBox.Show(ex.Message);
    }
}
```

### Error Handling

```csharp
// Specific exceptions first, general last
try
{
    await ProcessTradeAsync();
}
catch (BloombergConnectionException ex)
{
    LogError("Bloomberg connection failed", ex);
    ShowUserError("Please ensure Bloomberg Terminal is running");
}
catch (FIXSessionException ex)
{
    LogError("FIX session error", ex);
    ShowUserError("GFI connection lost. Reconnecting...");
}
catch (Exception ex)
{
    LogError("Unexpected error", ex);
    ShowUserError("An unexpected error occurred");
}
```

### Logging Standards

```csharp
// Console logging for debug (Program.cs allocates console)
Console.WriteLine($"[TRACE] {DateTime.Now:HH:mm:ss.fff} | ParseTrade | Input: {input}");

// Color-coded console output
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("SUCCESS: Trade parsed");
Console.ResetColor();

// UI debug log panel
AppendLog($"[{DateTime.Now:HH:mm:ss}] Parsing: {input}");
```

---

## Integration Points

### Bloomberg Terminal Integration

**Prerequisites:**
- Bloomberg Terminal must be running
- Desktop API must be enabled
- `Bloomberglp.Blpapi.dll` referenced

**Connection Detection:**
```csharp
bool isConnected = Process.GetProcessesByName("bplus").Any();
```

**Window Automation:**
1. Find OVML window: `FindWindow(null, "OVML")`
2. Exclude browser windows: Check class name
3. Send commands: Win32 API keyboard simulation

**Market Data:**
```csharp
// Request spot rate
var spotRate = await _bloombergService.FetchSpotRateAsync("EURUSD");
// Returns: 1.0895 (PX_LAST field)
```

**⚠️ Important Notes:**
- Bloomberg API requires licensed terminal
- Window detection may fail if OVML window title changes
- Keyboard simulation requires active window focus

### OpenAI API Integration

**Configuration:**
```xml
<!-- App.config -->
<appSettings>
  <add key="OpenAIApiKey" value="sk-..." />
</appSettings>
```

**API Call:**
```csharp
var response = await _openAI.Chat.CreateChatCompletionAsync(new ChatRequest
{
    Model = "gpt-4o-mini",
    Temperature = 0.1,
    MaxTokens = 500,
    Messages = new List<ChatMessage>
    {
        new ChatMessage { Role = "system", Content = systemPrompt },
        new ChatMessage { Role = "user", Content = userInput }
    }
});
```

**Cost Optimization:**
- Model: gpt-4o-mini (cheaper than gpt-4)
- Temperature: 0.1 (deterministic, fewer retries)
- MaxTokens: 500 (limit response length)
- Caching: Avoid duplicate API calls

**⚠️ Important Notes:**
- API key must be kept secure
- Rate limiting may occur
- Graceful fallback if API unavailable
- Pattern learning reduces API calls over time

### GFI Fenics FIX Integration

**Connection Architecture:**
```
Application → QuickFix Session → SSLTunnelProxy (localhost:9443) → GFI Server
```

**Configuration:**
```ini
# quickfix.cfg
[DEFAULT]
ConnectionType=initiator
ReconnectInterval=30
FileStorePath=fix_store
FileLogPath=fix_logs

[SESSION]
BeginString=FIX.4.4
SenderCompID=YOUR_SENDER_ID
TargetCompID=GFI_TARGET_ID
SocketConnectHost=localhost
SocketConnectPort=9443
DataDictionary=FIX44.xml
```

**Message Flow:**

1. **Quote Request (35=R):**
```
User clicks "Get Quote" → TradeStructure → BuildQuoteRequest() → Send
```

2. **Quote Response (35=S):**
```
GFI sends quote → GFIFIXApplication.OnMessage() → Parse BID/OFFER → Update UI
```

3. **Order Execution (35=AB):**
```
User selects BID/OFFER → BuildNewOrderMultileg() → Send
```

4. **Execution Report (35=8):**
```
GFI confirms fill → Update TradeBlotter → Show success message
```

**Critical Custom Tags:**
- **Tag 5020**: Premium Delivery (canonical = T+1)
- **Tag 5235**: Expiry Date (yyyyMMdd format)
- **Tag 5678**: Volatility
- **Tag 5844**: Leg Premium Price
- **Tag 6035**: Number of Legs
- **Tag 6120**: NoMQEntries (repeating group count)
- **Tag 6436**: Premium (BID/OFFER pricing) ← **PRIMARY PRICING FIELD**
- **Tag 7940**: Structure Type (1=Vanilla, 5=RR, 8=Call Spread, 9=Put Spread, 10=Seagull)
- **Tag 9904**: Lot Size Multiplier

**⚠️ CRITICAL GOTCHAS:**

1. **Field Ordering:** GFI rejects messages with incorrect field order
2. **QuoteID Handling:** Use EXACT QuoteID from quote (don't transform)
   - BID quotes end with `-O`
   - OFFER quotes end with `-T`
3. **Date Policy:** Tag 75 = TODAY, Tag 5020 = T+1 (canonical dates)
4. **Premium vs Volatility:** Use Tag 6436 (Premium) for execution logic, not Tag 5678 (Volatility)
5. **Repeating Groups:** Tag 6120 must precede leg groups, each leg must have all required fields

**Recent Debug Focus:**
- Premium consistency between Tag 6436 and Tag 5844
- QuoteID suffix handling (-O for BID, -T for OFFER)
- Best offer detection: Maximum (least negative) premium

---

## Common Tasks

### Task 1: Debug Why a Trade Isn't Parsing

**Step-by-Step:**

1. **Check Console Output:** See which patterns were tried
2. **Test Each Pattern Manually:**
```csharp
var pattern = RegexTradePatterns.Vanilla_CurrencyLed();
var match = Regex.Match(input, pattern.Pattern, RegexOptions.IgnoreCase);
Console.WriteLine($"Match: {match.Success}");
foreach (Group group in match.Groups)
{
    Console.WriteLine($"{group.Name}: {group.Value}");
}
```

3. **Check Preprocessing:** Input might be modified before pattern matching
```csharp
string preprocessed = PreprocessInput(input);
Console.WriteLine($"Original: {input}");
Console.WriteLine($"Preprocessed: {preprocessed}");
```

4. **Test AI Fallback:**
```csharp
var aiResult = await _openAIService.ParseTradeWithAIAsync(input);
Console.WriteLine($"AI Result: {aiResult.OVML}");
```

5. **Check Validation:**
```csharp
var validation = _sanityChecker.ValidateTrade(ovml, input);
Console.WriteLine($"Valid: {validation.IsValid}, Confidence: {validation.Confidence}");
foreach (var error in validation.Errors)
{
    Console.WriteLine($"Error: {error}");
}
```

### Task 2: Add Support for a New Currency Pair

1. **Add to commonPairs array in TradeParser.cs:**
```csharp
private static readonly string[] commonPairs = new[]
{
    "EURUSD", "USDJPY", "GBPUSD", "AUDUSD", "USDCAD", "USDCHF",
    "NZDUSD", "EURGBP", "EURJPY", "GBPJPY", "EURCHF", "AUDJPY",
    "EURAUD", "EURNOK", "EURSEK", "USDNOK", "USDSEK", "USDTRY",
    "USDMXN", "USDZAR", "EURCAD", "AUDNZD", "NOKSEK", "EURTRY",
    "NEWPAIR" // Add here
};
```

2. **Test Bloomberg Integration:**
```csharp
var spot = await _bloombergService.FetchSpotRateAsync("NEWPAIR");
// Verify Bloomberg returns valid spot rate
```

3. **Test with Sample Trade:**
```csharp
var result = await ParseTradeAsync("buy 10M NEWPAIR 3M 1.5000 call");
```

### Task 3: Fix a FIX Message Rejection

**Common Rejection Reasons:**

1. **Incorrect Field Order:**
   - Check `RawFIXMessageBuilder.cs`
   - Compare with GFI specification PDF
   - Use Wireshark to capture accepted messages from other systems

2. **Invalid Tag Values:**
   - Check GFI specification for valid values
   - Example: Tag 7940 (Structure Type) must be 1, 5, 8, 9, or 10

3. **Missing Required Fields:**
   - Check session logs in `fix_logs/`
   - Look for reject messages (35=3)
   - Add missing fields in correct order

4. **Date Format Issues:**
   - Ensure Tag 75 is yyyyMMdd (e.g., "20251217")
   - Ensure Tag 5235 is yyyyMMdd
   - Check timezone handling

**Debug Process:**
```csharp
// Add detailed logging in RawFIXMessageBuilder.cs
Console.WriteLine($"[FIX OUT] {rawMessage.Replace('\x01', '|')}");

// Check session logs
// fix_logs/FIX.4.4-SENDER-GFI.messages.current.log

// Look for reject reasons
grep "35=3" fix_logs/*.log
```

### Task 4: Improve AI Parsing Accuracy

1. **Add Examples to System Prompt (OpenAIService.cs):**
```csharp
private string BuildSystemPrompt()
{
    return @"
    ...existing prompt...

    Additional Examples:
    Input: 'new trade type here'
    Output: OVML CURNCY MM/DD/YY ...
    ";
}
```

2. **Adjust Temperature:**
```csharp
Temperature = 0.05 // More deterministic (vs. 0.1 default)
```

3. **Add Validation Feedback Loop:**
```csharp
var aiResult = await ParseWithAI(input);
var validation = _sanityChecker.ValidateTrade(aiResult);
if (!validation.IsValid)
{
    // Send validation errors back to AI for correction
    var correctedResult = await ParseWithAI(input, validation.Errors);
}
```

4. **Monitor Learned Patterns:**
```csharp
// Check learned_patterns.json
// Remove low-confidence patterns
// Manually curate high-quality patterns
```

### Task 5: Handle New Multi-Leg Structure

**Example: Adding "Butterfly" Structure**

1. **Define Structure Type:**
```csharp
// In TradeStructure.cs or constants
public const string STRUCTURE_BUTTERFLY = "11"; // Check GFI spec for actual code
```

2. **Add Regex Pattern:**
```csharp
public static (string Pattern, string Name, int Priority) Butterfly()
{
    string pattern = @"butterfly\s+(?<ccy>[A-Z]{6})\s+(?<expiry>[^\s]+)\s+(?<strike1>[\d.]+)/(?<strike2>[\d.]+)/(?<strike3>[\d.]+)\s+(?<type>call|put)";
    return (pattern, "Butterfly", 90);
}
```

3. **Add OVML Generation Logic:**
```csharp
// In TradeParser.cs
if (structureType == "Butterfly")
{
    ovml = $"OVML {ccy} {expiry} 3L B,S,B {strike1}{type},{strike2}{type},{strike3}{type} N{not1}M,{not2}M,{not3}M {style}";
}
```

4. **Add to OVMLBridge.cs:**
```csharp
// Parse butterfly structure from OVML
if (legs.Count == 3 && directions == "BSB")
{
    trade.StructureType = "11"; // Butterfly
}
```

5. **Test End-to-End:**
```csharp
var result = await ParseTradeAsync("butterfly EURUSD 17Dec25 1.08/1.09/1.10 call");
Assert.AreEqual("OVML EURUSD 12/17/25 3L B,S,B 1.0800C,1.0900C,1.1000C N10M,20M,10M VA", result.OVML);
```

---

## Critical Knowledge

### OVML Format Specification

**Single-Leg Structure:**
```
OVML {CCY} {expiry} {B/S} {strike}{C/P} N{notional}M {VA/EU} [SP{spot}]

Example:
OVML EURNOK 12/17/25 B 9.8500C N25M VA SP10.9518
```

**Multi-Leg Structure:**
```
OVML {CCY} {expiry} {N}L {B/S,...} {strike1}{C/P},{strike2}{C/P},... N{not1}M,{not2}M,... {VA/EU} [SP{spot}]

Example (Risk Reversal):
OVML EURUSD 01/15/26 2L S,B 1.0500P,1.1000C N10M,10M VA SP1.0750
```

**Field Definitions:**
- **CCY**: 6-character currency pair (EURUSD, EURNOK, etc.)
- **Expiry**: MM/DD/YY format (12/17/25)
- **B/S**: BUY or SELL
- **Strike**: 4 decimal places (9.8500)
- **C/P**: CALL or PUT
- **Notional**: In millions (25M)
- **VA/EU**: VANILLA or EUROPEAN (style)
- **SP**: Spot reference (optional, used for premium calculation)

**Multi-Leg Ordering:**
- Directions: Comma-separated (B,S,B)
- Strikes: Comma-separated with type (1.05P,1.10C)
- Notionals: Comma-separated (10M,10M)
- **CRITICAL**: All three lists must have same count

### UBS Format Specification

**Single-Leg:**
```
{CCY} {strike}{C/P} {CCY1} {notional/10} MIO {dd-MMM-yy} SPOT {spot}

Example:
EURNOK 9.8500C EUR 2.5 MIO 17-Dec-25 SPOT 10.9518
```

**Multi-Leg:**
```
{CCY} {strike1}{C/P} {CCY1} {not1/10}MIO {date}, {CCY} {strike2}{C/P} {not2/10}MIO SPOT {spot}

Example:
EURUSD 1.0500P USD 1.0MIO 15-Jan-26, EURUSD 1.1000C 1.0MIO SPOT 1.0750
```

**Key Differences from OVML:**
- Date format: dd-MMM-yy (17-Dec-25) vs MM/DD/YY
- Notional: Divide by 10 (25M → 2.5 MIO)
- Currency: Base currency for notional (EUR for EURNOK)
- Direction: Not explicitly shown (inferred from context)

### Strike Ordering Rules

**Call Spreads:**
- Buy low strike, Sell high strike
- Example: Buy 1.05C, Sell 1.10C

**Put Spreads:**
- Buy high strike, Sell low strike
- Example: Buy 1.10P, Sell 1.05P

**Risk Reversals:**
- Sell Put (lower strike), Buy Call (higher strike)
- OR: Buy Put, Sell Call (reverse)

**Straddles:**
- Same strike for Call and Put
- Example: Buy 1.08C, Buy 1.08P

**Strangles:**
- Different strikes (Put lower, Call higher)
- Example: Buy 1.05P, Buy 1.10C

### Date Handling

**Input Formats Supported:**
- `17Dec25` (Bloomberg format)
- `17-12-25` (dashed format)
- `17 Dec 2025` (full year)
- `2025-12-17` (ISO format)
- `3M` / `6M` (tenor - calculated from today)
- `1Y` / `2Y` (year tenor)

**Output Formats:**
- **OVML**: `MM/DD/YY` (12/17/25)
- **UBS**: `dd-MMM-yy` (17-Dec-25)
- **FIX**: `yyyyMMdd` (20251217)

**Tenor Calculations:**
- 1M = +30 days
- 3M = +90 days
- 6M = +180 days
- 1Y = +365 days
- **Uses FxDateService.cs for business day adjustments**

### Premium vs Volatility Debate

**Recent Commits Show Preference for Premium:**

- **Tag 6436 (Premium)**: Primary field for execution logic
- **Tag 5678 (Volatility)**: Secondary, informational

**Why Premium is Preferred:**
1. Direct pricing (no conversion needed)
2. Consistent across LPs
3. Easier for traders to compare
4. Reduces calculation errors

**Code Pattern:**
```csharp
// CORRECT - Use Premium for execution
if (quote.Premium > bestOfferPremium)
{
    bestOfferQuote = quote;
}

// AVOID - Using volatility requires conversion
// var premium = CalculatePremium(quote.Volatility, spot, strike, expiry);
```

### QuoteID Handling

**CRITICAL: Use Exact QuoteID from Quote**

**BID Quotes:** End with `-O`
**OFFER Quotes:** End with `-T`

```csharp
// WRONG - Don't transform QuoteID
string quoteId = quote.QuoteID.Replace("-O", "-T"); // NO!

// CORRECT - Use as-is
string quoteId = quote.QuoteID; // YES!
```

**Why This Matters:**
- GFI uses QuoteID suffixes for internal routing
- Transforming QuoteID causes rejections
- Each side (BID/OFFER) has its own QuoteID

**Recent Fix:**
```csharp
// Before (WRONG):
if (selectedSide == "BID")
    quoteId = quoteId.Replace("-T", "-O");

// After (CORRECT):
// Use quoteId directly from the selected quote row
```

### Option Type Inference (Ambiguous Trades)

**Problem**: When traders say "11.75 in 10M" without specifying call/put, what should the system infer?

**Industry Standard**: Default to **OTM (Out-of-the-Money)** options
- OTM options are cheaper (lower premium)
- Traders typically buy OTM unless explicitly requesting ITM
- ITM options are more expensive and less common for speculative positions

**Inference Logic** (OpenAIService.cs line 390-442):
```csharp
// Only apply when:
// 1. User did NOT specify "call" or "put" in input
// 2. User did NOT provide spot reference (sr:, @, etc.)
// 3. Single-leg trade only (multi-leg strategies are intentional)

if (strike > spot)
    → CALL (OTM - upside speculation)
else if (strike < spot)
    → PUT (OTM - downside protection)
```

**Examples:**
```
Input: "eurnok 11.75 in 10M"
Spot: 11.78
Strike < Spot → Infer PUT
Result: OVML EURNOK ... B 11.7500P N10M VA SP11.78

Input: "eurusd 1.12 in 50M"
Spot: 1.10
Strike > Spot → Infer CALL
Result: OVML EURUSD ... B 1.1200C N50M VA SP1.10
```

**Debug Output:**
```
[AI-TYPE-CHECK] Strike=11.7500, Spot=11.7849, IsCall=True, ShouldBeCall=False
[AI-TYPE-CHECK] ✓ CORRECTED: 11.7500C → 11.7500P
[AI-TYPE-CHECK]   Reason: Strike < Spot → Should be P (OTM)
```

**Important Notes:**
- If user explicitly says "call" or "put", NO correction is applied
- If user provides spot reference (sr: value), NO correction is applied
- Multi-leg trades are never corrected (strategies are intentional)
- This was a **bug until 2025-11-27** - previous code failed to parse strike correctly

---

## Testing & Debugging

### Unit Testing Approach

**Test Parsing Pipeline:**
```csharp
[Test]
public async Task TestVanillaCallParsing()
{
    var parser = new TradeParser(_bloombergService, _openAIService, _sanityChecker);
    var result = await parser.ParseTradeAsync("buy 10M EURUSD 3M 1.10 call");

    Assert.AreEqual("Regex", result.ParseMethod);
    Assert.IsTrue(result.OVML.Contains("EURUSD"));
    Assert.IsTrue(result.OVML.Contains("B 1.1000C"));
    Assert.IsTrue(result.OVML.Contains("N10M"));
}

[Test]
public async Task TestRiskReversalParsing()
{
    var result = await parser.ParseTradeAsync("EURUSD risk reversal sell 1.05 put buy 1.10 call 50M");

    Assert.IsTrue(result.OVML.Contains("2L S,B"));
    Assert.IsTrue(result.OVML.Contains("1.0500P,1.1000C"));
    Assert.AreEqual(0.9, result.Confidence); // High confidence
}
```

**Test Validation Logic:**
```csharp
[Test]
public void TestExpiredDateValidation()
{
    string ovml = "OVML EURUSD 01/01/20 B 1.1000C N10M VA"; // Past date
    var validation = _sanityChecker.ValidateTrade(ovml, "buy call expired");

    Assert.IsFalse(validation.IsValid);
    Assert.Contains("expiry date has passed", validation.Errors);
}
```

**Test FIX Message Construction:**
```csharp
[Test]
public void TestQuoteRequestFieldOrder()
{
    var trade = new TradeStructure
    {
        Underlying = "EURUSD",
        Expiry = DateTime.Parse("2025-12-17"),
        Legs = new List<TradeLeg>
        {
            new TradeLeg { Direction = "BUY", OptionType = "CALL", Strike = 1.10, NotionalMM = 10 }
        }
    };

    var rawMessage = RawFIXMessageBuilder.BuildQuoteRequest(trade, "QuoteReq123");

    // Verify field order: Tag 75 before Tag 5020
    int tag75Pos = rawMessage.IndexOf("75=");
    int tag5020Pos = rawMessage.IndexOf("5020=");
    Assert.IsTrue(tag75Pos < tag5020Pos, "Tag 75 must come before Tag 5020");

    // Verify required fields present
    Assert.Contains("35=R", rawMessage); // Quote Request
    Assert.Contains("55=EURUSD", rawMessage);
    Assert.Contains("6120=1", rawMessage); // 1 leg
}
```

### Debugging Strategies

**1. Console Logging:**
```csharp
// Program.cs allocates console for debug output
[STAThread]
static void Main()
{
    AllocConsole(); // Creates console window
    Application.Run(new MainForm());
}

// Use throughout codebase
Console.WriteLine($"[TRACE] {DateTime.Now:HH:mm:ss.fff} | {eventName} | {details}");
```

**2. FIX Session Logs:**
```bash
# Location: fix_logs/
FIX.4.4-SENDER-GFI.messages.current.log  # Message history
FIX.4.4-SENDER-GFI.event.current.log     # Session events

# View recent messages
tail -f fix_logs/FIX.4.4-SENDER-GFI.messages.current.log

# Search for rejections
grep "35=3" fix_logs/*.log # Reject messages
grep "35=9" fix_logs/*.log # Cancel Reject
```

**3. Message Tracing:**
```csharp
// In GFIFIXApplication.cs
public override void OnMessage(QuickFix.FIX44.Quote quote, SessionID sessionID)
{
    Console.WriteLine($"[FIX IN] Quote received");
    Console.WriteLine($"  QuoteID: {quote.QuoteID.getValue()}");
    Console.WriteLine($"  BidPremium: {quote.GetField(6436)}");
    Console.WriteLine($"  OfferPremium: {quote.GetField(6436)}");
    // ... rest of processing
}
```

**4. UI Debug Panel:**
```csharp
// MainForm.cs has debug log panel
private void AppendLog(string message)
{
    txtDebugLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
}
```

**5. Network Traffic Analysis:**
```bash
# Use Wireshark to capture FIX messages
# Filter: tcp.port == 9443
# Analyze field order and values
```

### Common Issues & Solutions

**Issue 1: Bloomberg API Not Connecting**
- **Symptom**: "Bloomberg not connected" status
- **Check**: Is Bloomberg Terminal running? (`bplus.exe` process)
- **Check**: Is Desktop API enabled in Terminal? (WAPI <GO>)
- **Fix**: Restart Bloomberg Terminal, verify WAPI settings

**Issue 2: FIX Session Not Logging In**
- **Symptom**: Session stuck in "Connecting" state
- **Check**: Is SSLTunnelProxy running? (localhost:9443)
- **Check**: Are credentials correct in `quickfix.cfg`?
- **Fix**: Verify proxy settings, check GFI credentials

**Issue 3: Quote Requests Rejected**
- **Symptom**: Immediate reject after quote request
- **Check**: Field order in `RawFIXMessageBuilder.cs`
- **Check**: Required fields present (Tag 75, 5020, 6120, etc.)
- **Debug**: Compare with accepted messages in logs
- **Fix**: Adjust field order to match specification

**Issue 4: Execution Orders Rejected**
- **Symptom**: Order rejected with "Invalid QuoteID"
- **Check**: Using exact QuoteID from quote? (don't transform)
- **Check**: Quote still valid? (Tag 62 ValidUntilTime)
- **Fix**: Use QuoteID as-is, execute before expiry

**Issue 5: AI Parsing Incorrect**
- **Symptom**: AI returns wrong OVML
- **Check**: Validation logic catching errors?
- **Check**: System prompt comprehensive?
- **Debug**: Log AI response before validation
- **Fix**: Add examples to prompt, improve validation

**Issue 6: Wrong Option Type When Strike Near Spot (FIXED 2025-11-27)**
- **Symptom**: Input "11.75 in 10M" with spot=11.78 returns CALL (ITM) instead of PUT (OTM)
- **Root Cause**: Parsing bug in OpenAIService.cs line 398 - tried to parse "11.7500C" as double, failed due to 'C' suffix
- **Expected Behavior**: Strike < Spot → Should infer PUT (OTM option)
- **Fix Applied**: Extract numeric strike using regex before parsing, added debug logging
- **Check**: Look for `[AI-TYPE-CHECK]` console logs showing strike/spot comparison and correction
- **Note**: Correction only applies when user didn't specify "call" or "put" AND no spot reference provided

---

## Git Workflow

### Branch Naming Convention

```bash
# Feature branches (created by Claude)
claude/feature-name-sessionid-requestid

# Current branch
claude/claude-md-mihdmpmblb74y627-01A1GqpFBEwLwjcnBWdmsPfv
```

### Commit Message Standards

```bash
# Format: <type>: <brief description>

# Types:
feat:     # New feature
fix:      # Bug fix
refactor: # Code refactoring
docs:     # Documentation changes
test:     # Test additions/changes
chore:    # Build, config, dependencies

# Examples:
feat: Add butterfly spread pattern support
fix: Correct QuoteID handling for BID/OFFER quotes
refactor: Extract FIX field ordering logic
docs: Update CLAUDE.md with FIX integration notes
```

### Push Workflow

```bash
# 1. Verify you're on correct branch
git branch
# Should show: claude/...

# 2. Stage changes
git add -A

# 3. Commit with descriptive message
git commit -m "feat: Add comprehensive CLAUDE.md documentation"

# 4. Push to remote (with retry logic for network issues)
git push -u origin claude/claude-md-mihdmpmblb74y627-01A1GqpFBEwLwjcnBWdmsPfv
# Retry up to 4 times with exponential backoff (2s, 4s, 8s, 16s)
```

### Recent Development History

**Last 5 Commits (Most Recent First):**

1. **db4060e**: Revert "Change execution and highlighting logic to use VOLS instead of PREMIUMS"
   - **Impact**: Reverted to premium-based execution
   - **Reason**: Volatility-based logic caused execution issues

2. **12aef10**: Change execution and highlighting logic to use VOLS instead of PREMIUMS
   - **Impact**: Attempted vol-based execution (later reverted)
   - **Reason**: Experimentation with pricing method

3. **d22ae9e**: Add spot reference logging to investigate premium differences
   - **Impact**: Enhanced debugging for premium calculations
   - **Files**: GFIQuoteDialog.cs

4. **8f04287**: Add minimal premium logging to verify Tag 6436 values and conversion
   - **Impact**: Tag 6436 investigation
   - **Files**: GFIFIXApplication.cs

5. **1dfdbe7**: Remove premium debug logging after issue resolution
   - **Impact**: Cleanup after successful debug
   - **Files**: GFIFIXApplication.cs

**Key Takeaway**: Recent focus on **premium pricing accuracy** and **execution logic**. Prefer premium (Tag 6436) over volatility (Tag 5678) for execution.

---

## Additional Resources

### External Documentation

1. **GFI FIX Specification**: `/Documentation/BGC__GFI_FIX_Specification_for_FX_v1_4_4.pdf`
2. **Bloomberg API**: Bloomberg Terminal → WAPI <GO>
3. **QuickFix Documentation**: https://www.quickfixengine.org/
4. **OpenAI API**: https://platform.openai.com/docs/

### Configuration Files

- **quickfix.cfg**: FIX session configuration (credentials, connection)
- **App.config**: OpenAI API key, application settings
- **FIX44.xml**: FIX 4.4 data dictionary

### Persistence Locations

- **Trade History**: `%AppData%/FXOAiTranslator/trades.json`
- **Learned Patterns**: `%AppData%/FXOAiTranslator/learned_patterns.json`
- **FIX Logs**: `fix_logs/` directory
- **FIX Store**: `fix_store/` directory

### Key Constants

```csharp
// Currency pairs (24 supported)
"EURUSD", "USDJPY", "GBPUSD", "AUDUSD", "USDCAD", "USDCHF",
"NZDUSD", "EURGBP", "EURJPY", "GBPJPY", "EURCHF", "AUDJPY",
"EURAUD", "EURNOK", "EURSEK", "USDNOK", "USDSEK", "USDTRY",
"USDMXN", "USDZAR", "EURCAD", "AUDNZD", "NOKSEK", "EURTRY"

// Structure types (GFI FIX)
1 = Vanilla
5 = Risk Reversal
8 = Call Spread
9 = Put Spread
10 = Seagull

// Parse methods (UI color coding)
"Regex" = Green
"Learned" = Yellow
"AI" = Blue
"Warning" = Orange
"Error" = Red

// Confidence levels
0.9 = Perfect (no issues)
0.75 = Good (minor warnings)
0.5 = Acceptable (some concerns)
0.1 = Failed (validation errors)
```

---

## Appendix: Quick Reference

### Command Cheat Sheet

```bash
# Build solution
dotnet build FXOAiTranslator.sln

# Run main application
dotnet run --project FXOAiTranslator.csproj

# Run tests (if test project exists)
dotnet test

# Clean build artifacts
dotnet clean

# View git history
git log --oneline -10

# Check git status
git status

# View FIX logs
tail -f fix_logs/FIX.4.4-SENDER-GFI.messages.current.log
```

### Code Snippets

**Parse Trade:**
```csharp
var result = await _tradeParser.ParseTradeAsync("buy 10M EURUSD 3M 1.10 call");
Console.WriteLine($"OVML: {result.OVML}");
Console.WriteLine($"UBS: {result.UBSFormat}");
Console.WriteLine($"Method: {result.ParseMethod}");
Console.WriteLine($"Confidence: {result.Confidence}");
```

**Send to Bloomberg:**
```csharp
if (_bloombergService.IsBloombergConnected())
{
    await _bloombergService.SendOVMLAsync(ovml);
}
```

**Get Quote from GFI:**
```csharp
var trade = OVMLBridge.ConvertToTradeStructure(ovml);
_fixSession.SendQuoteRequest(trade);
// Wait for OnMessage(Quote) callback
```

**Execute Trade:**
```csharp
_fixSession.SendNewOrderMultileg(trade, quoteId, selectedSide);
// Wait for OnMessage(ExecutionReport) callback
```

---

## Change Log

| Date | Version | Changes |
|------|---------|---------|
| 2025-11-27 | 1.0 | Initial comprehensive CLAUDE.md creation |

---

## Contact & Support

For questions about this codebase, refer to:
- Recent git history: `git log --oneline -20`
- Code comments in key files (MainForm.cs, TradeParser.cs)
- GFI FIX specification PDF
- Bloomberg WAPI documentation

---

**End of CLAUDE.md**