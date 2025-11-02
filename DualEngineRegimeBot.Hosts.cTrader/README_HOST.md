# cTrader Host Adapter

## Overview

This project provides a production-grade host adapter that connects the `DualEngineRegimeBot.Core` library to the cTrader/cAlgo trading platform. It manages the tick feed, engine coordination, risk management, order execution, news-based restrictions, and FTMO compliance enforcement.

## Architecture

### Key Components

1. **IMarketDataAdapter** - Interface for market data feed
   - Provides tick events (Bid/Ask/Timestamp)
   - Symbol information (digits, point size, tick value, etc.)

2. **IOrderAdapter** - Interface for order execution
   - Place orders (market, limit, stop)
   - Close/modify positions
   - Query open positions and account status

3. **cTraderHost** - Main orchestrator
   - Wires tick feed → features → engines → risk → execution
   - Implements session windows (end-exclusive)
   - Broker-midnight daily loss reset
   - Position reconciliation on startup
   - Graceful shutdown with state persistence

4. **Mock Adapters** - Testing implementations
   - `MockMarketDataAdapter` - Simulates tick feed
   - `MockOrderAdapter` - Simulates broker with in-memory positions

## Configuration

The host uses `HostConfig` with an embedded `FtmoPreset`:

```csharp
var config = new HostConfig
{
    AppVersion = "1.2.0",
    Symbol = "XAUUSD",
    Preset = FtmoPreset.CreateDefault(),
    StatePath = "bot_state.json",
    TelemetryPath = "bot_telemetry.csv",
    NewsSource = "json",          // or "none"
    NewsJsonPath = "news.json",
    MaxEntrySpreadPts = 2.0       // From Preset
};
```

### FTMO Preset Parameters

- **MaxRiskPercentPerTrade**: 0.5% (risk per position)
- **MaxDailyLossPercent**: 5.0% (daily loss limit at broker midnight)
- **MaxDrawdownPercent**: 10.0% (total drawdown limit)
- **SessionStartHour**: 7 (UTC, inclusive)
- **SessionEndHour**: 21 (UTC, **EXCLUSIVE** - no entries at or after 21:00)
- **BrokerUtcOffsetHours**: +2 (for broker local time / daily reset calculation)
- **MaxEntrySpreadPts**: 2.0 points (spread guard threshold)
- **LabelPrefix**: "FTMO_DER" (position identifier prefix)
- **NewsSource**: "json" or "none" (news feed configuration)
- **NewsJsonPath**: Path to JSON file with news events

## Startup Banner

On startup, the host prints:
- App version
- Config version tag
- **SHA-256 config hash** (for audit trail)
- Symbol
- Broker UTC offset
- Session window
- Risk parameters
- Start timestamp (UTC)

Example:
```
╔══════════════════════════════════════════════════════════════╗
║       DualEngineRegimeBot - cTrader Host Adapter            ║
╚══════════════════════════════════════════════════════════════╝
App Version:       1.2.0
Config Version:    FTMO_Safe_v1.2
Config Hash:       a3f2b8c9d1e... (64-char hex)
Symbol:            XAUUSD
Broker UTC Offset: +2 hours
Session Window:    07:00 - 21:00 UTC (end exclusive)
Max Risk/Trade:    0.50%
Max Daily Loss:    5.00%
Max Drawdown:      10.00%
Label Prefix:      FTMO_DER
Started:           2025-11-01 12:00:00 UTC
════════════════════════════════════════════════════════════════
```

## Daily Loss Reset (Broker Midnight)

The host monitors for **broker midnight** (not UTC midnight) using `BrokerUtcOffsetHours`. This ensures daily loss limits reset at the broker's local day boundary, which is critical for FTMO compliance.

### How It Works

1. **Broker Local Time Calculation**:
   ```
   BrokerLocalTime = UtcTime + TimeSpan.FromHours(BrokerUtcOffsetHours)
   ```

2. **Midnight Detection**:
   - Compares broker local dates between ticks
   - When `prevBrokerDate < currentBrokerDate`, reset occurs

3. **Reset Actions**:
   - `_dailyRealizedPnL` reset to 0
   - `_dailyLossLocked` flag cleared
   - Peak equity tracking reset
   - Reset event logged with broker local time

4. **Example** (BrokerUtcOffsetHours = +2):
   - UTC 21:59 = Broker 23:59 (same day)
   - UTC 22:01 = Broker 00:01 (next day) → **Reset triggered**

### Testing

Use `FtmoPreset.HasCrossedBrokerMidnight(prevUtc, currUtc)` to verify logic:
```csharp
var preset = FtmoPreset.CreateDefault() with { BrokerUtcOffsetHours = 2 };
var crossed = preset.HasCrossedBrokerMidnight(
    new DateTime(2025, 11, 1, 21, 0, 0, DateTimeKind.Utc),  // Broker 23:00
    new DateTime(2025, 11, 1, 23, 0, 0, DateTimeKind.Utc)); // Broker 01:00 next day
// crossed == true
```

## Session Window (End-Exclusive)

- **Inside session**: New entries allowed (subject to other filters)
- **At session end hour**: No new entries (e.g., at 21:00 UTC, no entries)
- **Outside session**: Only exits/closes allowed

