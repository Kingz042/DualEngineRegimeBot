using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using DualEngineRegimeBot.Hosts.cTrader;

namespace DualEngineRegimeBot.Tests
{
    /// <summary>
    /// Tests for news adapter implementations.
    /// </summary>
    public class NewsAdapterTests : IDisposable
    {
        private readonly string _testDir;
        
        public NewsAdapterTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "NewsAdapterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }
        
        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                try { Directory.Delete(_testDir, recursive: true); } catch { /* Best effort */ }
            }
        }
        
        [Fact]
        public void JsonNewsAdapter_ReturnsNormal_WhenNoEvents()
        {
            var adapter = new JsonNewsAdapter(new List<(DateTime, DateTime, NewsPhase)>());
            
            var phase = adapter.GetPhase(DateTime.UtcNow);
            
            Assert.Equal(NewsPhase.Normal, phase);
        }
        
        [Fact]
        public void JsonNewsAdapter_ReturnsCorrectPhase_DuringEvent()
        {
            var events = new List<(DateTime From, DateTime To, NewsPhase Phase)>
            {
                (new DateTime(2025, 11, 1, 10, 0, 0, DateTimeKind.Utc),
                 new DateTime(2025, 11, 1, 10, 30, 0, DateTimeKind.Utc),
                 NewsPhase.Block)
            };
            
            var adapter = new JsonNewsAdapter(events);
            
            // Before event
            var beforePhase = adapter.GetPhase(new DateTime(2025, 11, 1, 9, 59, 0, DateTimeKind.Utc));
            Assert.Equal(NewsPhase.Normal, beforePhase);
            
            // During event
            var duringPhase = adapter.GetPhase(new DateTime(2025, 11, 1, 10, 15, 0, DateTimeKind.Utc));
            Assert.Equal(NewsPhase.Block, duringPhase);
            
            // After event
            var afterPhase = adapter.GetPhase(new DateTime(2025, 11, 1, 10, 31, 0, DateTimeKind.Utc));
            Assert.Equal(NewsPhase.Normal, afterPhase);
        }
        
        [Fact]
        public void JsonNewsAdapter_ReturnsMostRestrictive_WhenOverlapping()
        {
            var events = new List<(DateTime From, DateTime To, NewsPhase Phase)>
            {
                (new DateTime(2025, 11, 1, 10, 0, 0, DateTimeKind.Utc),
                 new DateTime(2025, 11, 1, 10, 30, 0, DateTimeKind.Utc),
                 NewsPhase.Restricted),
                (new DateTime(2025, 11, 1, 10, 10, 0, DateTimeKind.Utc),
                 new DateTime(2025, 11, 1, 10, 20, 0, DateTimeKind.Utc),
                 NewsPhase.Block)
            };
            
            var adapter = new JsonNewsAdapter(events);
            
            // At 10:15, both events are active - should return Block (more restrictive)
            var phase = adapter.GetPhase(new DateTime(2025, 11, 1, 10, 15, 0, DateTimeKind.Utc));
            Assert.Equal(NewsPhase.Block, phase);
            
            // At 10:25, only Restricted is active
            phase = adapter.GetPhase(new DateTime(2025, 11, 1, 10, 25, 0, DateTimeKind.Utc));
            Assert.Equal(NewsPhase.Restricted, phase);
        }
        
        [Fact]
        public void JsonNewsAdapter_HandlesMultipleOverlaps_CorrectPriority()
        {
            // Test priority: Block > UnwindOnly > Restricted > Normal
            var testCases = new[]
            {
                (Phases: new[] { NewsPhase.Block, NewsPhase.UnwindOnly }, Expected: NewsPhase.Block),
                (Phases: new[] { NewsPhase.Block, NewsPhase.Restricted }, Expected: NewsPhase.Block),
                (Phases: new[] { NewsPhase.UnwindOnly, NewsPhase.Restricted }, Expected: NewsPhase.UnwindOnly),
                (Phases: new[] { NewsPhase.Restricted, NewsPhase.Normal }, Expected: NewsPhase.Restricted)
            };
            
            foreach (var tc in testCases)
            {
                var events = new List<(DateTime From, DateTime To, NewsPhase Phase)>();
                var testTime = new DateTime(2025, 11, 1, 10, 0, 0, DateTimeKind.Utc);
                
                foreach (var phase in tc.Phases)
                {
                    events.Add((testTime.AddMinutes(-5), testTime.AddMinutes(5), phase));
                }
                
                var adapter = new JsonNewsAdapter(events);
                var result = adapter.GetPhase(testTime);
                
                Assert.Equal(tc.Expected, result);
            }
        }
        
        [Fact]
        public void JsonNewsAdapter_LoadsFromJson_Successfully()
        {
            string jsonPath = Path.Combine(_testDir, "news.json");
            string json = @"[
                {
                    ""from"": ""2025-11-01T10:00:00Z"",
                    ""to"": ""2025-11-01T10:30:00Z"",
                    ""phase"": ""Block""
                },
                {
                    ""from"": ""2025-11-01T14:00:00Z"",
                    ""to"": ""2025-11-01T14:15:00Z"",
                    ""phase"": ""Restricted""
                }
            ]";
            
            File.WriteAllText(jsonPath, json);
            
            var adapter = new JsonNewsAdapter(jsonPath);
            
            Assert.Equal(2, adapter.EventCount);
            
            var phase1 = adapter.GetPhase(new DateTime(2025, 11, 1, 10, 15, 0, DateTimeKind.Utc));
            Assert.Equal(NewsPhase.Block, phase1);
            
            var phase2 = adapter.GetPhase(new DateTime(2025, 11, 1, 14, 10, 0, DateTimeKind.Utc));
            Assert.Equal(NewsPhase.Restricted, phase2);
        }
        
        [Fact]
        public void JsonNewsAdapter_ReturnsNormal_WhenFileNotFound()
        {
            var adapter = new JsonNewsAdapter("nonexistent.json");
            
            Assert.Equal(0, adapter.EventCount);
            
            var phase = adapter.GetPhase(DateTime.UtcNow);
            Assert.Equal(NewsPhase.Normal, phase);
        }
        
        [Fact]
        public void JsonNewsAdapter_ReturnsNormal_WhenJsonEmpty()
        {
            string jsonPath = Path.Combine(_testDir, "empty.json");
            File.WriteAllText(jsonPath, "[]");
            
            var adapter = new JsonNewsAdapter(jsonPath);
            
            Assert.Equal(0, adapter.EventCount);
            
            var phase = adapter.GetPhase(DateTime.UtcNow);
            Assert.Equal(NewsPhase.Normal, phase);
        }
        
        [Fact]
        public void JsonNewsAdapter_HandlesInvalidJson_GraceFully()
        {
            string jsonPath = Path.Combine(_testDir, "invalid.json");
            File.WriteAllText(jsonPath, "{ invalid json }");
            
            var adapter = new JsonNewsAdapter(jsonPath);
            
            // Should default to empty events (Normal phase)
            Assert.Equal(0, adapter.EventCount);
            
            var phase = adapter.GetPhase(DateTime.UtcNow);
            Assert.Equal(NewsPhase.Normal, phase);
        }
        
        [Fact]
        public void JsonNewsAdapter_IsTimezoneAgnostic()
        {
            // All times in JSON should be UTC
            var events = new List<(DateTime From, DateTime To, NewsPhase Phase)>
            {
                (new DateTime(2025, 11, 1, 10, 0, 0, DateTimeKind.Utc),
                 new DateTime(2025, 11, 1, 10, 30, 0, DateTimeKind.Utc),
                 NewsPhase.Block)
            };
            
            var adapter = new JsonNewsAdapter(events);
            
            // Query with UTC time
            var phase = adapter.GetPhase(new DateTime(2025, 11, 1, 10, 15, 0, DateTimeKind.Utc));
            Assert.Equal(NewsPhase.Block, phase);
        }
        
        [Fact]
        public void NoNewsAdapter_AlwaysReturnsNormal()
        {
            var adapter = new NoNewsAdapter();
            
            var phase1 = adapter.GetPhase(DateTime.UtcNow);
            Assert.Equal(NewsPhase.Normal, phase1);
            
            var phase2 = adapter.GetPhase(DateTime.UtcNow.AddDays(100));
            Assert.Equal(NewsPhase.Normal, phase2);
        }
        
        [Fact]
        public void JsonNewsAdapter_HandlesEventAtBoundary()
        {
            var events = new List<(DateTime From, DateTime To, NewsPhase Phase)>
            {
                (new DateTime(2025, 11, 1, 10, 0, 0, DateTimeKind.Utc),
                 new DateTime(2025, 11, 1, 10, 30, 0, DateTimeKind.Utc),
                 NewsPhase.Block)
            };
            
            var adapter = new JsonNewsAdapter(events);
            
            // At exact start time - should be included
            var phaseAtStart = adapter.GetPhase(new DateTime(2025, 11, 1, 10, 0, 0, DateTimeKind.Utc));
            Assert.Equal(NewsPhase.Block, phaseAtStart);
            
            // At exact end time - should be excluded (end is exclusive)
            var phaseAtEnd = adapter.GetPhase(new DateTime(2025, 11, 1, 10, 30, 0, DateTimeKind.Utc));
            Assert.Equal(NewsPhase.Normal, phaseAtEnd);
        }
        
        [Fact]
        public void JsonNewsAdapter_SortsEventsByStartTime()
        {
            // Events in reverse chronological order
            var events = new List<(DateTime From, DateTime To, NewsPhase Phase)>
            {
                (new DateTime(2025, 11, 1, 14, 0, 0, DateTimeKind.Utc),
                 new DateTime(2025, 11, 1, 14, 15, 0, DateTimeKind.Utc),
                 NewsPhase.Restricted),
                (new DateTime(2025, 11, 1, 10, 0, 0, DateTimeKind.Utc),
                 new DateTime(2025, 11, 1, 10, 30, 0, DateTimeKind.Utc),
                 NewsPhase.Block)
            };
            
            var adapter = new JsonNewsAdapter(events);
            
            // Should still find correct phase despite unsorted input
            var phase1 = adapter.GetPhase(new DateTime(2025, 11, 1, 10, 15, 0, DateTimeKind.Utc));
            Assert.Equal(NewsPhase.Block, phase1);
            
            var phase2 = adapter.GetPhase(new DateTime(2025, 11, 1, 14, 10, 0, DateTimeKind.Utc));
            Assert.Equal(NewsPhase.Restricted, phase2);
        }
    }
}

