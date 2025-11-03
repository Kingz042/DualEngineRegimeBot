using Xunit;
using Xunit.Abstractions;
using DualEngineRegimeBot.Tests.Data;

namespace DualEngineRegimeBot.Tests
{
    /// <summary>
    /// xUnit test wrapper for SMS sanity test.
    /// Run this to execute the SMS sanity check from your test suite.
    /// 
    /// To run:
    ///   dotnet test --filter "FullyQualifiedName~SMSSanityTestRunner"
    /// </summary>
    public class SMSSanityTestRunner
    {
        private readonly ITestOutputHelper _output;

        public SMSSanityTestRunner(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Runs the SMS sanity test with synthetic data.
        /// This is a sanity check - replace StubSmsEngine with real SMS engine for production validation.
        /// </summary>
        [Fact(Skip = "Manual test - remove Skip attribute to run")]
        // Remove the Skip parameter above to enable this test
        public void RunSMSSanityTest_WithSyntheticData()
        {
            // Redirect console output to xUnit test output
            var originalOut = System.Console.Out;
            var writer = new System.IO.StringWriter();
            System.Console.SetOut(writer);

            try
            {
                // Run the test with stub implementations
                SMSSanityRunner.Run(
                    smsEngine: new StubSmsEngine(),
                    barLoader: new StubBarLoader(),
                    symbol: "XAUUSD",
                    barCount: 1000,
                    atrFloor: 0.5
                );

                // Capture output
                var output = writer.ToString();
                _output.WriteLine(output);

                // Verify output contains expected sections
                Assert.Contains("SMS range:", output);
                Assert.Contains("Vol/Quiet ratio:", output);
                Assert.Contains("ExecMult clamp observed:", output);

                _output.WriteLine("✓ SMS sanity test passed!");
            }
            finally
            {
                System.Console.SetOut(originalOut);
            }
        }

        /// <summary>
        /// Example of how to wire your real SMS engine.
        /// Uncomment and modify when you have a real SMS engine implementation.
        /// </summary>
        /*
        [Fact(Skip = "Requires real SMS engine")]
        public void RunSMSSanityTest_WithRealEngine()
        {
            // Replace with your actual SMS engine implementation
            var smsEngine = new YourRealSmsEngine();
            
            // Replace with your actual bar loader implementation
            var barLoader = new YourRealBarLoader();
            
            var originalOut = System.Console.Out;
            var writer = new System.IO.StringWriter();
            System.Console.SetOut(writer);

            try
            {
                SMSSanityRunner.Run(
                    smsEngine: smsEngine,
                    barLoader: barLoader,
                    symbol: "XAUUSD",
                    barCount: 1000,
                    atrFloor: 0.5
                );

                var output = writer.ToString();
                _output.WriteLine(output);

                Assert.Contains("SMS range:", output);
                Assert.Contains("Vol/Quiet ratio:", output);
                Assert.Contains("ExecMult clamp observed:", output);
            }
            finally
            {
                System.Console.SetOut(originalOut);
            }
        }
        */
    }
}

