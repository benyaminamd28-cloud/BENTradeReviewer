namespace BENTradeReviewer.Core.Models;

public sealed class TradeMarketContext
{
    // Session
    public string Session { get; set; } = "Unknown"; // RTH / ETH / Unknown

    // GEX Environment
    public string GexEnvironment { get; set; } = "Neutral"; // Positive / Negative / Neutral

    // GEX Location / Context
    public bool GexCallWall { get; set; }
    public bool GexPutWall { get; set; }
    public bool GexGammaFlip { get; set; }
    public bool GexHighLevel { get; set; }
    public bool GexNearLevel { get; set; }
    public string GexRelation { get; set; } = "Neutral"; // Supportive / Opposing / Neutral

    // Iceberg
    public bool IcebergPresent { get; set; }
    public bool IcebergNative { get; set; }
    public bool IcebergSynthetic { get; set; }
    public bool IcebergWithTrade { get; set; }
    public bool IcebergAgainstTrade { get; set; }
    public bool IcebergHeld { get; set; }
    public bool IcebergFailed { get; set; }

    // DOM Pressure
    public bool DomBidPressure { get; set; }
    public bool DomAskPressure { get; set; }
    public bool DomRepeatedRejection { get; set; }
    public bool DomRefill { get; set; }
    public bool DomLiquidityPull { get; set; }
    public bool DomPressureDivergence { get; set; }

    // Footprint
    public bool FootprintPositiveDelta { get; set; }
    public bool FootprintNegativeDelta { get; set; }
    public bool FootprintStackedImbalance { get; set; }
    public bool FootprintDeltaDivergence { get; set; }
    public bool FootprintHvn { get; set; }
    public bool FootprintLvnRejection { get; set; }

    // Box Chart
    public bool BoxBuyPressure { get; set; }
    public bool BoxSellPressure { get; set; }
    public bool BoxPressureIncreasing { get; set; }
    public bool BoxPressureDecreasing { get; set; }
    public bool BoxPressureDivergence { get; set; }

    // Location
    public bool LocationDayHigh { get; set; }
    public bool LocationDayLow { get; set; }
    public bool LocationPreviousDayHigh { get; set; }
    public bool LocationPreviousDayLow { get; set; }
    public bool LocationPreviousClose { get; set; }
    public bool LocationSessionOpen { get; set; }
    public bool LocationVwap { get; set; }
    public bool LocationOpeningRange { get; set; }
    public bool LocationSupport { get; set; }
    public bool LocationResistance { get; set; }

    // Market State
    public bool MarketTrending { get; set; }
    public bool MarketRanging { get; set; }
    public bool MarketExpansion { get; set; }
    public bool MarketCompression { get; set; }
    public bool MarketChoppy { get; set; }

    // Event
    public string EventPhase { get; set; } = "None"; // None / Before / During / After
    public string EventName { get; set; } = string.Empty;

    // Trade intent
    public string TradeThesis { get; set; } = "Other";

    // Execution self-review
    public string ExecutionQuality { get; set; } = "Acceptable";

    // Self-assessment
    public int ContextConfidence { get; set; } = 0; // 0=Low, 1=Medium, 2=High
    public int TradeQuality { get; set; } = 0;     // 0..5 stars
}
