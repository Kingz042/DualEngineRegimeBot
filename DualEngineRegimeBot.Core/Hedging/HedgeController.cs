using System;
using System.Collections.Generic;
using System.Linq;
using DualEngineRegimeBot.Core.Config;
using DualEngineRegimeBot.Core.NewsGuard;

namespace DualEngineRegimeBot.Core.Hedging
{
    /// <summary>
    /// Defense-only hedge controller with clear FSM: Inactive → Hedged → (Unwind|ForcedExit) → Inactive.
    /// Never reverses net position, only protects existing exposure.
    /// </summary>
    public class HedgeController
    {
        private readonly HedgeConfig _config;
        private DateTime _lastHedgeTime = DateTime.MinValue;
        private HedgeState _currentState = HedgeState.Inactive;
        private HedgePosition? _activeHedge = null;
        
        // KPI tracking
        private readonly List<HedgePerformance> _hedgeHistory = new List<HedgePerformance>();
        
        public HedgeController(HedgeConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }
        
        /// <summary>
        /// Evaluates whether to open a hedge position.
        /// </summary>
        public HedgeDecision EvaluateHedgeOpen(
            HedgeEvaluationContext context,
            NewsGuard.NewsGuard newsGuard,
            double rollingMedianSpread)
        {
            if (!_config.Enabled)
                return HedgeDecision.NoAction("Hedge controller disabled");
            
            if (_currentState != HedgeState.Inactive)
                return HedgeDecision.NoAction("Hedge already active");
            
            // Check cooldown
            if (IsCooldownActive(context.CurrentTime))
            {
                double cooldownRemaining = (_lastHedgeTime.AddMilliseconds(_config.CooldownMs) - context.CurrentTime).TotalSeconds;
                return HedgeDecision.NoAction($"Cooldown active ({cooldownRemaining:F1}s remaining)");
            }
            
            // Check NewsGuard
            if (!newsGuard.AllowHedges())
            {
                return HedgeDecision.NoAction($"NewsGuard blocking: phase={newsGuard.GetPhase()}");
            }
            
            // Check spread guard
            if (rollingMedianSpread > 0 && 
                context.CurrentSpread > _config.SpreadGuardMultiplier * rollingMedianSpread)
            {
                return HedgeDecision.NoAction($"Spread too wide: {context.CurrentSpread:F2} > {_config.SpreadGuardMultiplier}×{rollingMedianSpread:F2}");
            }
            
            // Check adverse move threshold
            double hmult = _config.TriggerMultiplier * newsGuard.GetHmultMultiplier();
            double triggerDistance = hmult * context.AtrM1;
            double adverseMove = CalculateAdverseMove(context);
            
            if (adverseMove < triggerDistance)
            {
                return HedgeDecision.NoAction($"Adverse move {adverseMove:F5} < trigger {triggerDistance:F5}");
            }
            
            // Calculate hedge size
            double hedgeVolume = Math.Min(context.PHVolume, context.PHVolume * _config.VolumeCap);
            
            // Check margin
            if (!CheckMarginAvailable(context, hedgeVolume))
            {
                return HedgeDecision.NoAction($"Insufficient margin for hedge volume {hedgeVolume:F2}");
            }
            
            // All checks passed - open hedge
            var hedgeSide = context.PHSide == TradeSide.Long ? TradeSide.Short : TradeSide.Long;
            
            return HedgeDecision.OpenHedge(
                hedgeSide,
                hedgeVolume,
                context.CurrentPrice,
                $"Adverse move {adverseMove:F5} exceeds {triggerDistance:F5}");
        }
        
