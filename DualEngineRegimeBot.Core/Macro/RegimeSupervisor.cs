using System;
using DualEngineRegimeBot.Core.Config;

namespace DualEngineRegimeBot.Core.Macro
{
    /// <summary>
    /// Regime supervisor implementing 5-case mid-position transition protocol.
    /// Manages position actions when macro regime flips during open trades.
    /// </summary>
    public class RegimeSupervisor
    {
        private RegimeSnapshot _lastRegime;
        private RegimeSnapshot _previousRegime;
        private DateTime _regimeChangeTime = DateTime.MinValue;
        private int _regimeBarsAge = 0;
        
        public RegimeSupervisor()
        {
            _lastRegime = new RegimeSnapshot
            {
                Direction = RegimeDirection.Neutral,
                VolState = RegimeVolState.LowVol,
                Confidence = 0.5,
                Timestamp = DateTime.UtcNow
            };
            _previousRegime = _lastRegime;
        }
        
        /// <summary>
        /// Updates regime tracking and detects flips.
        /// </summary>
        public void Update(RegimeSnapshot newRegime, DateTime currentTime)
        {
            bool regimeChanged = newRegime.Direction != _lastRegime.Direction ||
                                newRegime.VolState != _lastRegime.VolState;
            
            if (regimeChanged)
            {
                _regimeChangeTime = currentTime;
                _regimeBarsAge = 0;
            }
            else
            {
                _regimeBarsAge++;
            }
            
            _previousRegime = _lastRegime;
            _lastRegime = newRegime;
        }
        
        /// <summary>
        /// Evaluates position action based on 5-case regime transition protocol.
        /// </summary>
        public RegimeTransitionDecision EvaluatePositionAction(
            RegimeSnapshot currentRegime,
            PositionContext position,
            double sms)
        {
            // Determine if regime aligns with position
            RegimeAlignment alignment = DetermineAlignment(currentRegime, position);
            
            // Calculate UPL in ATR units (price movement, not dollar PnL)
            double priceDiff = position.Side == TradeSide.Long
                ? position.CurrentPrice - position.EntryPrice
                : position.EntryPrice - position.CurrentPrice;
            double uplATR = priceDiff / position.AtrM1;
            
            // Apply 5-case decision table
            return alignment switch
            {
                RegimeAlignment.Aligned => HandleAlignedCase(currentRegime, position, uplATR),
                RegimeAlignment.Opposed => HandleOpposedCase(currentRegime, position, uplATR, sms),
                RegimeAlignment.Ambiguous => HandleAmbiguousCase(currentRegime, position),
                _ => RegimeTransitionDecision.NoAction("Unknown alignment")
            };
        }
        
        /// <summary>
        /// Gets current regime age in bars since last change.
        /// </summary>
        public int GetRegimeAgeInBars() => _regimeBarsAge;
        
        /// <summary>
        /// Gets time since last regime change.
        /// </summary>
        public TimeSpan GetTimeSinceRegimeChange(DateTime now) => now - _regimeChangeTime;
        
        private RegimeAlignment DetermineAlignment(RegimeSnapshot regime, PositionContext position)
        {
            // Ambiguous if low confidence
            if (regime.Confidence < 0.5)
                return RegimeAlignment.Ambiguous;
            
            // Check directional alignment
            bool aligned = (position.Side == TradeSide.Long && regime.Direction == RegimeDirection.Bull) ||
                          (position.Side == TradeSide.Short && regime.Direction == RegimeDirection.Bear);
            
            return aligned ? RegimeAlignment.Aligned : RegimeAlignment.Opposed;
        }
        
        private RegimeTransitionDecision HandleAlignedCase(
            RegimeSnapshot regime,
            PositionContext position,
            double uplATR)
        {
            // Case 1: Aligned - keep PH with adaptive trailing
            
            // Check for confidence boost (compare current regime to previous regime)
            double confidenceBoost = regime.Confidence - _previousRegime.Confidence;
            
            if (confidenceBoost >= 0.15)
            {
                // Trail based on UPL
                double trailMultiplier = uplATR >= 2.0 ? 2.0 : 1.2;
                double trailDistance = trailMultiplier * position.AtrM1;
                
                return RegimeTransitionDecision.UpdateTrail(
                    trailDistance,
                    $"Aligned regime with confidence boost {confidenceBoost:F2}, UPL={uplATR:F2} ATR",
                    "RegimeAlignedTrail");
            }
            
            return RegimeTransitionDecision.NoAction("Regime aligned, no adjustment needed");
        }
        
