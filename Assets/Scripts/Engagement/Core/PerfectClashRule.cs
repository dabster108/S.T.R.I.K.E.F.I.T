// PerfectClashRule.cs
// Purpose: When a weapon clash counts as a PERFECT CLASH: during the monster's actual strike (not wind-up idle, not recovery), at
//          most `window` seconds BEFORE the strike's hit frame (the moment it would connect), with a real swing (at least the
//          game's minimum swing speed, the same gate the parry uses), never on an unblockable attack, and at most once per attack.
//          The window is a config value (EngagementConfig.perfectClashWindowSeconds). It is a reward only: the monster's damage is
//          decided by the existing rules (a parry, or not) and never by this. Times are on the monster's AI clock. Pure, tested in
//          EngagementCoreTests.
// Attach to: nothing (static)

namespace StrikeFit.Engagement
{
    public static class PerfectClashRule
    {
        /// <param name="clashTime">AI clock when the clash happened.</param>
        /// <param name="hitTime">AI clock of the strike's (next) hit frame; negative when there is none.</param>
        public static bool IsPerfect(float clashTime, float hitTime, float window, bool inStrike, bool unblockable, float swingSpeed, float minSwingSpeed)
        {
            if (!inStrike || unblockable || hitTime < 0f || window <= 0f) return false;
            if (swingSpeed < minSwingSpeed) return false;
            float early = hitTime - clashTime;   // how long before the hit frame
            const float eps = 1e-4f;             // AI-clock floats: a clash exactly on the window edge counts
            return early >= -eps && early <= window + eps;
        }

        /// <summary>One perfect clash per attack: <paramref name="attackSerial"/> is the monster's attack counter.</summary>
        public static bool FirstForAttack(int attackSerial, int lastPerfectSerial) => attackSerial != lastPerfectSerial;
    }
}