        /// <summary>
        /// Evaluates whether to unwind or force-exit the active hedge.
        /// </summary>
        public HedgeDecision EvaluateHedgeExit(
            HedgeEvaluationContext context,
            RegimeSnapshot regime,
            double sms,
            double midlinePrice)
        {
            if (_currentState != HedgeState.Hedged || _activeHedge == null)
                return HedgeDecision.NoAction("No active hedge");
            
            // Update hedge state
            _activeHedge.BarsHeld++;
            _activeHedge.MinutesHeld = (context.CurrentTime - _activeHedge.OpenTime).TotalMinutes;
            _activeHedge.CurrentPnL = CalculateHedgePnL(_activeHedge, context);
            
            // Priority 1: Hedge stop-loss
            if (CheckHedgeStopLoss(_activeHedge, context))
            {
                return HedgeDecision.ForceExit("Hedge SL hit", ExitReason.StopLoss);
            }
            
            // Priority 2: Parent position closed
            if (context.PHVolume <= 0.001)
            {
                return HedgeDecision.ForceExit("Parent position closed", ExitReason.ParentClosed);
            }
            
            // Priority 3: Recovery target reached
            if (CheckRecoveryTarget(_activeHedge, context))
            {
                return HedgeDecision.Unwind("Recovery target reached", ExitReason.RecoveryTarget, 1.0);
            }
            
            // Priority 4: Micro revival (SMS + midline cross)
            if (CheckMicroRevival(sms, context, midlinePrice))
            {
                return HedgeDecision.Unwind("Micro revival detected", ExitReason.MicroRevival, 1.0);
            }
            
            // Priority 5: Macro alignment
            if (CheckMacroAlignment(regime, context))
            {
                return HedgeDecision.Unwind("Macro regime aligned", ExitReason.MacroAlignment, 1.0);
            }
            
            // Priority 6: Time decay
            if (_activeHedge.MinutesHeld > _config.TimeDecayMinutes)
            {
                double minutesSinceDecay = _activeHedge.MinutesHeld - _config.TimeDecayMinutes;
                if (minutesSinceDecay > 0 && minutesSinceDecay % 3.0 < 0.1) // Every 3 minutes
                {
                    if (Math.Abs(context.PHVolume + _activeHedge.Volume) <= context.PHVolume)
                    {
                        return HedgeDecision.Unwind(
                            $"Time decay: {_activeHedge.MinutesHeld:F1} min",
                            ExitReason.TimeDecay,
                            _config.TimeDecayUnwindFraction);
                    }
                }
            }
            
            // Priority 7: Margin risk
            if (context.FreeMargin < context.UsedMargin * 0.2) // Free margin < 20% of used
            {
                return HedgeDecision.ForceExit("Margin risk", ExitReason.MarginRisk);
            }
            
            return HedgeDecision.NoAction("Hedge held");
        }
        
        /// <summary>
        /// Records hedge opening.
        /// </summary>
        public void RecordHedgeOpen(TradeSide side, double volume, double price, DateTime time)
        {
            _activeHedge = new HedgePosition
            {
                Side = side,
                Volume = volume,
                OpenPrice = price,
                OpenTime = time,
                BarsHeld = 0,
                MinutesHeld = 0,
                CurrentPnL = 0
            };
            
            _currentState = HedgeState.Hedged;
            _lastHedgeTime = time;
        }
        
        /// <summary>
        /// Records hedge closing and updates KPIs.
        /// </summary>
        public void RecordHedgeClose(double closePnL, ExitReason reason, DateTime time)
        {
            if (_activeHedge != null)
            {
                var perf = new HedgePerformance
                {
                    OpenTime = _activeHedge.OpenTime,
                    CloseTime = time,
                    DurationMinutes = (time - _activeHedge.OpenTime).TotalMinutes,
                    PnL = closePnL,
                    ExitReason = reason,
                    WasProfit = closePnL > 0
                };
                
                _hedgeHistory.Add(perf);
                
                // Trim history to last 100 hedges
                if (_hedgeHistory.Count > 100)
                    _hedgeHistory.RemoveAt(0);
            }
            
            _activeHedge = null;
            _currentState = HedgeState.Inactive;
        }
        
        /// <summary>
        /// Gets current hedge state.
        /// </summary>
        public HedgeState GetState() => _currentState;
        
        /// <summary>
        /// Gets active hedge position (if any).
        /// </summary>
        public HedgePosition? GetActiveHedge() => _activeHedge;
        
