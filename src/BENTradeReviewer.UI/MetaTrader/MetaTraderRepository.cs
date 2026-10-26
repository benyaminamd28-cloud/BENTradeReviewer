using System.IO;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace BENTradeReviewer.UI.MetaTrader;

public sealed class MetaTraderRepository
{
    private readonly string _dbPath;

    public MetaTraderRepository(string dbPath)
    {
        _dbPath = dbPath;

        InitializeDatabase();
    }

    private SqliteConnection OpenConnection()
    {
        var connection =
            new SqliteConnection(
                $"Data Source={_dbPath}");

        connection.Open();

        return connection;
    }

    private void InitializeDatabase()
    {
        var directory =
            Path.GetDirectoryName(_dbPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        using var connection =
            OpenConnection();

        using var command =
            connection.CreateCommand();

        command.CommandText = @"
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS MetaTrades
(
    TradeId       TEXT PRIMARY KEY,
    Symbol        TEXT NOT NULL,
    Side          TEXT NOT NULL,
    Quantity      TEXT NOT NULL,
    EntryTime     TEXT NOT NULL,
    EntryPrice    TEXT NOT NULL,
    StopLoss      TEXT NULL,
    PnL           TEXT NOT NULL DEFAULT '0',

    PositionId    TEXT NOT NULL DEFAULT '',
    TakeProfit    TEXT NULL,

    GrossPnl      TEXT NOT NULL DEFAULT '0',
    Commission    TEXT NOT NULL DEFAULT '0',
    Swap          TEXT NOT NULL DEFAULT '0',
    Fee           TEXT NOT NULL DEFAULT '0',
    NetPnl        TEXT NOT NULL DEFAULT '0',
    Outcome       TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS MetaTradeExits
(
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    TradeId       TEXT NOT NULL,
    Number        INTEGER NOT NULL DEFAULT 0,
    DealTicket    TEXT NOT NULL DEFAULT '',
    OrderTicket   TEXT NOT NULL DEFAULT '',
    Quantity      TEXT NOT NULL,
    Price         TEXT NOT NULL,
    Time          TEXT NOT NULL,

    GrossPnl      TEXT NOT NULL DEFAULT '0',
    Commission    TEXT NOT NULL DEFAULT '0',
    Swap          TEXT NOT NULL DEFAULT '0',
    Fee           TEXT NOT NULL DEFAULT '0',

    Reason        TEXT NOT NULL DEFAULT '',
    Comment       TEXT NOT NULL DEFAULT '',

    FOREIGN KEY (TradeId)
        REFERENCES MetaTrades(TradeId)
        ON DELETE CASCADE
);
";

        command.ExecuteNonQuery();

        EnsureColumn(
            connection,
            "MetaTrades",
            "PositionId",
            "TEXT NOT NULL DEFAULT ''");

        EnsureColumn(
            connection,
            "MetaTrades",
            "TakeProfit",
            "TEXT NULL");

        EnsureColumn(
            connection,
            "MetaTrades",
            "GrossPnl",
            "TEXT NOT NULL DEFAULT '0'");

        EnsureColumn(
            connection,
            "MetaTrades",
            "Commission",
            "TEXT NOT NULL DEFAULT '0'");

        EnsureColumn(
            connection,
            "MetaTrades",
            "Swap",
            "TEXT NOT NULL DEFAULT '0'");

        EnsureColumn(
            connection,
            "MetaTrades",
            "Fee",
            "TEXT NOT NULL DEFAULT '0'");

        EnsureColumn(
            connection,
            "MetaTrades",
            "NetPnl",
            "TEXT NOT NULL DEFAULT '0'");

        EnsureColumn(
            connection,
            "MetaTrades",
            "Outcome",
            "TEXT NOT NULL DEFAULT ''");

        EnsureColumn(
            connection,
            "MetaTradeExits",
            "Number",
            "INTEGER NOT NULL DEFAULT 0");

        EnsureColumn(
            connection,
            "MetaTradeExits",
            "DealTicket",
            "TEXT NOT NULL DEFAULT ''");

        EnsureColumn(
            connection,
            "MetaTradeExits",
            "OrderTicket",
            "TEXT NOT NULL DEFAULT ''");

        EnsureColumn(
            connection,
            "MetaTradeExits",
            "GrossPnl",
            "TEXT NOT NULL DEFAULT '0'");

        EnsureColumn(
            connection,
            "MetaTradeExits",
            "Commission",
            "TEXT NOT NULL DEFAULT '0'");

        EnsureColumn(
            connection,
            "MetaTradeExits",
            "Swap",
            "TEXT NOT NULL DEFAULT '0'");

        EnsureColumn(
            connection,
            "MetaTradeExits",
            "Fee",
            "TEXT NOT NULL DEFAULT '0'");

        EnsureColumn(
            connection,
            "MetaTradeExits",
            "Reason",
            "TEXT NOT NULL DEFAULT ''");

        EnsureColumn(
            connection,
            "MetaTradeExits",
            "Comment",
            "TEXT NOT NULL DEFAULT ''");

        using var indexCommand =
            connection.CreateCommand();

        indexCommand.CommandText = @"
CREATE INDEX IF NOT EXISTS IX_MetaTrades_EntryTime
ON MetaTrades(EntryTime);

CREATE INDEX IF NOT EXISTS IX_MetaTrades_Symbol
ON MetaTrades(Symbol);

CREATE INDEX IF NOT EXISTS IX_MetaTradeExits_TradeId
ON MetaTradeExits(TradeId);
";

        indexCommand.ExecuteNonQuery();
    }

    private static void EnsureColumn(
        SqliteConnection connection,
        string table,
        string column,
        string definition)
    {
        var exists = false;

        using (var check =
               connection.CreateCommand())
        {
            check.CommandText =
                $"PRAGMA table_info({table});";

            using var reader =
                check.ExecuteReader();

            while (reader.Read())
            {
                var name =
                    reader.GetString(1);

                if (name.Equals(
                        column,
                        StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
            return;

        using var alter =
            connection.CreateCommand();

        alter.CommandText =
            $"ALTER TABLE {table} ADD COLUMN {column} {definition};";

        alter.ExecuteNonQuery();
    }

    // ============================================================
    // UPSERT
    // ============================================================

    public void UpsertTrade(
        MetaTraderTrade trade)
    {
        using var connection =
            OpenConnection();

        using var transaction =
            connection.BeginTransaction();

        using (var command =
               connection.CreateCommand())
        {
            command.Transaction =
                transaction;

            command.CommandText = @"
INSERT INTO MetaTrades
(
    TradeId,
    Symbol,
    Side,
    Quantity,
    EntryTime,
    EntryPrice,
    StopLoss,
    PnL,
    PositionId,
    TakeProfit,
    GrossPnl,
    Commission,
    Swap,
    Fee,
    NetPnl,
    Outcome
)
VALUES
(
    $tradeId,
    $symbol,
    $side,
    $quantity,
    $entryTime,
    $entryPrice,
    $stopLoss,
    $pnl,
    $positionId,
    $takeProfit,
    $grossPnl,
    $commission,
    $swap,
    $fee,
    $netPnl,
    $outcome
)
ON CONFLICT(TradeId)
DO UPDATE SET
    Symbol      = excluded.Symbol,
    Side        = excluded.Side,
    Quantity    = excluded.Quantity,
    EntryTime   = excluded.EntryTime,
    EntryPrice  = excluded.EntryPrice,
    StopLoss    = excluded.StopLoss,
    PnL         = excluded.PnL,
    PositionId  = excluded.PositionId,
    TakeProfit  = excluded.TakeProfit,
    GrossPnl    = excluded.GrossPnl,
    Commission  = excluded.Commission,
    Swap        = excluded.Swap,
    Fee         = excluded.Fee,
    NetPnl      = excluded.NetPnl,
    Outcome     = excluded.Outcome;
";

            command.Parameters.AddWithValue(
                "$tradeId",
                trade.TradeId);

            command.Parameters.AddWithValue(
                "$symbol",
                trade.Symbol);

            command.Parameters.AddWithValue(
                "$side",
                trade.Side);

            command.Parameters.AddWithValue(
                "$quantity",
                ToInvariant(trade.Quantity));

            command.Parameters.AddWithValue(
                "$entryTime",
                trade.EntryTime.ToString("O"));

            command.Parameters.AddWithValue(
                "$entryPrice",
                ToInvariant(trade.EntryPrice));

            command.Parameters.AddWithValue(
                "$stopLoss",
                trade.StopLoss.HasValue
                    ? ToInvariant(trade.StopLoss.Value)
                    : (object)DBNull.Value);

            command.Parameters.AddWithValue(
                "$pnl",
                ToInvariant(trade.PnL));

            command.Parameters.AddWithValue(
                "$positionId",
                trade.PositionId.ToString(
                    CultureInfo.InvariantCulture));

            command.Parameters.AddWithValue(
                "$takeProfit",
                trade.TakeProfit.HasValue
                    ? ToInvariant(trade.TakeProfit.Value)
                    : (object)DBNull.Value);

            command.Parameters.AddWithValue(
                "$grossPnl",
                ToInvariant(trade.GrossPnl));

            command.Parameters.AddWithValue(
                "$commission",
                ToInvariant(trade.Commission));

            command.Parameters.AddWithValue(
                "$swap",
                ToInvariant(trade.Swap));

            command.Parameters.AddWithValue(
                "$fee",
                ToInvariant(trade.Fee));

            command.Parameters.AddWithValue(
                "$netPnl",
                ToInvariant(trade.NetPnl));

            command.Parameters.AddWithValue(
                "$outcome",
                string.IsNullOrWhiteSpace(
                    trade.Outcome)
                    ? CalculateOutcome(trade.NetPnl)
                    : trade.Outcome);

            command.ExecuteNonQuery();
        }

        using (var delete =
               connection.CreateCommand())
        {
            delete.Transaction =
                transaction;

            delete.CommandText = @"
DELETE FROM MetaTradeExits
WHERE TradeId = $tradeId;
";

            delete.Parameters.AddWithValue(
                "$tradeId",
                trade.TradeId);

            delete.ExecuteNonQuery();
        }

        foreach (
            var exit in trade.Exits
                .OrderBy(x => x.Time))
        {
            using var command =
                connection.CreateCommand();

            command.Transaction =
                transaction;

            command.CommandText = @"
INSERT INTO MetaTradeExits
(
    TradeId,
    Number,
    DealTicket,
    OrderTicket,
    Quantity,
    Price,
    Time,
    GrossPnl,
    Commission,
    Swap,
    Fee,
    Reason,
    Comment
)
VALUES
(
    $tradeId,
    $number,
    $dealTicket,
    $orderTicket,
    $quantity,
    $price,
    $time,
    $grossPnl,
    $commission,
    $swap,
    $fee,
    $reason,
    $comment
);
";

            command.Parameters.AddWithValue(
                "$tradeId",
                trade.TradeId);

            command.Parameters.AddWithValue(
                "$number",
                exit.Number);

            command.Parameters.AddWithValue(
                "$dealTicket",
                exit.DealTicket.ToString(
                    CultureInfo.InvariantCulture));

            command.Parameters.AddWithValue(
                "$orderTicket",
                exit.OrderTicket.ToString(
                    CultureInfo.InvariantCulture));

            command.Parameters.AddWithValue(
                "$quantity",
                ToInvariant(exit.Quantity));

            command.Parameters.AddWithValue(
                "$price",
                ToInvariant(exit.Price));

            command.Parameters.AddWithValue(
                "$time",
                exit.Time.ToString("O"));

            command.Parameters.AddWithValue(
                "$grossPnl",
                ToInvariant(exit.GrossPnl));

            command.Parameters.AddWithValue(
                "$commission",
                ToInvariant(exit.Commission));

            command.Parameters.AddWithValue(
                "$swap",
                ToInvariant(exit.Swap));

            command.Parameters.AddWithValue(
                "$fee",
                ToInvariant(exit.Fee));

            command.Parameters.AddWithValue(
                "$reason",
                exit.Reason ?? string.Empty);

            command.Parameters.AddWithValue(
                "$comment",
                exit.Comment ?? string.Empty);

            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    // ============================================================
    // GET ALL TRADES
    // ============================================================

    public List<MetaTraderTrade> GetAllTrades()
    {
        var result =
            new List<MetaTraderTrade>();

        using var connection =
            OpenConnection();

        using var command =
            connection.CreateCommand();

        command.CommandText = @"
SELECT
    TradeId,
    PositionId,
    Symbol,
    Side,
    Quantity,
    EntryTime,
    EntryPrice,
    StopLoss,
    TakeProfit,
    PnL,
    GrossPnl,
    Commission,
    Swap,
    Fee,
    NetPnl,
    Outcome
FROM MetaTrades
ORDER BY EntryTime DESC;
";

        using var reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            var trade =
                new MetaTraderTrade
                {
                    TradeId =
                        reader.GetString(0),

                    PositionId =
                        ParseLong(
                            reader.GetString(1)),

                    Symbol =
                        reader.GetString(2),

                    Side =
                        reader.GetString(3),

                    Quantity =
                        ParseDecimal(
                            reader.GetString(4)),

                    EntryTime =
                        ParseDateTime(
                            reader.GetString(5)),

                    EntryPrice =
                        ParseDecimal(
                            reader.GetString(6)),

                    StopLoss =
                        reader.IsDBNull(7)
                            ? null
                            : ParseDecimal(
                                reader.GetString(7)),

                    TakeProfit =
                        reader.IsDBNull(8)
                            ? null
                            : ParseDecimal(
                                reader.GetString(8)),

                    PnL =
                        ParseDecimal(
                            reader.GetString(9)),

                    GrossPnl =
                        ParseDecimal(
                            reader.GetString(10)),

                    Commission =
                        ParseDecimal(
                            reader.GetString(11)),

                    Swap =
                        ParseDecimal(
                            reader.GetString(12)),

                    Fee =
                        ParseDecimal(
                            reader.GetString(13)),

                    NetPnl =
                        ParseDecimal(
                            reader.GetString(14)),

                    Outcome =
                        reader.IsDBNull(15)
                            ? string.Empty
                            : reader.GetString(15)
                };

            result.Add(
                trade);
        }

        foreach (var trade in result)
        {
            using var exitCommand =
                connection.CreateCommand();

            exitCommand.CommandText = @"
SELECT
    Number,
    DealTicket,
    OrderTicket,
    Quantity,
    Price,
    Time,
    GrossPnl,
    Commission,
    Swap,
    Fee,
    Reason,
    Comment
FROM MetaTradeExits
WHERE TradeId = $tradeId
ORDER BY Time ASC, Id ASC;
";

            exitCommand.Parameters.AddWithValue(
                "$tradeId",
                trade.TradeId);

            using var exitReader =
                exitCommand.ExecuteReader();

            while (exitReader.Read())
            {
                trade.Exits.Add(
                    new MetaTraderExit
                    {
                        Number =
                            exitReader.GetInt32(0),

                        DealTicket =
                            ParseLong(
                                exitReader.GetString(1)),

                        OrderTicket =
                            ParseLong(
                                exitReader.GetString(2)),

                        Quantity =
                            ParseDecimal(
                                exitReader.GetString(3)),

                        Price =
                            ParseDecimal(
                                exitReader.GetString(4)),

                        Time =
                            ParseDateTime(
                                exitReader.GetString(5)),

                        GrossPnl =
                            ParseDecimal(
                                exitReader.GetString(6)),

                        Commission =
                            ParseDecimal(
                                exitReader.GetString(7)),

                        Swap =
                            ParseDecimal(
                                exitReader.GetString(8)),

                        Fee =
                            ParseDecimal(
                                exitReader.GetString(9)),

                        Reason =
                            exitReader.GetString(10),

                        Comment =
                            exitReader.GetString(11)
                    });
            }
        }

        return result;
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private static decimal ParseDecimal(
        string value)
    {
        return decimal.Parse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
    }

    private static long ParseLong(
        string value)
    {
        return long.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : 0L;
    }

    private static DateTime ParseDateTime(
        string value)
    {
        return DateTime.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }

    private static string ToInvariant(
        decimal value)
    {
        return value.ToString(
            CultureInfo.InvariantCulture);
    }

    private static string CalculateOutcome(
        decimal net)
    {
        if (net > 0m)
            return "WIN";

        if (net < 0m)
            return "LOSS";

        return "BREAK EVEN";
    }
}
