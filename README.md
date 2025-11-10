# FXO AI Translator

A sophisticated natural language processing tool for FX options trading that integrates with Bloomberg Terminal.

## Features

- **Natural Language Processing**: Convert conversational trade requests into structured formats
- **Bloomberg Terminal Integration**: Live market data and automated OVML command execution
- **Multi-Format Output**: Generate both OVML and UBS formats
- **AI Fallback**: OpenAI integration for complex or unusual trade patterns
- **Multi-Language Support**: English, Swedish, Norwegian date and command parsing
- **Pattern Learning**: System learns from successful AI translations

## Supported Trade Types

- Vanilla Options (Buy/Sell Calls/Puts)
- Risk Reversals
- Call/Put Spreads
- Straddles & Strangles
- Collars (3-leg strategies)
- Seagull structures

## Prerequisites

- Bloomberg Terminal with Desktop API
- .NET Framework 4.7.2 or higher
- OpenAI API key (optional, for AI fallback)
- Visual Studio 2019 or later

## Installation

1. Clone the repository
2. Add Bloomberg API reference (`Bloomberglp.Blpapi.dll`)
3. Configure API keys in `app.config`
4. Build and run

## Configuration

Create `app.config` based on `app.config.example`:
- Add your OpenAI API key
- Configure Bloomberg connection settings

## Usage

Input examples:
- `"EURNOK 17Dec25 9.85 in 25mio call"`
- `"buy 100M EURUSD 3M 1.10 call"`
- `"EURSEK risk reversal buy call 11.50 sell put 11.30 50M"`

## Security Notice

This repository contains Bloomberg Terminal integration code. Ensure:
- Keep API keys secure
- Follow your organization's Bloomberg usage policies
- Review all generated trading commands before execution
