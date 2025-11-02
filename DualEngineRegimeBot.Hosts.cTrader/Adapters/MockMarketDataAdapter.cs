using System;
using System.Collections.Generic;

namespace DualEngineRegimeBot.Hosts.cTrader.Adapters
{
    /// <summary>
    /// Mock market data adapter for testing and simulation.
    /// </summary>
    public sealed class MockMarketDataAdapter : IMarketDataAdapter
    {
        private readonly Dictionary<string, SymbolInfo> _symbolInfos = new();
        private readonly Dictionary<string, Tick> _currentTicks = new();
        
        /// <inheritdoc/>
        public event Action<Tick>? OnTick;
        
        /// <summary>
        /// Initializes a new instance of MockMarketDataAdapter.
        /// </summary>
        public MockMarketDataAdapter()
        {
            // Set up default XAUUSD symbol
            RegisterSymbol(new SymbolInfo
            {
                Symbol = "XAUUSD",
                Digits = 2,
                PointSize = 0.01,
                TickValue = 1.0,
                MinVolume = 0.01,
                MaxVolume = 100.0,
                VolumeStep = 0.01
            });
        }
        
        /// <summary>
        /// Registers a symbol for mock trading.
        /// </summary>
        public void RegisterSymbol(SymbolInfo symbolInfo)
        {
            _symbolInfos[symbolInfo.Symbol] = symbolInfo;
        }
        
        /// <inheritdoc/>
        public SymbolInfo GetSymbolInfo(string symbol)
        {
            if (_symbolInfos.TryGetValue(symbol, out var info))
            {
                return info;
            }
            
            throw new InvalidOperationException($"Symbol {symbol} not registered");
        }
        
        /// <inheritdoc/>
        public Tick? GetCurrentTick(string symbol)
        {
            return _currentTicks.TryGetValue(symbol, out var tick) ? tick : null;
        }
        
        /// <summary>
        /// Simulates a new tick by firing the OnTick event.
        /// </summary>
        public void SimulateTick(Tick tick)
        {
            _currentTicks[tick.Symbol] = tick;
            OnTick?.Invoke(tick);
        }
        
        /// <summary>
        /// Simulates a tick with specified prices.
        /// </summary>
        public void SimulateTick(string symbol, double bid, double ask, DateTime? timestampUtc = null)
        {
            var tick = new Tick
            {
                Symbol = symbol,
                Bid = bid,
                Ask = ask,
                TimestampUtc = timestampUtc ?? DateTime.UtcNow
            };
            SimulateTick(tick);
        }
    }
}

