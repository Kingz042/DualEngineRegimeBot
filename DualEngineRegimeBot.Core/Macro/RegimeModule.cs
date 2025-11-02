using System;
using DualEngineRegimeBot.Core.Config;

namespace DualEngineRegimeBot.Core.Macro
{
    /// <summary>
    /// Macro regime detection service (M15 layer).
    /// Determines trend direction, volatility state, and confidence via EMA/ATR analysis.
    /// </summary>
    public class RegimeModule : IRegimeService
    {
        private readonly RegimeConfig _config;
        
        private RegimeDirection _currentDirection = RegimeDirection.Neutral;
        private RegimeVolState _currentVolState = RegimeVolState.LowVol;
        private double _currentConfidence = 0.5;
        private DateTime _lastUpdateTime;
        
        // Hysteresis tracking
        private double _lastEmaSpread = 0.0;
        private double _lastVolRatio = 1.0;
        
        public RegimeModule(RegimeConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }
        
        /// <summary>
        /// Updates regime state on M15 boundary or scheduled cadence.
        /// </summary>
        /// <param name="context">Market context.</param>
        /// <param name="emaFast">Fast EMA value (e.g., 21-period M15).</param>
        /// <param name="emaSlow">Slow EMA value (e.g., 55-period M15).</param>
        /// <param name="atrFast">Fast ATR value (e.g., 10-period M15).</param>
        /// <param name="atrSlow">Slow ATR value (e.g., 30-period M15).</param>
        public void Update(MarketContext context, double emaFast, double emaSlow, double atrFast, double atrSlow)
        {
            if (double.IsNaN(emaFast) || double.IsNaN(emaSlow) || 
                double.IsNaN(atrFast) || double.IsNaN(atrSlow))
                return;
            
            // Floor ATR values to avoid division by zero
            atrSlow = Math.Max(atrSlow, 1e-8);
            atrFast = Math.Max(atrFast, 1e-8);
            
            _lastUpdateTime = context.Time;
            
            // 1) Determine Direction with hysteresis
            double emaSpread = emaFast - emaSlow;
            double emaSepPct = Math.Abs(emaSpread) / emaSlow * 100.0;
            
            RegimeDirection newDirection = DetermineDirection(emaSpread, emaSepPct);
            
            // 2) Determine Volatility State
            double volRatio = atrFast / atrSlow;
            _lastVolRatio = volRatio;
            
            RegimeVolState newVolState = volRatio >= _config.HighVolThreshold 
                ? RegimeVolState.HighVol 
                : RegimeVolState.LowVol;
            
            // 3) Compute Confidence [0..1]
            double confidence = ComputeConfidence(emaSepPct, volRatio, emaSpread);
            
            // Apply state changes
            _currentDirection = newDirection;
            _currentVolState = newVolState;
            _currentConfidence = Math.Clamp(confidence, 0.0, 1.0);
            _lastEmaSpread = emaSpread;
        }
        
        public RegimeDirection GetDirection() => _currentDirection;
        public RegimeVolState GetVolState() => _currentVolState;
        public double GetConfidence() => _currentConfidence;
        
        public RegimeSnapshot GetSnapshot()
        {
            return new RegimeSnapshot
            {
                Direction = _currentDirection,
                VolState = _currentVolState,
                Confidence = _currentConfidence,
                Timestamp = _lastUpdateTime
            };
        }
        
        /// <summary>
        /// Determines regime direction with hysteresis to avoid flip-flops.
        /// </summary>
        private RegimeDirection DetermineDirection(double emaSpread, double emaSepPct)
        {
            // Hysteresis: require crossing threshold + hysteresis to flip
            double hysteresisThreshold = _config.Hysteresis;
            
            // If already in a directional state, require crossing zero + hysteresis
            if (_currentDirection == RegimeDirection.Bull)
            {
                if (emaSpread < -hysteresisThreshold * Math.Abs(_lastEmaSpread))
                    return RegimeDirection.Bear;
                else if (Math.Abs(emaSpread) < hysteresisThreshold * Math.Abs(_lastEmaSpread))
                    return RegimeDirection.Neutral;
                else
                    return RegimeDirection.Bull;
            }
            else if (_currentDirection == RegimeDirection.Bear)
            {
                if (emaSpread > hysteresisThreshold * Math.Abs(_lastEmaSpread))
                    return RegimeDirection.Bull;
                else if (Math.Abs(emaSpread) < hysteresisThreshold * Math.Abs(_lastEmaSpread))
                    return RegimeDirection.Neutral;
                else
                    return RegimeDirection.Bear;
            }
            else // Neutral
            {
                // Require minimum separation to enter directional regime
                const double minSepThreshold = 0.1; // 0.1% minimum
                if (emaSpread > 0 && emaSepPct > minSepThreshold)
                    return RegimeDirection.Bull;
                else if (emaSpread < 0 && emaSepPct > minSepThreshold)
                    return RegimeDirection.Bear;
                else
                    return RegimeDirection.Neutral;
            }
        }
        
        /// <summary>
        /// Computes regime confidence as weighted blend of trend strength, vol ratio, consistency.
        /// </summary>
        private double ComputeConfidence(double emaSepPct, double volRatio, double emaSpread)
        {
            // Component 1: EMA separation strength (normalized sigmoid)
            double emaSepScore = Math.Tanh(emaSepPct / 0.5); // Normalize around 0.5%
            
            // Component 2: Volatility ratio stability (prefer moderate values)
            // Too high = unstable; ideal around 1.0-1.3
            double volScore = 1.0 - Math.Abs(volRatio - 1.15) / 2.0;
            volScore = Math.Clamp(volScore, 0.0, 1.0);
            
            // Component 3: Trend consistency (does current spread agree with recent?)
            double trendConsistency = 1.0;
            if (Math.Abs(_lastEmaSpread) > 1e-6)
            {
                double spreadRatio = emaSpread / _lastEmaSpread;
                trendConsistency = spreadRatio > 0 ? 1.0 : 0.5; // Penalize reversals
            }
            
            // Weighted blend
            double confidence = 
                _config.ConfidenceEmaSepWeight * emaSepScore +
                _config.ConfidenceVolWeight * volScore +
                _config.ConfidenceTrendWeight * trendConsistency;
            
            return confidence;
        }
    }
}

