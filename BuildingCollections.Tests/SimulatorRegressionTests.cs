using System.IO;
using BuildingCollections.Simulator;
using Xunit;

namespace BuildingCollections.Tests
{
    public class SimulatorRegressionTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void No_simulator_scenario_gets_worse_than_the_recorded_baseline(bool angled)
        {
            string path = BaselineStore.PathFor(angled);
            Assert.True(File.Exists(path), $"Missing {path}: run the simulator with 'regress --update'.");
            Simulation.Plain = Simulation.Catalogs[Simulation.DefaultCatalog];
            Simulation.WithAngled = angled;
            try
            {
                var expected = BaselineStore.Load(path);
                var seeds = Regression.Seeds();
                Assert.Null(BaselineStore.Mismatch(expected, Simulation.RegressionCatalog, seeds));

                var verdict = Regression.Compare(expected, Regression.Measure(Scenarios.All(), Simulation.RegressionCatalog, seeds));

                Assert.True(verdict.Passed, string.Join("\n", verdict.Failures()));
            }
            finally
            {
                Simulation.WithAngled = false;
            }
        }
    }
}
