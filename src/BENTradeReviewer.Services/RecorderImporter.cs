using System.Globalization;
using System.Text.RegularExpressions;
using BENTradeReviewer.Core.Enums;
using BENTradeReviewer.Core.Models;
using BENTradeReviewer.Data;

namespace BENTradeReviewer.Services;

public sealed class RecorderImporter
{
    private readonly TradeRepository _repository;

    public RecorderImporter(TradeRepository repository)
    {
        _repository = repository;
    }

    public async Task<int> ScanAndImportAsync(
        string rootFolder,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(rootFolder))
            return 0;

        var dirs = Directory
            .EnumerateDirectories(
                rootFolder,
                "*",
                SearchOption.AllDirectories)
            .Prepend(rootFolder);

        var count = 0;

        foreach (var dir in dirs)
        {
            ct.ThrowIfCancellationRequested();

            var info = Directory
                .EnumerateFiles(
                    dir,
                    "TradeInfo*.txt",
                    SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

            var videos = Directory
                .EnumerateFiles(
                    dir,
                    "*.*",
                    SearchOption.TopDirectoryOnly)
                .Where(IsVideo)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (info is null)
                continue;

            try
            {
                var trade = await ParseTradeFolderAsync(
                    dir,
                    info,
                    videos,
                    ct);

                if (trade is not null)
                {
                    await _repository.UpsertTradeAsync(trade);
                    count++;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Importer ERROR] {dir}: {ex}");
            }
        }

        return count;
    }

