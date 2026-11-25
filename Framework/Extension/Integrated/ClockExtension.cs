using System;
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
        public static float Elapsed(this ClockPlugin clock, int startTick)
            => (clock.Tick - startTick) * clock.TickInterval;
        public static float Remaining(this ClockPlugin clock, int endTick)
            => Math.Max(0, endTick - clock.Tick) * clock.TickInterval;
        public static int ToTicks(this ClockPlugin clock, float seconds)
            => (int)Math.Ceiling(seconds / (double)clock.TickInterval);
    }
}