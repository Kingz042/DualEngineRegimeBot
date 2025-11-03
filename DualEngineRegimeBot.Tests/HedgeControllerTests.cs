using System;
using Xunit;
using DualEngineRegimeBot.Core;
using DualEngineRegimeBot.Core.Hedging;
using DualEngineRegimeBot.Core.Config;
using DualEngineRegimeBot.Core.NewsGuard;

namespace DualEngineRegimeBot.Tests
{
    public class HedgeControllerTests
    {
        private readonly HedgeConfig _defaultConfig;
        private readonly HedgeController _controller;
        private readonly NewsGuardConfig _newsGuardConfig;
        private readonly DualEngineRegimeBot.Core.NewsGuard.NewsGuard _newsGuard;
        
        public HedgeControllerTests()
        {
            _defaultConfig = new HedgeConfig
            {
                Enabled = true,
                TriggerMultiplier = 1.2,
                VolumeCap = 1.0,
                CooldownMs = 2000,
                SpreadGuardMultiplier = 1.5,
                RecoveryTargetMultiplier = 0.6,
                MicroRevivalSMS = 1.1,
                MacroAlignmentConfidence = 0.55,
                TimeDecayMinutes = 15,
                TimeDecayUnwindFraction = 0.5,
                HedgeStopMultiplier = 0.8,
                MarginBufferMultiplier = 2.0
            };
            
            _controller = new HedgeController(_defaultConfig);
            
            _newsGuardConfig = new NewsGuardConfig { Enabled = false };
            _newsGuard = new DualEngineRegimeBot.Core.NewsGuard.NewsGuard(_newsGuardConfig);
        }
        
        [Fact]
        public void HedgeOpen_ShouldBlock_WhenDisabled()
        {
            var config = new HedgeConfig { Enabled = false };
            var controller = new HedgeController(config);
            
            var context = CreateDefaultContext();
            var decision = controller.EvaluateHedgeOpen(context, _newsGuard, 0.5);
            
            Assert.Equal(HedgeAction.None, decision.Action);
            Assert.Contains("disabled", decision.Reason.ToLower());
        }
        
        [Fact]
        public void HedgeOpen_ShouldBlock_WhenCooldownActive()
        {
            var context = CreateDefaultContext();
            
            // Record a hedge to activate cooldown
            _controller.RecordHedgeOpen(TradeSide.Short, 1.0, 1950.0, DateTime.UtcNow);
            _controller.RecordHedgeClose(10.0, DualEngineRegimeBot.Core.Hedging.ExitReason.RecoveryTarget, DateTime.UtcNow);
            
            // Try to open immediately (within 2s cooldown)
            context.CurrentTime = DateTime.UtcNow.AddSeconds(1);
            var decision = _controller.EvaluateHedgeOpen(context, _newsGuard, 0.5);
            
            Assert.Equal(HedgeAction.None, decision.Action);
            Assert.Contains("cooldown", decision.Reason.ToLower());
        }
        
        [Fact]
        public void HedgeOpen_ShouldBlock_WhenSpreadTooWide()
        {
            var context = CreateDefaultContext();
            context.CurrentSpread = 2.0; // 2.0 > 1.5 × 0.5 = 0.75
            
            var decision = _controller.EvaluateHedgeOpen(context, _newsGuard, 0.5);
            
            Assert.Equal(HedgeAction.None, decision.Action);
            Assert.Contains("spread", decision.Reason.ToLower());
        }
        
        [Fact]
        public void HedgeOpen_ShouldBlock_WhenAdverseMoveTooSmall()
        {
            var context = CreateDefaultContext();
            context.CurrentPrice = 1998.0; // Only 2.0 adverse, need 1.2 × 10 = 12
            
            var decision = _controller.EvaluateHedgeOpen(context, _newsGuard, 0.5);
            
            Assert.Equal(HedgeAction.None, decision.Action);
            Assert.Contains("adverse move", decision.Reason.ToLower());
        }
        
        [Fact]
        public void HedgeOpen_ShouldAllow_WhenAllConditionsMet()
        {
            var context = CreateDefaultContext();
            context.CurrentPrice = 1985.0; // 15 adverse from 2000 avg, exceeds 1.2 × 10 = 12
            
            var decision = _controller.EvaluateHedgeOpen(context, _newsGuard, 0.5);
            
            Assert.Equal(HedgeAction.Open, decision.Action);
            Assert.Equal(TradeSide.Short, decision.Side);
            Assert.Equal(1.0, decision.Volume);
        }
        
