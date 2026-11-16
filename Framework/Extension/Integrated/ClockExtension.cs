using ObFramework.Core;
using ObFramework.Integrated.Clock;

namespace ObFramework.Extension.Integrated
{
    public static class ClockExtension
    {
        public static void Register(this IFrameTickable tickable, ICanGetPlugin self)
            => self.GetPlugin<ClockPlugin>().RegisterTickable(tickable);
        public static void UnRegister(this IFrameTickable tickable, ICanGetPlugin self)
            => self.GetPlugin<ClockPlugin>().UnRegisterTickable(tickable);
    }
}