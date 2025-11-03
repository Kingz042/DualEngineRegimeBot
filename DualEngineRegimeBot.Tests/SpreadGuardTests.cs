using System;
using Xunit;
using DualEngineRegimeBot.Core.Config;
using DualEngineRegimeBot.Core.Risk;

namespace DualEngineRegimeBot.Tests
{
    public class SpreadGuardTests
    {
        [Fact]
        public void SpreadGuard_BlocksWideSpread()
        {
            var config = new RiskConfig { SpreadGuardMultiplier = 1.5 };
            var risk = new RiskService(config, 10000);
            
            double medianSpread = 2.0;
            double wideSpread = 4.0; // 2× median
            
            bool blocked = risk.IsSpreadTooWide(wideSpread, medianSpread);
            Assert.True(blocked);
        }
        
        [Fact]
        public void SpreadGuard_AllowsNormalSpread()
        {
            var config = new RiskConfig { SpreadGuardMultiplier = 1.5 };
            var risk = new RiskService(config, 10000);
            
            double medianSpread = 2.0;
            double normalSpread = 2.5; // 1.25× median
            
            bool blocked = risk.IsSpreadTooWide(normalSpread, medianSpread);
            Assert.False(blocked);
        }
    }
}

