using System;
using ObFramework.Integrated.GAS.Attributes.Core;

namespace ObFramework.Integrated.GAS.GameplayEffect.Components
{
    /// <summary>
    /// 检查资源是否足够
    /// </summary>
    public sealed class CostComponent : AEffectComponent
    {
        private readonly AttributeKey<float> _key;
        private readonly float _cost;

        public CostComponent(AttributeKey<float> key, float cost)
        {
            if (cost <= 0 ||
                float.IsNaN(cost) ||
                float.IsInfinity(cost))
                throw new ArgumentOutOfRangeException(nameof(cost));

            _key = key;
            _cost = cost;
        }

        public override bool CanApply(EffectContext context)
        {
            var attribute = context.Target.Attributes.Get(_key);
            return attribute.Base >= _cost;
        }

        public override void OnApply(EffectContext context)
        {
            var attribute = context.Target.Attributes.Get(_key);
            attribute.SetBase(attribute.Base - _cost);
        }
    }
}