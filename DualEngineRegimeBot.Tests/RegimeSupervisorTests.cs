using System;
using Xunit;
using DualEngineRegimeBot.Core;
using DualEngineRegimeBot.Core.Macro;

namespace DualEngineRegimeBot.Tests
{
    public class RegimeSupervisorTests
    {
        private readonly RegimeSupervisor _supervisor;
        
        public RegimeSupervisorTests()
        {
            _supervisor = new RegimeSupervisor();
        }
        
        [Fact]
        public void Case1_Aligned_ShouldUpdateTrail_OnConfidenceBoost()
        {
            // Setup aligned regime
            var regime = new RegimeSnapshot
            {
                Direction = RegimeDirection.Bull,
                VolState = RegimeVolState.LowVol,
                Confidence = 0.75, // High confidence
                Timestamp = DateTime.UtcNow
            };
            
            _supervisor.Update(regime, DateTime.UtcNow);
            
            // Position context: long, profitable (2.5 ATR)
            var position = new PositionContext
            {
                Side = TradeSide.Long,
                EntryPrice = 1900.0,
                CurrentPrice = 1925.0,
                UnrealizedPnL = 250.0,
                AtrM1 = 10.0,
                CurrentTrailDistance = 15.0,
                BarsOpen = 10
            };
            
            // Boost confidence to trigger trail update
            var newRegime = new RegimeSnapshot
            {
                Direction = RegimeDirection.Bull,
                VolState = RegimeVolState.LowVol,
                Confidence = 0.90, // +0.15 boost
                Timestamp = DateTime.UtcNow.AddMinutes(1)
            };
            
            _supervisor.Update(newRegime, DateTime.UtcNow.AddMinutes(1));
            
            var decision = _supervisor.EvaluatePositionAction(newRegime, position, 1.2);
            
            Assert.Equal(RegimeAction.UpdateTrail, decision.Action);
            Assert.Equal(20.0, decision.TrailDistance); // 2.0 × 10 ATR (UPL ≥ 2.0)
            Assert.Contains("aligned", decision.Reason.ToLower());
        }
        
        [Fact]
        public void Case2_OpposedSmallLoss_ShouldFlattenImmediately()
        {
            // Setup bull regime, then flip to bear
            var regime = new RegimeSnapshot
            {
                Direction = RegimeDirection.Bull,
                VolState = RegimeVolState.LowVol,
                Confidence = 0.65,
                Timestamp = DateTime.UtcNow
            };
            
            _supervisor.Update(regime, DateTime.UtcNow);
            
            // Flip to bear
            var opposedRegime = new RegimeSnapshot
            {
                Direction = RegimeDirection.Bear,
                VolState = RegimeVolState.LowVol,
                Confidence = 0.65,
                Timestamp = DateTime.UtcNow.AddMinutes(15)
            };
            
            _supervisor.Update(opposedRegime, DateTime.UtcNow.AddMinutes(15));
            
            // Position: long with small loss (UPL = 0.3 ATR)
            var position = new PositionContext
            {
                Side = TradeSide.Long,
                EntryPrice = 2000.0,
                CurrentPrice = 1997.0,
                UnrealizedPnL = -30.0,
                AtrM1 = 10.0,
                CurrentTrailDistance = 20.0,
                BarsOpen = 5
            };
            
            var decision = _supervisor.EvaluatePositionAction(opposedRegime, position, 0.8);
            
            Assert.Equal(RegimeAction.FlattenNow, decision.Action);
            Assert.Equal("RegimeConflictLoss", decision.SemanticTag);
            Assert.Contains("conflict", decision.Reason.ToLower());
        }
        
        [Fact]
        public void Case3_OpposedModerate_ShouldScaleOut50Percent()
        {
            // Setup bull regime, then flip to bear
            var regime = new RegimeSnapshot
            {
                Direction = RegimeDirection.Bull,
                VolState = RegimeVolState.LowVol,
                Confidence = 0.65,
                Timestamp = DateTime.UtcNow
            };
            
            _supervisor.Update(regime, DateTime.UtcNow);
            
            var opposedRegime = new RegimeSnapshot
            {
                Direction = RegimeDirection.Bear,
                VolState = RegimeVolState.LowVol,
                Confidence = 0.65,
                Timestamp = DateTime.UtcNow.AddMinutes(15)
            };
            
            _supervisor.Update(opposedRegime, DateTime.UtcNow.AddMinutes(15));
            
            // Position: long with moderate profit (UPL = 1.0 ATR)
            var position = new PositionContext
            {
                Side = TradeSide.Long,
                EntryPrice = 1990.0,
                CurrentPrice = 2000.0,
                UnrealizedPnL = 100.0,
                AtrM1 = 10.0,
                CurrentTrailDistance = 20.0,
                BarsOpen = 8
            };
            
            var decision = _supervisor.EvaluatePositionAction(opposedRegime, position, 1.2);
            
            Assert.Equal(RegimeAction.ScaleOut, decision.Action);
            Assert.Equal(0.5, decision.ScaleOutFraction);
            Assert.Equal(3, decision.TimeStopMinutes); // SMS > 1.0
            Assert.Equal("RegimeConflictScaleOut", decision.SemanticTag);
        }
        
