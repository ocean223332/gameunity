using System.Reflection;
using NUnit.Framework;
using TienTuyen.Presentation;
using UnityEngine;

namespace TienTuyen.Combat.Tests
{
    public sealed class FrameBudgetPolicyTests
    {
        private int previousTarget;
        private int previousVSync;

        [SetUp]
        public void RememberSettings()
        {
            previousTarget = Application.targetFrameRate;
            previousVSync = QualitySettings.vSyncCount;
        }

        [TearDown]
        public void RestoreSettings()
        {
            Application.targetFrameRate = previousTarget;
            QualitySettings.vSyncCount = previousVSync;
        }

        [TestCase(true, false, 60)]
        [TestCase(false, false, 15)]
        [TestCase(true, true, -1)]
        [TestCase(false, true, -1)]
        public void AppliesGameplayFocusAndBenchmarkBudget(bool focused, bool benchmark, int expectedRate)
        {
            QualitySettings.vSyncCount = 1;
            Apply(focused, benchmark);
            Assert.That(Application.targetFrameRate, Is.EqualTo(expectedRate));
            Assert.That(QualitySettings.vSyncCount, Is.Zero,
                "A 240 Hz display must not override the explicit 60 FPS gameplay budget.");
        }

        [Test]
        public void FocusReturnAndBenchmarkExitRestoreGameplayBudget()
        {
            Apply(true, false);
            Apply(false, false);
            Assert.That(Application.targetFrameRate, Is.EqualTo(15));
            Apply(true, false);
            Assert.That(Application.targetFrameRate, Is.EqualTo(60));
            Apply(true, true);
            Assert.That(Application.targetFrameRate, Is.EqualTo(-1));
            Apply(true, false);
            Assert.That(Application.targetFrameRate, Is.EqualTo(60));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FocusChangePreservesBenchmarkSelectedFrameRate(bool focused)
        {
            Application.targetFrameRate = 60;
            var type = typeof(CombatEnvironment).Assembly.GetType("TienTuyen.Presentation.FrameBudgetPolicy");
            Assert.That(type, Is.Not.Null);
            var focus = type.GetMethod("ApplyFocus", BindingFlags.Public | BindingFlags.Static);
            Assert.That(focus, Is.Not.Null, "Focus callbacks must preserve a benchmark's explicit -fps budget.");
            focus.Invoke(null, new object[] { focused, true });
            Assert.That(Application.targetFrameRate, Is.EqualTo(60));
        }

        private static void Apply(bool focused, bool benchmark)
        {
            var type = typeof(CombatEnvironment).Assembly.GetType("TienTuyen.Presentation.FrameBudgetPolicy");
            Assert.That(type, Is.Not.Null, "The runtime currently leaves gameplay uncapped.");
            var apply = type.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static);
            Assert.That(apply, Is.Not.Null);
            apply.Invoke(null, new object[] { focused, benchmark });
        }
    }
}
