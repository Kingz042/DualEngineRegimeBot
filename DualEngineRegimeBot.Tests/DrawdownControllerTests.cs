using System;
using Xunit;
using DualEngineRegimeBot.Core.Risk;
using DualEngineRegimeBot.Core.Config;

namespace DualEngineRegimeBot.Tests
{
    public class DrawdownControllerTests
    {
        [Fact]
        public void Damper_ShouldBe100Percent_WhenDrawdownBelow2Percent()
        {
            var config = new DrawdownScalingConfig
            {
                Enabled = true,
                ThresholdLevels = new[] { 2.0, 5.0, 10.0 },
                DamperValues = new[] { 1.0, 0.7, 0.4, 0.0 }
            };
            
            var survivalConfig = new SurvivalModeConfig { Enabled = false };
            var controller = new DrawdownController(config, survivalConfig, 10000.0);
            
            controller.Update(9850.0, DateTime.UtcNow); // 1.5% DD
            
            double damper = controller.GetDamper(9850.0);
            
            Assert.Equal(1.0, damper); // 100%
            Assert.False(controller.IsSurvivalModeActive());
        }
        
        [Fact]
        public void Damper_ShouldBe70Percent_WhenDrawdownBetween2And5Percent()
        {
            var config = new DrawdownScalingConfig
            {
                Enabled = true,
                ThresholdLevels = new[] { 2.0, 5.0, 10.0 },
                DamperValues = new[] { 1.0, 0.7, 0.4, 0.0 }
            };
            
            var survivalConfig = new SurvivalModeConfig { Enabled = false };
            var controller = new DrawdownController(config, survivalConfig, 10000.0);
            
            controller.Update(9700.0, DateTime.UtcNow); // 3% DD
            
            double damper = controller.GetDamper(9700.0);
            
            Assert.Equal(0.7, damper); // 70%
        }
        
        [Fact]
        public void Damper_ShouldBe40Percent_WhenDrawdownBetween5And10Percent()
        {
            var config = new DrawdownScalingConfig
            {
                Enabled = true,
                ThresholdLevels = new[] { 2.0, 5.0, 10.0 },
                DamperValues = new[] { 1.0, 0.7, 0.4, 0.0 }
            };
            
            var survivalConfig = new SurvivalModeConfig { Enabled = false };
            var controller = new DrawdownController(config, survivalConfig, 10000.0);
            
            controller.Update(9300.0, DateTime.UtcNow); // 7% DD
            
            double damper = controller.GetDamper(9300.0);
            
            Assert.Equal(0.4, damper); // 40%
        }
        
        [Fact]
        public void Damper_ShouldBe0Percent_WhenDrawdownAbove10Percent()
        {
            var config = new DrawdownScalingConfig
            {
                Enabled = true,
                ThresholdLevels = new[] { 2.0, 5.0, 10.0 },
                DamperValues = new[] { 1.0, 0.7, 0.4, 0.0 }
            };
            
            var survivalConfig = new SurvivalModeConfig { Enabled = false };
            var controller = new DrawdownController(config, survivalConfig, 10000.0);
            
            controller.Update(8900.0, DateTime.UtcNow); // 11% DD
            
            double damper = controller.GetDamper(8900.0);
            
            Assert.Equal(0.0, damper); // Locked
            Assert.False(controller.IsSurvivalModeActive());
        }
        
        [Fact]
        public void SurvivalMode_ShouldActivate_WhenDrawdownAbove10Percent()
        {
            var config = new DrawdownScalingConfig
            {
                Enabled = true,
                ThresholdLevels = new[] { 2.0, 5.0, 10.0 },
                DamperValues = new[] { 1.0, 0.7, 0.4, 0.0 }
            };
            
            var survivalConfig = new SurvivalModeConfig
            {
                Enabled = true,
                RiskCap = 0.10,
                TriggerThresholdPct = 10.0
            };
            
            var controller = new DrawdownController(config, survivalConfig, 10000.0);
            
            controller.Update(8900.0, DateTime.UtcNow); // 11% DD
            
            double damper = controller.GetDamper(8900.0);
            
            Assert.Equal(0.10, damper); // 10% risk cap
            Assert.True(controller.IsSurvivalModeActive());
        }
        