        private RegimeTransitionDecision HandleOpposedCase(
            RegimeSnapshot regime,
            PositionContext position,
            double uplATR,
            double sms)
        {
            // Case 2: Opposed with small loss (<+0.5 ATR)
            if (uplATR < 0.5)
            {
                return RegimeTransitionDecision.FlattenNow(
                    $"Regime conflict with UPL={uplATR:F2} ATR",
                    "RegimeConflictLoss");
            }
            
            // Case 3: Opposed with moderate profit (+0.5 to <+1.5 ATR)
            if (uplATR < 1.5)
            {
                int timeStopMinutes = sms > 1.0 ? 3 : 5;
                
                return RegimeTransitionDecision.ScaleOut(
                    0.5,
                    timeStopMinutes,
                    $"Regime conflict, moderate profit UPL={uplATR:F2} ATR",
                    "RegimeConflictScaleOut");
            }
            
            // Case 4: Opposed with significant profit (≥+1.5 ATR) - protected runner
            double trailMultiplier = _regimeBarsAge switch
            {
                < 2 => 1.5,   // New regime: wider trail
                < 4 => 1.3,   // Established regime: moderate trail
                _ => 1.0      // Old regime: tight trail
            };
            
            double trailDistance = trailMultiplier * position.AtrM1;
            
            return RegimeTransitionDecision.UpdateTrail(
                trailDistance,
                $"Protected runner: UPL={uplATR:F2} ATR, regime age={_regimeBarsAge} bars",
                "RegimeProtectedRunner");
        }
        
        private RegimeTransitionDecision HandleAmbiguousCase(
            RegimeSnapshot regime,
            PositionContext position)
        {
            // Case 5: Ambiguous (Confidence < 0.5)
            
            // Tighten trail by 10%
            double currentTrail = position.CurrentTrailDistance;
            double newTrail = currentTrail * 0.9;
            
            // Check for extended ambiguity
            bool extendedAmbiguity = regime.Confidence < 0.5 && _regimeBarsAge > 6;
            
            if (extendedAmbiguity)
            {
                return RegimeTransitionDecision.FlattenNow(
                    $"Extended regime ambiguity: {_regimeBarsAge} bars with Conf={regime.Confidence:F2}",
                    "RegimeAmbiguityExit",
                    diagnosticMode: true);
            }
            
            return RegimeTransitionDecision.UpdateTrail(
                newTrail,
                $"Ambiguous regime (Conf={regime.Confidence:F2}), tightened trail 10%",
                "RegimeAmbiguousTrail",
                suppressNewEntries: true);
        }
    }
    
    #region Supporting Types
    
    public enum RegimeAlignment
    {
        Aligned,
        Opposed,
        Ambiguous
    }
    
    public class PositionContext
    {
        public TradeSide Side { get; set; }
        public double EntryPrice { get; set; }
        public double CurrentPrice { get; set; }
        public double UnrealizedPnL { get; set; }
        public double AtrM1 { get; set; }
        public double CurrentTrailDistance { get; set; }
        public int BarsOpen { get; set; }
    }
    
    public class RegimeTransitionDecision
    {
        public RegimeAction Action { get; set; }
        public double TrailDistance { get; set; }
        public double ScaleOutFraction { get; set; }
        public int TimeStopMinutes { get; set; }
        public string Reason { get; set; }
        public string SemanticTag { get; set; }
        public bool SuppressNewEntries { get; set; }
        public bool EnableDiagnosticMode { get; set; }
        
        public static RegimeTransitionDecision NoAction(string reason) => new RegimeTransitionDecision
        {
            Action = RegimeAction.NoAction,
            Reason = reason,
            SemanticTag = "NoAction"
        };
        
        public static RegimeTransitionDecision FlattenNow(
            string reason,
            string tag,
            bool diagnosticMode = false) => new RegimeTransitionDecision
        {
            Action = RegimeAction.FlattenNow,
            Reason = reason,
            SemanticTag = tag,
            EnableDiagnosticMode = diagnosticMode
        };
        
        public static RegimeTransitionDecision ScaleOut(
            double fraction,
            int timeStopMinutes,
            string reason,
            string tag) => new RegimeTransitionDecision
        {
            Action = RegimeAction.ScaleOut,
            ScaleOutFraction = fraction,
            TimeStopMinutes = timeStopMinutes,
            Reason = reason,
            SemanticTag = tag
        };
        
        public static RegimeTransitionDecision UpdateTrail(
            double distance,
            string reason,
            string tag,
            bool suppressNewEntries = false) => new RegimeTransitionDecision
        {
            Action = RegimeAction.UpdateTrail,
            TrailDistance = distance,
            Reason = reason,
            SemanticTag = tag,
            SuppressNewEntries = suppressNewEntries
        };
    }
    
    public enum RegimeAction
    {
        NoAction,
        FlattenNow,
        ScaleOut,
        UpdateTrail
    }
    
    #endregion
}

