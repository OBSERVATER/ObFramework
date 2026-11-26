using ObFramework.Integrated.GAS.Attributes.Core;
using ObFramework.Integrated.GAS.GameplayEffect;
using ObFramework.Integrated.GAS.GameplayTag;

namespace ObFramework.Integrated.GAS
{
    public sealed class AbilitySystem
    {
        public AttributeSet Attributes { get; } = new();
        public TagContainer Tags { get; } = new();
        public EffectManager Effects { get; }

        public AbilitySystem()
        {
            Effects = new EffectManager(this);
        }
    }

}