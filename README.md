# BEN Trade Reviewer

A Windows x64 WPF trade-review application for BEN Trade Recorder archives.

## What is included

- Clean Architecture-style split into Core, Data, Services, Replay, UI and Tests.
- SQLite persistence under `%LOCALAPPDATA%\\BENTradeReviewer\\trades.db`.
- Automatic import of `TradeInfo.txt`, `TradeActivity.log` and up to four monitor video files per trade folder.
- Calendar dashboard, KPI metrics, day review, synchronized replay controls, notes and tags.
- Instrument-aware tick/point P&L and planned/realized R:R calculations.
- Explicitly nullable/absent media handling: trades import even when video or some optional metadata is missing.

## Requirements

- Windows 10/11 x64
- Visual Studio 2022 with **.NET desktop development** workload, or .NET 8 SDK
- .NET 8 SDK

## Build

From this directory:

```powershell
dotnet restore BENTradeReviewer.sln
dotnet build BENTradeReviewer.sln --configuration Release --platform x64
dotnet test src\\BENTradeReviewer.Tests\\BENTradeReviewer.Tests.csproj --configuration Release
```

Run:

```powershell
dotnet run --project src\\BENTradeReviewer.UI\\BENTradeReviewer.UI.csproj --configuration Release
```

Or open `BENTradeReviewer.sln` in Visual Studio and set `BENTradeReviewer.UI` as the startup project.

## Recorder source

The default recorder path is:

```text
C:\\BEN_TradeRecorder
```

The importer accepts folders such as:

```text
C:\\BEN_TradeRecorder\\2026-08-29\\Trade_20260829_093412\\
    TradeInfo.txt
    TradeActivity.log
    Monitor1_Charts.mp4
    Monitor2_DOM.mp4
    Monitor3_Footprint.mp4
    Monitor4_GEX.mp4
```

The app also works with `TradeInfo.txt` alone for a metadata-only test trade.

## TradeInfo format

The importer accepts `key=value` and `key:value` lines. Recommended fields:

```text
TradeId=Trade_20260829_093412
Symbol=6E
Account=SimAccount
Side=LONG
Quantity=1
EntryTime=2026-08-29 09:34:12.123
ExitTime=2026-08-29 09:36:41.123
EntryPrice=1.08500
ExitPrice=1.08600
InitialStopLoss=1.08400
InitialTakeProfit=1.08700
PreRollSeconds=5
PostRollSeconds=5
```

## Important implementation notes

This repository was generated and structurally checked in the current environment, but the environment does not contain the `dotnet` CLI, so an actual Windows/WPF compilation could not be performed here. The included solution is intended to be opened on Windows with .NET 8 and the WPF workload installed.

`MediaElement` is used for WPF playback. Frame stepping is based on an assumed 30 FPS increment; true codec-frame-accurate stepping depends on the media pipeline/codec and is not guaranteed by `MediaElement` itself.
