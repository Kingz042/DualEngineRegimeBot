using System;
using Xunit;
using DualEngineRegimeBot.Core;
using DualEngineRegimeBot.Core.Config;
using DualEngineRegimeBot.Core.NewsGuard;

namespace DualEngineRegimeBot.Tests
{
    public class NewsGuardTests
    {
        [Fact]
        public void NewsGuard_ShouldBeNormal_WhenDisabled()
        {
            var config = new NewsGuardConfig { Enabled = false };
            var guard = new DualEngineRegimeBot.Core.NewsGuard.NewsGuard(config);
            
            guard.Update(DateTime.UtcNow, 5.0, 10.0, 0.5); // Extreme values
            
            Assert.Equal(NewsGuardPhase.Normal, guard.GetPhase());
            Assert.True(guard.AllowEntries());
            Assert.True(guard.AllowHedges());
        }
        
        [Fact]
        public void NewsGuard_ShouldDetectSpike_OnLargeSMSDelta()
        {
            var config = new NewsGuardConfig
            {
                Enabled = true,
                SMSDeltaThreshold = 2.0,
                SpreadBlowoutMultiplier = 3.0,
                BlockPhaseMinutes = 2
            };
            
            var guard = new DualEngineRegimeBot.Core.NewsGuard.NewsGuard(config);
            var now = DateTime.UtcNow;
            
            // Build SMS history (60+ samples at 1/sec = 1 min)
            for (int i = 0; i < 60; i++)
            {
                guard.Update(now.AddSeconds(i), 1.0, 0.3, 0.3);
            }
            
            // Spike: SMS jumps from 1.0 to 5.0 (large delta)
            guard.Update(now.AddSeconds(61), 5.0, 0.3, 0.3);
            
            Assert.Equal(NewsGuardPhase.Block, guard.GetPhase());
            Assert.False(guard.AllowEntries());
            Assert.False(guard.AllowHedges());
            Assert.True(guard.AllowUnwinds());
        }
        
        [Fact]
        public void NewsGuard_ShouldDetectSpike_OnSpreadBlowout()
        {
            var config = new NewsGuardConfig
            {
                Enabled = true,
                SMSDeltaThreshold = 2.0,
                SpreadBlowoutMultiplier = 3.0,
                BlockPhaseMinutes = 2
            };
            
            var guard = new DualEngineRegimeBot.Core.NewsGuard.NewsGuard(config);
            var now = DateTime.UtcNow;
            
            // Normal spread history
            for (int i = 0; i < 60; i++)
            {
                guard.Update(now.AddSeconds(i), 1.0, 0.3, 0.3);
            }
            
            // Spread blows out: 1.5 > 3× median (0.3 × 3 = 0.9)
            guard.Update(now.AddSeconds(61), 1.0, 1.5, 0.3);
            
            Assert.Equal(NewsGuardPhase.Block, guard.GetPhase());
            Assert.False(guard.AllowEntries());
            Assert.False(guard.AllowHedges());
        }
        
        [Fact]
        public void NewsGuard_ShouldProgressThroughPhases()
        {
            var config = new NewsGuardConfig
            {
                Enabled = true,
                SMSDeltaThreshold = 2.0,
                SpreadBlowoutMultiplier = 3.0,
                BlockPhaseMinutes = 2,
                UnwindOnlyPhaseMinutes = 3,
                RestrictedPhaseMinutes = 10
            };
            
            var guard = new DualEngineRegimeBot.Core.NewsGuard.NewsGuard(config);
            var now = DateTime.UtcNow;
            
            // Build history and trigger spike
            for (int i = 0; i < 60; i++)
            {
                guard.Update(now.AddSeconds(i), 1.0, 0.3, 0.3);
            }
            
            DateTime spikeTime = now.AddSeconds(61);
            guard.Update(spikeTime, 5.0, 0.3, 0.3); // Spike at t=61s
            
            // Phase 1: Block (0-2 min after spike)
            Assert.Equal(NewsGuardPhase.Block, guard.GetPhase());
            guard.Update(spikeTime.AddMinutes(1), 1.0, 0.3, 0.3); // t=spike+1min
            Assert.Equal(NewsGuardPhase.Block, guard.GetPhase());
            
            // Phase 2: UnwindOnly (3-5 min after spike)
            guard.Update(spikeTime.AddMinutes(3), 1.0, 0.3, 0.3); // t=spike+3min
            Assert.Equal(NewsGuardPhase.UnwindOnly, guard.GetPhase());
            Assert.False(guard.AllowEntries());
            Assert.False(guard.AllowHedges());
            Assert.True(guard.AllowUnwinds());
            
            // Phase 3: Restricted (6-15 min after spike)
            guard.Update(spikeTime.AddMinutes(7), 1.0, 0.3, 0.3); // t=spike+7min
            Assert.Equal(NewsGuardPhase.Restricted, guard.GetPhase());
            Assert.False(guard.AllowEntries());
            Assert.True(guard.AllowHedges()); // Allowed with 2× Hmult
            Assert.Equal(2.0, guard.GetHmultMultiplier());
            
            // Phase 4: Normal (>15 min after spike)
            guard.Update(spikeTime.AddMinutes(16), 1.0, 0.3, 0.3); // t=spike+16min
            Assert.Equal(NewsGuardPhase.Normal, guard.GetPhase());
            Assert.True(guard.AllowEntries());
            Assert.True(guard.AllowHedges());
            Assert.Equal(1.0, guard.GetHmultMultiplier());
        }
        