        [Fact]
        public void HedgeUnwind_ShouldTrigger_OnRecoveryTarget()
        {
            // Setup: Open hedge
            _controller.RecordHedgeOpen(TradeSide.Short, 1.0, 1985.0, DateTime.UtcNow);
            
            // Price recovers by 0.6 × ATR = 6.0 from hedge open
            var context = CreateDefaultContext();
            context.CurrentPrice = 1991.0; // 1985 + 6 = 1991 (recovery for short hedge)
            
            var regime = new RegimeSnapshot
            {
                Direction = RegimeDirection.Bull,
                VolState = RegimeVolState.LowVol,
                Confidence = 0.6,
                Timestamp = DateTime.UtcNow
            };
            
            var decision = _controller.EvaluateHedgeExit(context, regime, 0.8, 1995.0);
            
            Assert.Equal(HedgeAction.Unwind, decision.Action);
            Assert.Contains("recovery", decision.Reason.ToLower());
            Assert.Equal(1.0, decision.UnwindFraction);
        }
        
        [Fact]
        public void HedgeForcedExit_ShouldTrigger_OnHedgeStopLoss()
        {
            // Setup: Open short hedge at 1985
            _controller.RecordHedgeOpen(TradeSide.Short, 1.0, 1985.0, DateTime.UtcNow);
            
            // Price moves against hedge by 0.8 × ATR = 8.0
            var context = CreateDefaultContext();
            context.CurrentPrice = 1993.0; // 1985 + 8 = 1993 (adverse for short)
            
            var regime = new RegimeSnapshot
            {
                Direction = RegimeDirection.Bull,
                VolState = RegimeVolState.LowVol,
                Confidence = 0.6,
                Timestamp = DateTime.UtcNow
            };
            
            var decision = _controller.EvaluateHedgeExit(context, regime, 0.8, 1995.0);
            
            Assert.Equal(HedgeAction.ForceExit, decision.Action);
            Assert.Contains("sl", decision.Reason.ToLower());
        }
        
        [Fact]
        public void HedgeForcedExit_ShouldTrigger_WhenParentClosed()
        {
            // Setup: Open hedge
            _controller.RecordHedgeOpen(TradeSide.Short, 1.0, 1985.0, DateTime.UtcNow);
            
            // Parent position volume = 0 (closed)
            var context = CreateDefaultContext();
            context.PHVolume = 0.0;
            
            var regime = new RegimeSnapshot
            {
                Direction = RegimeDirection.Bull,
                VolState = RegimeVolState.LowVol,
                Confidence = 0.6,
                Timestamp = DateTime.UtcNow
            };
            
            var decision = _controller.EvaluateHedgeExit(context, regime, 0.8, 1995.0);
            
            Assert.Equal(HedgeAction.ForceExit, decision.Action);
            Assert.Contains("parent", decision.Reason.ToLower());
        }
        
        [Fact]
        public void HedgeKPIs_ShouldTrack_WinRateAndDuration()
        {
            // Record several hedge outcomes
            _controller.RecordHedgeOpen(TradeSide.Short, 1.0, 1985.0, DateTime.UtcNow);
            _controller.RecordHedgeClose(50.0, DualEngineRegimeBot.Core.Hedging.ExitReason.RecoveryTarget, DateTime.UtcNow.AddMinutes(5)); // Win
            
            _controller.RecordHedgeOpen(TradeSide.Long, 1.0, 2015.0, DateTime.UtcNow.AddMinutes(10));
            _controller.RecordHedgeClose(-30.0, DualEngineRegimeBot.Core.Hedging.ExitReason.StopLoss, DateTime.UtcNow.AddMinutes(12)); // Loss
            
            _controller.RecordHedgeOpen(TradeSide.Short, 1.0, 1990.0, DateTime.UtcNow.AddMinutes(20));
            _controller.RecordHedgeClose(20.0, DualEngineRegimeBot.Core.Hedging.ExitReason.MicroRevival, DateTime.UtcNow.AddMinutes(27)); // Win
            
            var kpis = _controller.GetKPIs();
            
            Assert.Equal(3, kpis.TotalHedges);
            Assert.Equal(2.0 / 3.0, kpis.WinRate, 2); // 66.67%
            Assert.True(kpis.AvgDurationMinutes > 4 && kpis.AvgDurationMinutes < 7); // ~5.33 min avg
            Assert.Equal(40.0, kpis.TotalPnL); // 50 - 30 + 20
        }
        
        private HedgeEvaluationContext CreateDefaultContext()
        {
            return new HedgeEvaluationContext
            {
                CurrentTime = DateTime.UtcNow.AddSeconds(10), // After any cooldown
                CurrentPrice = 1990.0,
                CurrentSpread = 0.3,
                AtrM1 = 10.0,
                PHSide = TradeSide.Long,
                PHVolume = 1.0,
                PHAvgPrice = 2000.0,
                FreeMargin = 100000.0,
                UsedMargin = 10000.0
            };
        }
    }
}

