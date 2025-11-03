# DualEngineRegimeBot - QA Release Checklist

**Version:** 1.2  
**Schema Version:** 1.2  
**Last Updated:** 2025-11-01

---

## Pre-Release Gates

All items must be **PASS** before production deployment.

---

### 1. Unit Tests (All Must Pass)

- [ ] **Config versioning**: Hash computation deterministic
- [ ] **Parameter bundles**: EMA derivation correct (center ± span)
- [ ] **NewsGuard phases**: Spike detection + phase transitions
- [ ] **HedgeController**:
  - [ ] Open guards (margin, spread, cooldown, NewsGuard)
  - [ ] Unwind triggers (recovery, micro revival, macro, time decay)
  - [ ] Forced exits (hedge SL, PH closed, margin risk)
  - [ ] Cooldown enforcement (2s minimum)
- [ ] **RegimeSupervisor**:
  - [ ] 5-case decision table (all boundary conditions)
  - [ ] Alignment determination (aligned/opposed/ambiguous)
  - [ ] Adaptive trailing (UPL-based and regime-age-based)
- [ ] **StressTimer**:
  - [ ] 3-condition logic (underwater + SMS + regime)
  - [ ] Grace counter (warning → exit)
  - [ ] Reset on profitability
- [ ] **DrawdownController**:
  - [ ] Graduated damper (4 levels: 1.0, 0.7, 0.4, 0.0)
  - [ ] Hybrid peak reference (AllTimeHigh vs 0.95×Rolling30d)
  - [ ] Survival mode trigger + deactivation
- [ ] **FeatureBus + DLQ**:
  - [ ] Non-blocking publish
  - [ ] DLQ enqueue on exception
  - [ ] Rate limit detection (>10/hour)
- [ ] **StatePersistence**:
  - [ ] Atomic write (temp → move)
  - [ ] Backup fallback on corruption
  - [ ] Round-trip (save → load → verify)
- [ ] **ExecutionQoS**:
  - [ ] Slippage decomposition (base + latency + impact)
  - [ ] Percentile calculation (P50, P95, P99)
  - [ ] CSV logging format

---

### 2. Integration Tests

#### A/B Hedge Validation

**Test scenario:** 1000-trade backtest on XAUUSD M1 (3 months data)

| Metric | Hedge ON | Hedge OFF | Acceptance Criteria | Status |
|--------|----------|-----------|---------------------|--------|
| Max DD | X% | Y% | ↓ ≥10% (X ≤ 0.9Y) | ⬜ PASS / FAIL |
| Net PnL | $A | $B | Not ↓ >5% (A ≥ 0.95B) | ⬜ PASS / FAIL |
| Sharpe | S1 | S2 | Not ↓ >0.1 (S1 ≥ S2-0.1) | ⬜ PASS / FAIL |
| Tail P95 DD/trade | T1 | T2 | T1 < 0.7×T2 | ⬜ PASS / FAIL |

**Pass threshold:** ≥3/4 metrics meet criteria

- [ ] **Hedge ON results logged**: `hedge_on_report.csv`
- [ ] **Hedge OFF results logged**: `hedge_off_report.csv`
- [ ] **Comparison report generated**: `hedge_ab_comparison.txt`
- [ ] **Acceptance criteria met**: ⬜ YES / NO

#### SMS Conditional AUC

**Test scenario:** Calculate AUC for SMS predicting |move| >1×ATR by regime

| Regime | AUC | Sample Size | Acceptance | Status |
|--------|-----|-------------|------------|--------|
| Bull | X | N1 | >0.55 | ⬜ PASS / FAIL |
| Bear | Y | N2 | >0.55 | ⬜ PASS / FAIL |
| Neutral | Z | N3 | >0.55 | ⬜ PASS / FAIL |

**Pass threshold:** ≥3/4 regimes (including one special case) meet AUC >0.55

- [ ] **AUC calculations completed**
- [ ] **Results logged**: `sms_auc_validation.csv`
- [ ] **Acceptance criteria met**: ⬜ YES / NO

#### Execution Quality

**Test scenario:** 500 live/demo executions on XAUUSD M1

| Metric | Target | Actual | Status |
|--------|--------|--------|--------|
| Reject rate | ≤2% | __% | ⬜ PASS / FAIL |
| Avg slippage | ≤0.25× ATR | __× ATR | ⬜ PASS / FAIL |
| P95 latency | <200ms | __ms | ⬜ PASS / FAIL |

- [ ] **QoS report generated**: `execution_qos_validation.txt`
- [ ] **Acceptance criteria met**: ⬜ YES / NO

