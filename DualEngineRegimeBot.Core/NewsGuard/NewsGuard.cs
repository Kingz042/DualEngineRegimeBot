using System;
using System.Collections.Generic;
using System.Linq;
using DualEngineRegimeBot.Core.Config;

namespace DualEngineRegimeBot.Core.NewsGuard
{
    /// <summary>
    /// Phased news/volatility spike guard to protect during abnormal market conditions.
    /// Implements 4-phase approach: Block → Unwind-only → Restricted → Normal.
    /// </summary>
    public class NewsGuard
    {
        private readonly NewsGuardConfig _config;
        private readonly Queue<double> _smsHistory = new Queue<double>();
        private readonly Queue<double> _spreadHistory = new Queue<double>();
        
        private NewsGuardPhase _currentPhase = NewsGuardPhase.Normal;
        private DateTime _spikeDetectedAt = DateTime.MinValue;
        private double _spikeStrength = 0.0;
        
        public NewsGuard(NewsGuardConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }
        
        /// <summary>
        /// Updates spike detection with new market data.
        /// </summary>
        /// <param name="currentTime">Current timestamp.</param>
        /// <param name="sms">Current SMS value.</param>
        /// <param name="spread">Current spread in pips.</param>
        /// <param name="medianSpread">Rolling median spread.</param>
        public void Update(DateTime currentTime, double sms, double spread, double medianSpread)
        {
            if (!_config.Enabled)
            {
                _currentPhase = NewsGuardPhase.Normal;
                return;
            }
            
            // Track SMS history for delta calculation (5 minutes)
            _smsHistory.Enqueue(sms);
            if (_smsHistory.Count > 300) // 5 min at 1-tick per second
                _smsHistory.Dequeue();
            
            _spreadHistory.Enqueue(spread);
            if (_spreadHistory.Count > 300)
                _spreadHistory.Dequeue();
            
            // Check for spike
            bool spikeDetected = DetectSpike(sms, spread, medianSpread);
            
            if (spikeDetected && _currentPhase == NewsGuardPhase.Normal)
            {
                _spikeDetectedAt = currentTime;
                _spikeStrength = CalculateSpikeStrength(sms, spread, medianSpread);
                _currentPhase = NewsGuardPhase.Block;
            }
            
            // Update phase based on time since spike
            if (_currentPhase != NewsGuardPhase.Normal)
            {
                UpdatePhase(currentTime);
            }
        }
        
        /// <summary>
        /// Returns current news guard phase.
        /// </summary>
        public NewsGuardPhase GetPhase() => _currentPhase;
        
        /// <summary>
        /// Returns time in current phase (minutes).
        /// </summary>
        public double GetMinutesInPhase(DateTime currentTime)
        {
            if (_spikeDetectedAt == DateTime.MinValue)
                return 0;
            
            return (currentTime - _spikeDetectedAt).TotalMinutes;
        }
        
        /// <summary>
        /// Returns spike strength (0-1 normalized).
        /// </summary>
        public double GetSpikeStrength() => _spikeStrength;
        
        /// <summary>
        /// Checks if entries are allowed in current phase.
        /// </summary>
        public bool AllowEntries()
        {
            return _currentPhase == NewsGuardPhase.Normal;
        }
        
        /// <summary>
        /// Checks if hedges are allowed in current phase.
        /// </summary>
        public bool AllowHedges()
        {
            return _currentPhase == NewsGuardPhase.Normal || 
                   _currentPhase == NewsGuardPhase.Restricted;
        }
        
        /// <summary>
        /// Checks if unwinds are allowed in current phase.
        /// </summary>
        public bool AllowUnwinds()
        {
            return true; // Always allow unwinds
        }
        
        /// <summary>
        /// Gets Hmult multiplier for current phase.
        /// </summary>
        public double GetHmultMultiplier()
        {
            return _currentPhase == NewsGuardPhase.Restricted 
                ? _config.RestrictedPhaseHmultMultiplier 
                : 1.0;
        }
        
        /// <summary>
        /// Forces reset to normal phase (for testing/manual intervention).
        /// </summary>
        public void ForceResetToNormal()
        {
            _currentPhase = NewsGuardPhase.Normal;
            _spikeDetectedAt = DateTime.MinValue;
            _spikeStrength = 0.0;
        }
        
        private bool DetectSpike(double currentSms, double currentSpread, double medianSpread)
        {
            // Condition 1: Large SMS delta (2σ over 5 min)
            if (_smsHistory.Count >= 60) // At least 1 minute of data
            {
                double smsMean = _smsHistory.Average();
                double smsStdDev = CalculateStdDev(_smsHistory, smsMean);
                double smsDelta = Math.Abs(currentSms - smsMean);
                
                if (smsDelta > _config.SMSDeltaThreshold * smsStdDev)
                    return true;
            }
            
            // Condition 2: Spread blowout
            if (medianSpread > 0 && currentSpread > _config.SpreadBlowoutMultiplier * medianSpread)
                return true;
            
            return false;
        }
        
        private double CalculateSpikeStrength(double sms, double spread, double medianSpread)
        {
            double smsComponent = 0.0;
            if (_smsHistory.Count >= 60)
            {
                double smsMean = _smsHistory.Average();
                double smsStdDev = CalculateStdDev(_smsHistory, smsMean);
                if (smsStdDev > 0)
                    smsComponent = Math.Abs(sms - smsMean) / (_config.SMSDeltaThreshold * smsStdDev);
            }
            
            double spreadComponent = medianSpread > 0 
                ? spread / (_config.SpreadBlowoutMultiplier * medianSpread) 
                : 0.0;
            
            return Math.Clamp((smsComponent + spreadComponent) / 2.0, 0.0, 1.0);
        }
        
        private void UpdatePhase(DateTime currentTime)
        {
            double minutesSinceSpike = (currentTime - _spikeDetectedAt).TotalMinutes;
            
            // Phase timing per spec: 0-2min Block, 3-5min UnwindOnly, 6-15min Restricted, >15min Normal
            // Using <= for inclusive boundaries to match spec exactly
            if (minutesSinceSpike <= 2)  // 0-2 min
            {
                _currentPhase = NewsGuardPhase.Block;
            }
            else if (minutesSinceSpike <= 5)  // 3-5 min (anything after 2, up to 5)
            {
                _currentPhase = NewsGuardPhase.UnwindOnly;
            }
            else if (minutesSinceSpike <= 15)  // 6-15 min
            {
                _currentPhase = NewsGuardPhase.Restricted;
            }
            else
            {
                _currentPhase = NewsGuardPhase.Normal;
                _spikeDetectedAt = DateTime.MinValue;
            }
        }
        
        private double CalculateStdDev(IEnumerable<double> values, double mean)
        {
            double sumSquaredDiffs = values.Sum(v => Math.Pow(v - mean, 2));
            return Math.Sqrt(sumSquaredDiffs / values.Count());
        }
    }
    
    /// <summary>
    /// News guard phase enumeration.
    /// </summary>
    public enum NewsGuardPhase
    {
        /// <summary>Normal operation - all actions allowed.</summary>
        Normal,
        
        /// <summary>Block phase (0-2 min) - no entries, no hedges.</summary>
        Block,
        
        /// <summary>Unwind-only phase (3-5 min) - only unwinds allowed.</summary>
        UnwindOnly,
        
        /// <summary>Restricted phase (6-15 min) - hedges with 2× Hmult required.</summary>
        Restricted
    }
}

