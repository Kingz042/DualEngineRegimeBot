using System;

namespace DualEngineRegimeBot.Core.Config
{
    /// <summary>
    /// Regime detection parameters for macro layer (M15).
    /// </summary>
    public class RegimeConfig
    {
        /// <summary>Fast EMA period for trend direction (e.g., 21).</summary>
        public int EmaFastPeriod { get; set; } = 21;
        
        /// <summary>Slow EMA period for trend direction (e.g., 55).</summary>
        public int EmaSlowPeriod { get; set; } = 55;
        
        /// <summary>Fast ATR period for volatility state (e.g., 10).</summary>
        public int AtrFastPeriod { get; set; } = 10;
        
        /// <summary>Slow ATR period for volatility state (e.g., 30).</summary>
        public int AtrSlowPeriod { get; set; } = 30;
        
        /// <summary>Hysteresis threshold for regime transitions (0.0-1.0).</summary>
        public double Hysteresis { get; set; } = 0.15;
        
        /// <summary>Volatility ratio threshold for HighVol state (e.g., 1.3).</summary>
        public double HighVolThreshold { get; set; } = 1.3;
        
        /// <summary>Weight for EMA separation in confidence calculation.</summary>
        public double ConfidenceEmaSepWeight { get; set; } = 0.5;
        
        /// <summary>Weight for volatility ratio in confidence calculation.</summary>
        public double ConfidenceVolWeight { get; set; } = 0.3;
        
        /// <summary>Weight for trend consistency in confidence calculation.</summary>
        public double ConfidenceTrendWeight { get; set; } = 0.2;
    }

    /// <summary>
    /// Adaptive p/q trend-follower parameters (M1 meso layer).
    /// </summary>
    public class TrendFollowerConfig
    {
        /// <summary>Min persistence parameter (low vol).</summary>
        public double PersistenceMin { get; set; } = 0.75;
        
        /// <summary>Max persistence parameter (high vol).</summary>
        public double PersistenceMax { get; set; } = 0.92;
        
        /// <summary>Min shock-decay parameter (low vol).</summary>
        public double ShockDecayMin { get; set; } = 0.15;
        
        /// <summary>Max shock-decay parameter (high vol).</summary>
        public double ShockDecayMax { get; set; } = 0.35;
        
        /// <summary>Minimum TrendEnergy to trigger entry [0-1].</summary>
        public double MinTrendEnergy { get; set; } = 0.45;
        
        /// <summary>Minimum |TF_Bias| to trigger entry [0-1].</summary>
        public double MinBiasThreshold { get; set; } = 0.55;
        
        /// <summary>Re-entry cooldown after exit (bars).</summary>
        public int ReEntryCooldownBars { get; set; } = 5;
        
        /// <summary>Lookback period for trend energy calculation.</summary>
        public int EnergyLookbackBars { get; set; } = 20;
        
        /// <summary>ATR multiplier for TF stop-loss.</summary>
        public double StopLossAtrMultiplier { get; set; } = 2.5;
    }

    /// <summary>
    /// SARE mean-reversion engine parameters (M1 micro layer).
    /// </summary>
    public class SareConfig
    {
        /// <summary>Kalman filter measurement noise (R).</summary>
        public double KalmanR { get; set; } = 0.01;
        
        /// <summary>Kalman filter process noise (Q).</summary>
        public double KalmanQ { get; set; } = 0.0001;
        
        /// <summary>Initial Kalman covariance (P₀).</summary>
        public double KalmanP0 { get; set; } = 1.0;
        
        /// <summary>Base VDI threshold for long entries in LowVol (e.g., -1.8).</summary>
        public double ThetaLongLowVol { get; set; } = -1.8;
        
        /// <summary>Base VDI threshold for long entries in HighVol (e.g., -2.4).</summary>
        public double ThetaLongHighVol { get; set; } = -2.4;
        
        /// <summary>Base VDI threshold for short entries in LowVol (e.g., +1.8).</summary>
        public double ThetaShortLowVol { get; set; } = 1.8;
        
        /// <summary>Base VDI threshold for short entries in HighVol (e.g., +2.4).</summary>
        public double ThetaShortHighVol { get; set; } = 2.4;
        
        /// <summary>Beta multiplier for sigma in HighVol state.</summary>
        public double BetaHighVol { get; set; } = 1.25;
        
        /// <summary>Beta multiplier for sigma in LowVol state.</summary>
        public double BetaLowVol { get; set; } = 1.0;
        
        /// <summary>Lookback window for kappa (mean reversion speed) estimation.</summary>
        public int KappaWindowBars { get; set; } = 50;
        
        /// <summary>EMA smoothing period for kappa estimate.</summary>
        public int KappaEmaSmoothing { get; set; } = 10;
        
        /// <summary>Minimum kappa clamp (avoid negative/zero).</summary>
        public double KappaMin { get; set; } = 0.01;
        
        /// <summary>Maximum kappa clamp (avoid unrealistic high).</summary>
        public double KappaMax { get; set; } = 2.0;
        
        /// <summary>Maximum OU time-to-mean cap (bars).</summary>
        public int TauHatMaxBars { get; set; } = 10;
        
        /// <summary>Fraction of position to close on mean touch [0-1].</summary>
        public double MeanTouchCloseFraction { get; set; } = 0.5;
        
        /// <summary>ATR multiplier for trailing stop in HighVol.</summary>
        public double TrailStopAtrMultiplier { get; set; } = 1.5;
        
        /// <summary>Enable trailing stop only in HighVol.</summary>
        public bool TrailOnlyInHighVol { get; set; } = true;
        
