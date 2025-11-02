using System;
using System.Threading;
using System.Threading.Tasks;
using DualEngineRegimeBot.Core.Config;
using DualEngineRegimeBot.Core.Execution;
using DualEngineRegimeBot.Hosts.cTrader.Adapters;

namespace DualEngineRegimeBot.Hosts.cTrader
{
    /// <summary>
    /// Host configuration for cTrader bot.
    /// </summary>
    public sealed class HostConfig
    {
        /// <summary>Application version.</summary>
        public string AppVersion { get; set; } = "1.2.0";
        
        /// <summary>Symbol to trade.</summary>
        public string Symbol { get; set; } = "XAUUSD";
        
        /// <summary>FTMO preset configuration.</summary>
        public FtmoPreset Preset { get; set; } = FtmoPreset.CreateDefault();
        
        /// <summary>State persistence file path.</summary>
        public string StatePath { get; set; } = "bot_state.json";
        
        /// <summary>Telemetry log path.</summary>
        public string TelemetryPath { get; set; } = "bot_telemetry.csv";
        
        /// <summary>Maximum telemetry file size in bytes before rotation.</summary>
        public long MaxTelemetryFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB
        
        /// <summary>Number of rotated telemetry files to retain.</summary>
        public int TelemetryRetainCount { get; set; } = 5;
        
        /// <summary>News source type: "json" or "none".</summary>
        public string NewsSource { get; set; } = "none";
        
        /// <summary>Path to news JSON file (when NewsSource="json").</summary>
        public string NewsJsonPath { get; set; } = "news.json";
    }
    
    /// <summary>
    /// cTrader host adapter that wires Core to broker APIs.
    /// Manages tick feed, engines, risk, sizing, and order execution.
    /// </summary>
    public sealed class cTraderHost
    {
        private readonly HostConfig _config;
        private readonly IMarketDataAdapter _marketData;
        private readonly IOrderAdapter _orderAdapter;
        private readonly ExecutionRouter _executionRouter;
        private readonly INewsAdapter _newsAdapter;
        
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private DateTime _lastDailyResetTime = DateTime.MinValue;
        private double _dailyRealizedPnL = 0.0;
        private double _peakEquity = 0.0;
        private bool _dailyLossLocked = false;
        
        /// <summary>
        /// Initializes a new instance of cTraderHost.
        /// </summary>
        public cTraderHost(
            HostConfig config,
            IMarketDataAdapter marketData,
            IOrderAdapter orderAdapter)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _marketData = marketData ?? throw new ArgumentNullException(nameof(marketData));
            _orderAdapter = orderAdapter ?? throw new ArgumentNullException(nameof(orderAdapter));
            
            var routerConfig = new ExecutionRouterConfig
            {
                MaxEntrySpreadPts = config.Preset.MaxEntrySpreadPts,
                SpreadPenaltyMultiplier = 0.8
            };
            _executionRouter = new ExecutionRouter(routerConfig);
            
            // Initialize news adapter based on config
            _newsAdapter = config.NewsSource.ToLowerInvariant() == "json"
                ? new JsonNewsAdapter(config.NewsJsonPath)
                : new NoNewsAdapter();
        }
        
        /// <summary>
        /// Starts the bot with graceful shutdown support.
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            PrintStartupBanner();
            ReconcilePositionsOnStartup();
            InitializeDailyTracking();
            
            // Wire up tick feed
            _marketData.OnTick += OnTickReceived;
            
