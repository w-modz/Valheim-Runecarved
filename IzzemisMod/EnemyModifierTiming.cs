namespace Runecarved
{
    internal static class EnemyModifierTiming
    {
        internal static bool IsExpired(double now, double created, double lifetime)
        {
            return now - created >= lifetime;
        }

        internal static bool CanHit(double now, double previousHit)
        {
            return now - previousHit >= 1.0;
        }

        internal static float TarredElapsed(float duration)
        {
            // TODO: Check if duration is handled correctly; currently appears to be 10s ingame
            // StatusEffect.m_time is elapsed time, so ttl minus five leaves five seconds remaining
            return duration - 5f;
        }

        internal static bool ShouldExtendTarred(float duration, float remaining)
        {
            // Refresh only an active effect that has less than five seconds left
            return duration > 0f && remaining < 5f;
        }
    }
}
