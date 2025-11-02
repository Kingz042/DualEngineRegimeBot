using System;

namespace DualEngineRegimeBot.Hosts.cTrader.Adapters
{
    /// <summary>
    /// Represents a price tick from the market.
    /// </summary>
    public sealed record Tick
    {
        /// <summary>Symbol name.</summary>
        public string Symbol { get; init; } = "";
        
        /// <summary>Bid price.</summary>
        public double Bid { get; init; }
        
        /// <summary>Ask price.</summary>
        public double Ask { get; init; }
        
        /// <summary>Mid price (calculated).</summary>
        public double Mid => (Bid + Ask) / 2.0;
        
        /// <summary>Spread in price units.</summary>
        public double Spread => Ask - Bid;
        
        /// <summary>Tick timestamp (UTC).</summary>
        public DateTime TimestampUtc { get; init; }
    }
    
    /// <summary>
    /// Symbol information and specifications.
    /// </summary>
    public sealed record SymbolInfo
    {
        /// <summary>Symbol name.</summary>
        public string Symbol { get; init; } = "";
        
        /// <summary>Digits after decimal point.</summary>
        public int Digits { get; init; }
        
        /// <summary>Point size (minimum price movement).</summary>
        public double PointSize { get; init; }
        
        /// <summary>Tick value in account currency.</summary>
        public double TickValue { get; init; }
        
        /// <summary>Minimum volume.</summary>
        public double MinVolume { get; init; }
        
        /// <summary>Maximum volume.</summary>
        public double MaxVolume { get; init; }
        
        /// <summary>Volume step.</summary>
        public double VolumeStep { get; init; }
    }
    
    /// <summary>
    /// Interface for market data feed adapter.
    /// </summary>
    public interface IMarketDataAdapter
    {
        /// <summary>
        /// Event fired on each tick.
        /// </summary>
        event Action<Tick>? OnTick;
        
        /// <summary>
        /// Gets symbol information.
        /// </summary>
        /// <param name="symbol">Symbol name.</param>
        /// <returns>Symbol information.</returns>
        SymbolInfo GetSymbolInfo(string symbol);
        
        /// <summary>
        /// Gets current bid/ask for a symbol.
        /// </summary>
        /// <param name="symbol">Symbol name.</param>
        /// <returns>Current tick or null if unavailable.</returns>
        Tick? GetCurrentTick(string symbol);
    }
}