            try
            {
                // Main event loop - wait for cancellation
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
                Console.WriteLine("[HOST] Shutdown requested, closing positions...");
            }
            finally
            {
                _marketData.OnTick -= OnTickReceived;
                FlushStateAndTelemetry();
                Console.WriteLine("[HOST] Shutdown complete.");
            }
        }
        
        /// <summary>
        /// Prints startup banner with version, config hash, and settings.
        /// All required information for FTMO compliance audit trail.
        /// </summary>
        private void PrintStartupBanner()
        {
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║       DualEngineRegimeBot - cTrader Host Adapter            ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine($"App Version:       {_config.AppVersion}");
            Console.WriteLine($"Config Version:    {_config.Preset.VersionTag}");
            Console.WriteLine($"Config Hash:       {_config.Preset.ConfigHashSha256()}");
            Console.WriteLine($"Symbol:            {_config.Symbol}");
            Console.WriteLine($"Broker UTC Offset: {_config.Preset.BrokerUtcOffsetHours:+0;-0} hours");
            Console.WriteLine($"Session Window:    {_config.Preset.SessionStartHour:D2}:00 - {_config.Preset.SessionEndHour:D2}:00 UTC (end exclusive)");
            Console.WriteLine($"Max Risk/Trade:    {_config.Preset.MaxRiskPercentPerTrade:F2}%");
            Console.WriteLine($"Max Daily Loss:    {_config.Preset.MaxDailyLossPercent:F2}%");
            Console.WriteLine($"Max Drawdown:      {_config.Preset.MaxDrawdownPercent:F2}%");
            Console.WriteLine($"Label Prefix:      {_config.Preset.LabelPrefix}");
            Console.WriteLine($"News Source:       {_config.NewsSource}");
            Console.WriteLine($"Started:           {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine("════════════════════════════════════════════════════════════════");
            
            // Validate preset
            if (!_config.Preset.Validate(out string? error))
            {
                Console.WriteLine($"[WARNING] Preset validation failed: {error}");
            }
        }
        
        /// <summary>
        /// Reconciles open positions on startup to prevent duplicate hedges.
        /// </summary>
        private void ReconcilePositionsOnStartup()
        {
            var openPositions = _orderAdapter.GetOpenPositions(_config.Preset.LabelPrefix);
            Console.WriteLine($"[HOST] Reconciled {openPositions.Count} open position(s) on startup");
            
            foreach (var position in openPositions)
            {
                Console.WriteLine($"  - {position.Side} {position.Volume} lots @ {position.EntryPrice:F2}, Label: {position.Label}");
            }
        }
        
        /// <summary>
        /// Initializes daily tracking variables.
        /// </summary>
        private void InitializeDailyTracking()
        {
            double balance = _orderAdapter.GetAccountBalance();
            _peakEquity = balance;
            _lastDailyResetTime = DateTime.UtcNow;
            _dailyRealizedPnL = 0.0;
            _dailyLossLocked = false;
            
            Console.WriteLine($"[HOST] Initial balance: ${balance:F2}, Peak equity: ${_peakEquity:F2}");
        }
        
        /// <summary>
        /// Handles incoming tick events.
        /// </summary>
        private void OnTickReceived(Tick tick)
        {
            try
            {
                DateTime now = tick.TimestampUtc;
                
                // Check for broker midnight reset
                if (_config.Preset.HasCrossedBrokerMidnight(_lastDailyResetTime, now))
                {
                    ResetDailyCounters(now);
                }
                
                // Update tracking
                _lastUpdateTime = now;
                
                // Check daily loss lock
                if (_dailyLossLocked)
                {
                    // Only allow exits, no entries
                    return;
                }
                
                // Check session window
                if (!_config.Preset.IsWithinSession(now))
                {
                    // Outside session - no new entries, but exits allowed
                    return;
                }
                
                // TODO: Wire to actual engines, risk, sizing, etc.
                // For now, this is a minimal shell that demonstrates the structure
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Exception in tick handler: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Resets daily loss counters at broker midnight.
        /// </summary>
        private void ResetDailyCounters(DateTime currentUtc)
        {
            DateTime brokerTime = _config.Preset.GetBrokerLocalTime(currentUtc);
            Console.WriteLine($"[HOST] Broker midnight crossed at {brokerTime:yyyy-MM-dd HH:mm:ss} local");
            Console.WriteLine($"[HOST] Resetting daily counters. Previous daily P/L: ${_dailyRealizedPnL:F2}");
            
            _dailyRealizedPnL = 0.0;
            _dailyLossLocked = false;
            _lastDailyResetTime = currentUtc;
            
            // Reset peak equity tracking
            double currentEquity = _orderAdapter.GetAccountEquity();
            _peakEquity = currentEquity;
        }
        
        /// <summary>
        /// Checks and updates daily loss lock status.
        /// </summary>
        private void CheckDailyLossLock()
        {
            double balance = _orderAdapter.GetAccountBalance();
            double dailyLossPercent = (_dailyRealizedPnL / balance) * 100.0;
            
            if (dailyLossPercent <= -_config.Preset.MaxDailyLossPercent && !_dailyLossLocked)
            {
                _dailyLossLocked = true;
                Console.WriteLine($"[HOST] DAILY LOSS LOCK TRIGGERED: {dailyLossPercent:F2}% (limit: {_config.Preset.MaxDailyLossPercent:F2}%)");
            }
        }
        
        /// <summary>
        /// Flushes state and telemetry to disk on shutdown.
        /// </summary>
        private void FlushStateAndTelemetry()
        {
            try
            {
                Console.WriteLine("[HOST] Flushing state and telemetry...");
                // TODO: Call StatePersistence.Save() and telemetry flush
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to flush state: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Gets current daily P/L percentage.
        /// </summary>
        public double GetDailyLossPercent()
        {
            double balance = _orderAdapter.GetAccountBalance();
            return balance > 0 ? (_dailyRealizedPnL / balance) * 100.0 : 0.0;
        }
        
        /// <summary>
        /// Gets whether daily loss lock is active.
        /// </summary>
        public bool IsDailyLossLocked() => _dailyLossLocked;
        
        /// <summary>
        /// Gets current drawdown from peak.
        /// </summary>
        public double GetDrawdownPercent()
        {
            double currentEquity = _orderAdapter.GetAccountEquity();
            return _peakEquity > 0 ? ((currentEquity - _peakEquity) / _peakEquity) * 100.0 : 0.0;
        }
    }
}

