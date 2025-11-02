namespace DualEngineRegimeBot.Core.Config
{
    /// <summary>
    /// Preset factory for common symbols.
    /// </summary>
    public static class SymbolPresets
    {
        /// <summary>
        /// XAUUSD M1 preset with conservative mean-reversion tuning.
        /// </summary>
        public static SymbolPreset XAUUSD_M1()
        {
            return new SymbolPreset
            {
                SymbolName = "XAUUSD",
                Regime = new RegimeConfig
                {
                    EmaFastPeriod = 21,
                    EmaSlowPeriod = 55,
                    AtrFastPeriod = 10,
                    AtrSlowPeriod = 30,
                    Hysteresis = 0.15,
                    HighVolThreshold = 1.3
                },
                TrendFollower = new TrendFollowerConfig
                {
                    PersistenceMin = 0.75,
                    PersistenceMax = 0.92,
                    ShockDecayMin = 0.15,
                    ShockDecayMax = 0.35,
                    MinTrendEnergy = 0.45,
                    MinBiasThreshold = 0.55,
                    StopLossAtrMultiplier = 2.5
                },
                Sare = new SareConfig
                {
                    KalmanR = 0.01,
                    KalmanQ = 0.0001,
                    ThetaLongLowVol = -1.8,
                    ThetaLongHighVol = -2.4,
                    ThetaShortLowVol = 1.8,
                    ThetaShortHighVol = 2.4,
                    BetaHighVol = 1.25,
                    BetaLowVol = 1.0,
                    KappaWindowBars = 50,
                    TauHatMaxBars = 10,
                    MeanTouchCloseFraction = 0.5,
                    TrailStopAtrMultiplier = 1.5
                },
                Sizing = new SizingConfig
                {
                    BaseRiskPct = 0.50,
                    TargetNATR = 0.30,
                    VolMultMin = 0.5,
                    VolMultMax = 2.0,
                    MarginBufferX = 2.0,
                    MinVolume = 0.01,
                    MaxVolume = 5.0,
                    AtrPeriod = 14
                },
                TailHedge = new TailHedgeConfig
                {
                    VdiTrigger = 2.5,
                    KappaTrigger = 0.1,
                    AtrSpikeRatio = 1.40,
                    HedgeFraction = 0.75,
                    ExitVdiInside = 1.0,
                    ExitAtrRatio = 1.15,
                    ExitMaxBars = 8,
                    HedgeCooldownMs = 2000
                },
                Risk = new RiskConfig
                {
                    DailyLossLockPct = 2.0,
                    MaxDrawdownLockPct = 5.0,
                    SpreadGuardMultiplier = 1.5,
                    WarmupBars = 1000,
                    MaxConcurrentPositions = 3,
                    MaxNetExposureUnits = 5.0,
                    PortfolioBudgetFraction = 0.5
                }
            };
        }

        /// <summary>
        /// BTCUSD M1 preset with wider thresholds for crypto volatility.
        /// </summary>
        public static SymbolPreset BTCUSD_M1()
        {
            return new SymbolPreset
            {
                SymbolName = "BTCUSD",
                Regime = new RegimeConfig
                {
                    EmaFastPeriod = 21,
                    EmaSlowPeriod = 55,
                    AtrFastPeriod = 10,
                    AtrSlowPeriod = 30,
                    Hysteresis = 0.15,
                    HighVolThreshold = 1.4
                },
                TrendFollower = new TrendFollowerConfig
                {
                    PersistenceMin = 0.70,
                    PersistenceMax = 0.90,
                    ShockDecayMin = 0.20,
                    ShockDecayMax = 0.40,
                    MinTrendEnergy = 0.50,
                    MinBiasThreshold = 0.60,
                    StopLossAtrMultiplier = 3.0
                },
                Sare = new SareConfig
                {
                    KalmanR = 0.02,
                    KalmanQ = 0.0002,
                    ThetaLongLowVol = -2.0,
                    ThetaLongHighVol = -2.8,
                    ThetaShortLowVol = 2.0,
                    ThetaShortHighVol = 2.8,
                    BetaHighVol = 1.35,
                    BetaLowVol = 1.0,
                    KappaWindowBars = 50,
                    TauHatMaxBars = 10,
                    MeanTouchCloseFraction = 0.5,
                    TrailStopAtrMultiplier = 2.0
                },
                Sizing = new SizingConfig
                {
                    BaseRiskPct = 0.50,
                    TargetNATR = 0.80,
                    VolMultMin = 0.5,
                    VolMultMax = 2.0,
                    MarginBufferX = 2.0,
                    MinVolume = 0.01,
                    MaxVolume = 2.0,
                    AtrPeriod = 14
                },
                TailHedge = new TailHedgeConfig
                {
                    VdiTrigger = 3.0,
                    KappaTrigger = 0.12,
                    AtrSpikeRatio = 1.60,
                    HedgeFraction = 0.70,
                    ExitVdiInside = 1.2,
                    ExitAtrRatio = 1.20,
                    ExitMaxBars = 8,
                    HedgeCooldownMs = 2000
                },
                Risk = new RiskConfig
                {
                    DailyLossLockPct = 2.5,
                    MaxDrawdownLockPct = 6.0,
                    SpreadGuardMultiplier = 1.8,
                    WarmupBars = 1000,
                    MaxConcurrentPositions = 2,
                    MaxNetExposureUnits = 3.0,
                    PortfolioBudgetFraction = 0.5
                }
            };
        }
    }
}

