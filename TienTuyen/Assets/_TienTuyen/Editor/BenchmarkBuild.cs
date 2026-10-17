using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TienTuyen.Editor
{
    /// <summary>
    /// Development Windows build for graphics measurement, written to Builds/Benchmark (gitignored).
    /// Run it with <c>TienTuyen.exe -benchmark DIR</c>; see GraphicsBenchmark.
    /// </summary>
    public static class BenchmarkBuild
    {
        [MenuItem("Tien Tuyen/Combat/Build Benchmark (Development)")]
        public static void Build()
        {
            string project = Directory.GetParent(Application.dataPath).FullName;
            string output = Path.GetFullPath(Path.Combine(project, "../Builds/Benchmark/TienTuyen.exe"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { CombatSceneSetup.ScenePath }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            Debug.Log("[BenchmarkBuild] " + report.summary.result + " " + output + " " + report.summary.totalTime.TotalSeconds.ToString("F0") + " s");
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Benchmark build failed: " + report.summary.totalErrors + " errors");
        }
    }
}
