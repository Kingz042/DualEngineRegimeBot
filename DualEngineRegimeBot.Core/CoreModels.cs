using System;
using System.Collections.Generic;

namespace DualEngineRegimeBot.Core
{
    /// <summary>
    /// Macro regime direction state.
    /// </summary>
    public enum RegimeDirection
    {
        Bear = -1,
        Neutral = 0,
        Bull = 1
    }

    /// <summary>
    /// Macro volatility state.
    /// </summary>
    public enum RegimeVolState
    {
        LowVol,
        HighVol
    }

    /// <summary>
    /// Trade execution engine identifier.
    /// </summary>
    public enum ExecutionEngine
    {
        TrendFollower,
        SareMeanReversion,
        TailHedge
    }

    /// <summary>
    /// Trade side.
    /// </summary>
    public enum TradeSide
    {
        Long = 1,
        Short = -1
    }

    /// <summary>
    /// Exit reason taxonomy for telemetry.
    /// </summary>
    public enum ExitReason
    {
        None,
        StopLoss,
        TakeProfit,
        MeanTouch,
        OuTimeCap,
        TrailingStop,
        RegimeChange,
        DailyLossLock,
        DrawdownLock,
        ManualClose,
        HedgeAutoUnwind,
        PortfolioRebalance
    }

    /// <summary>
    /// Regime snapshot for state persistence and telemetry.
    /// </summary>
    public struct RegimeSnapshot
    {
        public RegimeDirection Direction;
        public RegimeVolState VolState;
        public double Confidence;
        public DateTime Timestamp;
    }

    /// <summary>
    /// Market context passed to all services per tick.
    /// </summary>
    public class MarketContext
    {
        public string Symbol { get; set; } = "";
        public DateTime Time { get; set; }
        public double Bid { get; set; }
        public double Ask { get; set; }
        public double Mid => (Bid + Ask) / 2.0;
        public double Spread => Ask - Bid;
        public double CurrentATR { get; set; }
        public double CurrentNATR { get; set; }
        public double TickSize { get; set; }
        public double TickValue { get; set; }
        public double PipSize { get; set; }
        public double AccountEquity { get; set; }
        public double AccountBalance { get; set; }
        public double FreeMargin { get; set; }
        public double UsedMargin { get; set; }
        public int BarCount { get; set; }
    }

    /// <summary>
    /// Order intent staged by engines before execution.
    /// </summary>
    public class OrderIntent
    {
        public ExecutionEngine Engine { get; set; }
        public TradeSide Side { get; set; }
        public double EntryPrice { get; set; }
        public double Units { get; set; }
        public double StopLoss { get; set; }
        public double? TakeProfit { get; set; }
        public double EffRiskPct { get; set; }
        public string Label { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Position state snapshot for engines.
    /// </summary>
    public class PositionSnapshot
    {
        public string Label { get; set; } = "";
        public ExecutionEngine Engine { get; set; }
        public TradeSide Side { get; set; }
        public double Units { get; set; }
        public double EntryPrice { get; set; }
        public double CurrentPrice { get; set; }
        public double UnrealizedPnL { get; set; }
        public DateTime EntryTime { get; set; }
        public int BarsOpen { get; set; }
    }

    /// <summary>
    /// Complete bot state for serialization.
    /// </summary>
    public class BotState
    {
        // Kalman filter state
        public double KalmanMu { get; set; }
        public double KalmanP { get; set; }
        
        // Kappa estimator state
        public double KappaSmoothed { get; set; }
        public List<double> KappaWindow { get; set; } = new List<double>();
        
        // OU timers (position label → bar count)
        public Dictionary<string, int> OuTimers { get; set; } = new Dictionary<string, int>();
        
        // Regime snapshot
        public RegimeSnapshot Regime { get; set; }
        
        // Cooldowns
        public DateTime LastHedgeTime { get; set; }
        public Dictionary<string, int> EngineCooldowns { get; set; } = new Dictionary<string, int>();
        
        // Risk tracking
        public double PeakEquity { get; set; }
        public double DailyStartEquity { get; set; }
        public DateTime LastResetDate { get; set; }
        
        // Spread history for median
        public List<double> SpreadHistory { get; set; } = new List<double>();
    }
}

