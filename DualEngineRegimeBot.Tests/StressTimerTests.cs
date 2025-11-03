using System;
using Xunit;
using DualEngineRegimeBot.Core.Risk;

namespace DualEngineRegimeBot.Tests
{
    public class StressTimerTests
    {
        private readonly StressTimer _stressTimer;
        
        public StressTimerTests()
        {
            _stressTimer = new StressTimer();
        }
        
        [Fact]
        public void StressTimer_ShouldNotTrigger_WhenOnlyOneConditionMet()
        {
            string posId = "pos1";
            
            // Only condition 1 met (underwater ≥2 bars)
            var context = new StressContext
            {
                Timestamp = DateTime.UtcNow,
                IsUnderwater = true,
                SMS = 0.8, // Above 0.4
                RegimeConfidence = 0.60 // Above 0.50
            };
            
            _stressTimer.Update(posId, context);
            _stressTimer.Update(posId, context); // Bar 2
            
            Assert.False(_stressTimer.ShouldExit(posId));
            
            var state = _stressTimer.GetState(posId);
            Assert.NotNull(state);
            Assert.Equal(2, state.UnderwaterBars);
            Assert.Equal(0, state.GraceCounter);
        }
        
        [Fact]
        public void StressTimer_ShouldNotTrigger_WhenTwoConditionsMet()
        {
            string posId = "pos2";
            
            // Conditions 1 & 2 met, but not 3
            var context = new StressContext
            {
                Timestamp = DateTime.UtcNow,
                IsUnderwater = true,
                SMS = 0.3, // Below 0.4
                RegimeConfidence = 0.60 // Above 0.50
            };
            
            _stressTimer.Update(posId, context);
            _stressTimer.Update(posId, context); // Bar 2
            
            Assert.False(_stressTimer.ShouldExit(posId));
            
            var state = _stressTimer.GetState(posId);
            Assert.Equal(2, state.UnderwaterBars);
            Assert.Equal(0, state.GraceCounter);
            Assert.True(state.LastCheck.Condition1Met);
            Assert.True(state.LastCheck.Condition2Met);
            Assert.False(state.LastCheck.Condition3Met);
        }
        
        [Fact]
        public void StressTimer_ShouldTriggerWarning_WhenAllConditionsMet()
        {
            string posId = "pos3";
            
            // All 3 conditions met
            var context = new StressContext
            {
                Timestamp = DateTime.UtcNow,
                IsUnderwater = true,
                SMS = 0.35, // Below 0.4
                RegimeConfidence = 0.45 // Below 0.50
            };
            
            _stressTimer.Update(posId, context);
            _stressTimer.Update(posId, context); // Bar 2: First trigger
            
            var state = _stressTimer.GetState(posId);
            Assert.NotNull(state);
            Assert.Equal(2, state.UnderwaterBars);
            Assert.Equal(1, state.GraceCounter); // Warning
            Assert.Equal("WARNING", state.GetStatus());
            Assert.False(_stressTimer.ShouldExit(posId)); // Not yet exit
        }
        
        [Fact]
        public void StressTimer_ShouldTriggerExit_OnSecondTrigger()
        {
            string posId = "pos4";
            
            // All 3 conditions met
            var context = new StressContext
            {
                Timestamp = DateTime.UtcNow,
                IsUnderwater = true,
                SMS = 0.35,
                RegimeConfidence = 0.45
            };
            
            _stressTimer.Update(posId, context);
            _stressTimer.Update(posId, context); // Bar 2: First trigger (warning)
            
            Assert.Equal("WARNING", _stressTimer.GetState(posId).GetStatus());
            
            _stressTimer.Update(posId, context); // Bar 3: Second trigger (exit)
            
            var state = _stressTimer.GetState(posId);
            Assert.Equal(3, state.UnderwaterBars);
            Assert.Equal(2, state.GraceCounter);
            Assert.Equal("EXIT", state.GetStatus());
            Assert.True(_stressTimer.ShouldExit(posId));
        }
        
