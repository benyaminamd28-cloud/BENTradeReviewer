using System.Globalization;
using System.IO;
using System.Text.Json;

namespace BENTradeReviewer.UI.MetaTrader;

public sealed class MetaTraderImportResult
{
    public int FilesFound { get; set; }

    public int Imported { get; set; }

    public int Failed { get; set; }

    public List<string> Errors { get; } = new();
}

public sealed class MetaTraderImporter
{
    private readonly MetaTraderRepository _repository;

    public MetaTraderImporter(
        MetaTraderRepository repository)
    {
        _repository = repository;
    }

    public MetaTraderImportResult ImportFolder(
        string folder)
    {
        var result =
            new MetaTraderImportResult();

        if (!Directory.Exists(folder))
        {
            result.Errors.Add(
                $"Folder does not exist: {folder}");

            result.Failed = 1;

            return result;
        }

        var files =
            Directory
                .EnumerateFiles(
                    folder,
                    "*.json",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(x => x)
                .ToList();

        result.FilesFound =
            files.Count;

        foreach (var file in files)
        {
            try
            {
                var trade =
                    ReadTradeFromJson(file);

                ValidateTrade(trade);

                _repository.UpsertTrade(trade);

                result.Imported++;
            }
            catch (Exception ex)
            {
                result.Failed++;

                result.Errors.Add(
                    $"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return result;
    }

    private static MetaTraderTrade ReadTradeFromJson(
        string file)
    {
        using var document =
            JsonDocument.Parse(
                File.ReadAllText(file));

        var root =
            document.RootElement;

        var trade =
            new MetaTraderTrade
            {
                TradeId =
                    GetString(
                        root,
                        "tradeId"),

                PositionId =
                    GetInt64(
                        root,
                        "positionId"),

                Symbol =
                    GetString(
                        root,
                        "symbol"),

                Side =
                    GetString(
                        root,
                        "side"),

                Quantity =
                    GetDecimal(
                        root,
                        "quantity"),

                EntryTime =
                    GetDateTime(
                        root,
                        "entryTime"),

                EntryPrice =
                    GetDecimal(
                        root,
                        "entryPrice"),

                StopLoss =
                    GetNullableDecimal(
                        root,
                        "stopLoss"),

                TakeProfit =
                    GetNullableDecimal(
                        root,
                        "takeProfit"),

                // IMPORTANT:
                // Read financial values explicitly from
                // the actual JSON contract.
                PnL =
                    GetDecimal(
                        root,
                        "pnl"),

                GrossPnl =
                    GetDecimal(
                        root,
                        "grossPnl"),

                Commission =
                    GetDecimal(
                        root,
                        "commission"),

                Swap =
                    GetDecimal(
                        root,
                        "swap"),

                Fee =
                    GetDecimal(
                        root,
                        "fee"),

                NetPnl =
                    GetDecimal(
                        root,
                        "netPnl"),

                Outcome =
                    GetString(
                        root,
                        "outcome")
            };

        // --------------------------------------------------------
        // Compatibility:
        // If grossPnl is absent/zero while pnl exists,
        // use pnl as gross.
        // --------------------------------------------------------

        if (trade.GrossPnl == 0m &&
            trade.PnL != 0m)
        {
            trade.GrossPnl =
                trade.PnL;
        }

        if (trade.PnL == 0m &&
            trade.GrossPnl != 0m)
        {
            trade.PnL =
                trade.GrossPnl;
        }

        // --------------------------------------------------------
        // Net is always reconstructed from explicit MT5 values.
        // --------------------------------------------------------

        trade.NetPnl =
            trade.GrossPnl
            + trade.Commission
            + trade.Swap
            + trade.Fee;

        trade.Outcome =
            trade.NetPnl > 0m
                ? "WIN"
                : trade.NetPnl < 0m
                    ? "LOSS"
                    : "BREAK EVEN";

        // --------------------------------------------------------
        // EXITS
        // --------------------------------------------------------

        trade.Exits =
            new List<MetaTraderExit>();

        if (root.TryGetProperty(
                "exits",
                out var exitsElement)
            &&
            exitsElement.ValueKind ==
                JsonValueKind.Array)
        {
            foreach (
                var exitElement
                in exitsElement.EnumerateArray())
            {
                var exit =
                    new MetaTraderExit
                    {
                        Number =
                            GetInt32(
                                exitElement,
                                "number"),

                        DealTicket =
                            GetInt64(
                                exitElement,
                                "dealTicket"),

                        OrderTicket =
                            GetInt64(
                                exitElement,
                                "orderTicket"),

                        Quantity =
                            GetDecimal(
                                exitElement,
                                "quantity"),

                        Price =
                            GetDecimal(
                                exitElement,
                                "price"),

                        Time =
                            GetDateTime(
                                exitElement,
                                "time"),

                        GrossPnl =
                            GetDecimal(
                                exitElement,
                                "grossPnl"),

                        Commission =
                            GetDecimal(
                                exitElement,
                                "commission"),

                        Swap =
                            GetDecimal(
                                exitElement,
                                "swap"),

                        Fee =
                            GetDecimal(
                                exitElement,
                                "fee"),

                        Reason =
                            GetString(
                                exitElement,
                                "reason"),

                        Comment =
                            GetString(
                                exitElement,
                                "comment")
                    };

                trade.Exits.Add(
                    exit);
            }
        }

        // Re-number exits in chronological order.
        trade.Exits =
            trade.Exits
                .OrderBy(x => x.Time)
                .ToList();

        for (int i = 0;
             i < trade.Exits.Count;
             i++)
        {
            trade.Exits[i].Number =
                i + 1;
        }

        return trade;
    }

    private static string GetString(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(
                property,
                out var value))
        {
            return string.Empty;
        }

        if (value.ValueKind ==
            JsonValueKind.Null)
        {
            return string.Empty;
        }

        return value.GetString() ?? string.Empty;
    }

    private static decimal GetDecimal(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(
                property,
                out var value))
        {
            return 0m;
        }

        if (value.ValueKind ==
            JsonValueKind.Number)
        {
            if (value.TryGetDecimal(
                    out var decimalValue))
            {
                return decimalValue;
            }

            return (decimal)value.GetDouble();
        }

        if (value.ValueKind ==
            JsonValueKind.String)
        {
            var text =
                value.GetString();

            if (decimal.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var result))
            {
                return result;
            }
        }

        return 0m;
    }

    private static decimal? GetNullableDecimal(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(
                property,
                out var value))
        {
            return null;
        }

        if (value.ValueKind ==
            JsonValueKind.Null)
        {
            return null;
        }

        return GetDecimal(
            element,
            property);
    }

