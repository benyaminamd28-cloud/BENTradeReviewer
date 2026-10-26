using System.Globalization;
using System.Text.RegularExpressions;
using BENTradeReviewer.Core.Enums;
using BENTradeReviewer.Core.Models;
using BENTradeReviewer.Data;

namespace BENTradeReviewer.Services;

public sealed class RecorderImporter
{
    private readonly TradeRepository _repository;

    // Import diagnostics from the most recent scan.
    public List<string> LastImportErrors { get; } = new();

    public int LastImportFailedCount => LastImportErrors.Count;

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

        LastImportErrors.Clear();

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
                .OrderBy(
                    Path.GetFileName,
                    StringComparer.OrdinalIgnoreCase)
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
                var message =
                    $"{Path.GetFileName(dir)}: {ex.Message}";

                LastImportErrors.Add(message);

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
            throw new InvalidDataException(
                $"Final Recorder TradeInfo.txt is missing: {directoryPath}");
        }

        var lines = await File.ReadAllLinesAsync(
            tradeInfoPath,
            ct);

        var kv = ParseKeyValueLines(lines);

        // ============================================================
        // SMART EXECUTION FALLBACK
        //
        // Some newer Recorder TradeInfo files can contain UNKNOWN/0
        // for Entry fields even though TradeExecutions.csv contains
        // the actual Entry executions.
        //
        // TradeExecutions.csv is used only to repair missing/unknown
        // Entry fields. TradeInfo remains the primary source.
        // ============================================================

        var executionsPath = Path.Combine(
            directoryPath,
            "TradeExecutions.csv");

        RepairEntryFieldsFromExecutions(
            kv,
            executionsPath);

        // ============================================================
        // FINAL RECORDER FORMAT ONLY
        // No legacy fallback.
        // No silent defaults.
        // ============================================================

        var tradeId = RequiredKey(
            kv,
            "TradeId",
            directoryPath);

        // ============================================================
        // IGNORE NON-TRADE RECORDER FOLDERS
        //
        // Recorder can generate a final TradeInfo.txt for a position
        // lifecycle that never produced a completed entry.
        //
        // These folders are not failed trades and must not be imported.
        // Current Recorder marker:
        //   TotalEntryQuantity = 0
        //   InitialEntryTime   = UNKNOWN
        // ============================================================

        if (kv.TryGetValue(
                "TotalEntryQuantity",
                out var rawQuantityText) &&
            int.TryParse(
                rawQuantityText.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var rawQuantity) &&
            rawQuantity == 0 &&
            kv.TryGetValue(
                "InitialEntryTime",
                out var rawInitialEntryTime) &&
            rawInitialEntryTime.Trim().Equals(
                "UNKNOWN",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // ============================================================
        // ROOT SYMBOL
        //
        // New Recorder format provides RootSymbol directly.
        // Legacy Recorder files may only provide Instrument, e.g.:
        //   ES SEP26
        //   E-mini S&P 500 (ES)
        //   6E SEP26
        //   Euro FX (6E)
        //
        // Prefer RootSymbol. Fall back to a deterministic extraction
        // from Instrument for legacy folders.
        // ============================================================

        var rootSymbol = string.Empty;

        if (kv.TryGetValue("RootSymbol", out var rootSymbolValue) &&
            !string.IsNullOrWhiteSpace(rootSymbolValue))
        {
            rootSymbol = rootSymbolValue.Trim();
        }
        else
        {
            var instrument = RequiredKey(
                kv,
                "Instrument",
                tradeId);

            rootSymbol = ExtractRootSymbolFromInstrument(
                instrument,
                tradeId);
        }

        var symbol = NormalizeFinalSymbol(
            rootSymbol,
            tradeId);

        var account = RequiredKey(
            kv,
            "Account",
            tradeId);

        var direction = RequiredKey(
            kv,
            "Direction",
            tradeId);

        TradeSide side;

        if (direction.Equals(
                "LONG",
                StringComparison.OrdinalIgnoreCase))
        {
            side = TradeSide.Long;
        }
        else if (direction.Equals(
                     "SHORT",
                     StringComparison.OrdinalIgnoreCase))
        {
            side = TradeSide.Short;
        }
        else
        {
            throw new InvalidDataException(
                $"Invalid Direction '{direction}' in trade '{tradeId}'.");
        }

        var quantity = RequiredPositiveInt(
            kv,
            "TotalEntryQuantity",
            tradeId);

        var entryPrice = RequiredDecimal(
            kv,
            "WeightedAverageEntryPrice",
            tradeId);

        var exitPrice = RequiredDecimal(
            kv,
            "WeightedAverageExitPrice",
            tradeId);

        var initialStop = OptionalDecimal(
            kv,
            "InitialStopPrice",
            0m);

        var entryTime = RequiredDate(
            kv,
            "InitialEntryTime",
            tradeId);

        var exitTime = RequiredDate(
            kv,
            "FinalExitTime",
            tradeId);

        var initialRiskUsd = OptionalMoney(
            kv,
            "InitialRiskUSD",
            0m);

        var realizedTicks = RequiredDecimal(
            kv,
            "TotalRealizedTicks",
            tradeId);

        var grossPnl = RequiredMoney(
            kv,
            "GrossRealizedPnL",
            tradeId);

        var realizedR = OptionalDecimal(
            kv,
            "RealizedR",
            0m);

        var maeTicks = OptionalDecimal(
            kv,
            "MAE_Ticks",
            0m);

        var maeUsd = OptionalMoney(
            kv,
            "MAE_USD",
            0m);

        var mfeTicks = OptionalDecimal(
            kv,
            "MFE_Ticks",
            0m);

        var mfeUsd = OptionalMoney(
            kv,
            "MFE_USD",
            0m);

        var tickSize = RequiredDecimal(
            kv,
            "TickSize",
            tradeId);

        var tickValue = RequiredMoney(
            kv,
            "TickValue",
            tradeId);

        if (tickSize <= 0)
        {
            throw new InvalidDataException(
                $"TickSize must be > 0 in trade '{tradeId}'.");
        }

        if (tickValue <= 0)
        {
            throw new InvalidDataException(
                $"TickValue must be > 0 in trade '{tradeId}'.");
        }

        // ============================================================
        // TARGETS
        // Final format can contain multiple InitialTarget lines.
        // Keep existing target/R aggregation behavior for now.
        // ============================================================

        decimal weightedR = 0m;
        int weightedQuantity = 0;
        decimal firstTargetPrice = 0m;

        // ============================================================
        // FINAL RECORDER TARGET CONTRACT
        //
        // The first contiguous Target1/Target2/Target3 block is the
        // original planned target set for the initial position.
        //
        // Once Recorder emits an "Entry" line, the initial target
        // block is finished. Later Target lines belong to subsequent
        // position changes and must NOT change the original Planned R:R.
        // ============================================================

        bool initialTargetBlockStarted = false;
        bool initialTargetBlockFinished = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (!line.StartsWith(
                    "InitialTarget:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var colon = line.IndexOf(':');

            if (colon < 0)
                continue;

            var payload = line[(colon + 1)..].Trim();

            var parts = payload.Split(
                '|',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

            var targetType = parts.Length > 0
                ? parts[0].Trim()
                : string.Empty;

            // "Entry" marks the end of the original target plan.
            if (targetType.Equals(
                    "Entry",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (initialTargetBlockStarted)
                    initialTargetBlockFinished = true;

                continue;
            }

            if (!targetType.StartsWith(
                    "Target",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (initialTargetBlockFinished)
                continue;

            initialTargetBlockStarted = true;

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
                    if (!int.TryParse(
                            p["Qty=".Length..].Trim(),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out qty) ||
                        qty <= 0)
                    {
                        throw new InvalidDataException(
                            $"Invalid initial target quantity in trade '{tradeId}': {p}");
                    }
                }
                else if (p.StartsWith(
                             "Price=",
                             StringComparison.OrdinalIgnoreCase))
                {
                    hasPrice = TryDecimalStrict(
                        p["Price=".Length..].Trim(),
                        out targetPrice);
                }
                else if (p.StartsWith(
                             "R=",
                             StringComparison.OrdinalIgnoreCase))
                {
                    hasR = TryDecimalStrict(
                        p["R=".Length..].Trim(),
                        out targetR);
                }
            }

            if (hasPrice && firstTargetPrice == 0m)
                firstTargetPrice = targetPrice;

            if (hasR)
            {
                weightedR += targetR * qty;
                weightedQuantity += qty;
            }
        }
        if (initialTargetBlockStarted && weightedQuantity <= 0)
        {
            throw new InvalidDataException(
                $"Initial target block contains no valid target quantity/R " +
                $"data in trade '{tradeId}'.");
        }

        var initialPlannedRR =
            weightedQuantity > 0
                ? Math.Round(
                    weightedR / weightedQuantity,
                    4)
                : 0m;

        if (firstTargetPrice > 0m)
        {
            // assigned after Trade is created
        }
        var trade = new Trade
        {
            FolderDirectory = directoryPath,
            TradeId = tradeId,
            Symbol = symbol,
            Account = account,
            Side = side,
            Quantity = quantity,
            EntryTime = entryTime,
            ExitTime = exitTime,
            EntryPrice = entryPrice,
            ExitPrice = exitPrice,
            InitialStopLoss = initialStop,
            PreRollSeconds = RequiredOptionalDouble(
                kv,
                "PreRollSeconds",
                5,
                tradeId),
            PostRollSeconds = RequiredOptionalDouble(
                kv,
                "PostRollSeconds",
                5,
                tradeId),

            InitialRiskUSD = initialRiskUsd,
            RealizedTicks = realizedTicks,
            GrossPnL = grossPnl,
            RealizedRR = realizedR,

            MAETicks = maeTicks,
            MAEUSD = maeUsd,
            MFETicks = mfeTicks,
            MFEUSD = mfeUsd
        };

        if (firstTargetPrice > 0)
            trade.InitialTakeProfit = firstTargetPrice;

        if (weightedQuantity > 0)
        {
            trade.PlannedRR = initialPlannedRR;

            if (trade.InitialRiskUSD > 0)
            {
                trade.ExpectedRewardUSD = Math.Round(
                    trade.InitialRiskUSD *
                    trade.PlannedRR.Value,
                    2);
            }
        }

        trade.RealizedPoints = Math.Round(
            trade.RealizedTicks * tickSize,
            8);

        // Final Recorder currently reports NetRealizedPnL as UNKNOWN.
        // Do not fabricate a separate commission calculation here.
        //
        // Existing application semantics continue to expose Gross P&L
        // through the NetPnL field until the explicit NetPnL/commission
        // data contract is handled in the next integrity pass.
        trade.NetPnL = trade.GrossPnL;

        // Validate InstrumentSpec against actual Recorder tick data.
        var existingSpec =
            await _repository.GetInstrumentSpecAsync(symbol);

        if (existingSpec is null)
        {
            var catalogSpec =
                InstrumentSpec
                    .GetDefaultCatalog()
                    .FirstOrDefault(
                        s => s.Symbol.Equals(
                            symbol,
                            StringComparison.OrdinalIgnoreCase));

            if (catalogSpec is not null)
            {
                existingSpec = catalogSpec;
            }
        }

        var spec = existingSpec ??
                   new InstrumentSpec
                   {
                       Symbol = symbol,
                       Name = symbol,
                       TickSize = tickSize,
                       TickValue = tickValue,
                       PointValue = tickValue / tickSize,
                       DefaultCommission = 0m
                   };

        // Final Recorder is authoritative for contract economics.
        if (spec.TickSize != tickSize ||
            spec.TickValue != tickValue)
        {
            spec.TickSize = tickSize;
            spec.TickValue = tickValue;
            spec.PointValue =
                tickSize > 0
                    ? tickValue / tickSize
                    : 0;
        }

        await _repository.UpsertInstrumentSpecAsync(spec);

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
        // VIDEO
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

    private static void RepairEntryFieldsFromExecutions(
        Dictionary<string, string> kv,
        string executionsPath)
    {
        if (!File.Exists(executionsPath))
            return;

        if (!TryGetSmartValue(
                kv,
                "TotalEntryQuantity",
                out var existingQuantity))
        {
            existingQuantity = string.Empty;
        }

        if (!TryGetSmartValue(
                kv,
                "WeightedAverageEntryPrice",
                out var existingEntryPrice))
        {
            existingEntryPrice = string.Empty;
        }

        if (!TryGetSmartValue(
                kv,
                "InitialEntryTime",
                out var existingEntryTime))
        {
            existingEntryTime = string.Empty;
        }

        var entryIsMissing =
            string.IsNullOrWhiteSpace(existingQuantity) ||
            existingQuantity.Trim() == "0" ||
            existingQuantity.Equals(
                "UNKNOWN",
                StringComparison.OrdinalIgnoreCase);

        var entryPriceIsMissing =
            string.IsNullOrWhiteSpace(existingEntryPrice) ||
            existingEntryPrice.Trim().Equals(
                "UNKNOWN",
                StringComparison.OrdinalIgnoreCase);

        var entryTimeIsMissing =
            string.IsNullOrWhiteSpace(existingEntryTime) ||
            existingEntryTime.Trim().Equals(
                "UNKNOWN",
                StringComparison.OrdinalIgnoreCase);

        if (!entryIsMissing &&
            !entryPriceIsMissing &&
            !entryTimeIsMissing)
        {
            return;
        }

        var rows = File.ReadAllLines(executionsPath);

        if (rows.Length < 2)
            return;

        var headers = SplitCsvLine(rows[0]);

        var orderNameIndex =
            FindColumn(headers, "OrderName");

        var timeIndex =
            FindColumn(headers, "Time");

        var quantityIndex =
            FindColumn(headers, "Quantity");

        var priceIndex =
            FindColumn(headers, "Price");

        if (orderNameIndex < 0 ||
            timeIndex < 0 ||
            quantityIndex < 0 ||
            priceIndex < 0)
        {
            return;
        }

        var totalQuantity = 0;
        decimal weightedPrice = 0m;
        DateTime? firstEntryTime = null;

        for (var i = 1; i < rows.Length; i++)
        {
            var fields = SplitCsvLine(rows[i]);

            if (fields.Count <= Math.Max(
                    Math.Max(orderNameIndex, timeIndex),
                    Math.Max(quantityIndex, priceIndex)))
            {
                continue;
            }

            var orderName =
                fields[orderNameIndex].Trim();

            // Recorder currently writes Entry executions with
            // OrderName="Entry" even when IsEntry/FillRole are wrong.
            if (!orderName.Equals(
                    "Entry",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(
                    fields[quantityIndex].Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var quantity) ||
                quantity <= 0)
            {
                continue;
            }

            if (!TryDecimalStrict(
                    fields[priceIndex].Trim(),
                    out var price))
            {
                continue;
            }

            if (!DateTime.TryParse(
                    fields[timeIndex].Trim(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal |
                    DateTimeStyles.AllowWhiteSpaces,
                    out var timestamp))
            {
                continue;
            }

            totalQuantity += quantity;
            weightedPrice += price * quantity;

            if (!firstEntryTime.HasValue ||
                timestamp < firstEntryTime.Value)
            {
                firstEntryTime = timestamp;
            }
        }

        if (totalQuantity <= 0)
            return;

        if (entryIsMissing)
        {
            kv[NormalizeFieldKey("TotalEntryQuantity")] =
                totalQuantity.ToString(
                    CultureInfo.InvariantCulture);
        }

        if (entryPriceIsMissing)
        {
            kv[NormalizeFieldKey("WeightedAverageEntryPrice")] =
                (weightedPrice / totalQuantity).ToString(
                    "0.########",
                    CultureInfo.InvariantCulture);
        }

        if (entryTimeIsMissing &&
            firstEntryTime.HasValue)
        {
            kv[NormalizeFieldKey("InitialEntryTime")] =
                firstEntryTime.Value.ToString(
                    "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture);
        }
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (ch == ',' && !quoted)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        result.Add(current.ToString());

        return result;
    }

    private static int FindColumn(
        IReadOnlyList<string> headers,
        string name)
    {
        var normalizedTarget =
            NormalizeFieldKey(name);

        for (var i = 0; i < headers.Count; i++)
        {
            if (NormalizeFieldKey(headers[i])
                .Equals(
                    normalizedTarget,
                    StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
    private static Dictionary<string, string> ParseKeyValueLines(
        IEnumerable<string> lines)
    {
        var kv = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var idx = line.IndexOf(':');

            if (idx <= 0)
                continue;

            var key = NormalizeFieldKey(line[..idx]);
            var value = line[(idx + 1)..].Trim();

            if (key.Length == 0)
                continue;

            kv[key] = value;
        }

        return kv;
    }

    private static string NormalizeFieldKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        return Regex.Replace(
            key.Trim(),
            @"[^A-Za-z0-9]",
            "");
    }

    private static IEnumerable<string> GetFieldAliases(
        string key)
    {
        var normalized = NormalizeFieldKey(key);

        return normalized switch
        {
            "TradeId" => new[]
            {
                "TradeID"
            },

            "RootSymbol" => new[]
            {
                "Symbol",
                "Root"
            },

            "TotalEntryQuantity" => new[]
            {
                "TotalEntryQty",
                "EntryQuantity",
                "EntryQty",
                "TotalQuantity",
                "InitialQuantity"
            },

            "WeightedAverageEntryPrice" => new[]
            {
                "AvgEntryPrice",
                "AverageEntryPrice",
                "EntryPrice",
                "InitialEntryPrice"
            },

            "WeightedAverageExitPrice" => new[]
            {
                "AvgExitPrice",
                "AverageExitPrice",
                "ExitPrice"
            },

            "InitialStopPrice" => new[]
            {
                "StopPrice",
                "InitialStop",
                "InitialStopLoss"
            },

            "InitialEntryTime" => new[]
            {
                "EntryTime"
            },

            "FinalExitTime" => new[]
            {
                "ExitTime"
            },

            "InitialRiskUSD" => new[]
            {
                "InitialRisk",
                "RiskUSD",
                "Risk"
            },

            "TotalRealizedTicks" => new[]
            {
                "TotalNetTicks",
                "RealizedTicks",
                "RealizedTick",
                "TotalTicks",
                "TotalNetTick",
                "NetTicks"
            },

            "GrossRealizedPnL" => new[]
            {
                "GrossPnL",
                "GrossRealizedPnl",
                "TotalPnL",
                "TotalPnl",
                "RealizedPnL",
                "PnL"
            },

            "RealizedR" => new[]
            {
                "RealizedRR",
                "RR"
            },

            "MAETicks" => new[]
            {
                "MAETick"
            },

            "MAEUSD" => new[]
            {
                "MAE"
            },

            "MFETicks" => new[]
            {
                "MFETick"
            },

            "MFEUSD" => new[]
            {
                "MFE"
            },

            "TickSize" => new[]
            {
                "Ticksize"
            },

            "TickValue" => new[]
            {
                "Tickvalue"
            },

            "PreRollSeconds" => new[]
            {
                "PreRoll"
            },

            "PostRollSeconds" => new[]
            {
                "PostRoll"
            },

            _ => Array.Empty<string>()
        };
    }

    private static bool TryGetSmartValue(
        Dictionary<string, string> kv,
        string key,
        out string value)
    {
        var normalizedKey = NormalizeFieldKey(key);

        // 1. Exact normalized key.
        if (kv.TryGetValue(normalizedKey, out value!) &&
            !string.IsNullOrWhiteSpace(value))
        {
            value = value.Trim();
            return true;
        }

        // 2. Explicit semantic aliases.
        foreach (var alias in GetFieldAliases(normalizedKey))
        {
            var normalizedAlias = NormalizeFieldKey(alias);

            if (kv.TryGetValue(normalizedAlias, out value!) &&
                !string.IsNullOrWhiteSpace(value))
            {
                value = value.Trim();
                return true;
            }
        }

        // 3. Final normalized comparison against every field actually
        // present in the Recorder file.
        //
        // This protects against harmless formatting/name variations
        // without depending on line order.
        foreach (var pair in kv)
        {
            var existingKey = NormalizeFieldKey(pair.Key);

            if (existingKey.Equals(
                    normalizedKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(pair.Value))
                {
                    value = pair.Value.Trim();
                    return true;
                }
            }

            foreach (var alias in GetFieldAliases(normalizedKey))
            {
                var normalizedAlias =
                    NormalizeFieldKey(alias);

                if (existingKey.Equals(
                        normalizedAlias,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(pair.Value))
                    {
                        value = pair.Value.Trim();
                        return true;
                    }
                }
            }
        }

        value = string.Empty;
        return false;
    }
    private static string RequiredKey(
        Dictionary<string, string> kv,
        string key,
        string tradeId)
    {
        if (TryGetSmartValue(kv, key, out var value))
            return value;

        throw new InvalidDataException(
            $"Required Recorder field '{key}' is missing in trade '{tradeId}'.");
    }

    private static string OptionalKey(
        Dictionary<string, string> kv,
        string key,
        string fallback)
    {
        return TryGetSmartValue(kv, key, out var value)
            ? value
            : fallback;
    }

    private static string ExtractRootSymbolFromInstrument(
        string instrument,
        string tradeId)
    {
        var text = instrument.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException(
                $"Instrument is empty in trade '{tradeId}'.");
        }

        // Examples:
        // ES SEP26
        // 6E SEP26
        var firstToken = text
            .Split(
                new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(firstToken) &&
            Regex.IsMatch(firstToken, @"^[A-Z0-9]+$"))
        {
            return firstToken;
        }

        // Examples:
        // E-mini S&P 500 (ES)
        // Euro FX (6E)
        var match = Regex.Match(
            text,
            @"\(([A-Z0-9]+)\)\s*$");

        if (match.Success)
            return match.Groups[1].Value;

        throw new InvalidDataException(
            $"Unable to derive RootSymbol from Instrument '{instrument}' " +
            $"in trade '{tradeId}'.");
    }
    private static string NormalizeFinalSymbol(
        string value,
        string tradeId)
    {
        var text = value.Trim().ToUpperInvariant();

        if (!Regex.IsMatch(text, @"^[A-Z0-9]+$"))
        {
            throw new InvalidDataException(
                $"Invalid RootSymbol '{value}' in trade '{tradeId}'.");
        }

        return text;
    }

    private static decimal OptionalDecimal(
        Dictionary<string, string> kv,
        string key,
        decimal fallback)
    {
        if (!TryGetSmartValue(kv, key, out var text))
            return fallback;

        return TryDecimalStrict(text, out var value)
            ? value
            : fallback;
    }

    private static decimal OptionalMoney(
        Dictionary<string, string> kv,
        string key,
        decimal fallback)
    {
        if (!TryGetSmartValue(kv, key, out var text))
            return fallback;

        if (text.Equals(
                "UNKNOWN",
                StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        return TryDecimalStrict(text, out var value)
            ? value
            : fallback;
    }
    private static int RequiredPositiveInt(
        Dictionary<string, string> kv,
        string key,
        string tradeId)
    {
        var text = RequiredKey(kv, key, tradeId);

        if (!int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value) ||
            value <= 0)
        {
            throw new InvalidDataException(
                $"Required positive integer '{key}' is invalid in trade '{tradeId}': '{text}'.");
        }

        return value;
    }

    private static decimal RequiredDecimal(
        Dictionary<string, string> kv,
        string key,
        string tradeId)
    {
        var text = RequiredKey(kv, key, tradeId);

        if (!TryDecimalStrict(text, out var value))
        {
            throw new InvalidDataException(
                $"Required decimal '{key}' is invalid in trade '{tradeId}': '{text}'.");
        }

        return value;
    }

    private static decimal RequiredMoney(
        Dictionary<string, string> kv,
        string key,
        string tradeId)
    {
        var text = RequiredKey(kv, key, tradeId);

        if (text.Equals(
                "UNKNOWN",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Required monetary field '{key}' is UNKNOWN in trade '{tradeId}'.");
        }

        if (!TryDecimalStrict(text, out var value))
        {
            throw new InvalidDataException(
                $"Required monetary field '{key}' is invalid in trade '{tradeId}': '{text}'.");
        }

        return value;
    }

    private static DateTime RequiredDate(
        Dictionary<string, string> kv,
        string key,
        string tradeId)
    {
        var text = RequiredKey(kv, key, tradeId);

        var formats = new[]
        {
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss",
            "O"
        };

        if (DateTime.TryParseExact(
                text,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal |
                DateTimeStyles.AllowWhiteSpaces,
                out var exact))
        {
            return exact;
        }

        if (DateTime.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal |
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
        {
            return parsed;
        }

        throw new InvalidDataException(
            $"Required date '{key}' is invalid in trade '{tradeId}': '{text}'.");
    }

    private static double RequiredOptionalDouble(
        Dictionary<string, string> kv,
        string key,
        double fallback,
        string tradeId)
    {
        if (!kv.TryGetValue(key, out var text) ||
            string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (!double.TryParse(
                text.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value) ||
            value < 0)
        {
            throw new InvalidDataException(
                $"Field '{key}' is invalid in trade '{tradeId}': '{text}'.");
        }

        return value;
    }

    private static bool TryDecimalStrict(
        string text,
        out decimal result)
    {
        result = 0m;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim()
            .Replace("$", "")
            .Replace("€", "")
            .Replace("£", "")
            .Replace(",", "")
            .Replace("USD", "",
                StringComparison.OrdinalIgnoreCase)
            .Replace("Ticks", "",
                StringComparison.OrdinalIgnoreCase)
            .Replace("Tick", "",
                StringComparison.OrdinalIgnoreCase)
            .Trim();

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign |
            NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowLeadingWhite |
            NumberStyles.AllowTrailingWhite,
            CultureInfo.InvariantCulture,
            out result);
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
