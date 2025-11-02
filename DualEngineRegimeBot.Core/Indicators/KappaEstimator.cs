using System;
using System.Collections.Generic;
using System.Linq;

namespace DualEngineRegimeBot.Core.Indicators
{
    /// <summary>
    /// Estimates mean-reversion speed (κ) via rolling OLS on OU process: Δx ≈ κ(μ - x).
    /// Result is EMA-smoothed and clamped to avoid instability.
    /// </summary>
    public class KappaEstimator
    {
        private readonly int _windowBars;
        private readonly int _emaSmoothing;
        private readonly double _kappaMin;
        private readonly double _kappaMax;
        
        private readonly List<(double price, double mean)> _history = new List<(double, double)>();
        private double _kappaSmoothed;
        private bool _initialized;
        
        /// <summary>
        /// Initializes kappa estimator.
        /// </summary>
        /// <param name="windowBars">Rolling window size for OLS (e.g., 50).</param>
        /// <param name="emaSmoothing">EMA period for smoothing raw kappa (e.g., 10).</param>
        /// <param name="kappaMin">Minimum clamp (e.g., 0.01).</param>
        /// <param name="kappaMax">Maximum clamp (e.g., 2.0).</param>
        public KappaEstimator(int windowBars, int emaSmoothing, double kappaMin = 0.01, double kappaMax = 2.0)
        {
            if (windowBars <= 10) throw new ArgumentException("Window too small for OLS.");
            
            _windowBars = windowBars;
            _emaSmoothing = emaSmoothing;
            _kappaMin = kappaMin;
            _kappaMax = kappaMax;
            _kappaSmoothed = 0.5; // Initial guess
        }
        
        /// <summary>
        /// Updates kappa estimate with new price and Kalman mean.
        /// </summary>
        public void Update(double price, double mean)
        {
            if (double.IsNaN(price) || double.IsNaN(mean))
                return;
            
            _history.Add((price, mean));
            
            if (_history.Count > _windowBars)
                _history.RemoveAt(0);
            
            if (_history.Count < 20) return; // Need minimum data
            
            // Compute OU regression: Δx[t] ≈ κ × (μ[t] - x[t-1])
            double sumXY = 0, sumXX = 0;
            
            for (int i = 1; i < _history.Count; i++)
            {
                double dx = _history[i].price - _history[i - 1].price;
                double deviation = _history[i - 1].mean - _history[i - 1].price;
                
                sumXY += dx * deviation;
                sumXX += deviation * deviation;
            }
            
            double kappaRaw = 0.0;
            if (Math.Abs(sumXX) > 1e-8)
                kappaRaw = sumXY / sumXX;
            
            // Clamp to valid range
            kappaRaw = Math.Clamp(kappaRaw, _kappaMin, _kappaMax);
            
            // EMA smoothing
            if (!_initialized)
            {
                _kappaSmoothed = kappaRaw;
                _initialized = true;
            }
            else
            {
                double alpha = 2.0 / (_emaSmoothing + 1);
                _kappaSmoothed = alpha * kappaRaw + (1 - alpha) * _kappaSmoothed;
            }
        }
        
        /// <summary>
        /// Returns smoothed kappa estimate.
        /// </summary>
        public double GetKappa() => Math.Clamp(_kappaSmoothed, _kappaMin, _kappaMax);
        
        /// <summary>
        /// Returns true if estimator has sufficient data.
        /// </summary>
        public bool IsReady() => _history.Count >= 20;
        
        /// <summary>
        /// Resets state with saved kappa (for persistence).
        /// </summary>
        public void Reset(double kappa)
        {
            _kappaSmoothed = Math.Clamp(kappa, _kappaMin, _kappaMax);
            _initialized = true;
        }
    }
}

