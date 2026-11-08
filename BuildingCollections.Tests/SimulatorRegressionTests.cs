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
            Assert.True(File.Exists(Regression.BaselinePath), $"Missing {Regression.BaselinePath}: run the simulator with 'regress --update'.");
            Simulation.Plain = Simulation.Catalogs[Simulation.DefaultCatalog];
            var expected = Regression.Load(Regression.BaselinePath);
            var seeds = Regression.Seeds();
            Assert.Null(Regression.Mismatch(expected, Simulation.DefaultCatalog, seeds));

            var verdict = Regression.Compare(expected, Regression.Measure(Scenarios.All(), Simulation.DefaultCatalog, seeds));

            Assert.True(verdict.Passed, string.Join("\n", verdict.Regressions.Concat(verdict.Missing.Select(m => "missing " + m))));
        }
    }
}
