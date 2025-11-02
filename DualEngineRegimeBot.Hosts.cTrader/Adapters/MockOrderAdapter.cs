using System;
using System.Collections.Generic;
using System.Linq;

namespace DualEngineRegimeBot.Hosts.cTrader.Adapters
{
    /// <summary>
    /// Mock order adapter for testing and simulation.
    /// </summary>
    public sealed class MockOrderAdapter : IOrderAdapter
    {
        private readonly Dictionary<long, LivePosition> _positions = new();
        private long _nextPositionId = 1000;
        private double _balance;
        private readonly object _lock = new();
        
        /// <summary>
        /// Initializes a new instance of MockOrderAdapter.
        /// </summary>
        /// <param name="initialBalance">Starting account balance.</param>
        public MockOrderAdapter(double initialBalance = 100000.0)
        {
            _balance = initialBalance;
        }
        
        /// <inheritdoc/>
        public IReadOnlyList<LivePosition> GetOpenPositions(string? labelPrefix = null)
        {
            lock (_lock)
            {
                var positions = _positions.Values.ToList();
                
                if (!string.IsNullOrEmpty(labelPrefix))
                {
                    positions = positions
                        .Where(p => p.Label != null && p.Label.StartsWith(labelPrefix))
                        .ToList();
                }
                
                return positions;
            }
        }
        
        /// <inheritdoc/>
        public OrderResult PlaceOrder(OrderRequest request)
        {
            lock (_lock)
            {
                try
                {
                    // Validate request
                    if (request.Volume <= 0)
                    {
                        return new OrderResult
                        {
                            IsSuccessful = false,
                            ErrorMessage = "Invalid volume",
                            TimestampUtc = DateTime.UtcNow
                        };
                    }
                    
                    // For market orders, use current price (mock)
                    double fillPrice = request.Price ?? (request.Side == TradeSide.Buy ? 2000.0 : 1999.0);
                    
                    long positionId = _nextPositionId++;
                    var position = new LivePosition
                    {
                        PositionId = positionId,
                        Symbol = request.Symbol,
                        Side = request.Side,
                        Volume = request.Volume,
                        EntryPrice = fillPrice,
                        CurrentPrice = fillPrice,
                        StopLoss = request.StopLoss,
                        TakeProfit = request.TakeProfit,
                        UnrealizedPnL = 0.0,
                        Label = request.Label,
                        EntryTimeUtc = DateTime.UtcNow
                    };
                    
                    _positions[positionId] = position;
                    
                    return new OrderResult
                    {
                        IsSuccessful = true,
                        PositionId = positionId,
                        FillPrice = fillPrice,
                        TimestampUtc = DateTime.UtcNow
                    };
                }
                catch (Exception ex)
                {
                    return new OrderResult
                    {
                        IsSuccessful = false,
                        ErrorMessage = ex.Message,
                        TimestampUtc = DateTime.UtcNow
                    };
                }
            }
        }
        
        /// <inheritdoc/>
        public bool ClosePosition(long positionId, string reason)
        {
            lock (_lock)
            {
                if (_positions.TryGetValue(positionId, out var position))
                {
                    // Realize P/L
                    _balance += position.UnrealizedPnL;
                    _positions.Remove(positionId);
                    return true;
                }
                
                return false;
            }
        }
        
        /// <inheritdoc/>
        public bool ModifyPosition(long positionId, double? newStopLoss, double? newTakeProfit)
        {
            lock (_lock)
            {
                if (_positions.TryGetValue(positionId, out var position))
                {
                    _positions[positionId] = position with
                    {
                        StopLoss = newStopLoss,
                        TakeProfit = newTakeProfit
                    };
                    return true;
                }
                
                return false;
            }
        }
        
        /// <inheritdoc/>
        public double GetAccountBalance()
        {
            lock (_lock)
            {
                return _balance;
            }
        }
        
        /// <inheritdoc/>
        public double GetAccountEquity()
        {
            lock (_lock)
            {
                double totalUnrealized = _positions.Values.Sum(p => p.UnrealizedPnL);
                return _balance + totalUnrealized;
            }
        }
        
        /// <summary>
        /// Updates position prices and unrealized P/L (for simulation).
        /// </summary>
        public void UpdatePositionPrice(long positionId, double currentPrice)
        {
            lock (_lock)
            {
                if (_positions.TryGetValue(positionId, out var position))
                {
                    double pnl = position.Side == TradeSide.Buy
                        ? (currentPrice - position.EntryPrice) * position.Volume * 100 // Simplified
                        : (position.EntryPrice - currentPrice) * position.Volume * 100;
                    
                    _positions[positionId] = position with
                    {
                        CurrentPrice = currentPrice,
                        UnrealizedPnL = pnl
                    };
                }
            }
        }
        
        /// <summary>
        /// Updates all positions to a new market price.
        /// </summary>
        public void UpdateAllPositions(string symbol, double bid, double ask)
        {
            lock (_lock)
            {
                foreach (var kvp in _positions.Where(p => p.Value.Symbol == symbol).ToList())
                {
                    double currentPrice = kvp.Value.Side == TradeSide.Buy ? bid : ask;
                    UpdatePositionPrice(kvp.Key, currentPrice);
                }
            }
        }
        
        /// <summary>
        /// Adds realized P/L to balance (for testing).
        /// </summary>
        public void AddRealizedPnL(double pnl)
        {
            lock (_lock)
            {
                _balance += pnl;
            }
        }
    }
}

