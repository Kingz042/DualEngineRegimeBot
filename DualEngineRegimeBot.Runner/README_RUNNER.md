# DualEngineRegimeBot Runner

## Overview

Console application for running batch **walk-forward** and **Monte Carlo** simulations on the DualEngineRegimeBot. Produces KPI metrics in CSV format for analysis and optimization.

## Features

- **Walk-Forward Analysis**: Sliding window optimization with in-sample and out-of-sample periods
- **Monte Carlo Simulation**: Randomized trade sequence testing for robustness
- **Deterministic Seeds**: Reproducible results for regression testing
- **KPI Metrics**: Comprehensive performance statistics
- **Config Hashing**: SHA-256 hash of configuration for audit trail

## Usage

### Basic Command Structure

```bash
DualEngineRegimeBot.Runner [options]
```

### Options

| Option | Description | Required |
|--------|-------------|----------|
| `--config <path>` | Path to configuration JSON | Yes |
| `--symbol <symbol>` | Trading symbol (e.g., XAUUSD) | Yes |
| `--from <date>` | Start date (yyyy-MM-dd) | No (default: 2024-01-01) |
| `--to <date>` | End date (yyyy-MM-dd) | No (default: 2025-10-31) |
| `--wf <NxM>` | Walk-forward: N months in-sample, M months out-sample | Mode |
| `--mc <iterations>` | Monte Carlo: number of iterations | Mode |
| `--out <path>` | Output CSV path | No (default: kpis.csv) |
| `--seed <number\|random>` | Random seed for reproducibility | No (default: 42) |
| `--data <path>` | Historical tick data CSV path (future) | No |
| `--help`, `-h` | Show help message | No |

## Examples

### Walk-Forward Analysis

Run a 4-month in-sample / 3-month out-of-sample walk-forward over 2024:

```bash
DualEngineRegimeBot.Runner \
  --config ftmo_preset.json \
  --symbol XAUUSD \
  --from 2024-01-01 \
  --to 2025-10-31 \
  --wf 4x3 \
  --out wf_results.csv
```

**Output**: CSV with one row per walk-forward window showing out-of-sample KPIs.

### Monte Carlo Simulation

Run 1000 Monte Carlo iterations with random seed:

```bash
DualEngineRegimeBot.Runner \
  --config ftmo_preset.json \
  --symbol XAUUSD \
  --mc 1000 \
  --seed random \
  --out mc_results.csv
```

**Output**: CSV with 1000 rows, one per Monte Carlo run.

### Deterministic Run

For regression testing, use a fixed seed:

```bash
DualEngineRegimeBot.Runner \
  --config ftmo_preset.json \
  --symbol XAUUSD \
  --mc 100 \
  --seed 12345 \
  --out regression_test.csv
```

## Output Format

The output CSV contains the following columns:

| Column | Description |
|--------|-------------|
| `RunId` | Run identifier (e.g., WF_1, MC_42) |
| `Symbol` | Trading symbol |
| `FromDate` | Period start date |
| `ToDate` | Period end date |
| `NumTrades` | Total number of trades |
| `WinRate` | Win rate percentage |
| `ProfitFactor` | Total profit / total loss |
| `NetProfit` | Net profit in account currency |
| `MaxDrawdown` | Maximum drawdown percentage |
| `CAGR` | Compound Annual Growth Rate |
| `Expectancy` | Average profit per trade |
| `AvgWin` | Average winning trade |
| `AvgLoss` | Average losing trade |
| `MAR` | MAR ratio (CAGR / MaxDrawdown) |
| `ConfigHash` | SHA-256 hash of config for audit |

### Example Output

```csv
RunId,Symbol,FromDate,ToDate,NumTrades,WinRate,ProfitFactor,NetProfit,MaxDrawdown,CAGR,Expectancy,AvgWin,AvgLoss,MAR,ConfigHash
WF_1,XAUUSD,2024-01-01,2024-04-01,45,58.00,1.85,5420.00,3.20,18.50,120.44,650.00,420.00,5.78,a3f2b8c9...
WF_2,XAUUSD,2024-04-01,2024-07-01,52,55.00,1.72,4820.00,4.10,16.20,92.69,680.00,450.00,3.95,a3f2b8c9...
```

## Walk-Forward Logic

The walk-forward process:

1. **Window Definition**: Define in-sample (IS) and out-of-sample (OOS) periods
2. **Optimization**: Optimize parameters on IS period (future feature)
3. **Testing**: Test optimized parameters on OOS period
4. **Slide**: Move window forward by IS period length
5. **Repeat**: Continue until end date reached

