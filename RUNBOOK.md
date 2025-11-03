# DualEngineRegimeBot - Operational Runbook

**Version:** 1.2  
**Last Updated:** 2025-11-01  
**Audience:** Trading operators, DevOps, Risk managers

---

## Table of Contents

1. [Pre-Session Checklist](#pre-session-checklist)
2. [Decision Tables](#decision-tables)
3. [During-Session Operations](#during-session-operations)
4. [Post-Session Review](#post-session-review)
5. [Emergency Procedures](#emergency-procedures)
6. [Troubleshooting Guide](#troubleshooting-guide)

---

## Pre-Session Checklist

### 1. System Health Verification

- [ ] **Config integrity**: Verify `ConfigHash` matches deployed version
- [ ] **State persistence**: Confirm `state_*.json` loaded successfully
- [ ] **Market data**: Check data feed latency <100ms
- [ ] **Execution connectivity**: Test order placement (paper trade)
- [ ] **Margin availability**: Free margin ≥ 50% of used margin
- [ ] **DLQ status**: Confirm `DLQErrorCount < 5/hour`

### 2. Risk Parameters Review

- [ ] **Drawdown status**: Confirm current DD% and damper level
- [ ] **Survival mode**: Check if active (should be `false` for normal ops)
- [ ] **Daily loss allowance**: Review remaining before lock (target: ≤2%)
- [ ] **Max concurrent positions**: Verify limit (default: 3 for XAU, 2 for BTC)
- [ ] **Hedge controller**: Confirm `Enabled=true`, `Hmult=1.2`

### 3. Market Conditions Assessment

- [ ] **Spread levels**: Confirm spread ≤ 1.5× median (use last 100 ticks)
- [ ] **NewsGuard**: Check for active phases (should be `Normal`)
- [ ] **Regime state**: Review Direction/VolState/Confidence from M15
- [ ] **Volatility**: Check if NATR within expected range (0.30% XAU, 0.80% BTC)

### 4. Telemetry & Logging

- [ ] **CSV logs**: Confirm `trades.csv`, `bars.csv`, `execution_qos.csv` writable
- [ ] **Disk space**: Ensure ≥1GB free in log directory
- [ ] **Backup state**: Verify `.bak` file exists and is recent

---

## Decision Tables

### Table 1: Hedge Lifecycle

| Condition | Action | Reason |
|-----------|--------|--------|
| **OPEN HEDGE** | | |
| Adverse move ≥ `Hmult × ATR_M1` | Open hedge opposite PH | Protection trigger |
| + Cooldown ≥ 2s | | Anti-flapping |
| + Spread ≤ 1.5× median | | Execution quality |
| + NewsGuard allows hedges | | Market conditions |
| + Margin ≥ 2× required | | Capital safety |
| **UNWIND (close H, keep PH)** | | |
| Recovery: PH reversion ≥ 0.6× ATR | Unwind 100% | Target reached |
| OR Micro revival: SMS ≥ 1.1 + midline cross | Unwind 100% | Momentum restored |
| OR Macro alignment: Regime flips back (Conf ≥0.55) | Unwind 100% | Directional agreement |
| OR Time decay: t >15 min | Unwind 50% every 3 min | Risk reduction |
| **FORCED EXIT** | | |
| Hedge SL: H adverse ≥ 0.8× ATR | Close H immediately | Stop loss |
| OR PH closed | Close H immediately | No orphan hedges |
| OR Margin risk: Free <20% Used | Close H first | Preserve capital |
| OR Net reverse: Conf ≥0.65 + adverse persists | Flatten PH & H | Full conflict |

### Table 2: Regime Transition Protocol (Mid-Position)

| Case | New Regime vs PH | UPL (ATR) | Regime Age | Action |
|------|------------------|-----------|------------|--------|
| **1. Aligned** | Same direction | any | any | **Keep PH**. If Conf ↑ ≥0.15: trail 1.2× ATR (<2 UPL) or 2.0× ATR (≥2 UPL) |
| **2. Opposed (small loss)** | Opposite | <+0.5 | any | **Flatten now**. Tag: `RegimeConflictLoss` |
| **3. Opposed (moderate)** | Opposite | +0.5 to <+1.5 | any | **Scale-out 50%**. Time-stop: 3 min (SMS>1.0) or 5 min. Tag: `RegimeConflictScaleOut` |
| **4. Opposed (runner)** | Opposite | ≥+1.5 | <2 bars | **Trail 1.5× ATR**. Tag: `RegimeProtectedRunner` |
| | | | 2-3 bars | **Trail 1.3× ATR** |
| | | | ≥4 bars | **Trail 1.0× ATR** (tighten) |
| **5. Ambiguous** | Conf <0.5 | any | — | **Tighten trail 10%**. Suppress new entries until Conf ≥0.6 |
| | | | >6 bars | **Flatten + DiagnosticMode**. Tag: `RegimeAmbiguityExit` |

### Table 3: NewsGuard Phases

| Phase | Duration | Entries | Hedges | Unwinds | Hmult Modifier |
|-------|----------|---------|--------|---------|----------------|
| **Normal** | — | ✅ Allowed | ✅ Allowed | ✅ Allowed | 1.0× |
| **Block** | 0-2 min | ❌ Blocked | ❌ Blocked | ✅ Allowed | — |
| **Unwind-Only** | 3-5 min | ❌ Blocked | ❌ Blocked | ✅ Allowed | — |
| **Restricted** | 6-15 min | ❌ Blocked | ⚠️  Allowed (2× Hmult) | ✅ Allowed | 2.0× |
| **Normal** | >15 min | ✅ Allowed | ✅ Allowed | ✅ Allowed | 1.0× |

**Spike Detection:**
- SMS delta >2σ over 5 min **OR** Spread >3× median

### Table 4: Stress-Timer Exit Logic

| Condition | Status | Action |
|-----------|--------|--------|
| Underwater ≥2 bars **AND** SMS <0.4 **AND** RegimeConf <0.50 | **First trigger** | ⚠️  Log warning, set grace=1 |
| (Same conditions persist) | **Second trigger** | 🛑 **Exit position**. Tag: `StressExit` |
| Position becomes profitable | Reset | Clear grace counter, reset bars |

---

## During-Session Operations

### Normal Monitoring (Every 15 Minutes)

1. **Check QoS dashboard:**
   ```
   RejectRate: ≤2% (target)
   AvgSlippage: ≤0.25× ATR (target)
   P95Latency: <200ms (target)
   ```

2. **Review drawdown status:**
   ```
   Current DD: <2% (Normal damper=1.0)
   2-5% DD: Moderate damper=0.7
   5-10% DD: Severe damper=0.4
   ≥10% DD: Locked (or Survival=0.1 if enabled)
   ```

3. **Monitor hedge KPIs (if active):**
   ```
   HedgeWinRate: >40% (good)
   AvgDuration: <8 min (good)
   Frequency: <0.3 per trade (good)
   ```

4. **Check DLQ:**
   ```
   ErrorsLastHour: <10 (threshold)
   If breached → investigate + optionally halt entries
   ```

### Anomaly Response

#### Scenario A: High Reject Rate (>5%)

1. Check execution logs: `execution_qos.csv`
2. Identify reject reasons (margin? quote unavailable?)
3. If broker issue: pause bot, contact broker
4. If config issue: adjust `MaxVolume` or `MarginBuffer`

#### Scenario B: Excessive Hedging (>0.5/trade)

1. Review hedge logs: check `HedgePnL` vs `TotalPnL`
2. If `HedgePnL/GrossPnL >30%`: hedges too aggressive
3. **Tuning action**: Increase `Hmult` from 1.2 to 1.4
4. Re-test over 24h before permanent change

#### Scenario C: Regime Ambiguity >6 Bars

1. **Automatic action**: Bot flattens positions, sets `DiagnosticMode=true`
2. **Operator action**: Review M15 chart for chop/consolidation
3. If sideways market: consider pausing bot until breakout
4. Check `RegimeConfidence` history for pattern

#### Scenario D: Survival Mode Triggered

1. **Automatic**: Risk capped at 10% of normal (0.10× multiplier)
2. **Operator review**: Assess what caused ≥10% DD
   - Bad trades? → Review trade logs for pattern
   - Market shock? → Check NewsGuard spike history
   - Parameter drift? → Run validation metrics
3. **Recovery plan**: Monitor until DD <8%, then deactivate Survival Mode

---

## Post-Session Review

### Daily Checklist (End of Trading Day)

1. **Generate summary report:**
   ```
   - Total trades: X
   - Win rate: Y%
   - Net PnL: $Z
   - Max DD: W%
   - Hedges used: N (effectiveness: ±P%)
   ```

2. **Review validation metrics:**
   ```bash
   - Regime flip rate: <X/hour (stable market indicator)
   - SMS AUC by regime: >0.55 in ≥3/4 regimes
   - QoS assessment: PASS/FAIL
   ```

3. **Check state persistence:**
   ```
   - Confirm state_*.json updated
   - Backup copy created
   - ConfigHash matches deployed version
   ```

4. **Log rotation:**
   ```
   - Archive CSVs older than 30 days
   - Compress: trades_YYYYMMDD.csv.gz
   - Clear DLQ if errors resolved
   ```

### Weekly Review

1. **Hedge KPI assessment:**
   - If `WinRate <40%`: Ease unwind thresholds (e.g., recovery 0.6→0.5× ATR)
   - If `AvgDuration >8 min`: Lower recovery target
   - If `Frequency >0.3`: Increase Hmult (1.2→1.3)

2. **Parameter validation:**
   - Run single-param sweeps for `Hmult`, `VolBand`, `θD`
   - Check if current values near optimal
   - Document any drift

3. **Model validation:**
   - SMS conditional AUC: Should be >0.55 in most regimes
   - Regime purity: Check if DirScore/VolRatio align
   - Drawdown scaling: Verify graduated damper vs binary lock baseline

---

## Emergency Procedures

### Emergency 1: Flatten All Positions

**When:** Unexpected market event, system malfunction, margin call imminent

**Actions:**
1. **Manual override:**
   ```csharp
   // In cTrader: Emergency Stop button
   // Or command line: FlattenAll()
   ```
2. Close all PH positions (market orders)
3. Close all hedges immediately
4. Set `EntriesEnabled=false` in config
5. Capture state snapshot before restart

### Emergency 2: Runaway DrawDown (>15%)

**When:** DD exceeds emergency threshold despite locks

**Actions:**
1. **Immediate**: Halt bot via `Stop()` command
2. Close all positions manually
3. **Investigation:**
   - Review last 50 trades in `trades.csv`
   - Check for repeated pattern (e.g., all `RegimeConflictLoss`)
   - Examine newsguard spike log
4. **Root cause analysis:**
   - Parameter misconfiguration?
   - Market regime shift not captured?
   - Execution quality degraded?
5. **Remediation:** Fix issue, backtest, staged restart

### Emergency 3: DLQ Rate Limit Breach

**When:** >10 errors/hour in FeatureBus

**Actions:**
1. **Automatic**: Bot halts new entries (if `HaltEntriesOnDLQBreach=true`)
2. **Operator review:**
   ```
   - Check DLQ entries: GetRecentEntries(50)
   - Identify exception type (most common)
   - Fix decoder/processor bug
   ```
3. **Clear DLQ** after fix: `ClearDLQ()`
4. **Resume**: Re-enable entries, monitor for recurrence

---

## Troubleshooting Guide

### Issue: No Trades Executing

**Diagnosis checklist:**
- [ ] Warmup complete? (Need 1000 bars ~16h)
- [ ] Kalman converged? (Check `IsConverged()`)
- [ ] Daily loss lock active? (Check DD%)
- [ ] Spread too wide? (Compare to median)
- [ ] NewsGuard blocking? (Check phase)
- [ ] Regime ambiguous? (Conf <0.6)
- [ ] SMS below threshold? (Check SMS_min=0.6)

**Fix:** Most likely warmup or spread issue. Wait or adjust thresholds.

### Issue: Hedges Not Unwinding

**Diagnosis checklist:**
- [ ] Recovery target not met? (Need 0.6× ATR reversion)
- [ ] SMS still low? (Need ≥1.1 for micro revival)
- [ ] Regime still opposed? (Need Conf ≥0.55 + alignment)
- [ ] Time decay active? (Check if >15 min held)

**Fix:** Monitor hedge age. If >20 min, consider manual close + tune recovery target.

### Issue: High Slippage (>0.5× ATR)

**Diagnosis checklist:**
- [ ] Check `execution_qos.csv` for breakdown:
   - `SlipBase`: Normal (0.1× ATR)
   - `SlipLatency`: High? → Network issue
   - `SlipImpact`: High? → Order size too large vs depth
- [ ] Broker liquidity issue?
- [ ] Spread widening during entry?

**Fix:** Reduce `MaxVolume`, improve network, or switch broker.

### Issue: Regime Flipping Too Fast

**Diagnosis checklist:**
- [ ] Check flip rate: >X/hour? (expect ~2-4/hour normal)
- [ ] Hysteresis too low? (Default 0.10)
- [ ] VolBand too narrow? (Default 0.10)
- [ ] Market genuinely choppy?

**Fix:** Increase hysteresis (0.10→0.15) or widen VolBand (0.10→0.15).

---

## Appendix: Configuration Quick Reference

### Default Parameters (Prod)

```json
{
  "Bundles": {
    "Hmult": 1.2,
    "VolBand": 0.10,
    "SLmult": 2.0,
    "SLdelta": 0.5,
    "DirThreshold": 0.05,
    "DirHysteresis": 0.10,
    "SMSzClip": 3.0,
    "SMSConfirmThreshold": 0.30
  },
  "DrawdownScaling": {
    "ThresholdLevels": [2.0, 5.0, 10.0],
    "DamperValues": [1.0, 0.7, 0.4, 0.0]
  },
  "Hedge": {
    "TriggerMultiplier": 1.2,
    "RecoveryTargetMultiplier": 0.6,
    "HedgeStopMultiplier": 0.8,
    "TimeDecayMinutes": 15
  },
  "NewsGuard": {
    "BlockPhaseMinutes": 2,
    "UnwindOnlyPhaseMinutes": 3,
    "RestrictedPhaseMinutes": 10
  }
}
```

### Semantic Exit Tags

| Tag | Meaning | Expected Frequency |
|-----|---------|-------------------|
| `RegimeConflictLoss` | Regime opposed, small loss | Rare (5-10% of exits) |
| `RegimeConflictScaleOut` | Regime opposed, moderate profit | Occasional (10-15%) |
| `RegimeProtectedRunner` | Regime opposed, big winner | Rare (3-5%) |
| `RegimeAmbiguityExit` | Extended low confidence | Very rare (<2%) |
| `StressExit` | 3-condition stress timer | Rare (5-8%) |
| `HedgeAutoUnwind` | Hedge recovery/time decay | As frequent as hedges |

---

**End of Runbook** | For technical details, see `README.md` | For QA gates, see `QA_CHECKLIST.md`

