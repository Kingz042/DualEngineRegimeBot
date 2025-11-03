# Regime Stability Test - M15 Sanity Check

## Overview

This test harness validates that your `RegimeSupervisor` produces stable, meaningful regime classifications across historical M15 bars. It's designed to catch excessive flip-rate (too noisy) or sticky regimes (too infrequent changes) before deploying to live trading.

## Purpose

- **Validate regime classification logic** on historical data
- **Detect configuration issues** (e.g., overly sensitive parameters)
- **Establish baseline metrics** for Bull/Bear × High/Low Vol classification
- **Generate CSV audit trail** for manual review

## Files Created

| File | Purpose |
|------|---------|
| `Data/IBarLoader.cs` | Interface for loading historical bars + stub implementation |
| `RegimeStabilityTest.cs` | Core test logic with CSV export and health checks |
| `RegimeStabilityRunner.cs` | Standalone console runner |
| `RegimeStabilityTestRunner.cs` | xUnit test wrapper (for CI/CD integration) |

## Quick Start

### Option 1: Run from xUnit Test Suite

```bash
cd DualEngineRegimeBot.Tests

# Edit RegimeStabilityTestRunner.cs and remove [Fact(Skip = "...")] attribute

dotnet test --filter "FullyQualifiedName~RegimeStabilityTestRunner"
```

### Option 2: Standalone Console Runner

Add this to your `Program.cs` or create a dedicated console app:

```csharp
using DualEngineRegimeBot.Tests;
using DualEngineRegimeBot.Tests.Data;

// Use stub loader (generates synthetic data)
var barLoader = new StubBarLoader();

// Or wire your real data loader:
// var barLoader = new YourCsvBarLoader("data/XAUUSD_M15.csv");

RegimeStabilityRunner.Run(
    barLoader: barLoader,
    symbol: "XAUUSD",
    barCount: 200,
    atrFloor: 1.0
);
```

### Option 3: Direct API Call

```csharp
using DualEngineRegimeBot.Core.Macro;
using DualEngineRegimeBot.Tests;
using DualEngineRegimeBot.Tests.Data;

// Load bars
var barLoader = new StubBarLoader();
var bars = barLoader.Load("XAUUSD", TimeFrame.Minute15, 200);

// Create supervisor
var supervisor = new RegimeSupervisor();

// Run test
RegimeStabilityTest.Run(
    supervisor: supervisor,
    m15Bars: bars,
    atrFloor: 1.0,
    outputPath: "regime_test.csv"
);
```

## Output

### Console Summary

```
═══════════════════════════════════════════════════════════
         REGIME STABILITY TEST - M15 XAUUSD
═══════════════════════════════════════════════════════════
Total bars to process: 200
ATR floor for normalization: 1.00
Output file: regime_test.csv

Processed: 200/200 bars...

═══════════════════════════════════════════════════════════
                     SUMMARY
═══════════════════════════════════════════════════════════
✓ Saved 200 rows to regime_test.csv

REGIME STATISTICS:
  Total regime flips:        12
  Flip rate (per hour):      0.24
  Avg regime duration:       16.7 bars
  Avg confidence:            0.67

HEALTH CHECK (Guidance):
  ⚠ Flip rate 0.24/hr is LOW (too sticky, target: 0.3-0.8)
  ✓ Avg regime duration 16.7 bars is HEALTHY (≥5 bars)
  ✓ Avg confidence 0.67 is HEALTHY (≥0.5)
═══════════════════════════════════════════════════════════
```

### CSV Output (`regime_test.csv`)

```csv
Index,Time,Direction,Volatility,Confidence
0,2025-11-01 10:00:00,Bull,LowVol,0.65
1,2025-11-01 10:15:00,Bull,LowVol,0.72
2,2025-11-01 10:30:00,Bull,HighVol,0.58
3,2025-11-01 10:45:00,Neutral,HighVol,0.42
...
```

## Health Metrics (Guidance)

| Metric | Healthy Range | Interpretation |
|--------|---------------|----------------|
| **Flip Rate** | 0.3–0.8 per hour | Too low = sticky/insensitive; Too high = noisy/overtrading |
| **Avg Regime Duration** | ≥5 bars (75 min) | Prevents excessive position churn |
| **Avg Confidence** | ≥0.5 | Majority of classifications should be decisive |

### Interpreting Results

#### ✅ **Healthy Example**
```
Flip rate: 0.45/hr (3 flips per ~7 hours)
Avg duration: 18 bars (4.5 hours)
Avg confidence: 0.68
```
→ Regime classifier is stable, confident, and actionable.

