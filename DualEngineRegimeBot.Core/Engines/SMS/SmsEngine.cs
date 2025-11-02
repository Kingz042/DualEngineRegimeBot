using System;
using System.Collections.Generic;

namespace DualEngineRegimeBot.Core.Engines.SMS
{
    /// <summary>
    /// SMS (Spread Momentum Score) Engine - measures market "micro energy"
    /// based on EMA spread change normalized by ATR.
    /// 
    /// CALCULATION:
    /// 1. spread = EMA(fast) - EMA(slow)
    /// 2. dSpread = spread[t] - spread[t-1]
    /// 3. norm = dSpread / max(ATR, atrFloor)
    /// 4. smsRaw = RMS(norm over window) = sqrt(mean(norm^2))
    /// 5. Optional: z-score normalization
    /// 6. SMS = clamp(smsRaw, 0, 6)
    /// 7. ExecMult = 1.0 + 0.5 * tanh(k * (SMS - baseline))
    /// 
    /// PROPERTIES:
    /// - Always non-negative (uses RMS, not signed mean)
    /// - Responsive to volatility changes
    /// - Normalized by ATR to be scale-invariant
    /// - ExecMult centered at 1.0 (throttle below, boost above)
    /// </summary>
    public class SmsEngine
    {
        private readonly SmsConfig _config;
        
        // EMA state
        private double _emaFast;
        private double _emaSlow;
        private double _prevSpread;
        private bool _firstBar = true;
        
        // ATR state
        private readonly Queue<double> _atrHistory = new Queue<double>();
        private double _atrSum = 0;
        
        // Norm^2 rolling window for RMS
        private readonly Queue<double> _normSquared = new Queue<double>();
        private double _normSquaredSum = 0;
        
        // SMS raw history for z-score
        private readonly Queue<double> _smsRawHistory = new Queue<double>();
        private double _smsRawSum = 0;
        
        // Telemetry counters
        private int _barCount = 0;
        private int _atrFloorHits = 0;
        
        // Cached result
        private SmsResult _lastResult;

        public SmsEngine(SmsConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _config.Validate();
            
            _lastResult = new SmsResult
            {
                Value = 0,
                ExecMult = 1.0,
                Atr = _config.AtrFloorPips,
                IsValid = false
            };
        }

