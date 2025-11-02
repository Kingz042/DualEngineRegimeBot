using System;
using System.Collections.Generic;
using System.Linq;

namespace DualEngineRegimeBot.Core.Indicators
{
    /// <summary>
    /// ATR (Average True Range) with EMA smoothing and NATR (Normalized ATR) calculation.
    /// </summary>
    public class AtrEma
    {
        private readonly int _period;
        private readonly List<double> _atrValues = new List<double>();
        private double _lastClose;
        private bool _initialized;
        
        /// <summary>
        /// Initializes ATR with specified period.
        /// </summary>
        /// <param name="period">ATR period (e.g., 14).</param>
        public AtrEma(int period)
        {
            if (period <= 0) throw new ArgumentException("Period must be positive.");
            _period = period;
        }
        
        /// <summary>
        /// Updates ATR with new bar data.
        /// </summary>
        /// <param name="high">Bar high.</param>
        /// <param name="low">Bar low.</param>
        /// <param name="close">Bar close.</param>
        public void Update(double high, double low, double close)
        {
            if (double.IsNaN(high) || double.IsNaN(low) || double.IsNaN(close))
                return;
            
            double trueRange;
            if (!_initialized)
            {
                trueRange = high - low;
                _initialized = true;
            }
            else
            {
                double hl = high - low;
                double hc = Math.Abs(high - _lastClose);
                double lc = Math.Abs(low - _lastClose);
                trueRange = Math.Max(hl, Math.Max(hc, lc));
            }
            
            _lastClose = close;
            _atrValues.Add(trueRange);
            
            // Keep only necessary history
            if (_atrValues.Count > _period * 3)
                _atrValues.RemoveAt(0);
        }
        
        /// <summary>
        /// Returns current ATR value (EMA-smoothed).
        /// </summary>
        public double GetATR()
        {
            if (_atrValues.Count < _period)
                return _atrValues.Count > 0 ? _atrValues.Average() : 0.0;
            
            double alpha = 2.0 / (_period + 1);
            double ema = _atrValues.Take(_period).Average();
            
            for (int i = _period; i < _atrValues.Count; i++)
                ema = alpha * _atrValues[i] + (1 - alpha) * ema;
            
            return Math.Max(ema, 1e-8); // Floor to avoid zero
        }
        
        /// <summary>
        /// Returns normalized ATR as percentage of price.
        /// </summary>
        public double GetNATR(double price)
        {
            if (price <= 0) return 0.0;
            return (GetATR() / price) * 100.0; // As percentage
        }
        
        /// <summary>
        /// Returns true if sufficient data for valid ATR.
        /// </summary>
        public bool IsReady() => _atrValues.Count >= _period;
    }
}

