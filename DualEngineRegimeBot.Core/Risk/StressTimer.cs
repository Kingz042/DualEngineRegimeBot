using System;
using System.Collections.Generic;

namespace DualEngineRegimeBot.Core.Risk
{
    /// <summary>
    /// Refined stress-timer with 3-condition check and grace counter.
    /// Exit only if ALL conditions true: underwater time ≥2 bars, SMS <0.4, RegimeConf <0.50.
    /// Implements grace period: first trigger warns, second trigger exits.
    /// </summary>
    public class StressTimer
    {
        private readonly Dictionary<string, StressState> _positionStates = new Dictionary<string, StressState>();
        
        /// <summary>
        /// Updates stress tracking for a position.
        /// </summary>
        public void Update(string positionId, StressContext context)
        {
            if (!_positionStates.ContainsKey(positionId))
            {
                _positionStates[positionId] = new StressState
                {
                    PositionId = positionId,
                    UnderwaterBars = 0,
                    GraceCounter = 0,
                    FirstTriggerTime = null
                };
            }
            
            var state = _positionStates[positionId];
            
            // Update underwater bars
            if (context.IsUnderwater)
            {
                state.UnderwaterBars++;
            }
            else
            {
                // Reset if position becomes profitable
                state.UnderwaterBars = 0;
                state.GraceCounter = 0;
                state.FirstTriggerTime = null;
            }
            
            // Check all 3 conditions
            bool condition1 = state.UnderwaterBars >= 2;
            bool condition2 = context.SMS < 0.4;
            bool condition3 = context.RegimeConfidence < 0.50;
            
            state.LastCheck = new StressCheck
            {
                Timestamp = context.Timestamp,
                Condition1Met = condition1,
                Condition2Met = condition2,
                Condition3Met = condition3,
                AllConditionsMet = condition1 && condition2 && condition3
            };
            
            // Trigger logic
            if (state.LastCheck.AllConditionsMet)
            {
                if (state.GraceCounter == 0)
                {
                    // First trigger - warning
                    state.GraceCounter = 1;
                    state.FirstTriggerTime = context.Timestamp;
                }
                else if (state.GraceCounter == 1)
                {
                    // Second trigger - exit
                    state.GraceCounter = 2;
                    state.ShouldExit = true;
                }
            }
        }
        
        /// <summary>
        /// Checks if position should exit due to stress.
        /// </summary>
        public bool ShouldExit(string positionId)
        {
            if (!_positionStates.TryGetValue(positionId, out var state))
                return false;
            
            return state.ShouldExit;
        }
        
        /// <summary>
        /// Gets stress state for a position.
        /// </summary>
        public StressState GetState(string positionId)
        {
            return _positionStates.TryGetValue(positionId, out var state) ? state : null;
        }
        
        /// <summary>
        /// Removes stress tracking for a closed position.
        /// </summary>
        public void RemovePosition(string positionId)
        {
            _positionStates.Remove(positionId);
        }
        
        /// <summary>
        /// Gets all positions in stress warning state (grace=1).
        /// </summary>
        public List<string> GetPositionsInWarning()
        {
            var warnings = new List<string>();
            
            foreach (var kvp in _positionStates)
            {
                if (kvp.Value.GraceCounter == 1 && !kvp.Value.ShouldExit)
                    warnings.Add(kvp.Key);
            }
            
            return warnings;
        }
    }
    
    /// <summary>
    /// Stress evaluation context.
    /// </summary>
    public class StressContext
    {
        public DateTime Timestamp { get; set; }
        public bool IsUnderwater { get; set; }
        public double SMS { get; set; }
        public double RegimeConfidence { get; set; }
    }
    
    /// <summary>
    /// Stress state for a position.
    /// </summary>
    public class StressState
    {
        public string PositionId { get; set; }
        public int UnderwaterBars { get; set; }
        public int GraceCounter { get; set; }  // 0=normal, 1=warning, 2=exit
        public DateTime? FirstTriggerTime { get; set; }
        public bool ShouldExit { get; set; }
        public StressCheck LastCheck { get; set; }
        
        public string GetStatus()
        {
            if (ShouldExit)
                return "EXIT";
            if (GraceCounter == 1)
                return "WARNING";
            return "NORMAL";
        }
        
        public override string ToString()
        {
            string status = GetStatus();
            string conditions = LastCheck != null 
                ? $"[U={LastCheck.Condition1Met}, S={LastCheck.Condition2Met}, R={LastCheck.Condition3Met}]"
                : "[--]";
            
            return $"Stress: {status}, UW_bars={UnderwaterBars}, Grace={GraceCounter}, Conditions={conditions}";
        }
    }
    
    /// <summary>
    /// Stress check result.
    /// </summary>
    public class StressCheck
    {
        public DateTime Timestamp { get; set; }
        public bool Condition1Met { get; set; }  // Underwater ≥2 bars
        public bool Condition2Met { get; set; }  // SMS <0.4
        public bool Condition3Met { get; set; }  // Regime confidence <0.50
        public bool AllConditionsMet { get; set; }
        
        public override string ToString()
        {
            return $"Stress check @ {Timestamp:HH:mm:ss}: " +
                   $"UnderwaterTime={Condition1Met}, LowSMS={Condition2Met}, " +
                   $"LowConfidence={Condition3Met} → Trigger={AllConditionsMet}";
        }
    }
}

