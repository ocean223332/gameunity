using UnityEngine;

namespace TienTuyen.Presentation
{
    /// <summary>One desktop frame budget shared by menus, combat, and the duel scene.</summary>
    public sealed class FrameBudgetPolicy : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (Application.isEditor || FindFirstObjectByType<FrameBudgetPolicy>() != null) return;
            var owner = new GameObject("FrameBudgetPolicy");
            DontDestroyOnLoad(owner);
            owner.AddComponent<FrameBudgetPolicy>();
        }

        private void Awake() => ApplyCurrentFocus(Application.isFocused);

        private void OnApplicationFocus(bool focused) => ApplyCurrentFocus(focused);

        private static void ApplyCurrentFocus(bool focused)
        {
            // Release players cannot use a benchmark argument to bypass their gameplay budget.
            ApplyFocus(focused, Debug.isDebugBuild && GraphicsBenchmark.Requested);
        }

        public static void ApplyFocus(bool focused, bool benchmark)
        {
            // The benchmark owns its selected rate, including an explicit capped acceptance run.
            if (benchmark) return;
            Apply(focused, false);
        }

        public static void Apply(bool focused, bool benchmark)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = benchmark ? -1 : focused ? 60 : 15;
        }
    }
}
