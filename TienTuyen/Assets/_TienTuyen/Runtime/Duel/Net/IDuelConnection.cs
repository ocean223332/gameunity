using System;

namespace TienTuyen.Duel.Net
{
    public enum DuelConnectionState { Offline, Starting, Listening, Connected, Failed }

    /// <summary>
    /// How two machines find each other, kept apart from what they say once connected
    /// (<see cref="DuelNetworkBootstrap"/> and its <see cref="DuelNetChannel"/>). LAN today; a Relay
    /// implementation drops in behind the same calls.
    /// </summary>
    public interface IDuelConnection
    {
        DuelConnectionState State { get; }

        /// <summary>What the host gives their friend: "192.168.1.14:7777" on LAN, a join code on Relay.</summary>
        string JoinInfo { get; }

        /// <summary>Why the last attempt failed or ended, in plain Vietnamese ready to show as is.</summary>
        string LastError { get; }

        void BeginHost();

        /// <summary>"ip" or "ip:port" on LAN; a join code on Relay.</summary>
        void BeginClient(string joinTarget);

        /// <summary>Pump every frame; drives setup that spans frames (deferred restarts, timeouts, Relay allocation).</summary>
        void Tick();

        void Shutdown();

        event Action<DuelConnectionState> StateChanged;
    }
}
