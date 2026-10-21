using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using TienTuyen.Duel.Net;
using UnityEngine;

namespace TienTuyen.Duel.Play
{
    /// <summary>
    /// With <c>-duelstats</c> on the command line, writes one row per frame of a running duel to
    /// %TEMP%/TienTuyenDuel/stats_&lt;process id&gt;.csv: frame time, server ticks, snapshot arrivals, reconciliation and
    /// the host's queue of joiner input. Rows carry the wall clock, not Stopwatch (each process has its own zero), so
    /// a host's and a joiner's file on one machine line up to measure how old the opponent is when it is drawn.
    /// </summary>
    internal sealed class DuelNetStats : IDisposable
    {
        public const string Flag = "-duelstats";
        private readonly StreamWriter writer;
        private int rows;

        private DuelNetStats(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            writer = new StreamWriter(path, false);
            writer.WriteLine("ms,dt_ms,slot,stage,server_tick,opponent_tick,snapshots,replayed,correction_mm,backlog,starved," +
                             "local_x,local_z,opponent_x,opponent_z");
            UnityEngine.Debug.Log("Duel stats: writing " + path);
        }

        /// <summary>The log when the flag is given, else null.</summary>
        public static DuelNetStats Open() => Array.IndexOf(Environment.GetCommandLineArgs(), Flag) < 0 ? null :
            new DuelNetStats(Path.Combine(Path.GetTempPath(), "TienTuyenDuel", "stats_" + Process.GetCurrentProcess().Id + ".csv"));

        public void Write(DuelSession session, float deltaTime)
        {
            if (!session.HasState) return;
            var local = session.LocalState;
            var opponent = session.Opponent;
            var correction = session.TakeLargestReconciliation();
            writer.WriteLine(string.Join(",",
                F(DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerMillisecond), F(deltaTime * 1000.0), session.LocalSlot.ToString(),
                session.Stage.ToString(), session.Match.ServerTick.ToString(), opponent.ServerTick.ToString(),
                session.SnapshotsReceived.ToString(), correction.ReplayedInputs.ToString(), F(correction.PositionError * 1000.0),
                session.JoinerInputBacklog.ToString(), session.JoinerStarvedSteps.ToString(),
                F(local.Position.x), F(local.Position.z), F(opponent.Position.x), F(opponent.Position.z)));
            if (++rows % 60 == 0) writer.Flush();
        }

        public void Dispose() => writer.Dispose();

        private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
