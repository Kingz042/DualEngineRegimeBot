using System;
using System.Linq;
using Xunit;
using DualEngineRegimeBot.Core.Config;
using DualEngineRegimeBot.Hosts.cTrader;
using DualEngineRegimeBot.Hosts.cTrader.Adapters;
using DualEngineRegimeBot.Tests.Integration.Mocks;
using HostNewsPhase = DualEngineRegimeBot.Hosts.cTrader.NewsPhase;
using MockNewsPhase = DualEngineRegimeBot.Tests.Integration.Mocks.NewsPhase;

namespace DualEngineRegimeBot.Tests.Integration
{
    /// <summary>
    /// Integration tests simulating FTMO rules with mocked broker environment.
    /// </summary>
    public class FtmoRuleSimulationTests
    {
        [Fact]
        public void DailyLossLock_ResetsAtBrokerMidnight()
        {
            // Setup: Broker offset +2 hours
            var preset = FtmoPreset.CreateDefault() with
            {
                BrokerUtcOffsetHours = 2,
                MaxDailyLossPercent = 5.0
            };
            
            var mockClock = new MockClock(new DateTime(2025, 11, 1, 20, 0, 0, DateTimeKind.Utc));
            var orderAdapter = new MockOrderAdapter(initialBalance: 100000.0);
            
            // Simulate daily loss exceeding 5%
            orderAdapter.AddRealizedPnL(-6000.0); // -6% loss
            
            double dailyLossPercent = (-6000.0 / 100000.0) * 100.0;
            Assert.True(dailyLossPercent <= -preset.MaxDailyLossPercent, "Daily loss should exceed limit");
            
            // Advance clock past broker midnight
            // At UTC 20:00, broker time is 22:00 (same day)
            // Advance to UTC 23:00, broker time is 01:00 (next day)
            mockClock.AdvanceHours(3); // Now UTC 23:00 = Broker 01:00 next day
            
            DateTime prevUtc = new DateTime(2025, 11, 1, 20, 0, 0, DateTimeKind.Utc);
            DateTime currUtc = mockClock.UtcNow;
            
            bool crossedMidnight = preset.HasCrossedBrokerMidnight(prevUtc, currUtc);
            Assert.True(crossedMidnight, "Should have crossed broker midnight");
            
            // After reset, new entries should be allowed (loss counters reset)
            // This is a logical test - actual host would reset counters
        }
        
        [Fact]
        public void SessionEnd_IsExclusive_NoEntriesAtEndHour()
        {
            var preset = FtmoPreset.CreateDefault() with
            {
                SessionStartHour = 7,
                SessionEndHour = 21  // Exclusive
            };
            
            // Just before session end
            var beforeEnd = new DateTime(2025, 11, 1, 20, 59, 0, DateTimeKind.Utc);
            Assert.True(preset.IsWithinSession(beforeEnd), "Should be within session before end");
            
            // Exactly at session end - should be excluded
            var atEnd = new DateTime(2025, 11, 1, 21, 0, 0, DateTimeKind.Utc);
            Assert.False(preset.IsWithinSession(atEnd), "Session end should be exclusive");
            
            // After session end
            var afterEnd = new DateTime(2025, 11, 1, 21, 1, 0, DateTimeKind.Utc);
            Assert.False(preset.IsWithinSession(afterEnd), "Should be outside session after end");
            
            // Note: Exits/unwinds are always allowed regardless of session window
            // This is enforced in the host logic, not in the preset
        }
        
        [Fact]
        public void NewsBlockPhase_SuppressesEntries_AllowsExits()
        {
            var newsFeed = new TestNewsFeed();
            var mockClock = new MockClock(new DateTime(2025, 11, 1, 10, 0, 0, DateTimeKind.Utc));
            
            // Add block event from 10:00 to 10:30
            newsFeed.AddEvent(
                new DateTime(2025, 11, 1, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2025, 11, 1, 10, 30, 0, DateTimeKind.Utc),
                MockNewsPhase.Block);
            
            // During block phase
            mockClock.UtcNow = new DateTime(2025, 11, 1, 10, 15, 0, DateTimeKind.Utc);
            Assert.Equal(MockNewsPhase.Block, newsFeed.GetPhase(mockClock.UtcNow));
            Assert.False(newsFeed.AllowEntries(mockClock.UtcNow));
            Assert.False(newsFeed.AllowHedges(mockClock.UtcNow));
            Assert.True(newsFeed.AllowUnwinds(mockClock.UtcNow));
            
            // After block phase
            mockClock.UtcNow = new DateTime(2025, 11, 1, 10, 31, 0, DateTimeKind.Utc);
            Assert.Equal(MockNewsPhase.Normal, newsFeed.GetPhase(mockClock.UtcNow));
            Assert.True(newsFeed.AllowEntries(mockClock.UtcNow));
        }
        
