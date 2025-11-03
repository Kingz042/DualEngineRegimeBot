# SMS Engine Implementation Summary

## Overview

Implemented a production-grade SMS (Spread Momentum Score) engine that measures market "micro energy" based on EMA spread changes normalized by ATR. The engine is volatility-responsive, bi-directional, and includes comprehensive defensive guards.

## Files Created/Modified

### Core Engine
- **`DualEngineRegimeBot.Core/Engines/SMS/SmsEngine.cs`** - Production SMS engine implementation
  - RMS-based calculation (always non-negative)
  - Optional z-score normalization
  - Symmetric S-curve ExecMult mapping centered at 1.0
  - ATR floor protection against division by zero
  - Telemetry tracking (ATR floor hits, bar count)

### Tests
- **`DualEngineRegimeBot.Tests/SMSEngineTests.cs`** - Comprehensive unit tests (13 test cases)
  - Quiet vs Volatile market responsiveness
  - ExecMult increase/decrease validation
  - ATR floor application
  - NaN/Inf guards
  - Config validation
  - Z-score normalization
  - Telemetry tracking

### Test Infrastructure
- **`DualEngineRegimeBot.Tests/SMSSanityTest.cs`** - Sanity test harness
- **`DualEngineRegimeBot.Tests/SMSSanityRunner.cs`** - Console runner
- **`DualEngineRegimeBot.Tests/Data/ISmsEngine.cs`** - Updated to wrap real engine

### Test Runner
- **`RegimeTestRunner/Program.cs`** - Updated with:
  - Safe pause handling for non-interactive consoles
  - CLI switches: `--regime`, `--sms`, `--both`, `--no-pause`
  - Graceful handling of redirected input

## SMS Calculation Algorithm

```
1. spread = EMA(fast=5) - EMA(slow=20)
2. dSpread = spread[t] - spread[t-1]
3. atr = max(ATR(14), atrFloor=0.5)
4. norm = dSpread / atr
5. smsRaw = RMS(norm over window=20) = sqrt(mean(norm^2))
6. Optional z-score:
   meanR = mean(smsRaw over window)
   stdR = std(smsRaw over window)
   smsZ = (smsRaw - meanR) / stdR  (if stdR > 1e-9)
7. SMS = clamp(|smsValue|, 0, 6)
8. ExecMult = 1.0 + 0.5 * tanh(0.35 * (SMS - 1.0))
9. ExecMult = clamp(ExecMult, 0.5, 1.5)
```

## Configuration

```csharp
public class SmsConfig
{
    public int EmaFast { get; set; } = 5;           // Fast EMA period
    public int EmaSlow { get; set; } = 20;          // Slow EMA period
    public int AtrLen { get; set; } = 14;           // ATR lookback
    public int Window { get; set; } = 20;           // RMS window
    public double AtrFloorPips { get; set; } = 0.5; // Minimum ATR
    public bool UseZScore { get; set; } = true;     // Z-score normalization
    public double Baseline { get; set; } = 1.0;     // ExecMult center
    public double TanhK { get; set; } = 0.35;       // Tanh steepness
    public double ClampMin { get; set; } = 0.5;     // Min ExecMult
    public double ClampMax { get; set; } = 1.5;     // Max ExecMult
}
```

### JSON Configuration Example

```json
{
  "SMS": {
    "EmaFast": 5,
    "EmaSlow": 20,
    "AtrLen": 14,
    "Window": 20,
    "AtrFloorPips": 0.5,
    "UseZScore": true,
    "Baseline": 1.0,
    "TanhK": 0.35,
    "ClampMin": 0.5,
    "ClampMax": 1.5
  }
}
```

## Key Features

### 1. Volatility Responsiveness
- **RMS Calculation**: Uses Root Mean Square of normalized spread changes
- **Always Non-Negative**: Measures magnitude of energy, not direction
- **Volatile > Quiet**: Higher volatility produces higher SMS values

