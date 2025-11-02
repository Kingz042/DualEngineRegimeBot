using System;
using DualEngineRegimeBot.Core.Config;

namespace DualEngineRegimeBot.Core.Sizing
{
    /// <summary>
    /// Inverse-volatility equity-% position sizer.
    /// Keeps monetary risk stable by scaling down in high volatility.
    /// </summary>
    public class InverseVolSizer : ISizerService
    {
        /// <summary>
        /// Computes effective risk % incorporating all adjustment factors.
        /// </summary>
        public double ComputeEffRiskPct(
            double baseRiskPct,
            double volMult,
            double regimeConf,
            double strategyConf,
            double tfDamp)
        {
            if (baseRiskPct <= 0) return 0.0;
            
            // All multipliers are in [0..1] or clamped ranges
            double effRisk = baseRiskPct * volMult * regimeConf * strategyConf * tfDamp;
            
            // Final safety clamp
            return Math.Clamp(effRisk, 0.01, baseRiskPct * 2.0);
        }
        
        /// <summary>
        /// Computes VolMult = clamp(TargetNATR / CurrentNATR, min, max).
        /// </summary>
        public double ComputeVolMult(double currentNATR, SizingConfig config)
        {
            if (currentNATR <= 0 || config.TargetNATR <= 0)
                return 1.0;
            
            double volMult = config.TargetNATR / currentNATR;
            return Math.Clamp(volMult, config.VolMultMin, config.VolMultMax);
        }
        
        /// <summary>
        /// Converts risk % and stop distance to units, enforcing margin buffer and clamps.
        /// Formula: units = (equity × effRiskPct/100) / (stopDistPips × pipValue)
        /// Then validate against margin and min/max volume.
        /// </summary>
        public double ComputeUnits(
            double effRiskPct,
            double stopDistancePips,
            MarketContext context,
            SizingConfig config)
        {
            if (effRiskPct <= 0 || stopDistancePips <= 0 || context.AccountEquity <= 0)
                return 0.0;
            
            // Risk amount in account currency
            double riskAmount = context.AccountEquity * (effRiskPct / 100.0);
            
            // Pip value (money per pip per unit)
            double pipValue = context.PipSize * context.TickValue / context.TickSize;
            if (pipValue <= 0) pipValue = 1.0; // Fallback
            
            // Required units to achieve risk amount
            double units = riskAmount / (stopDistancePips * pipValue);
            
            // Enforce margin buffer: require 2× margin available
            double requiredMargin = ComputeRequiredMargin(units, context);
            if (requiredMargin * config.MarginBufferX > context.FreeMargin)
            {
                // Scale down to fit margin
                units = context.FreeMargin / (config.MarginBufferX * (requiredMargin / units));
            }
            
            // Apply volume clamps
            units = Math.Clamp(units, config.MinVolume, config.MaxVolume);
            
            // Round to broker precision (e.g., 0.01 lots)
            units = Math.Round(units / config.MinVolume) * config.MinVolume;
            
            return units;
        }
        
        /// <summary>
        /// Validates that computed units respect margin and broker limits.
        /// </summary>
        public bool ValidateUnits(double units, MarketContext context, SizingConfig config)
        {
            if (units < config.MinVolume || units > config.MaxVolume)
                return false;
            
            double requiredMargin = ComputeRequiredMargin(units, context);
            if (requiredMargin * config.MarginBufferX > context.FreeMargin)
                return false;
            
            return true;
        }
        
        /// <summary>
        /// Estimates required margin for given units (simplified for cTrader).
        /// Actual margin depends on leverage and symbol specs; this is conservative.
        /// </summary>
        private double ComputeRequiredMargin(double units, MarketContext context)
        {
            // Simplified: margin ≈ units × price / leverage
            // For cTrader, actual calculation uses symbol.GetMarginRequirement or similar
            // Here we use a conservative estimate
            double notional = units * context.Mid;
            double leverage = 100.0; // Default assumption; should be injected
            return notional / leverage;
        }
    }
}