Example with `--wf 4x3`:
- Window 1: IS = Jan-Apr, OOS = May-Jul
- Window 2: IS = May-Aug, OOS = Sep-Nov
- Window 3: IS = Sep-Dec, OOS = Jan-Mar (next year)
- ...

Only OOS results are recorded in the output CSV.

## Monte Carlo Logic

The Monte Carlo process:

1. **Initialize**: Set random seed (or use random)
2. **Iterate**: Run N independent simulations
3. **Randomize**: Shuffle trade sequences or parameters (implementation-dependent)
4. **Record**: Store KPIs for each iteration
5. **Aggregate**: Analyze distribution of results (median, percentiles, etc.)

## Configuration Loading

The runner loads an FTMO preset from JSON (future enhancement). Currently uses `FtmoPreset.CreateDefault()`.

### Example Config JSON (future)

```json
{
  "versionTag": "FTMO_Safe_v1.2",
  "maxRiskPercentPerTrade": 0.5,
  "maxDailyLossPercent": 5.0,
  "maxDrawdownPercent": 10.0,
  "maxOpenPositions": 3,
  "sessionStartHour": 7,
  "sessionEndHour": 21,
  "brokerUtcOffsetHours": 2
}
```

## KPI Metrics Explained

### Win Rate
Percentage of winning trades: `(Wins / Total Trades) × 100`

### Profit Factor
Ratio of total profit to total loss: `TotalProfit / TotalLoss`
- Values > 1.0 indicate profitability
- Typical good systems: 1.5-2.5

### CAGR (Compound Annual Growth Rate)
Annualized return: `((FinalBalance / InitialBalance)^(1/Years) - 1) × 100`

### Max Drawdown
Largest peak-to-trough decline: `((Peak - Trough) / Peak) × 100`

### Expectancy
Average profit per trade: `NetProfit / NumTrades`

### MAR Ratio (Managed Account Return)
Risk-adjusted return: `CAGR / MaxDrawdown`
- Values > 1.0 indicate return exceeds drawdown
- Typical good systems: 2.0-5.0

## Performance

- **Walk-Forward**: ~1-10 windows per run (depends on period length)
- **Monte Carlo**: 1000 iterations in ~1-5 seconds (simplified simulation)
- **Output**: Single CSV file, typically < 1 MB for 1000 runs

## Limitations (Current Version)

1. **Simplified Simulation**: Generates random trade sequences instead of replaying actual bot logic
2. **No Historical Data**: Future version will replay tick CSVs through full bot engine
3. **No Parameter Optimization**: Walk-forward currently uses fixed parameters
4. **No Trade Shuffling**: Monte Carlo doesn't shuffle actual trade sequences yet

## Future Enhancements

1. **Historical Replay**: Load tick CSVs and replay through full bot
2. **Parameter Optimization**: Optimize on IS period, test on OOS
3. **Trade Shuffling**: Monte Carlo with actual trade sequence permutations
4. **Parallel Execution**: Multi-threaded for faster MC iterations
5. **JSON Config Loading**: Full config deserialization from file
6. **Advanced KPIs**: Sharpe ratio, Sortino ratio, Calmar ratio, UPI

## Testing

Basic tests verify:
- Command-line parsing
- KPI calculations
- CSV output format
- Walk-forward windowing logic
- Deterministic seeding

## Integration with Host

The runner uses `MockMarketDataAdapter` and `MockOrderAdapter` to simulate trading without a live broker connection. This allows:
- Fast backtesting
- Reproducible results
- No broker API dependencies

## Notes

- **Deterministic**: Using fixed seed ensures identical results across runs
- **Config Hash**: Includes SHA-256 hash in output for audit/compliance
- **CSV Format**: Standard CSV for easy import into Excel, R, Python
- **No Warm-Up**: First trades may show initialization effects

## Example Workflow

1. **Develop Strategy**: Code in `DualEngineRegimeBot.Core`
2. **Run Walk-Forward**: Test on historical periods
3. **Analyze Results**: Load CSV into analysis tool
4. **Run Monte Carlo**: Assess robustness
5. **Deploy Live**: Use `cTraderHost` with real broker

## Support

For issues or questions:
- Check command-line options with `--help`
- Verify config file exists and is valid JSON
- Ensure date ranges are reasonable
- Check output directory is writable

