# Quick Start Guide - Dual-Engine Regime Bot

## What You Have Now

Your `C:\Users\kelechi\Documents\DualEngineRegimeBot\` folder contains:

```
DualEngineRegimeBot/
├── README.md                        # Full documentation
├── QUICKSTART.md                    # This file
├── DualEngineRegimeBot.sln          # Visual Studio solution
├── CTRADER_SINGLE_FILE_BOT.cs       # Template for cTrader
│
├── DualEngineRegimeBot.Core/        # ✅ COMPLETE - All logic
│   ├── CoreModels.cs
│   ├── ServiceInterfaces.cs
│   ├── Config/
│   │   ├── ConfigModels.cs
│   │   └── SymbolPresets.cs
│   ├── Indicators/
│   │   ├── KalmanMean.cs
│   │   ├── AtrEma.cs
│   │   └── KappaEstimator.cs
│   ├── Sizing/
│   │   └── InverseVolSizer.cs
│   ├── Risk/
│   │   └── RiskService.cs
│   ├── Telemetry/
│   │   └── CsvTelemetry.cs
│   ├── Macro/
│   │   └── RegimeModule.cs
│   ├── Engines/
│   │   ├── TrendFollowerPQ/
│   │   │   └── TrendFollowerService.cs
│   │   └── SareMeanReversion/
│   │       └── SareService.cs
│   ├── Hedging/
│   │   └── TailHedgeService.cs
│   └── State/
│       └── JsonStateStore.cs
│
└── DualEngineRegimeBot.Tests/       # ✅ COMPLETE - Unit tests
    ├── SizingTests.cs
    ├── KappaEstimatorTests.cs
    └── SpreadGuardTests.cs
```

## Step 1: Build & Test Core Library

```powershell
# Open PowerShell and navigate to your project
cd C:\Users\kelechi\Documents\DualEngineRegimeBot

# Build Core library
cd DualEngineRegimeBot.Core
dotnet build

# Run tests
cd ..\DualEngineRegimeBot.Tests
dotnet test

# You should see: 3 tests passed
```

## Step 2: Choose Your Integration Path

### Option A: For cTrader (Recommended)

Since cTrader's Automate doesn't easily support multi-project solutions, you need to create a **single-file cBot**.

**What to do:**

1. Open `CTRADER_SINGLE_FILE_BOT.cs` in this folder
2. Follow the instructions inside to merge all Core classes
3. Copy the merged file into cTrader Automate
4. Configure and run

**Estimated time:** 30-60 minutes of copy/paste + light refactoring

### Option B: For Visual Studio Development

If you want to develop/test in Visual Studio first:

1. Open `DualEngineRegimeBot.sln` in Visual Studio 2022
2. Create the `DualEngineRegimeBot.Algo` project (requires cTrader SDK)
3. Wire services following the architecture in README.md
4. Build and test before moving to cTrader

**Estimated time:** 2-4 hours (requires cTrader SDK setup)

## Step 3: Configuration

Before running, decide on:

1. **Symbol:** XAUUSD or BTCUSD (presets included)
2. **Timeframe:** M1 (1-minute bars)
3. **Risk:** 0.5% base risk per trade (adjustable)
4. **Output:** Logs will be in `Documents\DualEngineBot_Logs\`

The presets in `Config/SymbolPresets.cs` are production-ready defaults.

## Step 4: Backtest First (Critical!)

**Do NOT go live without backtesting!**

1. Run a 72-hour backtest in cTrader
2. Check `DualEngineBot_Logs/trades.csv` for:
   - Entry/exit reasons
   - Risk percentages
   - VDI/Kappa values
3. Verify state persistence by stopping/restarting bot
4. Confirm spread guard blocks wide spreads
5. Verify daily loss lock activates at -2%

## Step 5: Paper Trade (Recommended)

After successful backtests:

1. Run in cTrader **demo account** for 1 week
2. Monitor telemetry daily
3. Verify warmup period (1000 bars = ~16 hours)
4. Check tail-hedge triggers (should be rare: 1-3/week)

## Step 6: Go Live (Proceed with Caution)

Only after successful demo:

1. Start with **minimum volume** (0.01 lots)
2. Monitor for 2-3 days
3. Gradually scale up volume
4. Always respect risk limits (daily loss, drawdown)

---

## Common Issues & Solutions

### "I can't build the project"

**Solution:** Ensure .NET 6.0 SDK is installed:
```powershell
dotnet --version
# Should show: 6.0.x or higher
```

If not installed, download from: https://dotnet.microsoft.com/download/dotnet/6.0

### "Tests are failing"

**Solution:** Check test output for specific failures. Common issues:
- Floating-point precision (use `Assert.Equal(x, y, precision)`)
- NaN/Infinity guards (ensure indicators initialized)

### "cTrader can't find Core classes"

**Solution:** You need to create a single-file bot or set up DLL references. See Option A above.

### "Logs folder not created"

**Solution:** Check that:
1. Bot has `AccessRights.FileSystem` in `[Robot]` attribute
2. Path is correct: `Documents\DualEngineBot_Logs\`
3. Bot actually ran (check for warmup completion)

### "State not persisting"

**Solution:**
1. Check `state_*.json` file exists in logs folder
2. Verify atomic write succeeded (no `.tmp` file stuck)
3. Enable debug logging in `JsonStateStore.cs`

---

## Next Steps After Setup

1. **Tune Parameters** - Adjust VDI thresholds based on backtest results
2. **Monitor Telemetry** - Review CSV logs weekly for patterns
3. **Optimize Regime Detection** - Tweak M15 EMA/ATR periods if needed
4. **Add New Symbols** - Create presets for EUR/USD, etc.
5. **Extend Engines** - Add custom entry/exit logic

---

## File Locations Summary

| File Type | Location |
|-----------|----------|
| **Source Code** | `C:\Users\kelechi\Documents\DualEngineRegimeBot\` |
| **Build Output** | `DualEngineRegimeBot.Core\bin\Debug\net6.0\` |
| **Trade Logs** | `C:\Users\kelechi\Documents\DualEngineBot_Logs\trades.csv` |
| **Bar Logs** | `C:\Users\kelechi\Documents\DualEngineBot_Logs\bars.csv` |
| **State** | `C:\Users\kelechi\Documents\DualEngineBot_Logs\state_*.json` |

---

## Support

This is a complete, self-contained implementation. For issues:

1. Check **README.md** for architecture details
2. Review **telemetry logs** for runtime behavior
3. Run **unit tests** to verify core logic
4. Inspect **state.json** for persistence issues

**No external support is provided.** This is educational/research code.

---

## Final Checklist Before Live Trading

- [ ] Backtested 72+ hours with realistic spread/slippage
- [ ] Verified all 3 unit tests pass
- [ ] Confirmed state persistence works (stop/restart test)
- [ ] Checked spread guard blocks wide spreads
- [ ] Validated daily loss lock activates at threshold
- [ ] Reviewed 50+ trades in telemetry logs
- [ ] Paper traded 1+ week in demo account
- [ ] Understand every exit reason in ExitReason enum
- [ ] Know how to interpret VDI, Kappa, TF_Bias values
- [ ] Have emergency stop procedure (force close all positions)

**If any checkbox is unchecked, DO NOT go live.**

---

**Good luck! Remember: Test, tune, monitor. 🚀**

