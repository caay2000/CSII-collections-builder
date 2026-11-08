using System.IO;
using System.Linq;
using BuildingCollections.Simulator;
using Xunit;

namespace BuildingCollections.Tests
{
    public class SimulatorRegressionTests
    {
        [Fact]
        public void No_simulator_scenario_gets_worse_than_the_recorded_baseline()
        {
            Assert.True(File.Exists(BaselineStore.DefaultPath), $"Missing {BaselineStore.DefaultPath}: run the simulator with 'regress --update'.");
            Simulation.Plain = Simulation.Catalogs[Simulation.DefaultCatalog];
            var expected = BaselineStore.Load(BaselineStore.DefaultPath);
            var seeds = Regression.Seeds();
            Assert.Null(BaselineStore.Mismatch(expected, Simulation.DefaultCatalog, seeds));

            var verdict = Regression.Compare(expected, Regression.Measure(Scenarios.All(), Simulation.DefaultCatalog, seeds));

            Assert.True(verdict.Passed, string.Join("\n", verdict.Failures()));
        }
    }
}
