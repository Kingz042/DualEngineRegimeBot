using System;
using System.Collections.Generic;
using DualEngineRegimeBot.Core.Config;

namespace DualEngineRegimeBot.Core
{
    /// <summary>
    /// Regime detection service interface (macro M15 layer).
    /// </summary>
    public interface IRegimeService
    {
        /// <summary>Updates regime state on M15 boundary or scheduled cadence.</summary>
        void Update(MarketContext context, double emaFast, double emaSlow, double atrFast, double atrSlow);
        
        RegimeDirection GetDirection();
        RegimeVolState GetVolState();
        double GetConfidence();
        RegimeSnapshot GetSnapshot();
    }

    /// <summary>
    /// Adaptive p/q trend-follower service (meso M1 layer).
    /// </summary>
    public interface ITrendFollowerService
    {
        /// <summary>Updates internal state per tick/bar.</summary>
        void Update(MarketContext context, RegimeSnapshot regime);
        
        /// <summary>Returns directional bias in [-1, +1].</summary>
        double GetBias();
        
        /// <summary>Returns trend energy in [0, 1].</summary>
        double GetEnergy();
        
        /// <summary>Checks if TF entry conditions met; returns intent if yes.</summary>
        OrderIntent? CheckEntry(MarketContext context, RegimeSnapshot regime, double effRiskPct);
        
        /// <summary>Checks if TF exit conditions met for given position.</summary>
        bool CheckExit(PositionSnapshot position, MarketContext context);
        
        /// <summary>Resets cooldown timers (e.g., after exit).</summary>
        void ResetCooldown();
    }

    /// <summary>
    /// SARE mean-reversion service (micro M1 layer).
    /// </summary>
    public interface ISareService
    {
        /// <summary>Updates Kalman filter, VDI, kappa, sigma per tick.</summary>
        void Update(MarketContext context, RegimeSnapshot regime);
        
        /// <summary>Returns current Kalman mean (μ).</summary>
        double GetMean();
        
        /// <summary>Returns current effective sigma (σ × β).</summary>
        double GetSigma();
        
        /// <summary>Returns current VDI (Volatility-Deviation Index).</summary>
        double GetVDI();
        
        /// <summary>Returns current mean-reversion speed (κ).</summary>
        double GetKappa();
        
        /// <summary>Returns OU time-to-mean estimate (τ̂) in bars.</summary>
        int GetTauHat();
        
        /// <summary>Computes MR confidence: tanh(κ) × RegimeConf × (1 - |TF_Bias|).</summary>
        double GetMrConfidence(RegimeSnapshot regime, double tfBias);
        
        /// <summary>Checks if SARE entry conditions met; returns intent if yes.</summary>
        OrderIntent? CheckEntry(MarketContext context, RegimeSnapshot regime, double tfBias, double effRiskPct);
        
        /// <summary>Checks if SARE exit conditions met (mean touch, OU cap, trail).</summary>
        (bool shouldExit, double closeFraction, ExitReason reason) CheckExit(
            PositionSnapshot position, MarketContext context, RegimeSnapshot regime);
        
        /// <summary>Returns true if Kalman filter has converged (P below threshold).</summary>
        bool IsConverged();
    }

    /// <summary>
    /// Inverse-volatility equity-% sizing service.
    /// </summary>
    public interface ISizerService
    {
        /// <summary>
        /// Computes effective risk % incorporating vol mult, regime conf, strategy conf, TF damp.
        /// </summary>
        double ComputeEffRiskPct(
            double baseRiskPct,
            double volMult,
            double regimeConf,
            double strategyConf,
            double tfDamp);
        
        /// <summary>
        /// Converts risk % and stop distance to units, enforcing margin buffer and clamps.
        /// </summary>
        double ComputeUnits(
            double effRiskPct,
            double stopDistancePips,
            MarketContext context,
            SizingConfig config);
        
        /// <summary>
        /// Computes VolMult = clamp(TargetNATR / CurrentNATR, min, max).
        /// </summary>
        double ComputeVolMult(double currentNATR, SizingConfig config);
        
        /// <summary>
        /// Validates that computed units respect margin and broker limits.
        /// </summary>
        bool ValidateUnits(double units, MarketContext context, SizingConfig config);
    }

