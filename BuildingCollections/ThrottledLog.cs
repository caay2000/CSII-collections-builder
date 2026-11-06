using System;
using System.Collections.Generic;

namespace BuildingCollections
{
    public sealed class ThrottledLog
    {
        readonly TimeSpan interval;
        readonly Dictionary<string, (DateTime at, int skipped)> seen = new Dictionary<string, (DateTime, int)>();

        public ThrottledLog(TimeSpan interval)
        {
            this.interval = interval;
        }

        public void Error(Exception e) => Write(e.GetType().FullName + e.Message, skipped => Mod.Log.Error(Suffix(e.ToString(), skipped)));

        public void Info(string key, string message) => Write(key, skipped => Mod.Log.Info(Suffix(message, skipped)));

        void Write(string key, Action<int> log)
        {
            var now = DateTime.UtcNow;
            if (seen.TryGetValue(key, out var last) && now - last.at < interval)
            {
                seen[key] = (last.at, last.skipped + 1);
                return;
            }

            log(last.skipped);
            seen[key] = (now, 0);
        }

        static string Suffix(string message, int skipped) => skipped > 0 ? $"{message} (repeated {skipped} times without logging)" : message;
    }
}
