using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObFramework.Core;

namespace ObFramework.Integrated.Clock
{
    public class ClockPlugin : IPlugin
    {
        public int Tick { get; private set; }
        public float TickInterval { get; }
        private double _accumulator;
        private FrameDispatcher _dispatcher;
        private readonly List<TimerEntry> _entries;
        public ClockPlugin(float interval)
        {
            TickInterval = interval;
            _dispatcher = new FrameDispatcher();
            _entries = new List<TimerEntry>();
        }
        void IPlugin.Register(PluginRegistration registration) { }
        Task IPlugin.InitAsync(IPluginContext plugins, CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(IPluginContext plugins) => Task.CompletedTask;
        public void RegisterTickable(IFrameTickable tickable) => _dispatcher.Register(tickable);
        public void UnRegisterTickable(IFrameTickable tickable) => _dispatcher.UnRegister(tickable);
        internal void DispatchUpdate(float dt, Func<bool>? canContinue = null)
        {
            Next(dt);
            _dispatcher.Dispatch(canContinue);
        }
        internal void DispatchFixed(Func<bool>? canContinue = null) => _dispatcher.DispatchFixed(canContinue);
        internal void DispatchLate(Func<bool>? canContinue = null) => _dispatcher.DispatchLate(canContinue);
        /// <summary>
        /// 驱动下一帧
        /// </summary>
        public void Next(float dt)
        {
            _accumulator += dt;

            while (_accumulator >= TickInterval)
            {
                _accumulator -= TickInterval;
                Advance(1);
            }
        }
        /// <summary>
        /// 直接推进tick
        /// </summary>
        /// <param name="tick"></param>
        public void Advance(int tick)
        {
            for (int i = 0; i < tick; i++)
            {
                Tick++;
                _dispatcher.DispatchLogic();
                UpdateTimers();
            }
        }
        public void Repeat(int tick,Action action)
        {
            if (tick <= 0) throw new ArgumentOutOfRangeException(nameof(tick));
            _entries.Add(new TimerEntry(Tick + (long)tick, tick, action));
        }
        public void Schedule(int tick,Action action)
        {
            if (tick <= 0)
                throw new ArgumentOutOfRangeException(nameof(tick));
            _entries.Add(new TimerEntry(Tick + (long)tick, 0, action));
        }
        private void UpdateTimers()
        {
            for (int i = 0; i < _entries.Count;)
            {
                var timer = _entries[i];
                if (timer.NextTick > Tick)
                {
                    i++;
                    continue;
                }

                if (timer.Interval == 0)
                    _entries.RemoveAt(i);
                else
                {
                    timer.NextTick += timer.Interval;
                    i++;
                }

                timer.Action();
            }
        }
        private class TimerEntry
        {
            public long NextTick;
            public int Interval;
            public Action Action;
            public TimerEntry(long nextTick, int interval, Action action)
            {
                NextTick = nextTick;
                Interval = interval;
                Action = action;
            }
        }
    }

}