#### ⚠️ **Too Noisy**
```
Flip rate: 1.2/hr (every ~50 minutes)
Avg duration: 3 bars (45 minutes)
Avg confidence: 0.52
```
→ **Action**: Increase EMA periods, tighten confidence thresholds, or add regime change cooldown.

#### ⚠️ **Too Sticky**
```
Flip rate: 0.1/hr (every ~10 hours)
Avg duration: 50 bars (12.5 hours)
Avg confidence: 0.58
```
→ **Action**: Decrease EMA periods, lower confidence threshold, or check if volatility detection is too conservative.

## Wiring Your Real Data Source

Replace `StubBarLoader` with your actual data source:

### Example: CSV Loader

```csharp
public class CsvBarLoader : IBarLoader
{
    private readonly string _dataDirectory;

    public CsvBarLoader(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    public IList<Bar> Load(string symbol, TimeFrame timeFrame, int lastNBars)
    {
        var filePath = Path.Combine(_dataDirectory, $"{symbol}_{timeFrame}.csv");
        var bars = new List<Bar>();

        foreach (var line in File.ReadLines(filePath).Skip(1)) // Skip header
        {
            var parts = line.Split(',');
            bars.Add(new Bar
            {
                Time = DateTime.Parse(parts[0]),
                Open = double.Parse(parts[1]),
                High = double.Parse(parts[2]),
                Low = double.Parse(parts[3]),
                Close = double.Parse(parts[4]),
                Volume = long.Parse(parts[5])
            });
        }

        // Return last N bars
        return bars.Skip(Math.Max(0, bars.Count - lastNBars)).ToList();
    }
}
```

### Example: cTrader/cAlgo Integration

```csharp
public class CTraderBarLoader : IBarLoader
{
    private readonly Robot _robot; // Your cAlgo Robot instance

    public CTraderBarLoader(Robot robot)
    {
        _robot = robot;
    }

    public IList<Bar> Load(string symbol, TimeFrame timeFrame, int lastNBars)
    {
        var bars = new List<Bar>();
        var series = _robot.MarketData.GetBars(ConvertTimeFrame(timeFrame), symbol);

        for (int i = Math.Max(0, series.Count - lastNBars); i < series.Count; i++)
        {
            bars.Add(new Bar
            {
                Time = series[i].OpenTime,
                Open = series[i].Open,
                High = series[i].High,
                Low = series[i].Low,
                Close = series[i].Close,
                Volume = (long)series[i].TickVolume
            });
        }

        return bars;
    }

    private cAlgo.API.TimeFrame ConvertTimeFrame(TimeFrame tf)
    {
        return tf switch
        {
            TimeFrame.Minute15 => cAlgo.API.TimeFrame.Minute15,
            _ => throw new NotSupportedException($"TimeFrame {tf} not supported")
        };
    }
}
```

## Integration with CI/CD

Add to your test suite for automated regime validation:

```yaml
# .github/workflows/test.yml
- name: Run Regime Stability Test
  run: |
    dotnet test --filter "FullyQualifiedName~RegimeStabilityTestRunner"
    cat regime_test.csv | head -20
```

## Troubleshooting

### Issue: "No bars loaded"
**Solution**: Verify your `IBarLoader` implementation returns data for the specified symbol/timeframe.

### Issue: Flip rate too high (>1.0/hr)
**Solution**: 
- Increase EMA smoothing periods in `RegimeModule`
- Raise confidence threshold for regime changes
- Add hysteresis (require N consecutive bars before flip)

### Issue: Flip rate too low (<0.2/hr)
**Solution**:
- Decrease EMA periods for faster response
- Lower confidence threshold
- Check if volatility state is stuck in one mode

### Issue: Low average confidence (<0.4)
**Solution**:
- Review regime classification logic
- Ensure ATR floor is not too high (masking true vol changes)
- Validate EMA cross-over thresholds

## Defensive Guards Implemented

1. **Confidence Clamping**: All confidence values are clamped to `[0, 1]` before logging
2. **ATR Floor**: Minimum ATR value (`atrFloor` parameter) prevents division by zero and excessive normalization in low-vol periods
3. **Null Safety**: All inputs validated with `ArgumentNullException` guards
4. **CSV Escaping**: Timestamps use fixed format to avoid locale issues

## Next Steps

1. **Run with synthetic data** to validate test infrastructure
2. **Wire your real bar loader** (CSV, database, or broker API)
3. **Establish your baseline** flip rate and confidence for XAUUSD M15
4. **Tune parameters** if metrics fall outside healthy ranges
5. **Add to CI/CD** for regression testing on regime logic changes

---

**Note**: This is a **sanity check**, not a backtest. It validates regime classification stability, not trading performance. Use this to ensure your regime detector behaves predictably before running full strategy backtests.