---

### 3. State Persistence Validation

- [ ] **Save/Load round-trip**:
  - [ ] Open 3 positions (2 PH + 1 hedge)
  - [ ] Save state
  - [ ] Restart bot
  - [ ] Load state
  - [ ] Verify all positions restored with correct params
- [ ] **Corruption recovery**:
  - [ ] Corrupt primary `state.json`
  - [ ] Restart bot
  - [ ] Verify loads from `.bak` backup
  - [ ] Log warning about corrupted primary
- [ ] **Crash recovery**:
  - [ ] Force-kill bot mid-bar
  - [ ] Restart
  - [ ] Verify Kalman μ, Kappa, Regime state restored
  - [ ] Verify no duplicate positions

---

### 4. Risk Controls Validation

#### Drawdown Scaling

- [ ] **Test DD=1.5%**: Damper should be 1.0 (normal)
- [ ] **Test DD=3.0%**: Damper should be 0.7 (moderate)
- [ ] **Test DD=7.0%**: Damper should be 0.4 (severe)
- [ ] **Test DD=12%**: Damper should be 0.0 or 0.1 (survival mode if enabled)
- [ ] **Test recovery**: DD drops to 4% → damper should scale back to 0.7

#### Stress-Timer

- [ ] **Scenario 1**: Position underwater 2 bars, SMS=0.3, Conf=0.4
  - [ ] **Expected**: Warning logged, grace=1
- [ ] **Scenario 2**: Conditions persist next bar
  - [ ] **Expected**: Position exited, tag `StressExit`
- [ ] **Scenario 3**: Position becomes profitable after first trigger
  - [ ] **Expected**: Grace counter reset, no exit

#### NewsGuard

- [ ] **Spike detection**: SMS delta >2σ → Phase=Block
- [ ] **Phase progression**: Block (2min) → UnwindOnly (3min) → Restricted (10min) → Normal
- [ ] **Restricted phase**: Hedges require 2× Hmult (1.2 → 2.4)
- [ ] **Entries blocked**: No new PH during Block/UnwindOnly phases

---

### 5. Validation Metrics APIs

- [ ] **Regime duration stats**: Export CSV with KM-style statistics
- [ ] **Regime flip rate**: Calculate per-hour over last 7 days
- [ ] **SMS AUC by regime**: Export per-regime AUC values
- [ ] **Hedge impact stats**: MaxDD, Ulcer, recovery time, PnL share
- [ ] **Parameter sweep**: Single-param ±20% for `Hmult`, `VolBand`, `θD`
- [ ] **Parameter grid**: 5×5 grid for (`VolBand`, `θD`) and (`SMS_min`, `SMS_window`)

---

### 6. Documentation Review

- [ ] **README.md**:
  - [ ] Config schema documented (with v1.2 fields)
  - [ ] Parameter bundles explained (EMA derivation, VolBand, etc.)
  - [ ] Survival mode documented
  - [ ] CSV logging fields listed (trades, bars, execution_qos)
- [ ] **RUNBOOK.md**:
  - [ ] Decision tables (hedge lifecycle, regime transitions, NewsGuard)
  - [ ] Pre/during/post-session SOPs
  - [ ] Emergency procedures (flatten all, runaway DD, DLQ breach)
- [ ] **QA_CHECKLIST.md**:
  - [ ] This document complete and reviewed
- [ ] **Code comments**:
  - [ ] All public APIs have XML doc comments
  - [ ] Complex logic (e.g., slippage model) explained inline

---

### 7. Configuration Validation

- [ ] **Schema version**: Set to `1.2`
- [ ] **ConfigHash**: Computed on load, logged in every trade/hedge record
- [ ] **BotName**: Set (e.g., `FracMeanDualEngine_V12`)
- [ ] **DeployedAt**: UTC timestamp present
- [ ] **Survival mode**: `Enabled=false` (unless explicitly needed)
- [ ] **Parameter bundles**:
  - [ ] `EmaCenter=10`, `EmaSpan=5` → derives `[5, 8, 10, 13, 20]`
  - [ ] `VolBand=0.10` → `VolHi=1.10`, `VolLo=0.90`
  - [ ] `SLmult=2.0`, `SLdelta=0.5` → LowVol=1.5, HighVol=2.5
  - [ ] `SMSzClip=3.0` (fixed)
  - [ ] `Hmult=1.2` (tunable)
