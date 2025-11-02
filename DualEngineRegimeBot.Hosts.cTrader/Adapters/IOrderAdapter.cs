using System;
using System.Collections.Generic;

namespace DualEngineRegimeBot.Hosts.cTrader.Adapters
{
    /// <summary>
    /// Trade side enumeration.
    /// </summary>
    public enum TradeSide
    {
        /// <summary>Buy/Long.</summary>
        Buy,
        
        /// <summary>Sell/Short.</summary>
        Sell
    }
    
    /// <summary>
    /// Order type enumeration.
    /// </summary>
    public enum OrderType
    {
        /// <summary>Market order.</summary>
        Market,
        
        /// <summary>Limit order.</summary>
        Limit,
        
        /// <summary>Stop order.</summary>
        Stop
    }
    
    /// <summary>
    /// Represents a live open position.
    /// </summary>
    public sealed record LivePosition
    {
        /// <summary>Position ID.</summary>
        public long PositionId { get; init; }
        
        /// <summary>Symbol.</summary>
        public string Symbol { get; init; } = "";
        
        /// <summary>Trade side.</summary>
        public TradeSide Side { get; init; }
        
        /// <summary>Volume in lots.</summary>
        public double Volume { get; init; }
        
        /// <summary>Entry price.</summary>
        public double EntryPrice { get; init; }
        
        /// <summary>Current price.</summary>
        public double CurrentPrice { get; init; }
        
        /// <summary>Stop loss price (optional).</summary>
        public double? StopLoss { get; init; }
        
        /// <summary>Take profit price (optional).</summary>
        public double? TakeProfit { get; init; }
        
        /// <summary>Unrealized P/L in account currency.</summary>
        public double UnrealizedPnL { get; init; }
        
        /// <summary>Position label/comment.</summary>
        public string? Label { get; init; }
        
        /// <summary>Entry time (UTC).</summary>
        public DateTime EntryTimeUtc { get; init; }
    }
    
    /// <summary>
    /// Order request to submit to broker.
    /// </summary>
    public sealed record OrderRequest
    {
        /// <summary>Symbol to trade.</summary>
        public string Symbol { get; init; } = "";
        
        /// <summary>Trade side.</summary>
        public TradeSide Side { get; init; }
        
        /// <summary>Order type.</summary>
        public OrderType Type { get; init; }
        
        /// <summary>Volume in lots.</summary>
        public double Volume { get; init; }
        
        /// <summary>Limit/stop price (for non-market orders).</summary>
        public double? Price { get; init; }
        
        /// <summary>Stop loss price (optional).</summary>
        public double? StopLoss { get; init; }
        
        /// <summary>Take profit price (optional).</summary>
        public double? TakeProfit { get; init; }
        
        /// <summary>Order label/comment.</summary>
        public string? Label { get; init; }
    }
    
    /// <summary>
    /// Result of order submission.
    /// </summary>
    public sealed record OrderResult
    {
        /// <summary>Whether order was successful.</summary>
        public bool IsSuccessful { get; init; }
        
        /// <summary>Position ID if successful.</summary>
        public long? PositionId { get; init; }
        
        /// <summary>Fill price if successful.</summary>
        public double? FillPrice { get; init; }
        
        /// <summary>Error message if failed.</summary>
        public string? ErrorMessage { get; init; }
        
        /// <summary>Timestamp (UTC).</summary>
        public DateTime TimestampUtc { get; init; }
    }
    
    /// <summary>
    /// Interface for order execution adapter.
    /// </summary>
    public interface IOrderAdapter
    {
        /// <summary>
        /// Gets all open positions with matching label prefix.
        /// </summary>
        /// <param name="labelPrefix">Label prefix to filter (optional).</param>
        /// <returns>List of open positions.</returns>
        IReadOnlyList<LivePosition> GetOpenPositions(string? labelPrefix = null);
        
        /// <summary>
        /// Places a new order.
        /// </summary>
        /// <param name="request">Order request.</param>
        /// <returns>Order result.</returns>
        OrderResult PlaceOrder(OrderRequest request);
        
        /// <summary>
        /// Closes an open position.
        /// </summary>
        /// <param name="positionId">Position ID to close.</param>
        /// <param name="reason">Close reason for logging.</param>
        /// <returns>True if successfully closed.</returns>
        bool ClosePosition(long positionId, string reason);
        
        /// <summary>
        /// Modifies stop loss and take profit on existing position.
        /// </summary>
        /// <param name="positionId">Position ID to modify.</param>
        /// <param name="newStopLoss">New stop loss price (optional).</param>
        /// <param name="newTakeProfit">New take profit price (optional).</param>
        /// <returns>True if successfully modified.</returns>
        bool ModifyPosition(long positionId, double? newStopLoss, double? newTakeProfit);
        
        /// <summary>
        /// Gets account balance.
        /// </summary>
        /// <returns>Current account balance in account currency.</returns>
        double GetAccountBalance();
        
        /// <summary>
        /// Gets account equity.
        /// </summary>
        /// <returns>Current account equity in account currency.</returns>
        double GetAccountEquity();
    }
}

