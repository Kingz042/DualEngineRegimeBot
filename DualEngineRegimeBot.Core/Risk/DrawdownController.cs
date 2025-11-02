using System;
using System.Collections.Generic;
using System.Linq;
using DualEngineRegimeBot.Core.Config;

namespace DualEngineRegimeBot.Core.Risk
{
    /// <summary>
    /// Graduated drawdown controller with quadratic scaling and hybrid peak reference.
    /// Replaces binary locks with smooth risk reduction and optional Survival Mode.
    /// </summary>
    public class DrawdownController
    {
        private readonly DrawdownScalingConfig _config;
        private readonly SurvivalModeConfig _survivalConfig;
        
        private double _allTimeHigh;
        private readonly Queue<(DateTime time, double equity)> _equityHistory = new Queue<(DateTime, double)>();
        private double _rollingPeakEquity;
        private bool _survivalModeActive = false;
        
        public DrawdownController(DrawdownScalingConfig config, SurvivalModeConfig survivalConfig, double initialEquity)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _survivalConfig = survivalConfig ?? throw new ArgumentNullException(nameof(survivalConfig));
            
            _allTimeHigh = initialEquity;
            _rollingPeakEquity = initialEquity;
            _equityHistory.Enqueue((DateTime.UtcNow, initialEquity));
        }
        
        /// <summary>
        /// Updates drawdown tracking with current equity.
        /// </summary>
        public void Update(double currentEquity, DateTime currentTime)
        {
            // Update all-time high
            if (currentEquity > _allTimeHigh)
                _allTimeHigh = currentEquity;
            
            // Update equity history for rolling peak
            _equityHistory.Enqueue((currentTime, currentEquity));
            
            // Trim history older than rolling window
            DateTime cutoff = currentTime.AddDays(-_config.RollingPeakWindowDays);
            while (_equityHistory.Count > 0 && _equityHistory.Peek().time < cutoff)
                _equityHistory.Dequeue();
            
            // Update rolling peak
            _rollingPeakEquity = _equityHistory.Max(e => e.equity);
        }
        
        /// <summary>
        /// Computes current drawdown percentage from peak.
        /// </summary>
        public double GetDrawdownPct(double currentEquity)
        {
            double peak = GetPeakReference();
            if (peak <= 0)
                return 0;
            
            return Math.Max(0, (peak - currentEquity) / peak * 100.0);
        }
        
        /// <summary>
        /// Computes risk damper multiplier based on current drawdown.
        /// Uses graduated scaling: <2%→1.0, 2-5%→0.7, 5-10%→0.4, ≥10%→0.0 (or survival)
        /// </summary>
        public double GetDamper(double currentEquity)
        {
            if (!_config.Enabled)
                return 1.0;
            
            double ddPct = GetDrawdownPct(currentEquity);
            
            // Check survival mode trigger
            if (_survivalConfig.Enabled && ddPct >= _survivalConfig.TriggerThresholdPct)
            {
                _survivalModeActive = true;
                return _survivalConfig.RiskCap;
            }
            
            // Graduated damping
            for (int i = _config.ThresholdLevels.Length - 1; i >= 0; i--)
            {
                if (ddPct >= _config.ThresholdLevels[i])
                    return _config.DamperValues[i + 1];
            }
            
            return _config.DamperValues[0]; // No drawdown
        }
        
        /// <summary>
        /// Gets peak reference using hybrid logic: max(AllTimeHigh, 0.95 × RollingHigh_30d).
        /// </summary>
        public double GetPeakReference()
        {
            if (!_config.UseHybridPeak)
                return _allTimeHigh;
            
            double adjustedRollingPeak = _rollingPeakEquity * _config.RollingPeakMultiplier;
            return Math.Max(_allTimeHigh, adjustedRollingPeak);
        }
        
        /// <summary>
        /// Checks if survival mode is currently active.
        /// </summary>
        public bool IsSurvivalModeActive() => _survivalModeActive;
        
        /// <summary>
        /// Deactivates survival mode (when drawdown recovers).
        /// </summary>
        public void DeactivateSurvivalMode()
        {
            _survivalModeActive = false;
        }
        
        /// <summary>
        /// Gets drawdown statistics for monitoring.
        /// </summary>
        public DrawdownStats GetStats(double currentEquity)
        {
            double ddPct = GetDrawdownPct(currentEquity);
            double damper = GetDamper(currentEquity);
            double peak = GetPeakReference();
            
            return new DrawdownStats
            {
                CurrentDrawdownPct = ddPct,
                DamperMultiplier = damper,
                PeakReference = peak,
                AllTimeHigh = _allTimeHigh,
                RollingPeak = _rollingPeakEquity,
                SurvivalModeActive = _survivalModeActive,
                DamperLevel = GetDamperLevel(ddPct)
            };
        }
        
        /// <summary>
        /// Gets descriptive damper level for logging.
        /// </summary>
        private string GetDamperLevel(double ddPct)
        {
            if (ddPct >= 10.0)
                return _survivalModeActive ? "Survival" : "Locked";
            if (ddPct >= 5.0)
                return "Severe (40%)";
            if (ddPct >= 2.0)
                return "Moderate (70%)";
            return "Normal (100%)";
        }
        
        /// <summary>
        /// Resets all-time high (use with caution, typically for account resets).
        /// </summary>
        public void ResetAllTimeHigh(double newHigh)
        {
            _allTimeHigh = newHigh;
        }
    }
    
    /// <summary>
    /// Drawdown statistics snapshot.
    /// </summary>
    public class DrawdownStats
    {
        public double CurrentDrawdownPct { get; set; }
        public double DamperMultiplier { get; set; }
        public double PeakReference { get; set; }
        public double AllTimeHigh { get; set; }
        public double RollingPeak { get; set; }
        public bool SurvivalModeActive { get; set; }
        public string DamperLevel { get; set; }
        
        public override string ToString()
        {
            return $"DD={CurrentDrawdownPct:F2}%, Damper={DamperMultiplier:F2} ({DamperLevel}), " +
                   $"Peak={PeakReference:F2}, ATH={AllTimeHigh:F2}, Rolling={RollingPeak:F2}" +
                   (SurvivalModeActive ? " [SURVIVAL MODE]" : "");
        }
    }
}