        [Fact]
        public void StressTimer_ShouldReset_WhenPositionBecomesProfitable()
        {
            string posId = "pos5";
            
            // Setup: trigger warning
            var underwaterContext = new StressContext
            {
                Timestamp = DateTime.UtcNow,
                IsUnderwater = true,
                SMS = 0.35,
                RegimeConfidence = 0.45
            };
            
            _stressTimer.Update(posId, underwaterContext);
            _stressTimer.Update(posId, underwaterContext); // Warning triggered
            
            Assert.Equal(1, _stressTimer.GetState(posId).GraceCounter);
            
            // Position becomes profitable
            var profitableContext = new StressContext
            {
                Timestamp = DateTime.UtcNow.AddMinutes(1),
                IsUnderwater = false, // Now profitable
                SMS = 0.35,
                RegimeConfidence = 0.45
            };
            
            _stressTimer.Update(posId, profitableContext);
            
            var state = _stressTimer.GetState(posId);
            Assert.Equal(0, state.UnderwaterBars); // Reset
            Assert.Equal(0, state.GraceCounter); // Reset
            Assert.False(_stressTimer.ShouldExit(posId));
        }
        
        [Fact]
        public void StressTimer_ShouldTrackMultiplePositions_Independently()
        {
            string pos1 = "pos1";
            string pos2 = "pos2";
            
            // Pos1: Trigger warning
            var stressContext = new StressContext
            {
                Timestamp = DateTime.UtcNow,
                IsUnderwater = true,
                SMS = 0.35,
                RegimeConfidence = 0.45
            };
            
            _stressTimer.Update(pos1, stressContext);
            _stressTimer.Update(pos1, stressContext); // Warning
            
            // Pos2: Normal (underwater but other conditions not met)
            var normalContext = new StressContext
            {
                Timestamp = DateTime.UtcNow,
                IsUnderwater = true,
                SMS = 0.8, // High SMS
                RegimeConfidence = 0.60 // High confidence
            };
            
            _stressTimer.Update(pos2, normalContext);
            _stressTimer.Update(pos2, normalContext);
            
            // Verify independent tracking
            Assert.Equal("WARNING", _stressTimer.GetState(pos1).GetStatus());
            Assert.Equal("NORMAL", _stressTimer.GetState(pos2).GetStatus());
            
            Assert.False(_stressTimer.ShouldExit(pos1)); // Warning, not exit
            Assert.False(_stressTimer.ShouldExit(pos2)); // Normal
        }
        
        [Fact]
        public void StressTimer_GetPositionsInWarning_ShouldReturnCorrectList()
        {
            // Setup multiple positions
            var stressContext = new StressContext
            {
                Timestamp = DateTime.UtcNow,
                IsUnderwater = true,
                SMS = 0.35,
                RegimeConfidence = 0.45
            };
            
            _stressTimer.Update("pos1", stressContext);
            _stressTimer.Update("pos1", stressContext); // Warning
            
            _stressTimer.Update("pos2", stressContext);
            _stressTimer.Update("pos2", stressContext); // Warning
            _stressTimer.Update("pos2", stressContext); // Exit
            
            var normalContext = new StressContext
            {
                Timestamp = DateTime.UtcNow,
                IsUnderwater = true,
                SMS = 0.8,
                RegimeConfidence = 0.60
            };
            
            _stressTimer.Update("pos3", normalContext);
            _stressTimer.Update("pos3", normalContext); // Normal
            
            var warnings = _stressTimer.GetPositionsInWarning();
            
            Assert.Single(warnings); // Only pos1 in warning
            Assert.Contains("pos1", warnings);
            Assert.DoesNotContain("pos2", warnings); // pos2 is in exit state
            Assert.DoesNotContain("pos3", warnings); // pos3 is normal
        }
    }
}

