using Effect = ObFramework.Integrated.GAS.GameplayEffect.GameplayEffect;

namespace ObFramework.Integrated.GAS.GameplayAbility
{
    internal sealed class ApplyEffectBehavior : AAbilityBehaviour
    {
        private readonly Effect _effect;

        public ApplyEffectBehavior(Effect effect)
        {
            _effect = effect;
        }

        public override void OnActivate(ActiveAbility ability)
        {
            if (!ability.TryCommit())
            {
                ability.Cancel();
                return;
            }

            ability.Target.Effects.TryApply(_effect, ability.Owner, out _);
            ability.End();
        }
    }
}