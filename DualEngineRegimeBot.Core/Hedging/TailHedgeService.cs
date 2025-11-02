using System;
using DualEngineRegimeBot.Core.Config;

namespace DualEngineRegimeBot.Core.Hedging
{
    /// <summary>
    /// Intrabar tail-hedge service for shock protection.
    /// Triggers on extreme VDI + low kappa OR ATR spike; auto-unwinds per rules.
    /// </summary>
    public class TailHedgeService : ITailHedgeService
    {
        private readonly TailHedgeConfig _config;
        private DateTime _lastHedgeTime = DateTime.MinValue;
        
        public TailHedgeService(TailHedgeConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }
        
        /// <summary>
        /// Probes whether hedge trigger conditions are met.
        /// </summary>
        public bool ShouldTriggerHedge(
            MarketContext context,
            double vdi,
            double kappa,
            double atrRatio,
            double tfBias,
            double netExposure)
        {
            // Must have net exposure
            if (Math.Abs(netExposure) < 0.01)
                return false;
            
            // Cooldown active?
            if (IsCooldownActive(context.Time))
                return false;
            
            // Skip if strong TF_Bias aligns with move direction
            bool vdiUp = vdi > 0;
            bool tfAligns = (vdiUp && tfBias > _config.TfBiasDisableThreshold) ||
                           (!vdiUp && tfBias < -_config.TfBiasDisableThreshold);
            
            if (tfAligns)
                return false; // TF says this is trend, not tail
            
            // Trigger condition 1: Extreme VDI + low kappa (reversion unlikely)
            bool vdiShock = Math.Abs(vdi) > _config.VdiTrigger && kappa < _config.KappaTrigger;
            
            // Trigger condition 2: ATR spike
            bool atrSpike = atrRatio > _config.AtrSpikeRatio;
            
            return vdiShock || atrSpike;
        }
        
        /// <summary>
        /// Computes hedge size as fraction of net exposure.
        /// </summary>
        public double ComputeHedgeSize(double netExposure, TailHedgeConfig config)
        {
            return Math.Abs(netExposure) * config.HedgeFraction;
        }
        
        /// <summary>
        /// Checks if hedge should auto-unwind.
        /// </summary>
        public bool ShouldExitHedge(
            PositionSnapshot hedge,
            MarketContext context,
            double vdi,
            double atrRatio)
        {
            if (hedge.Engine != ExecutionEngine.TailHedge)
                return false;
            
            // Exit if VDI cooled back inside threshold
            if (Math.Abs(vdi) < _config.ExitVdiInside)
                return true;
            
            // Exit if ATR ratio cooled
            if (atrRatio < _config.ExitAtrRatio)
                return true;
            
            // Exit if max bars exceeded
            if (hedge.BarsOpen >= _config.ExitMaxBars)
                return true;
            
            // Exit if min cover profit achieved
            if (hedge.UnrealizedPnL >= _config.ExitMinCoverProfit)
                return true;
            
            return false;
        }
        
        public bool IsCooldownActive(DateTime now)
        {
            return (now - _lastHedgeTime).TotalMilliseconds < _config.HedgeCooldownMs;
        }
        
        public void ResetCooldown(DateTime now)
        {
            _lastHedgeTime = now;
        }
    }
}

