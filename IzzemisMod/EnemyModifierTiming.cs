namespace Runecarved
{
    internal static class EnemyModifierTiming
    {
        private const float TarredDuration = 10f;

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
            // StatusEffect.m_time is elapsed time, so subtract the desired remaining duration
            return duration - TarredDuration;
        }

        internal static bool ShouldExtendTarred(float duration, float remaining)
        {
            return duration > 0f && remaining < TarredDuration;
        }
    }
}
