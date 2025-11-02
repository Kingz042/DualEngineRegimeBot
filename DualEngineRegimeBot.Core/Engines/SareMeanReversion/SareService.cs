using System;
using DualEngineRegimeBot.Core.Config;
using DualEngineRegimeBot.Core.Indicators;

namespace DualEngineRegimeBot.Core.Engines.SareMeanReversion
{
    /// <summary>
    /// SARE (Statistical Adaptive Reversion Engine) mean-reversion service.
    /// Uses Kalman mean, VDI thresholds, OU time-cap, and regime-aware exits.
    /// </summary>
    public class SareService : ISareService
    {
        private readonly SareConfig _config;
        private readonly KalmanMean _kalman;
        private readonly KappaEstimator _kappaEstimator;
        
        private double _currentSigma = 0.0;
        private double _currentVDI = 0.0;
        private double _currentBeta = 1.0;
        private RegimeVolState _lastVolState = RegimeVolState.LowVol;
        
        public SareService(SareConfig config, double initialPrice)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            
            _kalman = new KalmanMean(
                config.KalmanR,
                config.KalmanQ,
                initialPrice,
                config.KalmanP0);
            
            _kappaEstimator = new KappaEstimator(
                config.KappaWindowBars,
                config.KappaEmaSmoothing,
                config.KappaMin,
                config.KappaMax);
        }
        
        /// <summary>
        /// Updates Kalman filter, VDI, kappa, sigma per tick.
        /// </summary>
        public void Update(MarketContext context, RegimeSnapshot regime)
        {
            double price = context.Mid;
            
            if (double.IsNaN(price) || price <= 0)
                return;
            
            // Update Kalman mean
            _kalman.Update(price);
            double mu = _kalman.GetMean();
            
            // Update kappa estimator
            _kappaEstimator.Update(price, mu);
            
            // Update beta based on regime vol state
            _lastVolState = regime.VolState;
            _currentBeta = regime.VolState == RegimeVolState.HighVol 
                ? _config.BetaHighVol 
                : _config.BetaLowVol;
            
            // Compute sigma from ATR/NATR
            double atr = context.CurrentATR;
            _currentSigma = Math.Max(atr * _currentBeta, 1e-8);
            
            // Compute VDI = (Price - μ) / (β·σ)
            _currentVDI = (price - mu) / _currentSigma;
        }
        
        public double GetMean() => _kalman.GetMean();
        public double GetSigma() => _currentSigma;
        public double GetVDI() => _currentVDI;
        public double GetKappa() => _kappaEstimator.GetKappa();
        
        /// <summary>
        /// Returns OU time-to-mean estimate (τ̂) in bars.
        /// Formula: τ̂ ≈ |Δx|² / (3σ²κ), clamped to max.
        /// </summary>
        public int GetTauHat()
        {
            double kappa = _kappaEstimator.GetKappa();
            double dx = Math.Abs(_currentVDI * _currentSigma);
            double sigma2 = _currentSigma * _currentSigma;
            
            if (kappa <= 0 || sigma2 <= 0)
                return _config.TauHatMaxBars;
            
            double tauHat = (dx * dx) / (3.0 * sigma2 * kappa);
            return Math.Min((int)Math.Ceiling(tauHat), _config.TauHatMaxBars);
        }
        
        /// <summary>
        /// Computes MR confidence: tanh(κ) × RegimeConf × (1 - |TF_Bias|).
        /// </summary>
        public double GetMrConfidence(RegimeSnapshot regime, double tfBias)
        {
            double kappa = _kappaEstimator.GetKappa();
            double kappaConf = Math.Tanh(kappa); // Normalize to [0..1]
            double tfDamp = 1.0 - Math.Abs(tfBias); // Reduce when TF strong
            
            return kappaConf * regime.Confidence * tfDamp;
        }
        
        public bool IsConverged() => _kalman.IsConverged(0.1);
        