- [ ] **Defaults verified**:
  - [ ] `Hedge.RecoveryTargetMultiplier=0.6`
  - [ ] `Hedge.HedgeStopMultiplier=0.8`
  - [ ] `NewsGuard.BlockPhaseMinutes=2`
  - [ ] `DrawdownScaling.ThresholdLevels=[2.0, 5.0, 10.0]`
  - [ ] `DrawdownScaling.DamperValues=[1.0, 0.7, 0.4, 0.0]`

---

### 8. Performance & Stability

- [ ] **Memory leak check**: Run 48h continuous → RAM usage stable
- [ ] **CPU usage**: OnTick <5ms avg, OnBar <50ms avg
- [ ] **File I/O**: CSV flush on bar close (not OnTick)
- [ ] **DLQ performance**: <1ms overhead per feature publish
- [ ] **State save**: <100ms per save (atomic write)

---

### 9. Logging & Telemetry

#### Trade Log (`trades.csv`)

- [ ] **Columns present**: Time, Symbol, Engine, Side, Qty, EntryPx, SLPx, StopDist, EffRiskPct, NATR, VolMult, VDI, Kappa, TFBias, RegimeDir, RegimeVol, RegimeConf, ExitReason, PnL, **ConfigHash**
- [ ] **Semantic tags used**: `RegimeConflictLoss`, `RegimeProtectedRunner`, `StressExit`, `HedgeAutoUnwind`, etc.
- [ ] **ConfigHash**: Present in every row

#### Bar Log (`bars.csv`)

- [ ] **Columns present**: Time, Symbol, NATR, VolMult, Theta, Mu, Kappa, TauHat, Spread, AtrRatio, NetUnits, HedgeUnits
- [ ] **Frequency**: One row per M1 bar

#### Execution QoS Log (`execution_qos.csv`)

- [ ] **Columns present**: Timestamp, OrderId, Side, SignalPrice, FillPrice, SlippagePips, SlipBase, SlipLatency, SlipImpact, LatencyMs, Spread, AtrM1, OrderSize, AvgDepth, WasRejected, WasPartial, RejectReason
- [ ] **Slippage decomposition**: Base + Latency + Impact computed correctly
- [ ] **Frequency**: One row per execution

---

### 10. Commit Verification

- [ ] **Commit 1**: `feat(config): add versioning, survival mode, bundles`
- [ ] **Commit 2**: `feat(news-guard): phased spike handling`
- [ ] **Commit 3**: `feat(hedge): defense-only lifecycle + KPIs`
- [ ] **Commit 4**: `feat(regime-flip): 5-case protocol + tags`
- [ ] **Commit 5**: `feat(stress): 3-condition + grace`
- [ ] **Commit 6**: `feat(dd-scaling): graduated damper + hybrid peak`
- [ ] **Commit 7**: `feat(bus): DLQ + state persistence`
- [ ] **Commit 8**: `feat(qos): slippage model + CSV`
- [ ] **Commit 9**: `test(valid): persistence, hedge, regime, stress, dd`
- [ ] **Commit 10**: `docs: runbook + qa checklist + readme`

---

## Final Approval

### Sign-Off Checklist

- [ ] **All unit tests passing** (100%)
- [ ] **Integration tests meet acceptance criteria** (≥3/4 metrics)
- [ ] **Validation metrics APIs functional**
- [ ] **Documentation complete and reviewed**
- [ ] **Configuration validated against schema v1.2**
- [ ] **Performance benchmarks met**
- [ ] **Logging format correct (with ConfigHash)**
- [ ] **State persistence crash recovery tested**
- [ ] **Risk controls validated (DD scaling, stress-timer, NewsGuard)**
- [ ] **Commits atomic and tagged**

### Approvers

| Role | Name | Signature | Date |
|------|------|-----------|------|
| **Developer** | | | |
| **QA Lead** | | | |
| **Risk Manager** | | | |
| **DevOps** | | | |

---

## Post-Release Monitoring (First 48h)

- [ ] **Hour 1**: Monitor for DLQ errors, execution rejects
- [ ] **Hour 6**: Review first 10+ trades for semantic tags accuracy
- [ ] **Day 1**: Check hedge KPIs (win rate, duration, frequency)
- [ ] **Day 2**: Validate drawdown scaling in action (if any DD events)
- [ ] **Day 7**: Generate weekly validation report (SMS AUC, regime stats, QoS)

---

**QA Status:** ⬜ PENDING / IN REVIEW / **APPROVED FOR PRODUCTION**

**Release Tag:** `v1.2.0`  
**Release Date:** _______________  
**Deployed By:** _______________

---

**End of QA Checklist** | For operational procedures, see `RUNBOOK.md`

