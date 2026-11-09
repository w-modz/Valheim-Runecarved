using System;

namespace Runecarved
{
    internal static class EnemyModifierCombatRules
    {
        internal static bool IntervalElapsed(long now, long start, int seconds)
        {
            return start > 0 && now >= start && now - start >= seconds * TimeSpan.TicksPerSecond;
        }

        internal static bool ShouldReplaceShield(float currentRemaining, float candidate)
        {
            return candidate > currentRemaining;
        }

        internal static bool SummonConfirmedGone(bool serverChanged, bool exists, bool destroyed)
        {
            return !exists && (serverChanged || destroyed);
        }
    }
}
