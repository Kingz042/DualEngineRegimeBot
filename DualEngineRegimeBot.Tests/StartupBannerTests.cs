using System;
using System.IO;
using Xunit;
using DualEngineRegimeBot.Core.Config;
using DualEngineRegimeBot.Hosts.cTrader;
using DualEngineRegimeBot.Hosts.cTrader.Adapters;

namespace DualEngineRegimeBot.Tests
{
    /// <summary>
    /// Tests for startup banner output validation.
    /// </summary>
    public class StartupBannerTests
    {
        [Fact]
        public void StartupBanner_ContainsAppVersion()
        {
            var config = new HostConfig { AppVersion = "1.2.0-test" };
            var output = CaptureStartupBanner(config);
            
            Assert.Contains("App Version", output);
            Assert.Contains("1.2.0-test", output);
        }
        
        [Fact]
        public void StartupBanner_ContainsConfigVersionTag()
        {
            var preset = FtmoPreset.CreateDefault() with { VersionTag = "FTMO_Custom_v2" };
            var config = new HostConfig { Preset = preset };
            var output = CaptureStartupBanner(config);
            
            Assert.Contains("Config Version", output);
            Assert.Contains("FTMO_Custom_v2", output);
        }
        
        [Fact]
        public void StartupBanner_ContainsConfigHash()
        {
            var config = new HostConfig();
            var output = CaptureStartupBanner(config);
            
            Assert.Contains("Config Hash", output);
            
            // Verify hash is present and looks like SHA-256 (64 hex characters)
            string hash = config.Preset.ConfigHashSha256();
            Assert.Contains(hash, output);
            Assert.Equal(64, hash.Length);
        }
        
        [Fact]
        public void StartupBanner_ContainsBrokerUtcOffset()
        {
            var preset = FtmoPreset.CreateDefault() with { BrokerUtcOffsetHours = 2 };
            var config = new HostConfig { Preset = preset };
            var output = CaptureStartupBanner(config);
            
            Assert.Contains("Broker UTC Offset", output);
            Assert.Contains("+2", output);
        }
        
        [Fact]
        public void StartupBanner_ContainsSessionWindows()
        {
            var preset = FtmoPreset.CreateDefault() with
            {
                SessionStartHour = 7,
                SessionEndHour = 21
            };
            var config = new HostConfig { Preset = preset };
            var output = CaptureStartupBanner(config);
            
            Assert.Contains("Session Window", output);
            Assert.Contains("07:00", output);
            Assert.Contains("21:00", output);
            Assert.Contains("end exclusive", output.ToLower());
        }
        
        [Fact]
        public void StartupBanner_ContainsAllRequiredFields()
        {
            var config = new HostConfig();
            var output = CaptureStartupBanner(config);
            
            // Verify all required fields from spec
            var requiredTokens = new[]
            {
                "App Version",
                "Config Version",
                "Config Hash",
                "Broker UTC Offset",
                "Session Window",
                "end exclusive"
            };
            
            foreach (var token in requiredTokens)
            {
                Assert.Contains(token, output, StringComparison.OrdinalIgnoreCase);
            }
        }
        
        [Fact]
        public void StartupBanner_DisplaysSymbol()
        {
            var config = new HostConfig { Symbol = "EURUSD" };
            var output = CaptureStartupBanner(config);
            
            Assert.Contains("Symbol", output);
            Assert.Contains("EURUSD", output);
        }
        
        [Fact]
        public void StartupBanner_DisplaysRiskParameters()
        {
            var preset = FtmoPreset.CreateDefault() with
            {
                MaxRiskPercentPerTrade = 0.5,
                MaxDailyLossPercent = 5.0,
                MaxDrawdownPercent = 10.0
            };
            var config = new HostConfig { Preset = preset };
            var output = CaptureStartupBanner(config);
            
            Assert.Contains("Max Risk/Trade", output);
            Assert.Contains("0.50%", output);
            Assert.Contains("Max Daily Loss", output);
            Assert.Contains("5.00%", output);
            Assert.Contains("Max Drawdown", output);
            Assert.Contains("10.00%", output);
        }
        
        [Fact]
        public void StartupBanner_DisplaysNewsSource()
        {
            var config = new HostConfig { NewsSource = "json" };
            var output = CaptureStartupBanner(config);
            
            Assert.Contains("News Source", output);
            Assert.Contains("json", output);
        }
        
        [Fact]
        public void StartupBanner_HandlesNegativeBrokerOffset()
        {
            var preset = FtmoPreset.CreateDefault() with { BrokerUtcOffsetHours = -5 };
            var config = new HostConfig { Preset = preset };
            var output = CaptureStartupBanner(config);
            
            Assert.Contains("Broker UTC Offset", output);
            Assert.Contains("-5", output);
        }
        
        [Fact]
        public void StartupBanner_DisplaysTimestamp()
        {
            var config = new HostConfig();
            var output = CaptureStartupBanner(config);
            
            Assert.Contains("Started", output);
            Assert.Contains("UTC", output);
        }
        
        private string CaptureStartupBanner(HostConfig config)
        {
            // Capture console output
            var originalOut = Console.Out;
            try
            {
                using var writer = new StringWriter();
                Console.SetOut(writer);
                
                // Create host which prints banner in constructor/startup
                var marketData = new MockMarketDataAdapter();
                var orderAdapter = new MockOrderAdapter();
                var host = new cTraderHost(config, marketData, orderAdapter);
                
                // Call RunAsync but cancel immediately to trigger banner without running
                using var cts = new System.Threading.CancellationTokenSource();
                cts.Cancel();
                
                try
                {
                    host.RunAsync(cts.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
                
                return writer.ToString();
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}

