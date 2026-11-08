using System;

namespace BuildingCollections.Simulator
{
    public readonly struct Slack
    {
        readonly double fixedPart;
        readonly double perRoot;
        readonly double share;

        Slack(double fixedPart, double perRoot, double share)
        {
            this.fixedPart = fixedPart;
            this.perRoot = perRoot;
            this.share = share;
        }

        public static Slack Noise(double fixedPart, double perRoot) => new Slack(fixedPart, perRoot, 0);

        public static Slack Share(double fixedPart, double share) => new Slack(fixedPart, 0, share);

        public double Allowed(double baseline) => fixedPart + perRoot * Math.Sqrt(Math.Max(0, baseline)) + share * baseline;
    }

    public sealed class MetricRule
    {
        public string Name { get; }
        public Func<Metrics, double> Value { get; }
        public Func<Metrics, Slack> PerScenario { get; }
        public Func<float, Slack> PerOverlap { get; }

        public MetricRule(string name, Func<Metrics, double> value, Func<Metrics, Slack> perScenario, Func<float, Slack> perOverlap)
        {
            Name = name;
            Value = value;
            PerScenario = perScenario;
            PerOverlap = perOverlap;
        }
    }
}