## News Adapter (INewsAdapter)

The host supports pluggable news feed adapters to enforce trading restrictions during high-impact news events.

### Configuration

```csharp
NewsSource = "json"  // or "none"
NewsJsonPath = "news.json"
```

### News Phases

1. **Normal**: All actions allowed
2. **Block** (0-2 min): No entries, no hedges
3. **UnwindOnly** (3-5 min): Only unwinds/exits allowed
4. **Restricted** (6-15 min): Hedges allowed with 2× threshold

### JSON Format

```json
[
  {
    "from": "2025-11-01T14:30:00Z",
    "to": "2025-11-01T14:45:00Z",
    "phase": "Block"
  },
  {
    "from": "2025-11-01T16:00:00Z",
    "to": "2025-11-01T16:15:00Z",
    "phase": "Restricted"
  }
]
```

### Overlapping Events

If multiple events overlap, the **most restrictive** phase is applied:
- Priority: Block > UnwindOnly > Restricted > Normal

### Implementations

- **JsonNewsAdapter**: Loads events from JSON file, binary-search lookup
- **NoNewsAdapter**: Always returns Normal (when NewsSource="none")

## Position Reconciliation

On startup, the host calls `GetOpenPositions(labelPrefix)` to reconcile any existing positions from a previous run. This prevents duplicate hedges and maintains state consistency.

## Graceful Shutdown

The host accepts a `CancellationToken`. On cancellation:
1. Stops accepting new ticks
2. Closes all open positions (optional, configurable)
3. Flushes state to `StatePath` (atomic write)
4. Flushes telemetry to `TelemetryPath` (with rotation)
5. Logs shutdown complete

## Wiring to Real cTrader API

To integrate with the actual cTrader/cAlgo API:

1. **Implement IMarketDataAdapter**:
   ```csharp
   public class cAlgoMarketDataAdapter : IMarketDataAdapter
   {
       private readonly Robot _robot; // cAlgo bot instance
       
       public cAlgoMarketDataAdapter(Robot robot)
       {
           _robot = robot;
           _robot.Bars.BarOpened += (args) => OnBar(args);
       }
       
       public SymbolInfo GetSymbolInfo(string symbol)
       {
           var sym = _robot.Symbols.GetSymbol(symbol);
           return new SymbolInfo
           {
               Symbol = symbol,
               Digits = sym.Digits,
               PointSize = sym.TickSize,
               TickValue = sym.TickValue,
               // ... map other fields
           };
       }
       
       // Wire _robot.MarketData.GetQuote() → OnTick event
   }
   ```

2. **Implement IOrderAdapter**:
   ```csharp
   public class cAlgoOrderAdapter : IOrderAdapter
   {
       private readonly Robot _robot;
       
       public OrderResult PlaceOrder(OrderRequest request)
       {
           var tradeType = request.Side == TradeSide.Buy 
               ? TradeType.Buy 
               : TradeType.Sell;
           
           var result = _robot.ExecuteMarketOrder(
               tradeType,
               request.Symbol,
               volumeInUnits: ConvertLotsToUnits(request.Volume),
               label: request.Label,
               stopLossPips: ConvertPriceToP ips(request.StopLoss),
               takeProfitPips: ConvertPriceToPips(request.TakeProfit));
           
           return new OrderResult
           {
               IsSuccessful = result.IsSuccessful,
               PositionId = result.Position?.Id ?? 0,
               FillPrice = result.Position?.EntryPrice,
               ErrorMessage = result.Error?.ToString(),
               TimestampUtc = DateTime.UtcNow
           };
       }
       
       // Implement other methods similarly
   }
   ```

3. **Instantiate cTraderHost** in cAlgo `OnStart()`:
   ```csharp
   public class MyBot : Robot
   {
       private cTraderHost _host;
       private CancellationTokenSource _cts;
       
       protected override void OnStart()
       {
           var marketData = new cAlgoMarketDataAdapter(this);
           var orderAdapter = new cAlgoOrderAdapter(this);
           var config = new HostConfig { Symbol = SymbolName };
           
           _host = new cTraderHost(config, marketData, orderAdapter);
           _cts = new CancellationTokenSource();
           
           Task.Run(() => _host.RunAsync(_cts.Token));
       }
       
       protected override void OnStop()
       {
           _cts?.Cancel();
       }
   }
   ```

## Testing

Use `MockMarketDataAdapter` and `MockOrderAdapter` for:
- Unit tests (without broker API)
- Backtesting from CSV tick files
- Walk-forward/MC simulations (see `DualEngineRegimeBot.Runner`)

## Notes

- **Thread Safety**: Host uses single-threaded tick processing. If cTrader API is multi-threaded, add locking.
- **State Persistence**: Uses `AtomicFile` for crash-safe writes.
- **Telemetry Rotation**: Rotates CSV logs at configurable size (default 10 MB, retain 5 files).
- **Config Hash**: SHA-256 hash printed at startup for audit/compliance verification.

## Next Steps

1. Wire actual cTrader API (see above)
2. Integrate Core engines (MR, Trend, SMS, NewsGuard, Hedging)
3. Connect Risk & Sizer modules
4. Enable state persistence on tick or interval
5. Add comprehensive logging/telemetry