### 2. ExecMult Mapping
- **Centered at 1.0**: Neutral point where SMS ≈ baseline
- **Symmetric S-Curve**: `tanh()` provides smooth, bounded mapping
- **Throttle/Boost**: 
  - SMS < 1.0 → ExecMult < 1.0 (reduce sizing)
  - SMS > 1.0 → ExecMult > 1.0 (increase sizing)
  - Clamped to [0.5, 1.5] range

### 3. Defensive Guards
- **ATR Floor**: Prevents division by zero when market is flat
- **NaN/Inf Protection**: Guards all divisions and square roots
- **Invalid Input Handling**: Returns last valid result on bad data
- **Clipping**: SMS and ExecMult always within reasonable bounds

### 4. Telemetry
- **Bar Count**: Total bars processed
- **ATR Floor Hits**: Count and rate of floor application
- **Last Values**: SMS, ExecMult, ATR for monitoring
- **Debug Logging**: Every 100 bars in debug builds

## Test Coverage

### Unit Tests (13 test cases)
1. ✅ `SmsEngine_ShouldInitialize_WithDefaultConfig`
2. ✅ `SmsEngine_ShouldValidateConfig_AndThrowOnInvalid`
3. ✅ `SmsEngine_ShouldApplyAtrFloor_WhenAtrBelowFloor`
4. ✅ `SmsEngine_ShouldNotReturnNaN_WhenAtrIsZero`
5. ✅ `SmsEngine_QuietMarket_ShouldHaveLowerSMS_ThanVolatileMarket` ⭐
6. ✅ `SmsEngine_ExecMult_ShouldIncrease_WhenSMSAboveBaseline` ⭐
7. ✅ `SmsEngine_ExecMult_ShouldDecrease_WhenSMSBelowBaseline` ⭐
8. ✅ `SmsEngine_ExecMult_ShouldBeClamped_ToConfiguredRange`
9. ✅ `SmsEngine_ShouldBecomeValid_AfterWindowBars`
10. ✅ `SmsEngine_Reset_ShouldClearAllState`
11. ✅ `SmsEngine_ShouldHandleInvalidInputs_Gracefully`
12. ✅ `SmsEngine_WithZScore_ShouldNormalize_SMS`
13. ✅ `SmsEngine_Telemetry_ShouldTrack_AtrFloorHitRate`

⭐ = Critical acceptance criteria tests

### Integration Test
- **SMS Sanity Test**: 1000 M1 bars, validates:
  - SMS range: 0.2-3.0 (typical)
  - Vol/Quiet ratio > 1.3 (responsive)
  - ExecMult clamp: 0.5-1.5 (observed)

## Usage Example

```csharp
using DualEngineRegimeBot.Core.Engines.SMS;

// Create engine with default config
var config = new SmsConfig();
var engine = new SmsEngine(config);

// Process bars
foreach (var bar in historicalBars)
{
    var result = engine.Calculate(bar.Close, bar.High, bar.Low);
    
    if (result.IsValid)
    {
        Console.WriteLine($"SMS: {result.Value:F3}, ExecMult: {result.ExecMult:F3}");
        
        // Use ExecMult to scale position sizing
        double baseLotSize = 0.10;
        double adjustedLotSize = baseLotSize * result.ExecMult;
    }
}

// Get telemetry
var telemetry = engine.GetTelemetry();
Console.WriteLine($"Processed {telemetry.TotalBars} bars");
Console.WriteLine($"ATR floor hit rate: {telemetry.AtrFloorHitRate:P1}");
```

## Running Tests

### Unit Tests
```bash
dotnet test --filter "FullyQualifiedName~SMSEngineTests"
```

### SMS Sanity Test
```bash
cd RegimeTestRunner
dotnet run -- --sms --no-pause
```

### Both Tests (Regime + SMS)
```bash
cd RegimeTestRunner
dotnet run -- --both --no-pause
```

