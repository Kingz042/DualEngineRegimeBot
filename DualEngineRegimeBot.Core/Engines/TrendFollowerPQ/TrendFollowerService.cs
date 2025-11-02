using System;
using System.Collections.Generic;
using System.Linq;
using DualEngineRegimeBot.Core.Config;

namespace DualEngineRegimeBot.Core.Engines.TrendFollowerPQ
{
    /// <summary>
    /// Adaptive p/q trend-following engine (meso M1 layer).
    /// Emits TF_Bias and TrendEnergy; entries require alignment with macro regime.
    /// </summary>
    public class TrendFollowerService : ITrendFollowerService
    {
        private readonly TrendFollowerConfig _config;
        
        private double _tfBias = 0.0;
        private double _trendEnergy = 0.0;
        private double _currentP = 0.8;
        private double _currentQ = 0.2;
        private int _cooldownBarsRemaining = 0;
        
        // Price history for energy calculation
        private readonly List<double> _priceHistory = new List<double>();
        
        public TrendFollowerService(TrendFollowerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }
        
        /// <summary>
        /// Updates internal state per tick/bar.
        /// Adapts p/q based on regime vol state and confidence.
        /// </summary>
        public void Update(MarketContext context, RegimeSnapshot regime)
        {
            if (double.IsNaN(context.Bid) || double.IsNaN(context.Ask))
                return;
            
            double price = context.Mid;
            _priceHistory.Add(price);
            
            // Keep limited history for energy calculation
            if (_priceHistory.Count > _config.EnergyLookbackBars + 10)
                _priceHistory.RemoveAt(0);
            
            // Adapt p/q based on regime
            AdaptPQ(regime);
            
            // Compute TF_Bias using adaptive momentum
            UpdateBias(price, regime);
            
            // Compute TrendEnergy
            UpdateEnergy();
            
            // Decrement cooldown
            if (_cooldownBarsRemaining > 0)
                _cooldownBarsRemaining--;
        }
        
        public double GetBias() => _tfBias;
        public double GetEnergy() => _trendEnergy;
        
        /// <summary>
        /// Checks if TF entry conditions met; returns intent if yes.
        /// </summary>
        public OrderIntent? CheckEntry(MarketContext context, RegimeSnapshot regime, double effRiskPct)
        {
            // Cooldown active?
            if (_cooldownBarsRemaining > 0)
                return null;
            
            // Require minimum energy and bias
            if (_trendEnergy < _config.MinTrendEnergy)
                return null;
            
            if (Math.Abs(_tfBias) < _config.MinBiasThreshold)
                return null;
            
            // Direction must align with regime (no counter-trend)
            if (regime.Direction == RegimeDirection.Bull && _tfBias < 0)
                return null;
            if (regime.Direction == RegimeDirection.Bear && _tfBias > 0)
                return null;
            if (regime.Direction == RegimeDirection.Neutral)
                return null; // No TF entries in neutral
            
            // Determine side
            TradeSide side = _tfBias > 0 ? TradeSide.Long : TradeSide.Short;
            
            // Compute stop-loss distance (ATR-based)
            double stopDistancePips = context.CurrentATR * _config.StopLossAtrMultiplier / context.PipSize;
            double entryPrice = side == TradeSide.Long ? context.Ask : context.Bid;
            double stopLoss = side == TradeSide.Long 
                ? entryPrice - stopDistancePips * context.PipSize
                : entryPrice + stopDistancePips * context.PipSize;
            
            return new OrderIntent
            {
                Engine = ExecutionEngine.TrendFollower,
                Side = side,
                EntryPrice = entryPrice,
                Units = 0, // To be filled by sizer
                StopLoss = stopLoss,
                TakeProfit = null, // TF uses trailing/momentum exit
                EffRiskPct = effRiskPct,
                Label = $"TF_{context.Time:yyyyMMddHHmmss}",
                Timestamp = context.Time
            };
        }
        
