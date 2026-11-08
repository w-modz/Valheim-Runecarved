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
            return duration - 5f;
        }

        internal static bool ShouldExtendTarred(float duration, float remaining)
        {
            return duration > 0f && remaining < 5f;
        }
    }
}
