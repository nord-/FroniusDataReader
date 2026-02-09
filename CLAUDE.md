# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

C# .NET 10.0 console application that fetches daily solar energy production data from a Fronius inverter on the local network and copies it to the clipboard as tab-separated values.

## Build and Test Commands

```bash
dotnet build                                    # Build the project
dotnet run                                      # Run (defaults to previous month's data)
dotnet run -- "2025-07-01" "2025-07-15"         # Run with custom date range
dotnet test                                     # Run all tests
dotnet test --filter "FroniusJsonParsingTests"  # Run a specific test class
dotnet test --filter "DisplayName~ShouldExtract" # Run a single test by partial name
```

## Architecture

**Data flow:** `appsettings.json` config → split date range into MaxDays chunks → parallel HTTP requests to Fronius Solar API (`/solar_api/v1/GetArchiveData.cgi`) → parse JSON responses → aggregate into `Dictionary<DateTime, decimal>` → format as TSV → copy to clipboard.

**Key files:**
- `Program.cs` — Entry point; handles config loading, date range chunking, parallel API calls, JSON parsing, and result aggregation. Uses `lock()` for thread-safe dictionary access during parallel processing.
- `FroniusSettings.cs` — Configuration POCO bound from `appsettings.json` (`InverterIp`, `MaxDays`).
- `Clipboard.cs` — Static clipboard wrapper using TextCopy; includes `DictionaryToText()` extension method that formats output as ISO date + tab + rounded Wh value.
- `appsettings.json` — Runtime config (inverter IP address, MaxDays=15 API chunk limit).

**Tests** (xUnit + FluentAssertions, in the same project):
- `FroniusJsonParsingTests.cs` — Unit tests using `sample_response.json` to validate JSON parsing and timestamp conversion.
- `FroniusApiTests.cs` — Integration tests that hit the live Fronius API (require network access to the inverter).

## Key Dependencies

Flurl (URL building), System.Text.Json (parsing), Microsoft.Extensions.Configuration (config binding), TextCopy (clipboard), FluentAssertions (test assertions).

## Conventions

- Dates use ISO format (`yyyy-MM-dd`)
- Nullable reference types enabled; CS8618 warning suppressed in project config
- C# latest language version with implicit usings
