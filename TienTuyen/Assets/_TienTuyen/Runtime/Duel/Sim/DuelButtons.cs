using System;

namespace TienTuyen.Duel
{
    [Flags]
    public enum DuelButtons : ushort
    {
        None = 0, Fire = 1, Aim = 2, Reload = 4, Jump = 8, Crouch = 16, Walk = 32
    }
}
