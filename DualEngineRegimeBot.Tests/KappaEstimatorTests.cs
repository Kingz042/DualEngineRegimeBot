using System;
using Xunit;
using DualEngineRegimeBot.Core.Indicators;

namespace DualEngineRegimeBot.Tests
{
    public class KappaEstimatorTests
    {
        [Fact]
        public void Kappa_ConvergesOnMeanRevertingSeries()
        {
            var kappa = new KappaEstimator(50, 10, 0.01, 2.0);
            double mean = 100.0;
            var random = new Random(42);
            
            // Simulate OU process
            for (int i = 0; i < 200; i++)
            {
                double noise = (random.NextDouble() - 0.5) * 2.0;
                double price = mean + noise;
                kappa.Update(price, mean);
            }
            
            double kappaEst = kappa.GetKappa();
            Assert.True(kappaEst > 0.01 && kappaEst < 2.0);
            Assert.True(kappa.IsReady());
        }
        
        [Fact]
        public void Kappa_ClampsToValidRange()
        {
            var kappa = new KappaEstimator(50, 10, 0.05, 1.5);
            
            // Feed extreme data
            for (int i = 0; i < 100; i++)
            {
                kappa.Update(i * 100, 50);
            }
            
            double kappaEst = kappa.GetKappa();
            Assert.InRange(kappaEst, 0.05, 1.5);
        }
    }
}