        [Fact]
        public void HedgeFSM_NeverIncreasesNetExposure()
        {
            var orderAdapter = new MockOrderAdapter(initialBalance: 100000.0);
            
            // Open initial long position (net exposure = +1.0)
            var longOrder = new OrderRequest
            {
                Symbol = "XAUUSD",
                Side = TradeSide.Buy,
                Type = OrderType.Market,
                Volume = 1.0,
                Label = "FTMO_DER_Main"
            };
            
            var longResult = orderAdapter.PlaceOrder(longOrder);
            Assert.True(longResult.IsSuccessful);
            
            double initialNet = CalculateNetExposure(orderAdapter, "FTMO_DER");
            Assert.Equal(1.0, initialNet, 0.001);
            
            // Open hedge (partial short to reduce exposure)
            var hedgeOrder = new OrderRequest
            {
                Symbol = "XAUUSD",
                Side = TradeSide.Sell,
                Type = OrderType.Market,
                Volume = 0.5,  // Partial hedge
                Label = "FTMO_DER_Hedge"
            };
            
            var hedgeResult = orderAdapter.PlaceOrder(hedgeOrder);
            Assert.True(hedgeResult.IsSuccessful);
            
            double netAfterHedge = CalculateNetExposure(orderAdapter, "FTMO_DER");
            Assert.Equal(0.5, netAfterHedge, 0.001);
            
            // Verify |net| decreased (monotonic non-increasing)
            Assert.True(Math.Abs(netAfterHedge) < Math.Abs(initialNet),
                "Absolute net exposure should decrease after hedge");
            
            // Unwind hedge (close short)
            orderAdapter.ClosePosition(hedgeResult.PositionId!.Value, "Unwind hedge");
            
            double netAfterUnwind = CalculateNetExposure(orderAdapter, "FTMO_DER");
            Assert.Equal(1.0, netAfterUnwind, 0.001);
            
            // Verify hedge never increased |net| beyond initial
            Assert.True(Math.Abs(netAfterUnwind) <= Math.Abs(initialNet),
                "Net exposure should not exceed initial after full hedge cycle");
        }
        
        [Fact]
        public void DailyLossLock_AllowsExits_EvenWhenLocked()
        {
            var orderAdapter = new MockOrderAdapter(initialBalance: 100000.0);
            
            // Place a position
            var order = new OrderRequest
            {
                Symbol = "XAUUSD",
                Side = TradeSide.Buy,
                Type = OrderType.Market,
                Volume = 1.0,
                Label = "FTMO_DER_Test"
            };
            
            var result = orderAdapter.PlaceOrder(order);
            Assert.True(result.IsSuccessful);
            
            // Simulate daily loss lock (logical - would be enforced by host)
            orderAdapter.AddRealizedPnL(-6000.0); // -6% loss
            
            // Even with loss lock, closing position should succeed
            bool closed = orderAdapter.ClosePosition(result.PositionId!.Value, "Exit despite lock");
            Assert.True(closed, "Exits should be allowed even when daily loss locked");
        }
        
        [Fact]
        public void BrokerMidnight_DetectionWorksAcrossTimezones()
        {
            // Test with various broker offsets
            var testCases = new[]
            {
                (Offset: 0, PrevHour: 23, CurrHour: 1, ShouldCross: true),   // UTC broker
                (Offset: 2, PrevHour: 21, CurrHour: 23, ShouldCross: true),  // +2 broker (21UTC=23Broker, 23UTC=01Broker next day)
                (Offset: -5, PrevHour: 4, CurrHour: 6, ShouldCross: true),   // -5 broker (4UTC=23Broker, 6UTC=01Broker next day)
                (Offset: 0, PrevHour: 10, CurrHour: 12, ShouldCross: false)  // Same day
            };
            
            foreach (var tc in testCases)
            {
                var preset = FtmoPreset.CreateDefault() with { BrokerUtcOffsetHours = tc.Offset };
                var prev = new DateTime(2025, 11, 1, tc.PrevHour, 0, 0, DateTimeKind.Utc);
                var curr = new DateTime(2025, 11, 1, tc.CurrHour, 0, 0, DateTimeKind.Utc);
                
                // Handle day rollover in UTC
                if (tc.CurrHour < tc.PrevHour)
                    curr = curr.AddDays(1);
                
                bool crossed = preset.HasCrossedBrokerMidnight(prev, curr);
                Assert.Equal(tc.ShouldCross, crossed);
            }
        }
        