        [Fact]
        public void NewsGuard_ShouldCalculateSpikeStrength()
        {
            var config = new NewsGuardConfig
            {
                Enabled = true,
                SMSDeltaThreshold = 2.0,
                SpreadBlowoutMultiplier = 3.0,
                BlockPhaseMinutes = 2
            };
            
            var guard = new DualEngineRegimeBot.Core.NewsGuard.NewsGuard(config);
            var now = DateTime.UtcNow;
            
            // Build history
            for (int i = 0; i < 120; i++) // 2 min of data
            {
                guard.Update(now.AddSeconds(i), 1.0, 0.3, 0.3);
            }
            
            // Large spike
            guard.Update(now.AddSeconds(121), 6.0, 1.2, 0.3);
            
            double strength = guard.GetSpikeStrength();
            
            Assert.True(strength > 0.5); // Should be significant
            Assert.True(strength <= 1.0); // Clamped to 1.0
        }
        
        [Fact]
        public void NewsGuard_ShouldAllowManualReset()
        {
            var config = new NewsGuardConfig
            {
                Enabled = true,
                SMSDeltaThreshold = 2.0,
                BlockPhaseMinutes = 2
            };
            
            var guard = new DualEngineRegimeBot.Core.NewsGuard.NewsGuard(config);
            var now = DateTime.UtcNow;
            
            // Build history and trigger spike
            for (int i = 0; i < 60; i++)
            {
                guard.Update(now.AddSeconds(i), 1.0, 0.3, 0.3);
            }
            
            guard.Update(now.AddSeconds(61), 5.0, 0.3, 0.3);
            Assert.Equal(NewsGuardPhase.Block, guard.GetPhase());
            
            // Manual reset
            guard.ForceResetToNormal();
            
            Assert.Equal(NewsGuardPhase.Normal, guard.GetPhase());
            Assert.True(guard.AllowEntries());
            Assert.True(guard.AllowHedges());
            Assert.Equal(0.0, guard.GetSpikeStrength());
        }
        
        [Fact]
        public void NewsGuard_ShouldTrackMinutesInPhase()
        {
            var config = new NewsGuardConfig
            {
                Enabled = true,
                SMSDeltaThreshold = 2.0,
                BlockPhaseMinutes = 2
            };
            
            var guard = new DualEngineRegimeBot.Core.NewsGuard.NewsGuard(config);
            var now = DateTime.UtcNow;
            
            // Build history and trigger spike
            for (int i = 0; i < 60; i++)
            {
                guard.Update(now.AddSeconds(i), 1.0, 0.3, 0.3);
            }
            
            var spikeTime = now.AddSeconds(61);
            guard.Update(spikeTime, 5.0, 0.3, 0.3);
            
            // Check time in phase
            guard.Update(spikeTime.AddMinutes(1), 1.0, 0.3, 0.3);
            
            double minutes = guard.GetMinutesInPhase(spikeTime.AddMinutes(1));
            Assert.Equal(1.0, minutes, 1); // ~1 minute
        }
    }
}

