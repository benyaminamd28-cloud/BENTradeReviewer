using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using BENTradeReviewer.Core.Enums;
using BENTradeReviewer.Core.Models;

namespace BENTradeReviewer.Data;

public sealed class TradeRepository
{
    private readonly string _connectionString;

    public TradeRepository(string dbPath)
    {
        var fullPath = Path.GetFullPath(dbPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath, ForeignKeys = true }.ToString();
        InitializeDatabase();
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    private void InitializeDatabase()
    {
        using var conn = CreateConnection();
        conn.Open();
        conn.Execute(@"
CREATE TABLE IF NOT EXISTS Instruments (
    Symbol TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    TickSize REAL NOT NULL,
    TickValue REAL NOT NULL,
    PointValue REAL NOT NULL,
    Currency TEXT NOT NULL DEFAULT 'USD',
    DefaultCommission REAL NOT NULL DEFAULT 0.0
);
CREATE TABLE IF NOT EXISTS Trades (
    TradeId TEXT PRIMARY KEY,
    FolderDirectory TEXT NOT NULL,
    Symbol TEXT NOT NULL,
    Account TEXT,
    Side TEXT NOT NULL,
    Quantity INTEGER NOT NULL,
    EntryTime TEXT NOT NULL,
    ExitTime TEXT NOT NULL,
    EntryPrice REAL NOT NULL,
    ExitPrice REAL NOT NULL,
    InitialStopLoss REAL,
    InitialTakeProfit REAL,
    PreRollSeconds REAL DEFAULT 5.0,
    PostRollSeconds REAL DEFAULT 5.0,
    NetPnL REAL,
    GrossPnL REAL,
    Commission REAL DEFAULT 0.0,
    PlannedRR REAL,
    RealizedRR REAL,
    RealizedPoints REAL,
    RealizedTicks REAL,
    InitialRiskUSD REAL DEFAULT 0,
    ExpectedRewardUSD REAL DEFAULT 0,
    MAETicks REAL,
    MAEUSD REAL,
    MFETicks REAL,
    MFEUSD REAL,
    Notes TEXT,
    IsWin INTEGER,
    ImportedAt TEXT NOT NULL,
    FOREIGN KEY (Symbol) REFERENCES Instruments(Symbol)
);
CREATE TABLE IF NOT EXISTS TradeVideos (
    VideoId INTEGER PRIMARY KEY AUTOINCREMENT,
    TradeId TEXT NOT NULL,
    MonitorIndex INTEGER NOT NULL,
    MonitorName TEXT,
    VideoPath TEXT NOT NULL,
    DurationSeconds REAL,
    Resolution TEXT,
    FOREIGN KEY (TradeId) REFERENCES Trades(TradeId) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS TradeEvents (
    EventId INTEGER PRIMARY KEY AUTOINCREMENT,
    TradeId TEXT NOT NULL,
    Timestamp TEXT NOT NULL,
    EventType TEXT NOT NULL,
    Price REAL,
    Quantity INTEGER,
    Message TEXT,
    VideoTimestampOffsetSeconds REAL,
    FOREIGN KEY (TradeId) REFERENCES Trades(TradeId) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS TradeTags (
    TagId INTEGER PRIMARY KEY AUTOINCREMENT,
    TradeId TEXT NOT NULL,
    TagText TEXT NOT NULL,
    FOREIGN KEY (TradeId) REFERENCES Trades(TradeId) ON DELETE CASCADE
);
CREATE TABLE IF NOT EXISTS TradeMarketContexts (
    TradeId TEXT PRIMARY KEY,
    Session TEXT NOT NULL DEFAULT 'Unknown',

    GexCallWall INTEGER NOT NULL DEFAULT 0,
    GexPutWall INTEGER NOT NULL DEFAULT 0,
    GexGammaFlip INTEGER NOT NULL DEFAULT 0,
    GexHighLevel INTEGER NOT NULL DEFAULT 0,
    GexNearLevel INTEGER NOT NULL DEFAULT 0,
    GexRelation TEXT NOT NULL DEFAULT 'Neutral',

    IcebergPresent INTEGER NOT NULL DEFAULT 0,
    IcebergNative INTEGER NOT NULL DEFAULT 0,
    IcebergSynthetic INTEGER NOT NULL DEFAULT 0,
    IcebergWithTrade INTEGER NOT NULL DEFAULT 0,
    IcebergAgainstTrade INTEGER NOT NULL DEFAULT 0,
    IcebergHeld INTEGER NOT NULL DEFAULT 0,
    IcebergFailed INTEGER NOT NULL DEFAULT 0,

    DomBidPressure INTEGER NOT NULL DEFAULT 0,
    DomAskPressure INTEGER NOT NULL DEFAULT 0,
    DomRepeatedRejection INTEGER NOT NULL DEFAULT 0,
    DomRefill INTEGER NOT NULL DEFAULT 0,
    DomLiquidityPull INTEGER NOT NULL DEFAULT 0,
    DomPressureDivergence INTEGER NOT NULL DEFAULT 0,

    FootprintPositiveDelta INTEGER NOT NULL DEFAULT 0,
    FootprintNegativeDelta INTEGER NOT NULL DEFAULT 0,
    FootprintStackedImbalance INTEGER NOT NULL DEFAULT 0,
    FootprintDeltaDivergence INTEGER NOT NULL DEFAULT 0,
    FootprintHvn INTEGER NOT NULL DEFAULT 0,
    FootprintLvnRejection INTEGER NOT NULL DEFAULT 0,

    BoxBuyPressure INTEGER NOT NULL DEFAULT 0,
    BoxSellPressure INTEGER NOT NULL DEFAULT 0,
    BoxPressureIncreasing INTEGER NOT NULL DEFAULT 0,
    BoxPressureDecreasing INTEGER NOT NULL DEFAULT 0,
    BoxPressureDivergence INTEGER NOT NULL DEFAULT 0,

    LocationDayHigh INTEGER NOT NULL DEFAULT 0,
    LocationDayLow INTEGER NOT NULL DEFAULT 0,
    LocationPreviousDayHigh INTEGER NOT NULL DEFAULT 0,
    LocationPreviousDayLow INTEGER NOT NULL DEFAULT 0,
    LocationPreviousClose INTEGER NOT NULL DEFAULT 0,
    LocationSessionOpen INTEGER NOT NULL DEFAULT 0,
    LocationVwap INTEGER NOT NULL DEFAULT 0,
    LocationOpeningRange INTEGER NOT NULL DEFAULT 0,
    LocationSupport INTEGER NOT NULL DEFAULT 0,
    LocationResistance INTEGER NOT NULL DEFAULT 0,

    MarketTrending INTEGER NOT NULL DEFAULT 0,
    MarketRanging INTEGER NOT NULL DEFAULT 0,
    MarketExpansion INTEGER NOT NULL DEFAULT 0,
    MarketCompression INTEGER NOT NULL DEFAULT 0,
    MarketChoppy INTEGER NOT NULL DEFAULT 0,

    EventPhase TEXT NOT NULL DEFAULT 'None',
    EventName TEXT,

    TradeThesis TEXT NOT NULL DEFAULT 'Other',
    ExecutionQuality TEXT NOT NULL DEFAULT 'Acceptable',
    ContextConfidence INTEGER NOT NULL DEFAULT 0,
    TradeQuality INTEGER NOT NULL DEFAULT 0,

    FOREIGN KEY (TradeId) REFERENCES Trades(TradeId) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IDX_TradeMarketContexts_TradeId
ON TradeMarketContexts(TradeId);
CREATE INDEX IF NOT EXISTS IDX_Trades_EntryTime ON Trades(EntryTime);
CREATE INDEX IF NOT EXISTS IDX_Trades_Symbol ON Trades(Symbol);
CREATE INDEX IF NOT EXISTS IDX_TradeVideos_TradeId ON TradeVideos(TradeId);
CREATE INDEX IF NOT EXISTS IDX_TradeEvents_TradeId ON TradeEvents(TradeId);
CREATE INDEX IF NOT EXISTS IDX_TradeTags_TradeId ON TradeTags(TradeId);");

        // Existing database migration for Recorder-derived metrics.
        try { conn.Execute("ALTER TABLE Trades ADD COLUMN InitialRiskUSD REAL DEFAULT 0"); } catch { }
        try { conn.Execute("ALTER TABLE Trades ADD COLUMN ExpectedRewardUSD REAL DEFAULT 0"); } catch { }
        try { conn.Execute("ALTER TABLE Trades ADD COLUMN MAETicks REAL"); } catch { }
        try { conn.Execute("ALTER TABLE Trades ADD COLUMN MAEUSD REAL"); } catch { }
        try { conn.Execute("ALTER TABLE Trades ADD COLUMN MFETicks REAL"); } catch { }
        try { conn.Execute("ALTER TABLE Trades ADD COLUMN MFEUSD REAL"); } catch { }
        foreach (var spec in InstrumentSpec.GetDefaultCatalog())
        {
            conn.Execute(@"INSERT INTO Instruments(Symbol,Name,TickSize,TickValue,PointValue,Currency,DefaultCommission)
VALUES(@Symbol,@Name,@TickSize,@TickValue,@PointValue,@Currency,@DefaultCommission)
ON CONFLICT(Symbol) DO UPDATE SET Name=excluded.Name, TickSize=excluded.TickSize,
TickValue=excluded.TickValue, PointValue=excluded.PointValue, Currency=excluded.Currency,
DefaultCommission=excluded.DefaultCommission;", spec);
        }
    }

    public async Task UpsertInstrumentSpecAsync(InstrumentSpec spec)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
INSERT INTO Instruments(Symbol,Name,TickSize,TickValue,PointValue,Currency,DefaultCommission)
VALUES(@Symbol,@Name,@TickSize,@TickValue,@PointValue,@Currency,@DefaultCommission)
ON CONFLICT(Symbol) DO UPDATE SET Name=excluded.Name, TickSize=excluded.TickSize,
TickValue=excluded.TickValue, PointValue=excluded.PointValue, Currency=excluded.Currency,
DefaultCommission=excluded.DefaultCommission;
""", spec);
    }

    public async Task<InstrumentSpec?> GetInstrumentSpecAsync(string symbol)
    {
        await using var conn = CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<InstrumentSpec>("SELECT * FROM Instruments WHERE Symbol=@Symbol", new { Symbol = symbol.ToUpperInvariant() });
    }

    public async Task UpsertTradeAsync(Trade trade)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var args = new
        {
            trade.TradeId,
            trade.FolderDirectory,
            trade.Symbol,
            trade.Account,
            Side = trade.Side == TradeSide.Short ? "SHORT" : "LONG",
            trade.Quantity,
            EntryTime = trade.EntryTime.ToString("O", CultureInfo.InvariantCulture),
            ExitTime = trade.ExitTime.ToString("O", CultureInfo.InvariantCulture),
            trade.EntryPrice,
            trade.ExitPrice,
            trade.InitialStopLoss,
            trade.InitialTakeProfit,
            trade.PreRollSeconds,
            trade.PostRollSeconds,
            trade.NetPnL,
            trade.GrossPnL,
            trade.Commission,
            trade.PlannedRR,
            trade.RealizedRR,
            trade.RealizedPoints,
            trade.RealizedTicks,
            trade.InitialRiskUSD,
            trade.ExpectedRewardUSD,
            trade.MAETicks,
            trade.MAEUSD,
            trade.MFETicks,
            trade.MFEUSD,
            trade.Notes,
            IsWin = trade.IsWin ? 1 : 0,
            ImportedAt = trade.ImportedAt.ToString("O", CultureInfo.InvariantCulture)
        };

        await conn.ExecuteAsync(@"
INSERT INTO Trades
(
    TradeId,FolderDirectory,Symbol,Account,Side,Quantity,EntryTime,ExitTime,
    EntryPrice,ExitPrice,InitialStopLoss,InitialTakeProfit,
    PreRollSeconds,PostRollSeconds,NetPnL,GrossPnL,Commission,
    PlannedRR,RealizedRR,RealizedPoints,RealizedTicks,
    InitialRiskUSD,ExpectedRewardUSD,MAETicks,MAEUSD,MFETicks,MFEUSD,
    Notes,IsWin,ImportedAt
)
VALUES
(
    @TradeId,@FolderDirectory,@Symbol,@Account,@Side,@Quantity,@EntryTime,@ExitTime,
    @EntryPrice,@ExitPrice,@InitialStopLoss,@InitialTakeProfit,
    @PreRollSeconds,@PostRollSeconds,@NetPnL,@GrossPnL,@Commission,
    @PlannedRR,@RealizedRR,@RealizedPoints,@RealizedTicks,
    @InitialRiskUSD,@ExpectedRewardUSD,@MAETicks,@MAEUSD,@MFETicks,@MFEUSD,
    @Notes,@IsWin,@ImportedAt
)
ON CONFLICT(TradeId) DO UPDATE SET
    FolderDirectory=excluded.FolderDirectory,
    Symbol=excluded.Symbol,
    Account=excluded.Account,
    Side=excluded.Side,
    Quantity=excluded.Quantity,
    EntryTime=excluded.EntryTime,
    ExitTime=excluded.ExitTime,
    EntryPrice=excluded.EntryPrice,
    ExitPrice=excluded.ExitPrice,
    InitialStopLoss=excluded.InitialStopLoss,
    InitialTakeProfit=excluded.InitialTakeProfit,
    PreRollSeconds=excluded.PreRollSeconds,
    PostRollSeconds=excluded.PostRollSeconds,
    NetPnL=excluded.NetPnL,
    GrossPnL=excluded.GrossPnL,
    Commission=excluded.Commission,
    PlannedRR=excluded.PlannedRR,
    RealizedRR=excluded.RealizedRR,
    RealizedPoints=excluded.RealizedPoints,
    RealizedTicks=excluded.RealizedTicks,
    InitialRiskUSD=excluded.InitialRiskUSD,
    ExpectedRewardUSD=excluded.ExpectedRewardUSD,
    MAETicks=excluded.MAETicks,
    MAEUSD=excluded.MAEUSD,
    MFETicks=excluded.MFETicks,
    MFEUSD=excluded.MFEUSD,
    Notes=excluded.Notes,
    IsWin=excluded.IsWin,
    ImportedAt=excluded.ImportedAt;",
            args,
            tx);

        await conn.ExecuteAsync(
            "DELETE FROM TradeVideos WHERE TradeId=@TradeId",
            new { trade.TradeId },
            tx);

        foreach (var v in trade.Videos)
        {
            await conn.ExecuteAsync(
                "INSERT INTO TradeVideos(TradeId,MonitorIndex,MonitorName,VideoPath,DurationSeconds,Resolution) " +
                "VALUES(@TradeId,@MonitorIndex,@MonitorName,@VideoPath,@DurationSeconds,@Resolution)",
                new
                {
                    trade.TradeId,
                    v.MonitorIndex,
                    v.MonitorName,
                    v.VideoPath,
                    v.DurationSeconds,
                    v.Resolution
                },
                tx);
        }

        await conn.ExecuteAsync(
            "DELETE FROM TradeEvents WHERE TradeId=@TradeId",
            new { trade.TradeId },
            tx);

        foreach (var e in trade.EventTimeline)
        {
            await conn.ExecuteAsync(
                "INSERT INTO TradeEvents(TradeId,Timestamp,EventType,Price,Quantity,Message,VideoTimestampOffsetSeconds) " +
                "VALUES(@TradeId,@Timestamp,@EventType,@Price,@Quantity,@Message,@VideoTimestampOffsetSeconds)",
                new
                {
                    trade.TradeId,
                    Timestamp = e.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                    EventType = e.EventType.ToString(),
                    e.Price,
                    e.Quantity,
                    e.Message,
                    e.VideoTimestampOffsetSeconds
                },
                tx);
        }

        await conn.ExecuteAsync(
            "DELETE FROM TradeTags WHERE TradeId=@TradeId",
            new { trade.TradeId },
            tx);

        foreach (var tag in trade.Tags
                     .Where(t => !string.IsNullOrWhiteSpace(t))
                     .Select(t => t.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await conn.ExecuteAsync(
                "INSERT INTO TradeTags(TradeId,TagText) VALUES(@TradeId,@TagText)",
                new { trade.TradeId, TagText = tag },
                tx);
        }

        await tx.CommitAsync();
    }

    public async Task<List<Trade>> GetAllTradesAsync()
    {
        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync("SELECT * FROM Trades ORDER BY EntryTime DESC");
        return await MapRowsAsync(conn, rows);
    }

    public async Task<List<Trade>> GetTradesByDateRangeAsync(DateTime start, DateTime end)
    {
        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync("SELECT * FROM Trades WHERE EntryTime>=@Start AND EntryTime<=@End ORDER BY EntryTime ASC",
            new { Start = start.ToString("O", CultureInfo.InvariantCulture), End = end.ToString("O", CultureInfo.InvariantCulture) });
        return await MapRowsAsync(conn, rows);
    }

    public async Task UpdateTradeNotesAndTagsAsync(string tradeId, string notes, IEnumerable<string> tags)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync("UPDATE Trades SET Notes=@Notes WHERE TradeId=@TradeId", new { TradeId = tradeId, Notes = notes ?? string.Empty }, tx);
        await conn.ExecuteAsync("DELETE FROM TradeTags WHERE TradeId=@TradeId", new { TradeId = tradeId }, tx);
        foreach (var tag in tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            await conn.ExecuteAsync("INSERT INTO TradeTags(TradeId,TagText) VALUES(@TradeId,@TagText)", new { TradeId = tradeId, TagText = tag }, tx);
        await tx.CommitAsync();
    }

    private static async Task<List<Trade>> MapRowsAsync(SqliteConnection conn, IEnumerable<dynamic> rows)
    {
        var list = new List<Trade>();
        foreach (var row in rows)
        {
            var trade = MapTrade(row);
            await LoadRelationsAsync(conn, trade);
            list.Add(trade);
        }
        return list.Where(t => System.IO.Directory.Exists(t.FolderDirectory)).ToList();
    }

    public async Task UpsertTradeMarketContextAsync(string tradeId, TradeMarketContext context)
    {
        if (string.IsNullOrWhiteSpace(tradeId))
            return;

        context ??= new TradeMarketContext();

        await using var conn = CreateConnection();
        await conn.OpenAsync();

        const string sql = @"
INSERT INTO TradeMarketContexts
(
    TradeId, Session,
    GexCallWall, GexPutWall, GexGammaFlip, GexHighLevel, GexNearLevel,
    GexRelation,
    IcebergPresent, IcebergNative, IcebergSynthetic, IcebergWithTrade,
    IcebergAgainstTrade, IcebergHeld, IcebergFailed,
    DomBidPressure, DomAskPressure, DomRepeatedRejection, DomRefill,
    DomLiquidityPull, DomPressureDivergence,
    FootprintPositiveDelta, FootprintNegativeDelta, FootprintStackedImbalance,
    FootprintDeltaDivergence, FootprintHvn, FootprintLvnRejection,
    BoxBuyPressure, BoxSellPressure, BoxPressureIncreasing,
    BoxPressureDecreasing, BoxPressureDivergence,
    LocationDayHigh, LocationDayLow, LocationPreviousDayHigh,
    LocationPreviousDayLow, LocationPreviousClose, LocationSessionOpen,
    LocationVwap, LocationOpeningRange, LocationSupport, LocationResistance,
    MarketTrending, MarketRanging, MarketExpansion, MarketCompression, MarketChoppy,
    EventPhase, EventName, TradeThesis, ExecutionQuality,
    ContextConfidence, TradeQuality
)
VALUES
(
    @TradeId, @Session,
    @GexCallWall, @GexPutWall, @GexGammaFlip, @GexHighLevel, @GexNearLevel,
    @GexRelation,
    @IcebergPresent, @IcebergNative, @IcebergSynthetic, @IcebergWithTrade,
    @IcebergAgainstTrade, @IcebergHeld, @IcebergFailed,
    @DomBidPressure, @DomAskPressure, @DomRepeatedRejection, @DomRefill,
    @DomLiquidityPull, @DomPressureDivergence,
    @FootprintPositiveDelta, @FootprintNegativeDelta, @FootprintStackedImbalance,
    @FootprintDeltaDivergence, @FootprintHvn, @FootprintLvnRejection,
    @BoxBuyPressure, @BoxSellPressure, @BoxPressureIncreasing,
    @BoxPressureDecreasing, @BoxPressureDivergence,
    @LocationDayHigh, @LocationDayLow, @LocationPreviousDayHigh,
    @LocationPreviousDayLow, @LocationPreviousClose, @LocationSessionOpen,
    @LocationVwap, @LocationOpeningRange, @LocationSupport, @LocationResistance,
    @MarketTrending, @MarketRanging, @MarketExpansion, @MarketCompression, @MarketChoppy,
    @EventPhase, @EventName, @TradeThesis, @ExecutionQuality,
    @ContextConfidence, @TradeQuality
)
ON CONFLICT(TradeId) DO UPDATE SET
    Session=excluded.Session,
    GexCallWall=excluded.GexCallWall,
    GexPutWall=excluded.GexPutWall,
    GexGammaFlip=excluded.GexGammaFlip,
    GexHighLevel=excluded.GexHighLevel,
    GexNearLevel=excluded.GexNearLevel,
    GexRelation=excluded.GexRelation,
    IcebergPresent=excluded.IcebergPresent,
    IcebergNative=excluded.IcebergNative,
    IcebergSynthetic=excluded.IcebergSynthetic,
    IcebergWithTrade=excluded.IcebergWithTrade,
    IcebergAgainstTrade=excluded.IcebergAgainstTrade,
    IcebergHeld=excluded.IcebergHeld,
    IcebergFailed=excluded.IcebergFailed,
    DomBidPressure=excluded.DomBidPressure,
    DomAskPressure=excluded.DomAskPressure,
    DomRepeatedRejection=excluded.DomRepeatedRejection,
    DomRefill=excluded.DomRefill,
    DomLiquidityPull=excluded.DomLiquidityPull,
    DomPressureDivergence=excluded.DomPressureDivergence,
    FootprintPositiveDelta=excluded.FootprintPositiveDelta,
    FootprintNegativeDelta=excluded.FootprintNegativeDelta,
    FootprintStackedImbalance=excluded.FootprintStackedImbalance,
    FootprintDeltaDivergence=excluded.FootprintDeltaDivergence,
    FootprintHvn=excluded.FootprintHvn,
    FootprintLvnRejection=excluded.FootprintLvnRejection,
    BoxBuyPressure=excluded.BoxBuyPressure,
    BoxSellPressure=excluded.BoxSellPressure,
    BoxPressureIncreasing=excluded.BoxPressureIncreasing,
    BoxPressureDecreasing=excluded.BoxPressureDecreasing,
    BoxPressureDivergence=excluded.BoxPressureDivergence,
    LocationDayHigh=excluded.LocationDayHigh,
    LocationDayLow=excluded.LocationDayLow,
    LocationPreviousDayHigh=excluded.LocationPreviousDayHigh,
    LocationPreviousDayLow=excluded.LocationPreviousDayLow,
    LocationPreviousClose=excluded.LocationPreviousClose,
    LocationSessionOpen=excluded.LocationSessionOpen,
    LocationVwap=excluded.LocationVwap,
    LocationOpeningRange=excluded.LocationOpeningRange,
    LocationSupport=excluded.LocationSupport,
    LocationResistance=excluded.LocationResistance,
    MarketTrending=excluded.MarketTrending,
    MarketRanging=excluded.MarketRanging,
    MarketExpansion=excluded.MarketExpansion,
    MarketCompression=excluded.MarketCompression,
    MarketChoppy=excluded.MarketChoppy,
    EventPhase=excluded.EventPhase,
    EventName=excluded.EventName,
    TradeThesis=excluded.TradeThesis,
    ExecutionQuality=excluded.ExecutionQuality,
    ContextConfidence=excluded.ContextConfidence,
    TradeQuality=excluded.TradeQuality;";

        await conn.ExecuteAsync(sql, new
        {
            TradeId = tradeId,
            context.Session,
            GexCallWall = context.GexCallWall ? 1 : 0,
            GexPutWall = context.GexPutWall ? 1 : 0,
            GexGammaFlip = context.GexGammaFlip ? 1 : 0,
            GexHighLevel = context.GexHighLevel ? 1 : 0,
            GexNearLevel = context.GexNearLevel ? 1 : 0,
            context.GexRelation,

            IcebergPresent = context.IcebergPresent ? 1 : 0,
            IcebergNative = context.IcebergNative ? 1 : 0,
            IcebergSynthetic = context.IcebergSynthetic ? 1 : 0,
            IcebergWithTrade = context.IcebergWithTrade ? 1 : 0,
            IcebergAgainstTrade = context.IcebergAgainstTrade ? 1 : 0,
            IcebergHeld = context.IcebergHeld ? 1 : 0,
            IcebergFailed = context.IcebergFailed ? 1 : 0,

            DomBidPressure = context.DomBidPressure ? 1 : 0,
            DomAskPressure = context.DomAskPressure ? 1 : 0,
            DomRepeatedRejection = context.DomRepeatedRejection ? 1 : 0,
            DomRefill = context.DomRefill ? 1 : 0,
            DomLiquidityPull = context.DomLiquidityPull ? 1 : 0,
            DomPressureDivergence = context.DomPressureDivergence ? 1 : 0,

            FootprintPositiveDelta = context.FootprintPositiveDelta ? 1 : 0,
            FootprintNegativeDelta = context.FootprintNegativeDelta ? 1 : 0,
            FootprintStackedImbalance = context.FootprintStackedImbalance ? 1 : 0,
            FootprintDeltaDivergence = context.FootprintDeltaDivergence ? 1 : 0,
            FootprintHvn = context.FootprintHvn ? 1 : 0,
            FootprintLvnRejection = context.FootprintLvnRejection ? 1 : 0,

            BoxBuyPressure = context.BoxBuyPressure ? 1 : 0,
            BoxSellPressure = context.BoxSellPressure ? 1 : 0,
            BoxPressureIncreasing = context.BoxPressureIncreasing ? 1 : 0,
            BoxPressureDecreasing = context.BoxPressureDecreasing ? 1 : 0,
            BoxPressureDivergence = context.BoxPressureDivergence ? 1 : 0,

            LocationDayHigh = context.LocationDayHigh ? 1 : 0,
            LocationDayLow = context.LocationDayLow ? 1 : 0,
            LocationPreviousDayHigh = context.LocationPreviousDayHigh ? 1 : 0,
            LocationPreviousDayLow = context.LocationPreviousDayLow ? 1 : 0,
            LocationPreviousClose = context.LocationPreviousClose ? 1 : 0,
            LocationSessionOpen = context.LocationSessionOpen ? 1 : 0,
            LocationVwap = context.LocationVwap ? 1 : 0,
            LocationOpeningRange = context.LocationOpeningRange ? 1 : 0,
            LocationSupport = context.LocationSupport ? 1 : 0,
            LocationResistance = context.LocationResistance ? 1 : 0,

            MarketTrending = context.MarketTrending ? 1 : 0,
            MarketRanging = context.MarketRanging ? 1 : 0,
            MarketExpansion = context.MarketExpansion ? 1 : 0,
            MarketCompression = context.MarketCompression ? 1 : 0,
            MarketChoppy = context.MarketChoppy ? 1 : 0,

            context.EventPhase,
            context.EventName,
            context.TradeThesis,
            context.ExecutionQuality,
            context.ContextConfidence,
            context.TradeQuality
        });
    }
    private static Trade MapTrade(dynamic r)
    {
        return new Trade
        {
            TradeId = r.TradeId?.ToString() ?? string.Empty,
            FolderDirectory = r.FolderDirectory?.ToString() ?? string.Empty,
            Symbol = r.Symbol?.ToString() ?? string.Empty,
            Account = r.Account?.ToString() ?? string.Empty,
            Side = string.Equals(
                r.Side?.ToString(),
                "SHORT",
                StringComparison.OrdinalIgnoreCase)
                ? TradeSide.Short
                : TradeSide.Long,

            Quantity = Convert.ToInt32(r.Quantity),
            EntryTime = ParseDate(r.EntryTime?.ToString()),
            ExitTime = ParseDate(r.ExitTime?.ToString()),

            EntryPrice = Convert.ToDecimal(r.EntryPrice),
            ExitPrice = Convert.ToDecimal(r.ExitPrice),

            InitialStopLoss = r.InitialStopLoss is null
                ? null
                : Convert.ToDecimal(r.InitialStopLoss),

            InitialTakeProfit = r.InitialTakeProfit is null
                ? null
                : Convert.ToDecimal(r.InitialTakeProfit),

            PreRollSeconds = r.PreRollSeconds is null
                ? 5
                : Convert.ToDouble(r.PreRollSeconds),

            PostRollSeconds = r.PostRollSeconds is null
                ? 5
                : Convert.ToDouble(r.PostRollSeconds),

            NetPnL = r.NetPnL is null
                ? 0
                : Convert.ToDecimal(r.NetPnL),

            GrossPnL = r.GrossPnL is null
                ? 0
                : Convert.ToDecimal(r.GrossPnL),

            Commission = r.Commission is null
                ? 0
                : Convert.ToDecimal(r.Commission),

            PlannedRR = r.PlannedRR is null
                ? null
                : Convert.ToDecimal(r.PlannedRR),

            RealizedRR = r.RealizedRR is null
                ? null
                : Convert.ToDecimal(r.RealizedRR),

            RealizedPoints = r.RealizedPoints is null
                ? 0
                : Convert.ToDecimal(r.RealizedPoints),

            RealizedTicks = r.RealizedTicks is null
                ? 0
                : Convert.ToDecimal(r.RealizedTicks),

            InitialRiskUSD = r.InitialRiskUSD is null
                ? 0
                : Convert.ToDecimal(r.InitialRiskUSD),

            ExpectedRewardUSD = r.ExpectedRewardUSD is null
                ? 0
                : Convert.ToDecimal(r.ExpectedRewardUSD),

            MAETicks = r.MAETicks is null
                ? null
                : Convert.ToDecimal(r.MAETicks),

            MAEUSD = r.MAEUSD is null
                ? null
                : Convert.ToDecimal(r.MAEUSD),

            MFETicks = r.MFETicks is null
                ? null
                : Convert.ToDecimal(r.MFETicks),

            MFEUSD = r.MFEUSD is null
                ? null
                : Convert.ToDecimal(r.MFEUSD),

            Notes = r.Notes?.ToString() ?? string.Empty,
            ImportedAt = ParseDate(r.ImportedAt?.ToString())
        };
    }

    private static DateTime ParseDate(string? value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : DateTime.MinValue;

    private static async Task LoadRelationsAsync(SqliteConnection conn, Trade trade)
    {
        trade.Videos = (await conn.QueryAsync<TradeVideo>("SELECT * FROM TradeVideos WHERE TradeId=@TradeId ORDER BY MonitorIndex", new { trade.TradeId })).AsList();
        var events = await conn.QueryAsync("SELECT * FROM TradeEvents WHERE TradeId=@TradeId ORDER BY Timestamp", new { trade.TradeId });
        trade.EventTimeline = events.Select(e => new TradeEvent
        {
            EventId = Convert.ToInt32(e.EventId), TradeId = e.TradeId?.ToString() ?? trade.TradeId,
            Timestamp = ParseDate(e.Timestamp?.ToString()),
            EventType = Enum.TryParse<TradeEventType>(e.EventType?.ToString(), out TradeEventType et) ? et : TradeEventType.GenericEvent,
            Price = e.Price is null ? null : Convert.ToDecimal(e.Price), Quantity = e.Quantity is null ? null : Convert.ToInt32(e.Quantity),
            Message = e.Message?.ToString() ?? string.Empty,
            VideoTimestampOffsetSeconds = e.VideoTimestampOffsetSeconds is null ? 0 : Convert.ToDouble(e.VideoTimestampOffsetSeconds)
        }).ToList();
        trade.Tags = (await conn.QueryAsync<string>("SELECT TagText FROM TradeTags WHERE TradeId=@TradeId ORDER BY TagId", new { trade.TradeId })).AsList();        trade.MarketContext = await conn.QueryFirstOrDefaultAsync<TradeMarketContext>(
            "SELECT * FROM TradeMarketContexts WHERE TradeId=@TradeId",
            new { trade.TradeId }) ?? new TradeMarketContext();
    }
}