        /// <summary>Minimum MR_Conf to allow entry [0-1].</summary>
        public double MinMrConfidence { get; set; } = 0.25;
    }

    /// <summary>
    /// Inverse-volatility equity-% sizing parameters.
    /// </summary>
    public class SizingConfig
    {
        /// <summary>Base risk percentage of equity per trade (e.g., 0.50%).</summary>
        public double BaseRiskPct { get; set; } = 0.50;
        
        /// <summary>Target normalized ATR for this symbol (e.g., 0.30% for XAUUSD).</summary>
        public double TargetNATR { get; set; } = 0.30;
        
        /// <summary>Min clamp for VolMult.</summary>
        public double VolMultMin { get; set; } = 0.5;
        
        /// <summary>Max clamp for VolMult.</summary>
        public double VolMultMax { get; set; } = 2.0;
        
        /// <summary>Margin buffer multiplier (2.0 = require 2x margin).</summary>
        public double MarginBufferX { get; set; } = 2.0;
        
        /// <summary>Minimum volume in lots/units (broker-dependent).</summary>
        public double MinVolume { get; set; } = 0.01;
        
        /// <summary>Maximum volume in lots/units per trade.</summary>
        public double MaxVolume { get; set; } = 10.0;
        
        /// <summary>ATR period for sizing calculations.</summary>
        public int AtrPeriod { get; set; } = 14;
    }

    /// <summary>
    /// Intrabar tail-hedge service parameters.
    /// </summary>
    public class TailHedgeConfig
    {
        /// <summary>VDI absolute threshold to trigger hedge (e.g., 2.5).</summary>
        public double VdiTrigger { get; set; } = 2.5;
        
        /// <summary>Kappa upper bound to confirm reversion unlikely (e.g., 0.1).</summary>
        public double KappaTrigger { get; set; } = 0.1;
        
        /// <summary>ATR spike ratio threshold (e.g., 1.40).</summary>
        public double AtrSpikeRatio { get; set; } = 1.40;
        
        /// <summary>Fraction of net exposure to hedge (e.g., 0.75).</summary>
        public double HedgeFraction { get; set; } = 0.75;
        
        /// <summary>Exit hedge when |VDI| falls below this (e.g., 1.0).</summary>
        public double ExitVdiInside { get; set; } = 1.0;
        
        /// <summary>Exit hedge when ATR ratio cools below this (e.g., 1.15).</summary>
        public double ExitAtrRatio { get; set; } = 1.15;
        
        /// <summary>Max bars to hold hedge before auto-exit.</summary>
        public int ExitMaxBars { get; set; } = 8;
        
        /// <summary>Min profit in account currency to unwind hedge (optional).</summary>
        public double ExitMinCoverProfit { get; set; } = 0.0;
        
        /// <summary>Cooldown milliseconds between hedge probes.</summary>
        public int HedgeCooldownMs { get; set; } = 2000;
        
        /// <summary>Disable hedge if strong TF_Bias agrees with move.</summary>
        public double TfBiasDisableThreshold { get; set; } = 0.70;
    }

    /// <summary>
    /// Risk control parameters (locks, guards, limits).
    /// </summary>
    public class RiskConfig
    {
        /// <summary>Daily loss threshold % (of starting equity) to lock new entries.</summary>
        public double DailyLossLockPct { get; set; } = 2.0;
        
        /// <summary>Max drawdown % (from peak equity) to lock new entries.</summary>
        public double MaxDrawdownLockPct { get; set; } = 5.0;
        
        /// <summary>Spread guard multiplier vs. rolling median spread.</summary>
        public double SpreadGuardMultiplier { get; set; } = 1.5;
        
        /// <summary>Rolling window for median spread (bars).</summary>
        public int SpreadMedianWindowBars { get; set; } = 100;
        
        /// <summary>Warmup bars before allowing any entries.</summary>
        public int WarmupBars { get; set; } = 1000;
        
        /// <summary>Max concurrent positions (all engines, per symbol).</summary>
        public int MaxConcurrentPositions { get; set; } = 3;
        
        /// <summary>Max net exposure per symbol (units).</summary>
        public double MaxNetExposureUnits { get; set; } = 5.0;
        
        /// <summary>Portfolio budget allocation across symbols (1.0 = 100%).</summary>
        public double PortfolioBudgetFraction { get; set; } = 0.5;
        
        /// <summary>Trading session start hour (UTC, e.g., 0 = midnight).</summary>
        public int SessionStartHour { get; set; } = 0;
        
        /// <summary>Trading session end hour (UTC, e.g., 23 = 11 PM).</summary>
        public int SessionEndHour { get; set; } = 23;
        
        /// <summary>Enable session guard (block entries outside window).</summary>
        public bool EnableSessionGuard { get; set; } = false;
        
        /// <summary>Simulated slippage (pips/points, for backtests).</summary>
        public double SimulatedSlippagePips { get; set; } = 0.0;
        
        /// <summary>Max allowed slippage (pips) before rejecting live orders.</summary>
        public double MaxSlippagePips { get; set; } = 5.0;
    }

    /// <summary>
    /// Symbol-specific preset bundling all configs.
    /// </summary>
    public class SymbolPreset
    {
        public string SymbolName { get; set; } = "";
        public RegimeConfig Regime { get; set; } = new RegimeConfig();
        public TrendFollowerConfig TrendFollower { get; set; } = new TrendFollowerConfig();
        public SareConfig Sare { get; set; } = new SareConfig();
        public SizingConfig Sizing { get; set; } = new SizingConfig();
        public TailHedgeConfig TailHedge { get; set; } = new TailHedgeConfig();
        public RiskConfig Risk { get; set; } = new RiskConfig();
    }
}

