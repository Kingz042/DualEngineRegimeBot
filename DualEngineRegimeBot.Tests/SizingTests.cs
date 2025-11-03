using System;
using Xunit;
using DualEngineRegimeBot.Core;
using DualEngineRegimeBot.Core.Config;
using DualEngineRegimeBot.Core.Sizing;

namespace DualEngineRegimeBot.Tests
{
    public class SizingTests
    {
        [Fact]
        public void VolMult_ClampsCorrectly()
        {
            var config = new SizingConfig { TargetNATR = 0.30, VolMultMin = 0.5, VolMultMax = 2.0 };
            var sizer = new InverseVolSizer();
            
            // Low vol → high mult (clamped at 2.0)
            double volMult1 = sizer.ComputeVolMult(0.10, config);
            Assert.Equal(2.0, volMult1);
            
            // High vol → low mult (clamped at 0.5)
            double volMult2 = sizer.ComputeVolMult(1.0, config);
            Assert.Equal(0.5, volMult2);
            
            // Normal vol → proportional
            double volMult3 = sizer.ComputeVolMult(0.30, config);
            Assert.Equal(1.0, volMult3, 2);
        }
        
        [Fact]
        public void EffRiskPct_AppliesAllMultipliers()
        {
            var sizer = new InverseVolSizer();
            
            double effRisk = sizer.ComputeEffRiskPct(
                baseRiskPct: 0.50,
                volMult: 2.0,
                regimeConf: 0.8,
                strategyConf: 0.9,
                tfDamp: 0.7);
            
            double expected = 0.50 * 2.0 * 0.8 * 0.9 * 0.7;
            Assert.Equal(expected, effRisk, 3);
        }
        
        [Fact]
        public void ComputeUnits_RespectsMinMax()
        {
            var config = new SizingConfig 
            { 
                MinVolume = 0.01, 
                MaxVolume = 5.0,
                MarginBufferX = 2.0
            };
            
            var context = new MarketContext
            {
                AccountEquity = 10000,
                FreeMargin = 5000,
                TickSize = 0.01,
                TickValue = 1.0,
                PipSize = 0.01,
                Bid = 1999.5,
                Ask = 2000.5
            };
            
            var sizer = new InverseVolSizer();
            
            // Very small risk → should clamp to MinVolume
            double units1 = sizer.ComputeUnits(0.01, 100, context, config);
            Assert.True(units1 >= config.MinVolume);
            
            // Very large risk → should clamp to MaxVolume
            double units2 = sizer.ComputeUnits(10.0, 10, context, config);
            Assert.True(units2 <= config.MaxVolume);
        }
    }
}