        /// <summary>
        /// Calculates SMS for a new bar.
        /// </summary>
        /// <param name="close">Close price of current bar</param>
        /// <param name="high">High price of current bar</param>
        /// <param name="low">Low price of current bar</param>
        /// <returns>SMS calculation result</returns>
        public SmsResult Calculate(double close, double high, double low)
        {
            _barCount++;
            
            // Guard against invalid inputs
            if (double.IsNaN(close) || double.IsNaN(high) || double.IsNaN(low) ||
                double.IsInfinity(close) || double.IsInfinity(high) || double.IsInfinity(low))
            {
                return _lastResult; // Return last valid result
            }
            
            if (high < low || close < 0)
            {
                return _lastResult; // Invalid bar
            }

            // 1. Update EMAs
            double alphaFast = 2.0 / (_config.EmaFast + 1);
            double alphaSlow = 2.0 / (_config.EmaSlow + 1);
            
            if (_firstBar)
            {
                _emaFast = close;
                _emaSlow = close;
                _prevSpread = 0;
                _firstBar = false;
                return _lastResult; // Need at least 2 bars for dSpread
            }
            
            _emaFast = alphaFast * close + (1 - alphaFast) * _emaFast;
            _emaSlow = alphaSlow * close + (1 - alphaSlow) * _emaSlow;
            
            // 2. Calculate spread and dSpread
            double spread = _emaFast - _emaSlow;
            double dSpread = spread - _prevSpread;
            _prevSpread = spread;
            
            // 3. Update ATR
            double trueRange = Math.Max(high - low, 0.0001); // Prevent zero
            _atrHistory.Enqueue(trueRange);
            _atrSum += trueRange;
            
            if (_atrHistory.Count > _config.AtrLen)
            {
                _atrSum -= _atrHistory.Dequeue();
            }
            
            double atr = _atrHistory.Count > 0 ? _atrSum / _atrHistory.Count : trueRange;
            
            // Apply ATR floor
            double atrUsed = Math.Max(atr, _config.AtrFloorPips);
            if (atr < _config.AtrFloorPips)
            {
                _atrFloorHits++;
            }
            
            // 4. Normalize dSpread by ATR
            double norm = dSpread / atrUsed;
            
            // Guard against NaN/Inf
            if (double.IsNaN(norm) || double.IsInfinity(norm))
            {
                norm = 0;
            }
            
            // 5. Calculate RMS over window
            double normSq = norm * norm;
            _normSquared.Enqueue(normSq);
            _normSquaredSum += normSq;
            
            if (_normSquared.Count > _config.Window)
            {
                _normSquaredSum -= _normSquared.Dequeue();
            }
            
            double meanNormSq = _normSquared.Count > 0 ? _normSquaredSum / _normSquared.Count : 0;
            double smsRaw = Math.Sqrt(Math.Max(0, meanNormSq)); // Ensure non-negative input to sqrt
            
            // 6. Optional z-score normalization
            double smsValue;
            if (_config.UseZScore && _barCount > _config.Window)
            {
                _smsRawHistory.Enqueue(smsRaw);
                _smsRawSum += smsRaw;
                
                if (_smsRawHistory.Count > _config.Window)
                {
                    _smsRawSum -= _smsRawHistory.Dequeue();
                }
                
                double meanR = _smsRawHistory.Count > 0 ? _smsRawSum / _smsRawHistory.Count : smsRaw;
                
                // Calculate standard deviation
                double variance = 0;
                foreach (var val in _smsRawHistory)
                {
                    double diff = val - meanR;
                    variance += diff * diff;
                }
                variance = _smsRawHistory.Count > 1 ? variance / _smsRawHistory.Count : 0;
                double stdR = Math.Sqrt(Math.Max(0, variance));
                
                // Z-score
                smsValue = stdR > 1e-9 ? (smsRaw - meanR) / stdR : smsRaw;
            }
            else
            {
                smsValue = smsRaw;
            }
            
            // 7. Clamp SMS to reasonable range
            smsValue = Math.Clamp(Math.Abs(smsValue), 0.0, 6.0);
            
            // 8. Calculate ExecMult using symmetric S-curve
            double x = smsValue - _config.Baseline;
            double tanhValue = Math.Tanh(_config.TanhK * x);
            double execMult = 1.0 + 0.5 * tanhValue;
            execMult = Math.Clamp(execMult, _config.ClampMin, _config.ClampMax);
            
            // 9. Telemetry logging (every N bars)
            if (_barCount % 100 == 0)
            {
                LogTelemetry(smsValue, execMult, atrUsed);
            }
            
            // 10. Create result
            _lastResult = new SmsResult
            {
                Value = smsValue,
                ExecMult = execMult,
                Atr = atrUsed,
                IsValid = _barCount > _config.Window // Need full window for valid SMS
            };
            
            return _lastResult;
        }

        /// <summary>
        /// Gets the last calculated SMS result.
        /// </summary>
        public SmsResult GetLastResult() => _lastResult;

        /// <summary>
        /// Gets telemetry statistics.
        /// </summary>
        public SmsTelemetry GetTelemetry()
        {
            return new SmsTelemetry
            {
                TotalBars = _barCount,
                AtrFloorHits = _atrFloorHits,
                AtrFloorHitRate = _barCount > 0 ? (double)_atrFloorHits / _barCount : 0,
                LastSms = _lastResult.Value,
                LastExecMult = _lastResult.ExecMult,
                LastAtr = _lastResult.Atr
            };
        }

        private void LogTelemetry(double sms, double execMult, double atr)
        {
            // This would log to your telemetry system
            // For now, just console output in debug builds
            #if DEBUG
            Console.WriteLine($"[SMS Telemetry] Bar={_barCount}, SMS={sms:F3}, ExecMult={execMult:F3}, ATR={atr:F4}, FloorHits={_atrFloorHits}");
            #endif
        }

        /// <summary>
        /// Resets the engine state.
        /// </summary>
        public void Reset()
        {
            _emaFast = 0;
            _emaSlow = 0;
            _prevSpread = 0;
            _firstBar = true;
            _atrHistory.Clear();
            _atrSum = 0;
            _normSquared.Clear();
            _normSquaredSum = 0;
            _smsRawHistory.Clear();
            _smsRawSum = 0;
            _barCount = 0;
            _atrFloorHits = 0;
        }
    }