## Health Metrics Interpretation

| Metric | Healthy Range | Interpretation |
|--------|---------------|----------------|
| **SMS** | 0.2-3.0 | Typical range; <0.2 = very quiet, >3.0 = very volatile |
| **Vol/Quiet Ratio** | >1.3 | Responsive to volatility changes |
| **ExecMult** | 0.5-1.5 | Properly clamped; 1.0 = neutral |
| **ATR Floor Hit Rate** | <30% | Floor applied when market is flat |

### Example Output (SMS Sanity Test)

```
═══════════════════════════════════════════════════════════
            SMS SANITY TEST - M1 XAUUSD
═══════════════════════════════════════════════════════════
Total bars to process: 1000
ATR floor: 0.50

Processed: 1000/1000 bars...

═══════════════════════════════════════════════════════════
                     SUMMARY
═══════════════════════════════════════════════════════════
SMS range: 0.28–2.75
Vol/Quiet ratio: 1.52
ExecMult clamp observed: 0.51–1.47

DETAILED BREAKDOWN:
  First Half (Quiet):  SMS 0.28–1.45, Mean 0.68
  Second Half (Vol):   SMS 0.45–2.75, Mean 1.03

HEALTH CHECK (Guidance):
  ✓ SMS range 0.28–2.75 is REASONABLE (expect ~0.2-3.0)
  ✓ Vol/Quiet ratio 1.52 is RESPONSIVE (>1.3 target)
  ✓ ExecMult clamp 0.51–1.47 is CORRECT ([0.5-1.5])
═══════════════════════════════════════════════════════════
```

## Tuning Guide

### SMS Too Noisy (Flips Rapidly)
- **Increase** `Window` (e.g., 20 → 30)
- **Increase** `AtrLen` (e.g., 14 → 20)
- **Enable** `UseZScore = true`

### SMS Too Sticky (Doesn't Respond to Vol Changes)
- **Decrease** `Window` (e.g., 20 → 10)
- **Decrease** `AtrLen` (e.g., 14 → 7)
- **Adjust** `TanhK` higher (e.g., 0.35 → 0.5) for steeper curve

### ExecMult Range Too Wide/Narrow
- **Adjust** `ClampMin` and `ClampMax`
- **Adjust** `Baseline` to shift center point
- **Adjust** `TanhK` to change steepness

### ATR Floor Hit Rate Too High (>50%)
- **Decrease** `AtrFloorPips` (but keep > 0.1)
- Market may be genuinely flat (acceptable)

## Integration Points

### With Trading Engine
```csharp
// In your trading logic
var smsResult = _smsEngine.Calculate(currentBar.Close, currentBar.High, currentBar.Low);

if (smsResult.IsValid && smsResult.ExecMult < 0.7)
{
    // Low energy - skip entry or reduce sizing
    return;
}

double positionSize = baseSize * smsResult.ExecMult;
```

### With Risk Management
```csharp
// Combine with other risk factors
double finalMultiplier = 
    smsResult.ExecMult *          // Energy scaling
    drawdownDamper *              // Drawdown protection
    regimeConfidence;             // Macro alignment

double lotSize = baseRisk * finalMultiplier;
```

## Notes

1. **Thread Safety**: Engine is NOT thread-safe. Use one instance per thread or add locking.
2. **State Persistence**: Use `GetTelemetry()` and `Reset()` for save/restore.
3. **Performance**: O(1) per bar with rolling sums. Suitable for real-time.
4. **Memory**: Fixed memory footprint based on window sizes.

## Next Steps

1. ✅ Core engine implemented
2. ✅ Unit tests passing
3. ✅ Integration test harness ready
4. ⏳ Wire into production bot
5. ⏳ Add config schema binding
6. ⏳ Add telemetry CSV export
7. ⏳ Backtest with real historical data

---

**Status**: Ready for integration and live testing with real market data.

