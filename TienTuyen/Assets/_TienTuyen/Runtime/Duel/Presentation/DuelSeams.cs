using System.Collections.Generic;
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

        /// <summary>The index into <see cref="DuelRoster.All"/> this player will wear, or <see cref="DuelRoster.NoPick"/>.</summary>
        int LocalCharacter { get; }
        /// <summary>
        /// Picks a man from <see cref="DuelRoster.All"/>; <see cref="DuelRoster.NoPick"/> clears the pick. Pick before
        /// Host or Join: the pick goes to the other machine once, as soon as the two are connected, and a pick made after
        /// Host or Join is not worn this match.
        /// </summary>
        void RequestCharacter(int index);
        /// <summary>
        /// What the fighter in a slot wears. Both machines agree on it from the moment the match is there to show (the
        /// stage's Match is set), so build the bodies then; before it, the other slot reads as its default.
        /// </summary>
        DuelCharacter CharacterOf(int slot);
    }

    /// <summary>
    /// A man a fighter can wear. Cosmetic only: every one has the same health, speed, hitboxes and guns, and nothing in
    /// the simulation knows which it is.
    /// </summary>
    public readonly struct DuelCharacter
    {
        public readonly string Id;
        /// <summary>The name the lobby shows.</summary>
        public readonly string Name;
        /// <summary>Resources path of the character's SkinnedCharacterProfile.</summary>
        public readonly string Profile;

        public DuelCharacter(string id, string name, string profile) { Id = id; Name = name; Profile = profile; }
    }

    /// <summary>
    /// The men on offer. A pick crosses the wire as an index into <see cref="All"/>, so both machines need the same
    /// roster, that is the same build. Another man is one more line here. Both players may wear the same one.
    /// </summary>
    public static class DuelRoster
    {
        public const int NoPick = -1;

        public static readonly IReadOnlyList<DuelCharacter> All = new[]
        {
            new DuelCharacter("pavn", "Bộ đội", "Characters/PAVN/PAVN_ValveProfile"),
            new DuelCharacter("us", "Lính Mỹ", "Characters/US/US_ValveProfile")
        };

        /// <summary>
        /// What the fighter in <paramref name="slot"/> wears for <paramref name="pick"/>: the pick when it is on the
        /// roster, else the slot's own man (the host PAVN, the joiner US), so nobody ever spawns without a character.
        /// </summary>
        public static DuelCharacter For(int slot, int pick) =>
            pick >= 0 && pick < All.Count ? All[pick] : All[slot == 1 && All.Count > 1 ? 1 : 0];
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
        /// <summary>Rounds needed to take the match: 3 in a best of 5.</summary>
        int RoundsToWin { get; }
        int LocalRounds { get; }
        int OpponentRounds { get; }
        /// <summary>Seconds left in the round while fighting; the round length during the countdown.</summary>
        float RoundRemaining { get; }
        /// <summary>Seconds until the round starts, during <see cref="DuelPhase.Warmup"/>.</summary>
        float CountdownRemaining { get; }
        DuelRoundResult LastRound { get; }
        /// <summary>How the match ended for the local player; <see cref="DuelRoundResult.None"/> until it is over.
        /// A match can be drawn, so this is not a bool: rounds can finish level and leave the damage level too.</summary>
        DuelRoundResult MatchResult { get; }

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