        [Fact]
        public void Case4_OpposedRunner_ShouldTrail_AdaptiveByAge()
        {
            // Setup bull regime
            var regime = new RegimeSnapshot
            {
                Direction = RegimeDirection.Bull,
                VolState = RegimeVolState.LowVol,
                Confidence = 0.65,
                Timestamp = DateTime.UtcNow
            };
            
            _supervisor.Update(regime, DateTime.UtcNow);
            
            // Flip to bear (new regime)
            var opposedRegime = new RegimeSnapshot
            {
                Direction = RegimeDirection.Bear,
                VolState = RegimeVolState.LowVol,
                Confidence = 0.65,
                Timestamp = DateTime.UtcNow.AddMinutes(15)
            };
            
            _supervisor.Update(opposedRegime, DateTime.UtcNow.AddMinutes(15));
            
            // Position: long with big profit (UPL = 2.0 ATR)
            var position = new PositionContext
            {
                Side = TradeSide.Long,
                EntryPrice = 1980.0,
                CurrentPrice = 2000.0,
                UnrealizedPnL = 200.0,
                AtrM1 = 10.0,
                CurrentTrailDistance = 15.0,
                BarsOpen = 20
            };
            
            // Test: New regime (<2 bars) → trail 1.5× ATR
            var decision = _supervisor.EvaluatePositionAction(opposedRegime, position, 0.9);
            
            Assert.Equal(RegimeAction.UpdateTrail, decision.Action);
            Assert.Equal(15.0, decision.TrailDistance); // 1.5 × 10
            Assert.Equal("RegimeProtectedRunner", decision.SemanticTag);
            
            // Advance regime age to 3 bars
            for (int i = 0; i < 3; i++)
            {
                _supervisor.Update(opposedRegime, DateTime.UtcNow.AddMinutes(15 + i));
            }
            
            // Test: Established regime (≥2 bars) → trail 1.3× ATR
            decision = _supervisor.EvaluatePositionAction(opposedRegime, position, 0.9);
            
            Assert.Equal(RegimeAction.UpdateTrail, decision.Action);
            Assert.Equal(13.0, decision.TrailDistance); // 1.3 × 10
            
            // Advance regime age to 5 bars
            for (int i = 3; i < 5; i++)
            {
                _supervisor.Update(opposedRegime, DateTime.UtcNow.AddMinutes(15 + i));
            }
            
            // Test: Old regime (≥4 bars) → trail 1.0× ATR
            decision = _supervisor.EvaluatePositionAction(opposedRegime, position, 0.9);
            
            Assert.Equal(RegimeAction.UpdateTrail, decision.Action);
            Assert.Equal(10.0, decision.TrailDistance); // 1.0 × 10
        }
        
        [Fact]
        public void Case5_Ambiguous_ShouldTightenTrail_AndSuppressEntries()
        {
            // Setup ambiguous regime (low confidence)
            var regime = new RegimeSnapshot
            {
                Direction = RegimeDirection.Neutral,
                VolState = RegimeVolState.LowVol,
                Confidence = 0.45, // <0.5 = ambiguous
                Timestamp = DateTime.UtcNow
            };
            
            _supervisor.Update(regime, DateTime.UtcNow);
            
            // Position: any direction, any UPL
            var position = new PositionContext
            {
                Side = TradeSide.Long,
                EntryPrice = 2000.0,
                CurrentPrice = 2005.0,
                UnrealizedPnL = 50.0,
                AtrM1 = 10.0,
                CurrentTrailDistance = 20.0,
                BarsOpen = 5
            };
            
            var decision = _supervisor.EvaluatePositionAction(regime, position, 1.0);
            
            Assert.Equal(RegimeAction.UpdateTrail, decision.Action);
            Assert.Equal(18.0, decision.TrailDistance); // 20 × 0.9 (tighten 10%)
            Assert.True(decision.SuppressNewEntries);
            Assert.Contains("ambiguous", decision.Reason.ToLower());
        }
        
        [Fact]
        public void Case5_ExtendedAmbiguity_ShouldFlatten_AndEnableDiagnostic()
        {
            // Setup ambiguous regime
            var regime = new RegimeSnapshot
            {
                Direction = RegimeDirection.Neutral,
                VolState = RegimeVolState.LowVol,
                Confidence = 0.45,
                Timestamp = DateTime.UtcNow
            };
            
            _supervisor.Update(regime, DateTime.UtcNow);
            
            // Advance 7 bars (>6 threshold)
            for (int i = 0; i < 7; i++)
            {
                _supervisor.Update(regime, DateTime.UtcNow.AddMinutes(15 * i));
            }
            
            var position = new PositionContext
            {
                Side = TradeSide.Long,
                EntryPrice = 2000.0,
                CurrentPrice = 2005.0,
                UnrealizedPnL = 50.0,
                AtrM1 = 10.0,
                CurrentTrailDistance = 20.0,
                BarsOpen = 10
            };
            
            var decision = _supervisor.EvaluatePositionAction(regime, position, 1.0);
            
            Assert.Equal(RegimeAction.FlattenNow, decision.Action);
            Assert.Equal("RegimeAmbiguityExit", decision.SemanticTag);
            Assert.True(decision.EnableDiagnosticMode);
            Assert.Contains("extended", decision.Reason.ToLower());
        }
    }
}

