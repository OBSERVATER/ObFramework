using ObFramework.Integrated.GAS;
using ObFramework.Integrated.GAS.Attributes.Core;
using ObFramework.Integrated.GAS.GameplayAbility;
using ObFramework.Integrated.GAS.GameplayEffect;

using Effect = ObFramework.Integrated.GAS.GameplayEffect.GameplayEffect;
using Ability = ObFramework.Integrated.GAS.GameplayAbility.GameplayAbility;

namespace ObFramework.Extension.Integrated
{
    public static class GASExtension
    {
        #region Attribute

        public static T GetValue<T>(this AbilitySystem gas, AttributeKey<T> key) where T : struct 
            => gas.Attributes.Get(key).Value;

        public static T GetBase<T>(this AbilitySystem gas, AttributeKey<T> key) where T : struct 
            => gas.Attributes.Get(key).Base;

        public static void SetBase<T>(this AbilitySystem gas, AttributeKey<T> key, T value) where T : struct 
            => gas.Attributes.Get(key).SetBase(value);

        #endregion
        
        #region Effect

        public static bool TryApplyEffect(
            this AbilitySystem source,
            AbilitySystem target,
            Effect definition,
            out ActiveEffect? effect) 
            => target.Effects.TryApply(definition, source, out effect);

        public static bool TryApplyEffect(this AbilitySystem gas, Effect definition, out ActiveEffect? effect) 
            => gas.Effects.TryApply(definition, gas, out effect);

        #endregion

        #region Ability

        public static bool TryActivateAbility(
            this AbilitySystem gas,
            Ability definition,
            AbilitySystem target,
            out ActiveAbility? active) 
            => gas.Abilities.TryActivate(definition, target, out active);

        public static bool TryActivateAbility(this AbilitySystem gas, Ability definition, out ActiveAbility? active) 
            => gas.Abilities.TryActivate(definition, gas, out active);

        #endregion
    }
}