using System.Collections.Generic;
using NUnit.Framework;
using TienTuyen.Presentation;
using UnityEngine;

namespace TienTuyen.Combat.Tests
{
    /// <summary>The weather is a LightingPreset picked per run: random but never the last run's, pinned by -lighting.</summary>
    public sealed class LightingPresetTests
    {
        [Test]
        public void EveryPresetLightsTheSceneAndRunsReachAllOfThemNeverTwiceInARowUnlessOneIsForced()
        {
            var presets = LightingPreset.All;
            Assert.That(presets.Length, Is.GreaterThanOrEqualTo(2), "Misty and Overcast at least");
            foreach (var preset in presets)
            {
                // A preset whose sun points up or has no strength leaves the soldiers unreadable.
                Assert.That(preset.SunForward.normalized.y, Is.LessThan(-.2f), preset.name + " sun must shine down");
                Assert.That(preset.SunIntensity, Is.GreaterThan(.3f), preset.name);
                Assert.That(preset.AmbientIntensity, Is.GreaterThan(.3f), preset.name);
            }

            string forced = LightingPreset.Forced;
            string saved = PlayerPrefs.HasKey(LightingPreset.LastKey) ? PlayerPrefs.GetString(LightingPreset.LastKey) : null;
            try
            {
                LightingPreset.Forced = null;
                var seen = new HashSet<LightingPreset>();
                LightingPreset previous = null;
                for (int run = 0; run < 64; run++)
                {
                    var preset = LightingPreset.NextRun();
                    Assert.That(preset, Is.Not.SameAs(previous), "Two runs in a row got the same weather");
                    seen.Add(preset);
                    previous = preset;
                }
                Assert.That(seen.Count, Is.EqualTo(presets.Length), "Runs must reach every preset");

                // After a restart only the saved name is left, and the next run must still differ from it.
                foreach (var last in presets)
                {
                    PlayerPrefs.SetString(LightingPreset.LastKey, last.name);
                    Assert.That(LightingPreset.ForMenu(), Is.SameAs(last), "The menu shows the last run's weather");
                    for (int run = 0; run < 16; run++)
                    {
                        PlayerPrefs.SetString(LightingPreset.LastKey, last.name);
                        Assert.That(LightingPreset.NextRun(), Is.Not.SameAs(last), "The run after a restart got the last run's weather");
                    }
                }

                LightingPreset.Forced = presets[presets.Length - 1].name;
                for (int run = 0; run < 8; run++) Assert.That(LightingPreset.NextRun(), Is.SameAs(presets[presets.Length - 1]));
            }
            finally
            {
                LightingPreset.Forced = forced;
                if (saved == null) PlayerPrefs.DeleteKey(LightingPreset.LastKey);
                else PlayerPrefs.SetString(LightingPreset.LastKey, saved);
            }
        }
    }
}