        /// <summary>
        /// Checks if TF exit conditions met for given position.
        /// Exit on: energy fade below floor, or SL hit.
        /// </summary>
        public bool CheckExit(PositionSnapshot position, MarketContext context)
        {
            if (position.Engine != ExecutionEngine.TrendFollower)
                return false;
            
            // Energy fade exit
            if (_trendEnergy < _config.MinTrendEnergy * 0.5)
                return true;
            
            // Bias reversal exit (optional aggressive)
            bool biasReversed = (position.Side == TradeSide.Long && _tfBias < -0.2) ||
                               (position.Side == TradeSide.Short && _tfBias > 0.2);
            
            return biasReversed;
        }
        
        public void ResetCooldown()
        {
            _cooldownBarsRemaining = _config.ReEntryCooldownBars;
        }
        
        /// <summary>
        /// Adapts persistence (p) and shock-decay (q) based on regime.
        /// LowVol + high confidence → higher p (trend persistence).
        /// HighVol → higher q (faster shock decay).
        /// </summary>
        private void AdaptPQ(RegimeSnapshot regime)
        {
            if (regime.VolState == RegimeVolState.LowVol)
            {
                // Higher persistence in low vol with strong confidence
                _currentP = _config.PersistenceMin + 
                           (_config.PersistenceMax - _config.PersistenceMin) * regime.Confidence;
                _currentQ = _config.ShockDecayMin;
            }
            else // HighVol
            {
                // Lower persistence, higher shock decay
                _currentP = _config.PersistenceMin + 
                           (_config.PersistenceMax - _config.PersistenceMin) * 0.5;
                _currentQ = _config.ShockDecayMax;
            }
        }
        
        /// <summary>
        /// Updates TF_Bias using adaptive momentum with p/q dynamics.
        /// Simplified model: bias[t] = p × bias[t-1] + q × momentum[t].
        /// </summary>
        private void UpdateBias(double price, RegimeSnapshot regime)
        {
            if (_priceHistory.Count < 5)
            {
                _tfBias = 0.0;
                return;
            }
            
            // Compute short-term momentum
            int lookback = Math.Min(10, _priceHistory.Count - 1);
            double recentReturn = (_priceHistory[_priceHistory.Count - 1] - 
                                   _priceHistory[_priceHistory.Count - 1 - lookback]) / 
                                   _priceHistory[_priceHistory.Count - 1 - lookback];
            
            // Normalize to [-1, 1] via tanh
            double momentum = Math.Tanh(recentReturn * 200.0); // Scale factor for sensitivity
            
            // Apply p/q dynamics
            _tfBias = _currentP * _tfBias + _currentQ * momentum;
            
            // Clamp to [-1, 1]
            _tfBias = Math.Clamp(_tfBias, -1.0, 1.0);
        }
        
        /// <summary>
        /// Computes TrendEnergy as consistency of directional moves over lookback.
        /// High energy = consistent directional bars; low = choppy.
        /// </summary>
        private void UpdateEnergy()
        {
            if (_priceHistory.Count < _config.EnergyLookbackBars)
            {
                _trendEnergy = 0.0;
                return;
            }
            
            int lookback = _config.EnergyLookbackBars;
            int upBars = 0, downBars = 0;
            
            for (int i = _priceHistory.Count - lookback; i < _priceHistory.Count - 1; i++)
            {
                if (_priceHistory[i + 1] > _priceHistory[i])
                    upBars++;
                else if (_priceHistory[i + 1] < _priceHistory[i])
                    downBars++;
            }
            
            // Energy = dominant direction count / total
            int dominant = Math.Max(upBars, downBars);
            _trendEnergy = (double)dominant / lookback;
            
            // Boost energy if bias and direction align
            if ((_tfBias > 0 && upBars > downBars) || (_tfBias < 0 && downBars > upBars))
                _trendEnergy = Math.Min(_trendEnergy * 1.2, 1.0);
        }
    }
}