    public async Task<Trade?> ParseTradeFolderAsync(
        string directoryPath,
        string? tradeInfoPath,
        IReadOnlyList<string> videoPaths,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tradeInfoPath) ||
            !File.Exists(tradeInfoPath))
        {
            return null;
        }

        var lines = await File.ReadAllLinesAsync(
            tradeInfoPath,
            ct);

        var kv = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var idx = line.IndexOf(':');

            if (idx <= 0)
                idx = line.IndexOf('=');

            if (idx <= 0)
                continue;

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();

            kv[key] = value;
        }

        var trade = new Trade
        {
            FolderDirectory = directoryPath,
            TradeId = Get(
                kv,
                "Trade ID",
                Get(
                    kv,
                    "TradeId",
                    Path.GetFileName(directoryPath)))
        };

        // ============================================================
        // SYMBOL / INSTRUMENT
        // Supports:
        //   RootSymbol: ES
        //   Instrument: ES SEP26
        //   Instrument: E-mini S&P 500 (ES)
        //   Instrument: Euro FX (6E)
        // ============================================================

        trade.Symbol = ResolveSymbol(kv);

        // ============================================================
        // ACCOUNT
        // ============================================================

        trade.Account = Get(
            kv,
            "Account",
            "Sim101");

        // ============================================================
        // SIDE
        // ============================================================

        var sideText = Get(
            kv,
            "Direction",
            Get(kv, "Side", "LONG"));

        trade.Side =
            sideText.Contains(
                "SHORT",
                StringComparison.OrdinalIgnoreCase)
                ? TradeSide.Short
                : TradeSide.Long;

        // ============================================================
        // QUANTITY
        // ============================================================

        trade.Quantity = ParseInt(
            Get(
                kv,
                "TotalEntryQuantity",
                Get(
                    kv,
                    "InitialQuantity",
                    Get(
                        kv,
                        "Total Quantity",
                        Get(
                            kv,
                            "Quantity",
                            "1")))),
            1);

        // ============================================================
        // ENTRY PRICE
        // Supports new + legacy Recorder formats.
        // ============================================================

        trade.EntryPrice = ParseDecimal(
            Get(
                kv,
                "WeightedAverageEntryPrice",
                Get(
                    kv,
                    "InitialEntryPrice",
                    Get(
                        kv,
                        "Avg Entry Price",
                        Get(
                            kv,
                            "Entry Price",
                            Get(
                                kv,
                                "EntryPrice",
                                "0"))))));

        // ============================================================
        // EXIT PRICE
        // ============================================================

        trade.ExitPrice = ParseDecimal(
            Get(
                kv,
                "WeightedAverageExitPrice",
                Get(
                    kv,
                    "Avg Exit Price",
                    Get(
                        kv,
                        "Exit Price",
                        Get(
                            kv,
                            "ExitPrice",
                            trade.EntryPrice.ToString(
                                CultureInfo.InvariantCulture))))));

        // ============================================================
        // INITIAL STOP
        // ============================================================

        if (TryDecimal(
                Get(
                    kv,
                    "InitialStopPrice",
                    Get(
                        kv,
                        "InitialStopLoss",
                        Get(
                            kv,
                            "StopLoss",
                            ""))),
                out var stop))
        {
            trade.InitialStopLoss = stop;
        }

        // ============================================================
        // TIMES
        // ============================================================

        trade.EntryTime = ParseDate(
            Get(
                kv,
                "InitialEntryTime",
                Get(
                    kv,
                    "Entry Time",
                    Get(
                        kv,
                        "EntryTime",
                        ""))),
            Directory.GetCreationTime(directoryPath));

        trade.ExitTime = ParseDate(
            Get(
                kv,
                "FinalExitTime",
                Get(
                    kv,
                    "Exit Time",
                    Get(
                        kv,
                        "ExitTime",
                        ""))),
            trade.EntryTime);

        trade.PreRollSeconds = ParseDouble(
            Get(kv, "PreRollSeconds", "5"),
            5);

        trade.PostRollSeconds = ParseDouble(
            Get(kv, "PostRollSeconds", "5"),
            5);

        // ============================================================
        // INSTRUMENT SPEC
        // ============================================================

        var spec =
            await _repository.GetInstrumentSpecAsync(trade.Symbol)
            ?? InstrumentSpec
                .GetDefaultCatalog()
                .FirstOrDefault(
                    s => s.Symbol.Equals(
                        trade.Symbol,
                        StringComparison.OrdinalIgnoreCase))
            ?? new InstrumentSpec
            {
                Symbol = trade.Symbol,
                Name = trade.Symbol,
                TickSize = 0.25m,
                TickValue = 12.50m,
                PointValue = 50m,
                DefaultCommission = 2.50m
            };

        await _repository.UpsertInstrumentSpecAsync(spec);

        // ============================================================
        // MULTI TARGET
        // New Recorder format only.
        // ============================================================

        decimal weightedR = 0m;
        int weightedQuantity = 0;
        decimal firstTargetPrice = 0m;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (!line.StartsWith(
                    "InitialTarget:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var idx = line.IndexOf(':');

            if (idx < 0)
                continue;

            var payload = line[(idx + 1)..].Trim();

            var parts = payload.Split(
                '|',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

            var qty = 0;
            var targetPrice = 0m;
            var targetR = 0m;

            var hasPrice = false;
            var hasR = false;

            foreach (var part in parts)
            {
                var p = part.Trim();

                if (p.StartsWith(
                        "Qty=",
                        StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(
                        p["Qty=".Length..].Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out qty);
                }
                else if (p.StartsWith(
                             "Price=",
                             StringComparison.OrdinalIgnoreCase))
                {
                    hasPrice = TryDecimal(
                        p["Price=".Length..].Trim(),
                        out targetPrice);
                }
                else if (p.StartsWith(
                             "R=",
                             StringComparison.OrdinalIgnoreCase))
                {
                    hasR = TryDecimal(
                        p["R=".Length..].Trim(),
                        out targetR);
                }
            }

            if (firstTargetPrice == 0m && hasPrice)
                firstTargetPrice = targetPrice;

            if (hasR && qty > 0)
            {
                weightedR += targetR * qty;
                weightedQuantity += qty;
            }
        }

        if (firstTargetPrice > 0m)
            trade.InitialTakeProfit = firstTargetPrice;

        if (weightedQuantity > 0)
        {
            trade.PlannedRR = Math.Round(
                weightedR / weightedQuantity,
                4);
        }

        // ============================================================
        // RECORDED INITIAL RISK
        // ============================================================

        if (TryDecimal(
                Get(kv, "InitialRiskUSD", ""),
                out var recordedRisk))
        {
            trade.InitialRiskUSD = recordedRisk;
        }

        // ============================================================
        // EXPECTED REWARD
        // ============================================================

        if (trade.InitialRiskUSD > 0 &&
            trade.PlannedRR.HasValue)
        {
            trade.ExpectedRewardUSD =
                Math.Round(
                    trade.InitialRiskUSD *
                    trade.PlannedRR.Value,
                    2);
        }

        // Fallback only for files without recorded risk/RR.
        if (trade.InitialRiskUSD <= 0 ||
            !trade.PlannedRR.HasValue)
        {
            var planned =
                RiskRewardEngine.CalculatePlannedRiskReward(
                    trade.Side,
                    trade.EntryPrice,
                    trade.InitialStopLoss,
                    trade.InitialTakeProfit,
                    trade.Quantity,
                    spec);

            if (trade.InitialRiskUSD <= 0 &&
                trade.InitialStopLoss.HasValue &&
                planned.PlannedRiskUSD > 0)
            {
                trade.InitialRiskUSD =
                    planned.PlannedRiskUSD;
            }

            if (trade.ExpectedRewardUSD <= 0 &&
                planned.PlannedRewardUSD > 0)
            {
                trade.ExpectedRewardUSD =
                    planned.PlannedRewardUSD;
            }

            if (!trade.PlannedRR.HasValue &&
                trade.InitialTakeProfit.HasValue &&
                trade.InitialStopLoss.HasValue)
            {
                trade.PlannedRR =
                    planned.PlannedRR;
            }
        }

        // ============================================================
        // REALIZED TICKS
        // Supports:
        //   TotalRealizedTicks
        //   Total Net Ticks
        // ============================================================

        if (TryDecimal(
                Get(
                    kv,
                    "TotalRealizedTicks",
                    Get(
                        kv,
                        "Total Net Ticks",
                        Get(kv, "RealizedTicks", ""))),
                out var realizedTicks))
        {
            trade.RealizedTicks = realizedTicks;
        }

        if (trade.RealizedTicks != 0 &&
            spec.TickSize > 0)
        {
            trade.RealizedPoints =
                trade.RealizedTicks *
                spec.TickSize;
        }
        else if (trade.EntryPrice != 0 &&
                 trade.ExitPrice != 0)
        {
            trade.RealizedPoints =
                trade.Side == TradeSide.Long
                    ? trade.ExitPrice - trade.EntryPrice
                    : trade.EntryPrice - trade.ExitPrice;
        }

        // ============================================================
        // REALIZED R
        // ============================================================

        if (TryDecimal(
                Get(kv, "RealizedR", ""),
                out var realizedR))
        {
            trade.RealizedRR = realizedR;
        }

        // ============================================================
        // GROSS P&L
        // Supports:
        //   GrossRealizedPnL
        //   Gross Realized PnL
        //   Total PnL
        // ============================================================

        if (TryDecimal(
                Get(
                    kv,
                    "GrossRealizedPnL",
                    Get(
                        kv,
                        "Gross Realized PnL",
                        Get(
                            kv,
                            "Total PnL",
                            Get(kv, "GrossPnL", "")))),
                out var grossPnL))
        {
            trade.GrossPnL = grossPnL;
        }

        // ============================================================
        // COMMISSION
        // ============================================================

        if (TryDecimal(
                Get(kv, "Commission", ""),
                out var commission))
        {
            trade.Commission = commission;
        }

        // ============================================================
        // NET P&L
        // If Recorder explicitly supplies NetRealizedPnL, use it.
        // If old Recorder has only Total PnL / Gross PnL, use that.
        // Never silently turn a valid legacy P&L into zero.
        // ============================================================

        if (TryDecimal(
                Get(
                    kv,
                    "NetRealizedPnL",
                    Get(
                        kv,
                        "Net PnL",
                        Get(kv, "NetPnL", ""))),
                out var netPnL))
        {
            trade.NetPnL = netPnL;
        }
        else
        {
            trade.NetPnL = trade.GrossPnL;
        }

        // ============================================================
        // MAE / MFE
        // Only files that actually contain them get these values.
        // ============================================================

        if (TryDecimal(
                Get(
                    kv,
                    "MAE_Ticks",
                    Get(kv, "MAETicks", "")),
                out var maeTicks))
        {
            trade.MAETicks = maeTicks;
        }

        if (TryDecimal(
                Get(
                    kv,
                    "MAE_USD",
                    Get(kv, "MAEUSD", "")),
                out var maeUsd))
        {
            trade.MAEUSD = maeUsd;
        }

        if (TryDecimal(
                Get(
                    kv,
                    "MFE_Ticks",
                    Get(kv, "MFETicks", "")),
                out var mfeTicks))
        {
            trade.MFETicks = mfeTicks;
        }

        if (TryDecimal(
                Get(
                    kv,
                    "MFE_USD",
                    Get(kv, "MFEUSD", "")),
                out var mfeUsd))
        {
            trade.MFEUSD = mfeUsd;
        }

        // ============================================================
        // ACTIVITY LOG
        // ============================================================

        var logPath = Path.Combine(
            directoryPath,
            "TradeActivity.log");

        if (File.Exists(logPath))
        {
            foreach (var line in await File.ReadAllLinesAsync(
                         logPath,
                         ct))
            {
                var ev = ParseLogLine(
                    line,
                    trade.EntryTime,
                    trade.PreRollSeconds,
                    trade.TradeId);

                if (ev is not null)
                    trade.EventTimeline.Add(ev);
            }
        }

        if (trade.EventTimeline.All(
                e => e.EventType != TradeEventType.EntryFilled))
        {
            trade.EventTimeline.Insert(
                0,
                NewEvent(
                    trade,
                    trade.EntryTime,
                    TradeEventType.EntryFilled,
                    trade.EntryPrice,
                    trade.Quantity,
                    $"Entry Fill @ {trade.EntryPrice}"));
        }

        if (trade.EventTimeline.All(
                e => e.EventType != TradeEventType.ExitFilled))
        {
            trade.EventTimeline.Add(
                NewEvent(
                    trade,
                    trade.ExitTime,
                    TradeEventType.ExitFilled,
                    trade.ExitPrice,
                    trade.Quantity,
                    $"Exit Fill @ {trade.ExitPrice}"));
        }

        // ============================================================
        // VIDEOS
        // ============================================================

        for (var i = 0; i < videoPaths.Count; i++)
        {
            trade.Videos.Add(
                new TradeVideo
                {
                    TradeId = trade.TradeId,
                    MonitorIndex = i,
                    MonitorName = $"Monitor {i + 1}",
                    VideoPath = videoPaths[i],
                    DurationSeconds =
                        trade.PreRollSeconds +
                        trade.Duration.TotalSeconds +
                        trade.PostRollSeconds
                });
        }

        return trade;
    }

    private static string ResolveSymbol(
        Dictionary<string, string> kv)
    {
        var root = Get(kv, "RootSymbol", "");

        if (!string.IsNullOrWhiteSpace(root))
            return NormalizeSymbol(root);

        var symbol = Get(kv, "Symbol", "");

        if (!string.IsNullOrWhiteSpace(symbol))
            return NormalizeSymbol(symbol);

        var instrument = Get(kv, "Instrument", "");

        if (string.IsNullOrWhiteSpace(instrument))
            return "6E";

        // E-mini S&P 500 (ES)
        var match = Regex.Match(
            instrument,
            @"\(([A-Z0-9]+)\)",
            RegexOptions.IgnoreCase);

        if (match.Success)
            return NormalizeSymbol(
                match.Groups[1].Value);

        // Common futures descriptions.
        if (instrument.Contains(
                "Euro FX",
                StringComparison.OrdinalIgnoreCase))
        {
            return "6E";
        }

        if (instrument.Contains(
                "S&P 500",
                StringComparison.OrdinalIgnoreCase))
        {
            return "ES";
        }

        // Contract style: ES SEP26 / 6E SEP26
        var token = instrument
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(token)
            ? "6E"
            : NormalizeSymbol(token);
    }

    private static string NormalizeSymbol(
        string value)
    {
        var text = value.Trim().ToUpperInvariant();

        var match = Regex.Match(
            text,
            @"\b(6E|ES|NQ|MES|MNQ|CL|GC|RTY|ZB)\b");

        return match.Success
            ? match.Groups[1].Value
            : text
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault("6E");
    }

    private static bool IsVideo(string path)
    {
        return path.EndsWith(
                   ".mp4",
                   StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(
                   ".mkv",
                   StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(
                   ".avi",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string Get(
        Dictionary<string, string> kv,
        string key,
        string fallback)
    {
        return kv.TryGetValue(
                   key,
                   out var value) &&
               !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static int ParseInt(
        string value,
        int fallback)
    {
        return int.TryParse(
                   value,
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out var number)
            ? Math.Max(1, number)
            : fallback;
    }

    private static decimal ParseDecimal(
        string value)
    {
        return TryDecimal(
                   value,
                   out var number)
            ? number
            : 0m;
    }

    private static bool TryDecimal(
        string value,
        out decimal result)
    {
        result = 0m;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim()
            .Replace("$", "")
            .Replace("€", "")
            .Replace("£", "")
            .Replace("USD", "",
                StringComparison.OrdinalIgnoreCase)
            .Replace(",", "")
            .Replace("Ticks", "",
                StringComparison.OrdinalIgnoreCase)
            .Replace("Tick", "",
                StringComparison.OrdinalIgnoreCase)
            .Trim();

        return decimal.TryParse(
            normalized,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static double ParseDouble(
        string value,
        double fallback)
    {
        return double.TryParse(
                   value,
                   NumberStyles.Any,
                   CultureInfo.InvariantCulture,
                   out var number)
            ? number
            : fallback;
    }

    private static DateTime ParseDate(
        string value,
        DateTime fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var formats = new[]
        {
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss",
            "O"
        };

        if (DateTime.TryParseExact(
                value,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal |
                DateTimeStyles.AllowWhiteSpaces,
                out var exact))
        {
            return exact;
        }

        return DateTime.TryParse(
                   value,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeLocal |
                   DateTimeStyles.AllowWhiteSpaces,
                   out var parsed)
            ? parsed
            : fallback;
    }

    private static TradeEvent NewEvent(
        Trade trade,
        DateTime timestamp,
        TradeEventType type,
        decimal price,
        int quantity,
        string message)
    {
        return new TradeEvent
        {
            TradeId = trade.TradeId,
            Timestamp = timestamp,
            EventType = type,
            Price = price,
            Quantity = quantity,
            Message = message,
            VideoTimestampOffsetSeconds =
                Math.Max(
                    0,
                    trade.PreRollSeconds +
                    (timestamp - trade.EntryTime)
                        .TotalSeconds)
        };
    }

    private static TradeEvent? ParseLogLine(
        string line,
        DateTime entryTime,
        double preRoll,
        string tradeId)
    {
        var match = Regex.Match(
            line,
            @"^\[(.*?)\]\s*\[(.*?)\]\s*(.*)$");

        if (!match.Success)
            return null;

        if (!DateTime.TryParse(
                match.Groups[1].Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var timestamp))
        {
            return null;
        }

        var raw =
            match.Groups[2].Value.ToUpperInvariant();

        var type =
            raw.Contains("ENTRY")
                ? TradeEventType.EntryFilled
                : raw.Contains("EXIT")
                    ? TradeEventType.ExitFilled
                    : raw.Contains("STOP")
                        ? TradeEventType.StopLossModified
                        : raw.Contains("TARGET")
                            ? TradeEventType.TakeProfitModified
                            : raw.Contains("ORDER")
                                ? TradeEventType.OrderSubmitted
                                : TradeEventType.GenericEvent;

        return new TradeEvent
        {
            TradeId = tradeId,
            Timestamp = timestamp,
            EventType = type,
            Message = match.Groups[3].Value,
            VideoTimestampOffsetSeconds =
                Math.Max(
                    0,
                    preRoll +
                    (timestamp - entryTime).TotalSeconds)
        };
    }
}
