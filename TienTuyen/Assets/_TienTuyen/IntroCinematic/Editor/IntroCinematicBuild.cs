using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Builds the Windows player used to film the intro (launch it with -intro &lt;outDir&gt;).</summary>
public static class IntroCinematicBuild
{
    private const string ScenePath = "Assets/_TienTuyen/Scenes/CombatSpike.unity";

    [MenuItem("Tien Tuyen/Intro/Build Capture Player")]
    public static void BuildCapturePlayer()
    {
        string project = Directory.GetParent(Application.dataPath).FullName;
        string output = Path.GetFullPath(Path.Combine(project, "../Builds/IntroCapture/TienTuyen.exe"));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath }, locationPathName = output,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Intro capture build failed: {report.summary.totalErrors} errors");
        Debug.Log($"Intro capture build succeeded: {output}");
    }
}