        /// <summary>
        /// Gets hedge KPIs for performance review.
        /// </summary>
        public HedgeKPIs GetKPIs()
        {
            if (_hedgeHistory.Count == 0)
                return new HedgeKPIs();
            
            int wins = _hedgeHistory.Count(h => h.WasProfit);
            double totalPnL = _hedgeHistory.Sum(h => h.PnL);
            double avgDuration = _hedgeHistory.Average(h => h.DurationMinutes);
            double frequency = _hedgeHistory.Count; // Raw count, normalize by trades externally
            
            return new HedgeKPIs
            {
                TotalHedges = _hedgeHistory.Count,
                WinRate = (double)wins / _hedgeHistory.Count,
                AvgDurationMinutes = avgDuration,
                TotalPnL = totalPnL,
                AvgPnL = totalPnL / _hedgeHistory.Count,
                Frequency = frequency
            };
        }
        
        private bool IsCooldownActive(DateTime now)
        {
            return (now - _lastHedgeTime).TotalMilliseconds < _config.CooldownMs;
        }
        
        private double CalculateAdverseMove(HedgeEvaluationContext context)
        {
            // Adverse move = distance from PH avg price in unfavorable direction
            if (context.PHSide == TradeSide.Long)
                return Math.Max(0, context.PHAvgPrice - context.CurrentPrice);
            else
                return Math.Max(0, context.CurrentPrice - context.PHAvgPrice);
        }
        
        private bool CheckMarginAvailable(HedgeEvaluationContext context, double hedgeVolume)
        {
            // Simplified check - in real impl would use Symbol.GetEstimatedMargin
            double estimatedMargin = hedgeVolume * context.CurrentPrice / 100.0; // Assume 1:100 leverage
            return context.FreeMargin >= estimatedMargin * _config.MarginBufferMultiplier;
        }
        
        private double CalculateHedgePnL(HedgePosition hedge, HedgeEvaluationContext context)
        {
            double priceDiff = hedge.Side == TradeSide.Long 
                ? context.CurrentPrice - hedge.OpenPrice
                : hedge.OpenPrice - context.CurrentPrice;
            
            return priceDiff * hedge.Volume; // Simplified
        }
        
        private bool CheckHedgeStopLoss(HedgePosition hedge, HedgeEvaluationContext context)
        {
            double stopDistance = _config.HedgeStopMultiplier * context.AtrM1;
            double priceDiff = hedge.Side == TradeSide.Long
                ? hedge.OpenPrice - context.CurrentPrice
                : context.CurrentPrice - hedge.OpenPrice;
            
            return priceDiff >= stopDistance;
        }
        
        private bool CheckRecoveryTarget(HedgePosition hedge, HedgeEvaluationContext context)
        {
            double recoveryTarget = _config.RecoveryTargetMultiplier * context.AtrM1;
            // Recovery = price moves back toward PH's favor (against the hedge)
            // For SHORT hedge: price going UP is recovery (hedge loses)
            // For LONG hedge: price going DOWN is recovery (hedge loses)
            double priceDiff = hedge.Side == TradeSide.Short
                ? context.CurrentPrice - hedge.OpenPrice  // UP movement for short
                : hedge.OpenPrice - context.CurrentPrice;  // DOWN movement for long
            
            return priceDiff >= recoveryTarget;
        }
        
        private bool CheckMicroRevival(double sms, HedgeEvaluationContext context, double midlinePrice)
        {
            if (sms < _config.MicroRevivalSMS)
                return false;
            
            // Check if price crossed midline in PH's favor
            bool midlineCross = (context.PHSide == TradeSide.Long && context.CurrentPrice > midlinePrice) ||
                               (context.PHSide == TradeSide.Short && context.CurrentPrice < midlinePrice);
            
            return midlineCross;
        }
        
        private bool CheckMacroAlignment(RegimeSnapshot regime, HedgeEvaluationContext context)
        {
            if (regime.Confidence < _config.MacroAlignmentConfidence)
                return false;
            
            // Check if regime aligns with PH direction
            bool aligned = (context.PHSide == TradeSide.Long && regime.Direction == RegimeDirection.Bull) ||
                          (context.PHSide == TradeSide.Short && regime.Direction == RegimeDirection.Bear);
            
            return aligned;
        }
    }
    
