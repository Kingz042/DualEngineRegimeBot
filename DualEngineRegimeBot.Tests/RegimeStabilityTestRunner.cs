using Xunit;
using Xunit.Abstractions;
using DualEngineRegimeBot.Tests.Data;

namespace DualEngineRegimeBot.Tests
{
    /// <summary>
    /// xUnit test wrapper for regime stability test.
    /// Run this to execute the regime stability sanity check from your test suite.
    /// 
    /// To run:
    ///   dotnet test --filter "FullyQualifiedName~RegimeStabilityTestRunner"
    /// </summary>
    public class RegimeStabilityTestRunner
    {
        private readonly ITestOutputHelper _output;

        public RegimeStabilityTestRunner(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Runs the regime stability test with synthetic data.
        /// This is a sanity check - replace StubBarLoader with real data for production validation.
        /// </summary>
        [Fact(Skip = "Manual test - uncomment Skip attribute to run")]
        // Remove the Skip parameter above to enable this test
        public void RunRegimeStabilityTest_WithSyntheticData()
        {
            // Redirect console output to xUnit test output
            var originalOut = System.Console.Out;
            var writer = new System.IO.StringWriter();
            System.Console.SetOut(writer);

            try
            {
                // Run the test with stub data
                RegimeStabilityRunner.Run(
                    barLoader: new StubBarLoader(),
                    symbol: "XAUUSD",
                    barCount: 200,
                    atrFloor: 1.0
                );

                // Capture output
                var output = writer.ToString();
                _output.WriteLine(output);

                // Verify output file was created
                Assert.True(System.IO.File.Exists("regime_test.csv"), "regime_test.csv should be created");

                // Verify CSV has expected row count (200 bars + 1 header)
                var lines = System.IO.File.ReadAllLines("regime_test.csv");
                Assert.True(lines.Length == 201, $"Expected 201 lines (1 header + 200 data), got {lines.Length}");

                _output.WriteLine("✓ Regime stability test passed!");
            }
            finally
            {
                System.Console.SetOut(originalOut);
            }
        }

        /// <summary>
        /// Example of how to wire your real data loader.
        /// Uncomment and modify when you have a real data source.
        /// </summary>
        /*
        [Fact(Skip = "Requires real data source")]
        public void RunRegimeStabilityTest_WithRealData()
        {
            // Replace with your actual bar loader implementation
            var barLoader = new YourRealBarLoader();
            
            var originalOut = System.Console.Out;
            var writer = new System.IO.StringWriter();
            System.Console.SetOut(writer);

            try
            {
                RegimeStabilityRunner.Run(
                    barLoader: barLoader,
                    symbol: "XAUUSD",
                    barCount: 200,
                    atrFloor: 1.0
                );

                var output = writer.ToString();
                _output.WriteLine(output);

                Assert.True(System.IO.File.Exists("regime_test.csv"));
            }
            finally
            {
                System.Console.SetOut(originalOut);
            }
        }
        */
    }
}

