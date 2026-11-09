using System;
using System.Collections.Generic;

namespace CarSales.Foundation
{
    /// <summary>
    /// Upgrades a module's JSON from an old version to the current one, one step at a time.
    /// Register a step whenever a module's data format changes (spec rule R8):
    /// <code>migrations.Register("game", 5, json => ...returns version-6 json...);</code>
    /// </summary>
    public sealed class SaveMigrations
    {
        readonly Dictionary<string, SortedDictionary<int, Func<string, string>>> steps =
            new Dictionary<string, SortedDictionary<int, Func<string, string>>>();

        /// <summary>Adds a step that turns version <paramref name="fromVersion"/> data into version fromVersion + 1.</summary>
        public void Register(string key, int fromVersion, Func<string, string> step)
        {
            if (!steps.TryGetValue(key, out var chain)) steps[key] = chain = new SortedDictionary<int, Func<string, string>>();
            chain[fromVersion] = step ?? throw new ArgumentNullException(nameof(step));
        }

        /// <summary>
        /// Brings <paramref name="module"/> up to <paramref name="targetVersion"/>. Versions without a registered step
        /// are passed through unchanged (the data was compatible). Throws if the save is NEWER than this game.
        /// </summary>
        public void Migrate(SaveModule module, int targetVersion)
        {
            if (module == null) return;
            if (module.Version > targetVersion)
                throw new InvalidOperationException(
                    $"Save module '{module.Key}' is version {module.Version}, but this game only understands up to {targetVersion}. Update the game.");
            steps.TryGetValue(module.Key, out var chain);
            while (module.Version < targetVersion)
            {
                if (chain != null && chain.TryGetValue(module.Version, out var step)) module.Data = step(module.Data);
                module.Version++;
            }
        }
    }
}