        /// <summary>
        /// Checks if SARE entry conditions met; returns intent if yes.
        /// </summary>
        public OrderIntent? CheckEntry(
            MarketContext context, 
            RegimeSnapshot regime, 
            double tfBias, 
            double effRiskPct)
        {
            // Must be converged
            if (!_kalman.IsConverged(0.2))
                return null;
            
            // Compute MR confidence
            double mrConf = GetMrConfidence(regime, tfBias);
            if (mrConf < _config.MinMrConfidence)
                return null;
            
            // Get threshold based on vol state
            double thetaLong = regime.VolState == RegimeVolState.LowVol 
                ? _config.ThetaLongLowVol 
                : _config.ThetaLongHighVol;
            
            double thetaShort = regime.VolState == RegimeVolState.LowVol 
                ? _config.ThetaShortLowVol 
                : _config.ThetaShortHighVol;
            
            // Check VDI breach
            TradeSide? side = null;
            if (_currentVDI <= thetaLong)
                side = TradeSide.Long;
            else if (_currentVDI >= thetaShort)
                side = TradeSide.Short;
            
            if (!side.HasValue)
                return null;
            
            // If strong TF_Bias opposes fade, stand down or reduce
            bool tfOpposes = (side == TradeSide.Long && tfBias < -0.5) ||
                            (side == TradeSide.Short && tfBias > 0.5);
            
            if (tfOpposes)
                return null; // Stand down entirely
            
            // Compute stop-loss (wider than VDI entry)
            double stopMultiplier = 1.5; // SL at 1.5× sigma beyond entry
            double mu = _kalman.GetMean();
            double entryPrice = side == TradeSide.Long ? context.Ask : context.Bid;
            double stopLoss = side == TradeSide.Long
                ? mu - stopMultiplier * _currentSigma
                : mu + stopMultiplier * _currentSigma;
            
            // Ensure SL is valid distance from entry
            double minSlDist = context.CurrentATR * 0.5;
            if (Math.Abs(stopLoss - entryPrice) < minSlDist)
            {
                stopLoss = side == TradeSide.Long
                    ? entryPrice - minSlDist
                    : entryPrice + minSlDist;
            }
            
            return new OrderIntent
            {
                Engine = ExecutionEngine.SareMeanReversion,
                Side = side.Value,
                EntryPrice = entryPrice,
                Units = 0, // Filled by sizer
                StopLoss = stopLoss,
                TakeProfit = null, // Use mean-touch exit
                EffRiskPct = effRiskPct,
                Label = $"SARE_{context.Time:yyyyMMddHHmmss}",
                Timestamp = context.Time
            };
        }
        
        /// <summary>
        /// Checks if SARE exit conditions met (mean touch, OU cap, trail).
        /// Returns (shouldExit, closeFraction, reason).
        /// Priority: SL (broker) → OU time-cap → Mean-touch → ATR trail.
        /// </summary>
        public (bool shouldExit, double closeFraction, ExitReason reason) CheckExit(
            PositionSnapshot position,
            MarketContext context,
            RegimeSnapshot regime)
        {
            if (position.Engine != ExecutionEngine.SareMeanReversion)
                return (false, 0, ExitReason.None);
            
            double mu = _kalman.GetMean();
            double currentPrice = position.Side == TradeSide.Long ? context.Bid : context.Ask;
            
            // 1) OU time-cap (hard exit)
            int tauHat = GetTauHat();
            if (position.BarsOpen >= tauHat)
                return (true, 1.0, ExitReason.OuTimeCap);
            
            // 2) Mean-touch partial/flat
            bool crossedMean = (position.Side == TradeSide.Long && currentPrice >= mu) ||
                              (position.Side == TradeSide.Short && currentPrice <= mu);
            
            if (crossedMean)
            {
                double closeFraction = _config.MeanTouchCloseFraction;
                ExitReason reason = closeFraction >= 0.99 
                    ? ExitReason.MeanTouch 
                    : ExitReason.MeanTouch;
                
                return (true, closeFraction, reason);
            }
            
            // 3) ATR trail in HighVol (optional, light)
            if (_config.TrailOnlyInHighVol && regime.VolState == RegimeVolState.HighVol)
            {
                double trailDist = context.CurrentATR * _config.TrailStopAtrMultiplier;
                double trailStop = position.Side == TradeSide.Long
                    ? currentPrice - trailDist
                    : currentPrice + trailDist;
                
                bool trailHit = (position.Side == TradeSide.Long && currentPrice < trailStop) ||
                               (position.Side == TradeSide.Short && currentPrice > trailStop);
                
                if (trailHit && position.UnrealizedPnL > 0)
                    return (true, 1.0, ExitReason.TrailingStop);
            }
            
            return (false, 0, ExitReason.None);
        }
    }
}