    private static long GetInt64(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(
                property,
                out var value))
        {
            return 0L;
        }

        if (value.ValueKind ==
            JsonValueKind.Number)
        {
            if (value.TryGetInt64(
                    out var number))
            {
                return number;
            }
        }

        if (value.ValueKind ==
            JsonValueKind.String)
        {
            if (long.TryParse(
                    value.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var number))
            {
                return number;
            }
        }

        return 0L;
    }

    private static int GetInt32(
        JsonElement element,
        string property)
    {
        var value =
            GetInt64(
                element,
                property);

        return value > int.MaxValue
            ? int.MaxValue
            : value < int.MinValue
                ? int.MinValue
                : (int)value;
    }

    private static DateTime GetDateTime(
        JsonElement element,
        string property)
    {
        if (!element.TryGetProperty(
                property,
                out var value))
        {
            throw new InvalidDataException(
                $"Missing DateTime property: {property}");
        }

        if (value.ValueKind ==
            JsonValueKind.String)
        {
            var text =
                value.GetString();

            if (DateTime.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var result))
            {
                return result;
            }
        }

        throw new InvalidDataException(
            $"Invalid DateTime property: {property}");
    }

    private static void ValidateTrade(
        MetaTraderTrade trade)
    {
        if (string.IsNullOrWhiteSpace(
                trade.TradeId))
        {
            throw new InvalidDataException(
                "TradeId is required.");
        }

        if (string.IsNullOrWhiteSpace(
                trade.Symbol))
        {
            throw new InvalidDataException(
                "Symbol is required.");
        }

        if (trade.Quantity <= 0m)
        {
            throw new InvalidDataException(
                $"Invalid quantity: {trade.Quantity}");
        }

        if (trade.EntryPrice <= 0m)
        {
            throw new InvalidDataException(
                $"Invalid entry price: {trade.EntryPrice}");
        }

        if (trade.Exits.Count == 0)
        {
            throw new InvalidDataException(
                "No exit events found.");
        }

        foreach (var exit in trade.Exits)
        {
            if (exit.Quantity <= 0m)
            {
                throw new InvalidDataException(
                    "Invalid exit quantity.");
            }

            if (exit.Price <= 0m)
            {
                throw new InvalidDataException(
                    "Invalid exit price.");
            }
        }
    }
}