    /// <summary>
    /// Intrabar tail-hedge service.
    /// </summary>
    public interface ITailHedgeService
    {
        /// <summary>
        /// Probes whether hedge trigger conditions are met (VDI shock, ATR spike, low κ).
        /// </summary>
        bool ShouldTriggerHedge(
            MarketContext context,
            double vdi,
            double kappa,
            double atrRatio,
            double tfBias,
            double netExposure);
        
        /// <summary>
        /// Computes hedge size as fraction of net exposure.
        /// </summary>
        double ComputeHedgeSize(double netExposure, TailHedgeConfig config);
        
        /// <summary>
        /// Checks if hedge should auto-unwind (cooled VDI, ATR, max bars, profit).
        /// </summary>
        bool ShouldExitHedge(
            PositionSnapshot hedge,
            MarketContext context,
            double vdi,
            double atrRatio);
        
        /// <summary>
        /// Enforces cooldown between hedge probes.
        /// </summary>
        bool IsCooldownActive(DateTime now);
        
        /// <summary>
        /// Resets cooldown timer.
        /// </summary>
        void ResetCooldown(DateTime now);
    }

    /// <summary>
    /// Risk control service (locks, guards, limits).
    /// </summary>
    public interface IRiskService
    {
        /// <summary>Updates daily P&L, peak equity, drawdown tracking.</summary>
        void Update(MarketContext context, double realizedPnL, double unrealizedPnL);
        
        /// <summary>Returns true if daily loss lock is active (new entries disabled).</summary>
        bool IsDailyLossLocked();
        
        /// <summary>Returns true if max drawdown lock is active.</summary>
        bool IsDrawdownLocked();
        
        /// <summary>Returns true if spread exceeds guard threshold.</summary>
        bool IsSpreadTooWide(double spread, double medianSpread);
        
        /// <summary>Returns true if within allowed trading session.</summary>
        bool IsInTradingSession(DateTime time);
        
        /// <summary>Returns true if warmup period complete.</summary>
        bool IsWarmupComplete(int barCount);
        
        /// <summary>Returns true if adding new position would exceed max concurrent.</summary>
        bool WouldExceedMaxPositions(int currentCount);
        
        /// <summary>Returns true if adding units would exceed symbol exposure cap.</summary>
        bool WouldExceedExposureCap(double currentNet, double addUnits, double cap);
        
        /// <summary>Resets daily P&L tracking (call at midnight).</summary>
        void ResetDailyTracking();
    }

    /// <summary>
    /// Telemetry service (logging, metrics).
    /// </summary>
    public interface ITelemetry
    {
        /// <summary>Logs a trade event with full context.</summary>
        void LogTrade(
            DateTime time,
            string symbol,
            ExecutionEngine engine,
            TradeSide side,
            double qty,
            double entryPx,
            double slPx,
            double stopDist,
            double effRiskPct,
            double natr,
            double volMult,
            double vdi,
            double kappa,
            double tfBias,
            RegimeSnapshot regime,
            ExitReason exitReason,
            double pnl);
        
        /// <summary>Logs bar-level metrics.</summary>
        void LogBar(
            DateTime time,
            string symbol,
            double natr,
            double volMult,
            double theta,
            double mu,
            double kappa,
            int tauHat,
            double spread,
            double atrRatio,
            double netUnits,
            double hedgeUnits);
        
        /// <summary>Flushes buffered logs to disk (call on bar close).</summary>
        void Flush();
        
        /// <summary>Updates rolling median spread tracker.</summary>
        void UpdateSpread(double spread);
        
        /// <summary>Returns rolling median spread.</summary>
        double GetMedianSpread();
    }

    /// <summary>
    /// State persistence service.
    /// </summary>
    public interface IStateStore
    {
        /// <summary>Serializes current state to disk.</summary>
        void Save(BotState state);
        
        /// <summary>Restores state from disk; returns null if not found.</summary>
        BotState? Load();
    }

    /// <summary>
    /// Time abstraction for determinism and testing.
    /// </summary>
    public interface IClock
    {
        DateTime Now { get; }
        DateTime UtcNow { get; }
    }
}