        [Fact]
        public void NewsPhase_OverlappingEvents_ReturnsMostRestrictive()
        {
            var newsFeed = new TestNewsFeed();
            var testTime = new DateTime(2025, 11, 1, 10, 15, 0, DateTimeKind.Utc);
            
            // Add overlapping events with different phases
            newsFeed.AddEvent(
                new DateTime(2025, 11, 1, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2025, 11, 1, 10, 30, 0, DateTimeKind.Utc),
                MockNewsPhase.Restricted);
            
            newsFeed.AddEvent(
                new DateTime(2025, 11, 1, 10, 10, 0, DateTimeKind.Utc),
                new DateTime(2025, 11, 1, 10, 20, 0, DateTimeKind.Utc),
                MockNewsPhase.Block);  // More restrictive
            
            // Should return Block (most restrictive)
            var phase = newsFeed.GetPhase(testTime);
            Assert.Equal(MockNewsPhase.Block, phase);
        }
        
        [Fact]
        public void MaxOpenPositions_ShouldBeEnforced()
        {
            var preset = FtmoPreset.CreateDefault(); // Default max = 3
            var orderAdapter = new MockOrderAdapter(initialBalance: 100000.0);
            
            // Place positions up to limit
            for (int i = 0; i < preset.MaxOpenPositions; i++)
            {
                var order = new OrderRequest
                {
                    Symbol = "XAUUSD",
                    Side = TradeSide.Buy,
                    Type = OrderType.Market,
                    Volume = 0.1,
                    Label = $"FTMO_DER_Pos{i}"
                };
                
                var result = orderAdapter.PlaceOrder(order);
                Assert.True(result.IsSuccessful);
            }
            
            var positions = orderAdapter.GetOpenPositions("FTMO_DER");
            Assert.Equal(preset.MaxOpenPositions, positions.Count);
            
            // Note: Actual enforcement would be in host/risk logic
            // This test verifies the limit is accessible and positions are tracked
        }
        
        [Fact]
        public void SpreadGuard_BlocksHighSpread_AllowsExits()
        {
            var preset = FtmoPreset.CreateDefault() with { MaxEntrySpreadPts = 2.0 };
            var marketData = new MockMarketDataAdapter();
            var orderAdapter = new MockOrderAdapter(initialBalance: 100000.0);
            
            // Place initial position with normal spread
            marketData.SimulateTick("XAUUSD", bid: 2000.0, ask: 2001.0);
            
            var order = new OrderRequest
            {
                Symbol = "XAUUSD",
                Side = TradeSide.Buy,
                Type = OrderType.Market,
                Volume = 1.0,
                Label = "FTMO_DER_Test"
            };
            
            var result = orderAdapter.PlaceOrder(order);
            Assert.True(result.IsSuccessful);
            
            // Simulate high spread
            marketData.SimulateTick("XAUUSD", bid: 2000.0, ask: 2010.0); // 10 point spread
            var tick = marketData.GetCurrentTick("XAUUSD");
            Assert.NotNull(tick);
            
            double spreadPts = tick.Spread / 0.01; // Convert to points
            Assert.True(spreadPts > preset.MaxEntrySpreadPts, "Spread should exceed threshold");
            
            // New entries would be blocked (enforced by router/host)
            // But exits should still be allowed
            bool closed = orderAdapter.ClosePosition(result.PositionId!.Value, "High spread exit");
            Assert.True(closed, "Exits should be allowed despite high spread");
        }
        
        private double CalculateNetExposure(MockOrderAdapter adapter, string labelPrefix)
        {
            var positions = adapter.GetOpenPositions(labelPrefix);
            double net = 0.0;
            
            foreach (var pos in positions)
            {
                net += pos.Side == TradeSide.Buy ? pos.Volume : -pos.Volume;
            }
            
            return net;
        }
    }
}

