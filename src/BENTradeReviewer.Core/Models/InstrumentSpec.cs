namespace BENTradeReviewer.Core.Models;

public sealed class InstrumentSpec
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal TickSize { get; set; }
    public decimal TickValue { get; set; }
    public decimal PointValue { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal DefaultCommission { get; set; }

    public static IReadOnlyList<InstrumentSpec> GetDefaultCatalog() => new List<InstrumentSpec>
    {
        new() { Symbol = "6E",  Name = "Euro FX Futures",              TickSize = 0.00005m, TickValue = 6.25m,  PointValue = 125000m, DefaultCommission = 2.50m },
        new() { Symbol = "ES",  Name = "E-mini S&P 500",                TickSize = 0.25m,    TickValue = 12.50m, PointValue = 50m,     DefaultCommission = 2.50m },
        new() { Symbol = "NQ",  Name = "E-mini Nasdaq-100",             TickSize = 0.25m,    TickValue = 5.00m,  PointValue = 20m,     DefaultCommission = 2.50m },
        new() { Symbol = "MES", Name = "Micro E-mini S&P 500",          TickSize = 0.25m,    TickValue = 1.25m,  PointValue = 5m,      DefaultCommission = 0.75m },
        new() { Symbol = "MNQ", Name = "Micro E-mini Nasdaq-100",       TickSize = 0.25m,    TickValue = 0.50m,  PointValue = 2m,      DefaultCommission = 0.75m },
        new() { Symbol = "CL",  Name = "Crude Oil Futures",              TickSize = 0.01m,     TickValue = 10m,    PointValue = 1000m,   DefaultCommission = 2.50m },
        new() { Symbol = "GC",  Name = "Gold Futures",                    TickSize = 0.10m,     TickValue = 10m,    PointValue = 100m,    DefaultCommission = 2.50m },
        new() { Symbol = "RTY", Name = "E-mini Russell 2000",             TickSize = 0.10m,     TickValue = 5m,     PointValue = 50m,     DefaultCommission = 2.50m },
        new() { Symbol = "ZB",  Name = "30-Year U.S. Treasury Bond",     TickSize = 0.03125m,  TickValue = 31.25m, PointValue = 1000m,   DefaultCommission = 2.50m }
    };
}
