using UnityEngine;

namespace TienTuyen.Duel.Presentation
{
    /// <summary>
    /// What the lobby screen drives: hosting a match or joining one by address. The connection layer implements it;
    /// the screen only reads these values every frame and forwards the player's requests.
    /// </summary>
    public interface IDuelLobbyHost
    {
        /// <summary>While hosting, the address the opponent types in (for example "192.168.1.23:7777"); empty otherwise.</summary>
        string JoinInfo { get; }
        /// <summary>One short line about what is happening now; empty when idle.</summary>
        string Status { get; }
        /// <summary>The last failure, in words a player understands; empty when there is none.</summary>
        string LastError { get; }
        /// <summary>True while hosting or connecting; the screen then offers only Cancel.</summary>
        bool Busy { get; }
        void RequestHost();
        void RequestJoin(string target);
        void RequestCancel();
    }

    public enum DuelRoundResult { None, Won, Lost, Draw }

    /// <summary>
    /// Everything the duel HUD shows, seen from the local player. Read-only and polled every frame; the match logic
    /// (or a fake) implements it. "Local" is the player at this screen, "Opponent" the other one.
    /// </summary>
    public interface IDuelHudState
    {
        DuelPhase Phase { get; }
        /// <summary>The round being played or just decided, from 1.</summary>
        int Round { get; }
        /// <summary>Rounds needed to take the match: 2 in a best of 3.</summary>
        int RoundsToWin { get; }
        int LocalRounds { get; }
        int OpponentRounds { get; }
        /// <summary>Seconds left in the round while fighting; the round length during the countdown.</summary>
        float RoundRemaining { get; }
        /// <summary>Seconds until the round starts, during <see cref="DuelPhase.Warmup"/>.</summary>
        float CountdownRemaining { get; }
        DuelRoundResult LastRound { get; }
        /// <summary>Valid in <see cref="DuelPhase.MatchOver"/>.</summary>
        bool MatchWon { get; }

        string LocalName { get; }
        string OpponentName { get; }
        float LocalHealth { get; }
        float LocalMaxHealth { get; }
        float OpponentHealth { get; }
        float OpponentMaxHealth { get; }

        /// <summary>Content id of the weapon in hand (picks its glyph), and its display name.</summary>
        string WeaponId { get; }
        string WeaponName { get; }
        int Ammo { get; }
        int MagazineSize { get; }
        /// <summary>Seconds left on a reload; zero when not reloading.</summary>
        float ReloadRemaining { get; }
        float ReloadDuration { get; }
    }

    /// <summary>
    /// The local player's view, read by whatever turns controls into a <see cref="DuelInputFrame"/> each frame: the duel
    /// camera implements it and owns look, aim-down-sights and the cursor lock, so the input side reads keys only.
    /// </summary>
    public interface IDuelAimSource
    {
        /// <summary>Degrees; the frame movement is turned by it into world XZ.</summary>
        float Yaw { get; }
        float Pitch { get; }
        /// <summary>The eye this frame, where the crosshair ray starts.</summary>
        Vector3 AimOrigin { get; }
        /// <summary>The crosshair ray, normalized.</summary>
        Vector3 AimDirection { get; }
        /// <summary>True while the right button or trigger is held.</summary>
        bool Aiming { get; }
    }
}
