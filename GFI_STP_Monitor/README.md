# GFI STP Monitor

Standalone console application for monitoring STP (Straight Through Processing) messages from GFI.

## Purpose

This application connects to GFI's FIX server using STP credentials and monitors all incoming Trade Capture Reports (message type AE) and other STP messages.

## Configuration

### FIX Session Settings (stp_quickfix.cfg)
- **SenderCompID**: GFI_BFXO_SWED_TC1
- **TargetCompID**: GFI
- **Username**: gfi_bfxo_swed_tc1
- **Connection**: localhost:9444 (SSL proxy)

### SSL Proxy
The application starts an SSL tunnel proxy that connects:
- Local: localhost:9444
- Remote: quotes.stage2.gfifx.com:443

## Usage

1. Open GFI_STP_Monitor.csproj in Visual Studio 2022
2. Build the solution
3. Run the application
4. The monitor will:
   - Start the SSL proxy
   - Connect to GFI FIX server
   - Log all incoming STP messages
   - Display Trade Capture Reports with parsed fields
5. Press Ctrl+C to stop

## Message Types

The application logs all messages but provides special formatting for:
- **Trade Capture Reports (AE)**: Shows TradeReportID, ExecID, Symbol, Side, Quantity, Price

## Credentials

STP credentials are configured in STPFixSession.cs:
- Username: gfi_bfxo_swed_tc1
- Password: ylhU6Q1eaxXf
- No OnBehalfOfCompID (unlike Trading session)

## Troubleshooting

If connection fails:
1. Check GFI has provisioned the STP account
2. Verify your IP is whitelisted
3. Check SSL proxy logs for certificate issues
4. Ensure port 9444 is not already in use
