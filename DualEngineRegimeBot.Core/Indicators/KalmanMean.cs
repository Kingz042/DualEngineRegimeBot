using System;

namespace DualEngineRegimeBot.Core.Indicators
{
    /// <summary>
    /// Adaptive Kalman filter for estimating mean price level (μₜ).
    /// Tracks underlying price with adaptive convergence based on R/Q tuning.
    /// </summary>
    public class KalmanMean
    {
        private readonly double _r; // Measurement noise
        private readonly double _q; // Process noise
        
        private double _mu;  // Current estimate (mean)
        private double _p;   // Current covariance
        
        /// <summary>
        /// Initializes Kalman filter with noise parameters and initial state.
        /// </summary>
        /// <param name="r">Measurement noise (e.g., 0.01).</param>
        /// <param name="q">Process noise (e.g., 0.0001).</param>
        /// <param name="initialPrice">Initial price for μ₀.</param>
        /// <param name="initialP">Initial covariance P₀ (e.g., 1.0).</param>
        public KalmanMean(double r, double q, double initialPrice, double initialP = 1.0)
        {
            if (r <= 0 || q <= 0) throw new ArgumentException("R and Q must be positive.");
            
            _r = r;
            _q = q;
            _mu = initialPrice;
            _p = initialP;
        }
        
        /// <summary>
        /// Updates filter with new price observation.
        /// </summary>
        /// <param name="price">Current market price.</param>
        public void Update(double price)
        {
            if (double.IsNaN(price) || double.IsInfinity(price))
                return; // Skip invalid observations
            
            // Prediction step
            double pPrior = _p + _q;
            
            // Update step
            double k = pPrior / (pPrior + _r); // Kalman gain
            _mu = _mu + k * (price - _mu);
            _p = (1.0 - k) * pPrior;
            
            // Clamp P to avoid numerical drift
            _p = Math.Max(_p, 1e-6);
        }
        
        /// <summary>
        /// Returns current mean estimate (μₜ).
        /// </summary>
        public double GetMean() => _mu;
        
        /// <summary>
        /// Returns current covariance (P). Low values indicate convergence.
        /// </summary>
        public double GetCovariance() => _p;
        
        /// <summary>
        /// Returns true if filter has converged (P below threshold).
        /// </summary>
        public bool IsConverged(double threshold = 0.1) => _p < threshold;
        
        /// <summary>
        /// Resets filter to new state (for state restoration).
        /// </summary>
        public void Reset(double mu, double p)
        {
            _mu = mu;
            _p = Math.Max(p, 1e-6);
        }
    }
}

