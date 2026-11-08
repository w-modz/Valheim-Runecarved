using UnityEngine;

namespace Runecarved
{
    internal sealed class EnemyModifierRegrowth : MonoBehaviour
    {
        private float elapsed;

        internal static void Update(Character character, float dt)
        {
            EnemyModifierRegrowth timer = character.GetComponent<EnemyModifierRegrowth>();
            float rate = 0f;
            if (character.IsOwner() && !character.IsDead())
            {
                foreach (EnemyModifierDefinition modifier in EnemyModifiers.GetModifiers(character))
                {
                    rate += modifier.RegenerationRate;
                }
            }
            if (rate <= 0f || character.GetHealth() >= character.GetMaxHealth())
            {
                if (timer != null)
                {
                    timer.elapsed = 0f;
                }
                return;
            }
            if (timer == null)
            {
                timer = character.gameObject.AddComponent<EnemyModifierRegrowth>();
            }
            timer.elapsed += dt;
            if (timer.elapsed >= 1f)
            {
                // Heal whole elapsed seconds and carry the fractional remainder into the next update
                float seconds = Mathf.Floor(timer.elapsed);
                timer.elapsed -= seconds;
                character.Heal(character.GetMaxHealth() * rate * seconds, false);
            }
        }
    }
}
