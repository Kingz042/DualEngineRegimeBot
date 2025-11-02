using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DualEngineRegimeBot.Core.Diagnostics
{
    /// <summary>
    /// Validation hooks and metrics APIs for parameter tuning and model validation.
    /// Provides statistical measures for regime, SMS, hedges, drawdown, and parameter sensitivity.
    /// </summary>
    public class ValidationMetrics
    {
        private readonly string _outputDirectory;
        
        // Regime tracking
        private readonly List<RegimeEpisode> _regimeHistory = new List<RegimeEpisode>();
        private DateTime _currentRegimeStart = DateTime.MinValue;
        private RegimeDirection _currentRegimeDirection = RegimeDirection.Neutral;
        
        // SMS performance tracking
        private readonly List<SMSSignal> _smsSignals = new List<SMSSignal>();
        
        // Hedge tracking
        private readonly List<HedgeOutcome> _hedgeOutcomes = new List<HedgeOutcome>();
        
        public ValidationMetrics(string outputDirectory)
        {
            _outputDirectory = outputDirectory;
            Directory.CreateDirectory(outputDirectory);
        }
        
        #region Regime Metrics
        
        /// <summary>
        /// Records a regime transition.
        /// </summary>
        public void RecordRegimeTransition(
            RegimeDirection newDirection, 
            RegimeVolState volState,
            double confidence,
            DateTime timestamp)
        {
            if (_currentRegimeDirection != newDirection && _currentRegimeStart != DateTime.MinValue)
            {
                // End previous regime episode
                var episode = new RegimeEpisode
                {
                    Direction = _currentRegimeDirection,
                    StartTime = _currentRegimeStart,
                    EndTime = timestamp,
                    DurationMinutes = (timestamp - _currentRegimeStart).TotalMinutes
                };
                
                _regimeHistory.Add(episode);
            }
            
            _currentRegimeDirection = newDirection;
            _currentRegimeStart = timestamp;
        }
        
        /// <summary>
        /// Computes regime duration statistics (Kaplan-Meier style).
        /// </summary>
        public RegimeDurationStats GetRegimeDurationStats()
        {
            if (_regimeHistory.Count == 0)
                return new RegimeDurationStats();
            
            var durations = _regimeHistory.Select(e => e.DurationMinutes).OrderBy(x => x).ToArray();
            
            return new RegimeDurationStats
            {
                Count = _regimeHistory.Count,
                MeanDurationMin = durations.Average(),
                MedianDurationMin = GetPercentile(durations, 0.50),
                P25DurationMin = GetPercentile(durations, 0.25),
                P75DurationMin = GetPercentile(durations, 0.75),
                MinDurationMin = durations.Min(),
                MaxDurationMin = durations.Max()
            };
        }
        
        /// <summary>
        /// Computes regime flip rate (transitions per hour).
        /// </summary>
        public double GetRegimeFlipRate(TimeSpan window)
        {
            DateTime cutoff = DateTime.UtcNow - window;
            int flips = _regimeHistory.Count(e => e.EndTime > cutoff);
            
            return flips / window.TotalHours;
        }
        
        /// <summary>
        /// Computes regime purity (% of bars reinforcing DirScore & VolRatio).
        /// Note: Requires bar-level tracking; placeholder implementation.
        /// </summary>
        public double GetRegimePurity()
        {
            // Placeholder - would need bar-level DirScore/VolRatio tracking
            return 0.0;
        }
        
        #endregion
        
        #region SMS Metrics
        
        /// <summary>
        /// Records an SMS signal with subsequent outcome.
        /// </summary>
        public void RecordSMSSignal(
            double smsValue,
            RegimeDirection regime,
            bool allowedMove,
            double subsequentMove,
            double atrM1)
        {
            _smsSignals.Add(new SMSSignal
            {
                Timestamp = DateTime.UtcNow,
                SMSValue = smsValue,
                Regime = regime,
                AllowedMove = allowedMove,
                SubsequentMovePips = subsequentMove,
                SubsequentMoveATRs = atrM1 > 0 ? subsequentMove / atrM1 : 0
            });
            
            // Keep last 10000 signals
            if (_smsSignals.Count > 10000)
                _smsSignals.RemoveAt(0);
        }
        
        /// <summary>
        /// Computes conditional ROC-AUC for SMS by regime.
        /// Measures: does high SMS predict large moves >1×ATR in allowed direction?
        /// </summary>
        public Dictionary<RegimeDirection, double> GetSMSConditionalAUC()
        {
            var result = new Dictionary<RegimeDirection, double>();
            
            foreach (var regime in new[] { RegimeDirection.Bull, RegimeDirection.Bear, RegimeDirection.Neutral })
            {
                var signals = _smsSignals
                    .Where(s => s.Regime == regime && Math.Abs(s.SubsequentMoveATRs) > 0.1)
                    .ToList();
                
                if (signals.Count < 30)
                {
                    result[regime] = 0.5; // Insufficient data
                    continue;
                }
                
                // Simple AUC approximation: correlation between SMS and |move|
                double correlation = CalculateCorrelation(
                    signals.Select(s => s.SMSValue).ToArray(),
                    signals.Select(s => Math.Abs(s.SubsequentMoveATRs)).ToArray());
                
                // Convert correlation to pseudo-AUC [0.5, 1.0]
                double auc = 0.5 + (correlation * 0.5);
                result[regime] = Math.Clamp(auc, 0.0, 1.0);
            }
            
            return result;
        }
        
        /// <summary>
        /// Computes MFE/MAE statistics by SMS bins.
        /// </summary>
        public SMSBinStats GetSMSBinStats()
        {
            var bins = new[] { 0.0, 0.6, 1.0, 1.5, double.MaxValue };
            var binStats = new List<(double threshold, double avgMFE, double avgMAE, int count)>();
            
            foreach (var threshold in bins.Take(bins.Length - 1))
            {
                var binSignals = _smsSignals.Where(s => s.SMSValue >= threshold).ToList();
                
                if (binSignals.Count > 0)
                {
                    // Placeholder - would need actual MFE/MAE tracking
                    binStats.Add((threshold, 0.0, 0.0, binSignals.Count));
                }
            }
            
            return new SMSBinStats { Bins = binStats };
        }
        
        #endregion
        
        #region Hedge Metrics
        
        /// <summary>
        /// Records a hedge outcome.
        /// </summary>
        public void RecordHedgeOutcome(
            double maxDD,
            double ulcerIndex,
            double timeToRecoveryMin,
            double hedgePnL,
            double totalPnL)
        {
            _hedgeOutcomes.Add(new HedgeOutcome
            {
                Timestamp = DateTime.UtcNow,
                MaxDD = maxDD,
                UlcerIndex = ulcerIndex,
                TimeToRecoveryMin = timeToRecoveryMin,
                HedgePnL = hedgePnL,
                TotalPnL = totalPnL,
                HedgePnLShare = totalPnL != 0 ? hedgePnL / totalPnL : 0
            });
        }
        
        /// <summary>
        /// Gets hedge impact statistics.
        /// </summary>
        public HedgeImpactStats GetHedgeImpactStats()
        {
            if (_hedgeOutcomes.Count == 0)
                return new HedgeImpactStats();
            
            return new HedgeImpactStats
            {
                AvgMaxDD = _hedgeOutcomes.Average(h => h.MaxDD),
                AvgUlcerIndex = _hedgeOutcomes.Average(h => h.UlcerIndex),
                AvgTimeToRecoveryMin = _hedgeOutcomes.Average(h => h.TimeToRecoveryMin),
                AvgHedgePnLShare = _hedgeOutcomes.Average(h => h.HedgePnLShare),
                TotalHedges = _hedgeOutcomes.Count
            };
        }
        
        #endregion
        
        #region Parameter Sensitivity
        
        /// <summary>
        /// Exports parameter sweep harness data (±20% single param).
        /// </summary>
        public void ExportParameterSweep(string paramName, double baseValue, List<double> values, List<double> outcomes)
        {
            string filename = Path.Combine(_outputDirectory, $"param_sweep_{paramName}.csv");
            
            var lines = new List<string> { "ParamValue,PercentDelta,Outcome" };
            
            for (int i = 0; i < values.Count; i++)
            {
                double percentDelta = (values[i] - baseValue) / baseValue * 100.0;
                lines.Add($"{values[i]:F4},{percentDelta:F2},{outcomes[i]:F4}");
            }
            
            File.WriteAllLines(filename, lines);
        }
        
        /// <summary>
        /// Exports parameter grid (5×5 for two params).
        /// </summary>
        public void ExportParameterGrid(
            string param1Name, 
            double[] param1Values,
            string param2Name,
            double[] param2Values,
            double[,] outcomes)
        {
            string filename = Path.Combine(_outputDirectory, $"param_grid_{param1Name}_{param2Name}.csv");
            
            var lines = new List<string>();
            
            // Header
            string header = param2Name + "," + string.Join(",", param2Values.Select(v => v.ToString("F4")));
            lines.Add(header);
            
            // Data rows
            for (int i = 0; i < param1Values.Length; i++)
            {
                var row = new List<string> { param1Values[i].ToString("F4") };
                
                for (int j = 0; j < param2Values.Length; j++)
                {
                    row.Add(outcomes[i, j].ToString("F4"));
                }
                
                lines.Add(string.Join(",", row));
            }
            
            File.WriteAllLines(filename, lines);
        }
        
        #endregion
        
        #region Drawdown Metrics
        
        /// <summary>
        /// Computes 95th percentile daily loss.
        /// </summary>
        public double Get95thPercentileDailyLoss(List<double> dailyReturns)
        {
            if (dailyReturns.Count == 0)
                return 0;
            
            var losses = dailyReturns.Where(r => r < 0).OrderBy(r => r).ToArray();
            
            if (losses.Length == 0)
                return 0;
            
            return GetPercentile(losses, 0.95);
        }
        
        #endregion
        
        #region Utility Methods
        
        private double GetPercentile(double[] sortedValues, double percentile)
        {
            if (sortedValues.Length == 0)
                return 0;
            
            int index = (int)Math.Ceiling(sortedValues.Length * percentile) - 1;
            index = Math.Clamp(index, 0, sortedValues.Length - 1);
            
            return sortedValues[index];
        }
        
        private double CalculateCorrelation(double[] x, double[] y)
        {
            if (x.Length != y.Length || x.Length == 0)
                return 0;
            
            double meanX = x.Average();
            double meanY = y.Average();
            
            double covariance = 0;
            double varX = 0;
            double varY = 0;
            
            for (int i = 0; i < x.Length; i++)
            {
                double dx = x[i] - meanX;
                double dy = y[i] - meanY;
                
                covariance += dx * dy;
                varX += dx * dx;
                varY += dy * dy;
            }
            
            if (varX == 0 || varY == 0)
                return 0;
            
            return covariance / Math.Sqrt(varX * varY);
        }
        
        #endregion
    }
    
    #region Supporting Types
    
    public class RegimeEpisode
    {
        public RegimeDirection Direction { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double DurationMinutes { get; set; }
    }
    
    public class RegimeDurationStats
    {
        public int Count { get; set; }
        public double MeanDurationMin { get; set; }
        public double MedianDurationMin { get; set; }
        public double P25DurationMin { get; set; }
        public double P75DurationMin { get; set; }
        public double MinDurationMin { get; set; }
        public double MaxDurationMin { get; set; }
        
        public override string ToString()
        {
            return $"Regime Duration: n={Count}, mean={MeanDurationMin:F1}min, " +
                   $"median={MedianDurationMin:F1}min, " +
                   $"IQR=[{P25DurationMin:F1}, {P75DurationMin:F1}]min";
        }
    }
    
    public class SMSSignal
    {
        public DateTime Timestamp { get; set; }
        public double SMSValue { get; set; }
        public RegimeDirection Regime { get; set; }
        public bool AllowedMove { get; set; }
        public double SubsequentMovePips { get; set; }
        public double SubsequentMoveATRs { get; set; }
    }
    
    public class SMSBinStats
    {
        public List<(double threshold, double avgMFE, double avgMAE, int count)> Bins { get; set; }
    }
    
    public class HedgeOutcome
    {
        public DateTime Timestamp { get; set; }
        public double MaxDD { get; set; }
        public double UlcerIndex { get; set; }
        public double TimeToRecoveryMin { get; set; }
        public double HedgePnL { get; set; }
        public double TotalPnL { get; set; }
        public double HedgePnLShare { get; set; }
    }
    
    public class HedgeImpactStats
    {
        public double AvgMaxDD { get; set; }
        public double AvgUlcerIndex { get; set; }
        public double AvgTimeToRecoveryMin { get; set; }
        public double AvgHedgePnLShare { get; set; }
        public int TotalHedges { get; set; }
        
        public override string ToString()
        {
            return $"Hedge Impact: n={TotalHedges}, avgMaxDD={AvgMaxDD:F2}%, " +
                   $"avgUlcer={AvgUlcerIndex:F2}, avgRecovery={AvgTimeToRecoveryMin:F1}min, " +
                   $"avgPnLShare={AvgHedgePnLShare:P1}";
        }
    }
    
    #endregion
}

