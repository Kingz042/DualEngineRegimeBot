using System;
using DualEngineRegimeBot.Core.Config;

namespace DualEngineRegimeBot.Core.Risk
{
    /// <summary>
    /// Risk control service managing locks, guards, and exposure limits.
    /// </summary>
    public class RiskService : IRiskService
    {
        private readonly RiskConfig _config;
        
        private double _peakEquity;
        private double _dailyStartEquity;
        private DateTime _lastResetDate;
        private double _currentDrawdownPct;
        private double _dailyPnLPct;
        
        public RiskService(RiskConfig config, double initialEquity)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _peakEquity = initialEquity;
            _dailyStartEquity = initialEquity;
            _lastResetDate = DateTime.UtcNow.Date;
        }
        
        /// <summary>
        /// Updates daily P&L, peak equity, and drawdown tracking.
        /// </summary>
        public void Update(MarketContext context, double realizedPnL, double unrealizedPnL)
        {
            double currentEquity = context.AccountEquity;
            
            // Update peak
            if (currentEquity > _peakEquity)
                _peakEquity = currentEquity;
            
            // Drawdown from peak
            _currentDrawdownPct = (_peakEquity - currentEquity) / _peakEquity * 100.0;
            
            // Daily P&L
            _dailyPnLPct = (_dailyStartEquity - currentEquity) / _dailyStartEquity * 100.0;
            
            // Auto-reset daily tracking at midnight UTC
            if (context.Time.Date > _lastResetDate)
                ResetDailyTracking();
        }
        
        public bool IsDailyLossLocked()
        {
            return _dailyPnLPct >= _config.DailyLossLockPct;
        }
        
        public bool IsDrawdownLocked()
        {
            return _currentDrawdownPct >= _config.MaxDrawdownLockPct;
        }
        
        public bool IsSpreadTooWide(double spread, double medianSpread)
        {
            if (medianSpread <= 0) return false; // No history yet
            return spread > _config.SpreadGuardMultiplier * medianSpread;
        }
        
        public bool IsInTradingSession(DateTime time)
        {
            if (!_config.EnableSessionGuard) return true;
            
            int hour = time.Hour;
            return hour >= _config.SessionStartHour && hour < _config.SessionEndHour;
        }
        
        public bool IsWarmupComplete(int barCount)
        {
            return barCount >= _config.WarmupBars;
        }
        
        public bool WouldExceedMaxPositions(int currentCount)
        {
            return currentCount >= _config.MaxConcurrentPositions;
        }
        
        public bool WouldExceedExposureCap(double currentNet, double addUnits, double cap)
        {
            return Math.Abs(currentNet + addUnits) > cap;
        }
        
        public void ResetDailyTracking()
        {
            _dailyStartEquity = _peakEquity; // Reset to current peak
            _lastResetDate = DateTime.UtcNow.Date;
            _dailyPnLPct = 0.0;
        }
    }
}