    /// <summary>
    /// SMS calculation result.
    /// </summary>
    public class SmsResult
    {
        /// <summary>
        /// SMS value - market micro energy (0-6 range, typically 0.2-3.0).
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// Execution multiplier [0.5, 1.5], centered at 1.0.
        /// Below 1.0 = throttle sizing, above 1.0 = boost sizing.
        /// </summary>
        public double ExecMult { get; set; }

        /// <summary>
        /// ATR used for normalization (with floor applied).
        /// </summary>
        public double Atr { get; set; }

        /// <summary>
        /// True if SMS is valid (enough bars processed).
        /// </summary>
        public bool IsValid { get; set; }
    }

    /// <summary>
    /// SMS telemetry for monitoring and debugging.
    /// </summary>
    public class SmsTelemetry
    {
        public int TotalBars { get; set; }
        public int AtrFloorHits { get; set; }
        public double AtrFloorHitRate { get; set; }
        public double LastSms { get; set; }
        public double LastExecMult { get; set; }
        public double LastAtr { get; set; }
    }

    /// <summary>
    /// SMS configuration.
    /// </summary>
    public class SmsConfig
    {
        /// <summary>Fast EMA period (default: 5).</summary>
        public int EmaFast { get; set; } = 5;

        /// <summary>Slow EMA period (default: 20).</summary>
        public int EmaSlow { get; set; } = 20;

        /// <summary>ATR lookback period (default: 14).</summary>
        public int AtrLen { get; set; } = 14;

        /// <summary>Rolling window for RMS calculation (default: 20).</summary>
        public int Window { get; set; } = 20;

        /// <summary>Minimum ATR in pips (default: 0.5).</summary>
        public double AtrFloorPips { get; set; } = 0.5;

        /// <summary>Use z-score normalization (default: true).</summary>
        public bool UseZScore { get; set; } = true;

        /// <summary>SMS baseline for ExecMult mapping (default: 1.0).</summary>
        public double Baseline { get; set; } = 1.0;

        /// <summary>Tanh steepness parameter (default: 0.35).</summary>
        public double TanhK { get; set; } = 0.35;

        /// <summary>ExecMult minimum clamp (default: 0.5).</summary>
        public double ClampMin { get; set; } = 0.5;

        /// <summary>ExecMult maximum clamp (default: 1.5).</summary>
        public double ClampMax { get; set; } = 1.5;

        /// <summary>
        /// Validates configuration parameters.
        /// </summary>
        public void Validate()
        {
            if (EmaFast < 1 || EmaFast > 100)
                throw new ArgumentException($"EmaFast must be between 1 and 100, got {EmaFast}");
            if (EmaSlow < 1 || EmaSlow > 200)
                throw new ArgumentException($"EmaSlow must be between 1 and 200, got {EmaSlow}");
            if (EmaSlow <= EmaFast)
                throw new ArgumentException($"EmaSlow ({EmaSlow}) must be greater than EmaFast ({EmaFast})");
            if (AtrLen < 1 || AtrLen > 100)
                throw new ArgumentException($"AtrLen must be between 1 and 100, got {AtrLen}");
            if (Window < 5 || Window > 200)
                throw new ArgumentException($"Window must be between 5 and 200, got {Window}");
            if (AtrFloorPips <= 0)
                throw new ArgumentException($"AtrFloorPips must be positive, got {AtrFloorPips}");
            if (Baseline < 0 || Baseline > 10)
                throw new ArgumentException($"Baseline must be between 0 and 10, got {Baseline}");
            if (TanhK <= 0 || TanhK > 2)
                throw new ArgumentException($"TanhK must be between 0 and 2, got {TanhK}");
            if (ClampMin < 0 || ClampMin >= ClampMax)
                throw new ArgumentException($"ClampMin ({ClampMin}) must be less than ClampMax ({ClampMax})");
            if (ClampMax > 3)
                throw new ArgumentException($"ClampMax must be <= 3, got {ClampMax}");
        }
    }
}

