using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TienTuyen.Editor
{
    /// <summary>
    /// Batch runs (tests, builds, captures) close the com.unity.pipeline server, which otherwise lets any agent
    /// that finds this Editor stop its Play mode mid-test. The interactive Editor keeps it for the unity CLI.
    /// </summary>
    [InitializeOnLoad]
    internal static class BatchPipelineServerOff
    {
        static BatchPipelineServerOff()
        {
            if (!Application.isBatchMode) return;
            // The package's owner is internal; touching it runs its own start first, so the stop always wins.
            var owner = System.Type.GetType("Unity.Pipeline.Editor.PipelineServerStartup, Unity.Pipeline.Editor");
            var stop = owner?.GetMethod("StopServer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (stop == null) Debug.LogWarning("Pipeline server not found; batch run keeps it open.");
            else stop.Invoke(null, null);
        }
    }
}