        [Fact]
        public void HybridPeak_ShouldUse_MaxOfAllTimeHighAnd95PercentRolling()
        {
            var config = new DrawdownScalingConfig
            {
                Enabled = true,
                UseHybridPeak = true,
                RollingPeakWindowDays = 30,
                RollingPeakMultiplier = 0.95,
                ThresholdLevels = new[] { 2.0, 5.0, 10.0 },
                DamperValues = new[] { 1.0, 0.7, 0.4, 0.0 }
            };
            
            var survivalConfig = new SurvivalModeConfig { Enabled = false };
            var controller = new DrawdownController(config, survivalConfig, 10000.0);
            
            // Scenario: All-time high = 10000, then drawdown, then recovery to 9800
            controller.Update(9000.0, DateTime.UtcNow.AddDays(-10)); // DD
            controller.Update(9800.0, DateTime.UtcNow); // Partial recovery
            
            double peak = controller.GetPeakReference();
            
            // Hybrid: max(10000, 0.95 × 9800) = max(10000, 9310) = 10000
            Assert.Equal(10000.0, peak);
            
            // Test drawdown calculation from hybrid peak
            double ddPct = controller.GetDrawdownPct(9800.0);
            Assert.Equal(2.0, ddPct, 1); // (10000 - 9800) / 10000 = 2%
        }
        
        [Fact]
        public void HybridPeak_ShouldUseRolling_WhenHigherThanAllTimeHigh()
        {
            var config = new DrawdownScalingConfig
            {
                Enabled = true,
                UseHybridPeak = true,
                RollingPeakWindowDays = 30,
                RollingPeakMultiplier = 0.95,
                ThresholdLevels = new[] { 2.0, 5.0, 10.0 },
                DamperValues = new[] { 1.0, 0.7, 0.4, 0.0 }
            };
            
            var survivalConfig = new SurvivalModeConfig { Enabled = false };
            var controller = new DrawdownController(config, survivalConfig, 8000.0);
            
            // Scenario: All-time high = 8000, but rolling 30d high = 11000
            controller.Update(11000.0, DateTime.UtcNow.AddDays(-5)); // Recent high
            controller.Update(10500.0, DateTime.UtcNow); // Current
            
            double peak = controller.GetPeakReference();
            
            // Hybrid: max(11000, 0.95 × 11000) = max(11000, 10450) = 11000
            // (Note: All-time high gets updated to 11000 on update)
            Assert.True(peak >= 10450); // At least 0.95 × 11000
        }
        
        [Fact]
        public void DrawdownStats_ShouldProvide_ComprehensiveSnapshot()
        {
            var config = new DrawdownScalingConfig
            {
                Enabled = true,
                ThresholdLevels = new[] { 2.0, 5.0, 10.0 },
                DamperValues = new[] { 1.0, 0.7, 0.4, 0.0 },
                UseHybridPeak = true,
                RollingPeakWindowDays = 30,
                RollingPeakMultiplier = 0.95
            };
            
            var survivalConfig = new SurvivalModeConfig { Enabled = false };
            var controller = new DrawdownController(config, survivalConfig, 10000.0);
            
            controller.Update(9300.0, DateTime.UtcNow); // 7% DD
            
            var stats = controller.GetStats(9300.0);
            
            Assert.Equal(7.0, stats.CurrentDrawdownPct, 1);
            Assert.Equal(0.4, stats.DamperMultiplier); // Severe (40%)
            Assert.Equal(10000.0, stats.AllTimeHigh);
            Assert.Equal("Severe (40%)", stats.DamperLevel);
            Assert.False(stats.SurvivalModeActive);
        }
        
        [Fact]
        public void Damper_ShouldReturn100Percent_WhenScalingDisabled()
        {
            var config = new DrawdownScalingConfig
            {
                Enabled = false, // Disabled
                ThresholdLevels = new[] { 2.0, 5.0, 10.0 },
                DamperValues = new[] { 1.0, 0.7, 0.4, 0.0 }
            };
            
            var survivalConfig = new SurvivalModeConfig { Enabled = false };
            var controller = new DrawdownController(config, survivalConfig, 10000.0);
            
            controller.Update(8000.0, DateTime.UtcNow); // 20% DD (extreme)
            
            double damper = controller.GetDamper(8000.0);
            
            Assert.Equal(1.0, damper); // No scaling when disabled
        }
    }
}