    #region Supporting Types
    
    public enum HedgeState
    {
        Inactive,
        Hedged
    }
    
    public class HedgePosition
    {
        public TradeSide Side { get; set; }
        public double Volume { get; set; }
        public double OpenPrice { get; set; }
        public DateTime OpenTime { get; set; }
        public int BarsHeld { get; set; }
        public double MinutesHeld { get; set; }
        public double CurrentPnL { get; set; }
    }
    
    public class HedgeEvaluationContext
    {
        public DateTime CurrentTime { get; set; }
        public double CurrentPrice { get; set; }
        public double CurrentSpread { get; set; }
        public double AtrM1 { get; set; }
        public TradeSide PHSide { get; set; }
        public double PHVolume { get; set; }
        public double PHAvgPrice { get; set; }
        public double FreeMargin { get; set; }
        public double UsedMargin { get; set; }
    }
    
    public class HedgeDecision
    {
        public HedgeAction Action { get; set; }
        public TradeSide? Side { get; set; }
        public double Volume { get; set; }
        public double Price { get; set; }
        public double UnwindFraction { get; set; }
        public string Reason { get; set; }
        public ExitReason ExitReasonType { get; set; }
        
        public static HedgeDecision NoAction(string reason) => new HedgeDecision 
        { 
            Action = HedgeAction.None, 
            Reason = reason 
        };
        
        public static HedgeDecision OpenHedge(TradeSide side, double volume, double price, string reason) => new HedgeDecision
        {
            Action = HedgeAction.Open,
            Side = side,
            Volume = volume,
            Price = price,
            Reason = reason
        };
        
        public static HedgeDecision Unwind(string reason, ExitReason exitReason, double fraction = 1.0) => new HedgeDecision
        {
            Action = HedgeAction.Unwind,
            UnwindFraction = fraction,
            Reason = reason,
            ExitReasonType = exitReason
        };
        
        public static HedgeDecision ForceExit(string reason, ExitReason exitReason) => new HedgeDecision
        {
            Action = HedgeAction.ForceExit,
            Reason = reason,
            ExitReasonType = exitReason
        };
    }
    
    public enum HedgeAction
    {
        None,
        Open,
        Unwind,
        ForceExit
    }
    
    public enum ExitReason
    {
        None,
        StopLoss,
        ParentClosed,
        RecoveryTarget,
        MicroRevival,
        MacroAlignment,
        TimeDecay,
        MarginRisk
    }
    
    public class HedgePerformance
    {
        public DateTime OpenTime { get; set; }
        public DateTime CloseTime { get; set; }
        public double DurationMinutes { get; set; }
        public double PnL { get; set; }
        public ExitReason ExitReason { get; set; }
        public bool WasProfit { get; set; }
    }
    
    public class HedgeKPIs
    {
        public int TotalHedges { get; set; }
        public double WinRate { get; set; }
        public double AvgDurationMinutes { get; set; }
        public double TotalPnL { get; set; }
        public double AvgPnL { get; set; }
        public double Frequency { get; set; }
        
        /// <summary>
        /// Generates auto-tuning suggestions based on KPIs.
        /// </summary>
        public List<string> GetTuningSuggestions()
        {
            var suggestions = new List<string>();
            
            if (WinRate < 0.40)
                suggestions.Add("HedgeWinRate < 40%: Consider easing unwind thresholds (lower recovery target)");
            
            if (AvgDurationMinutes > 8.0)
                suggestions.Add($"AvgHedgeDuration {AvgDurationMinutes:F1} min > 8 min: Consider lowering recovery target (e.g., 0.5× ATR)");
            
            if (Frequency > 0.3) // Per trade, checked externally
                suggestions.Add("HedgeFrequency > 0.3 per trade: Consider increasing Hmult");
            
            return suggestions;
        }
    }
    
    #endregion
}

