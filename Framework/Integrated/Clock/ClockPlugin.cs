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
        public double Time => Tick * (double)TickInterval + _accumulator;
        public float TickInterval { get; }
        private double _accumulator;
        private readonly FrameDispatcher _dispatcher;
        private bool _advancing;
        private readonly List<TimerEntry> _entries;
        public ClockPlugin(float interval)
        {
            if (interval <= 0 || float.IsNaN(interval) || float.IsInfinity(interval))
            {
                throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be finite and greater than zero.");
            }
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
            NextCore(dt, canContinue);
            _dispatcher.Dispatch(canContinue);
        }
        internal void DispatchFixed(Func<bool>? canContinue = null) => _dispatcher.DispatchFixed(canContinue);
        internal void DispatchLate(Func<bool>? canContinue = null) => _dispatcher.DispatchLate(canContinue);
        /// <summary>
        /// 驱动下一帧
        /// </summary>
        public void Next(float dt) => NextCore(dt, null);

        private void NextCore(float dt, Func<bool>? canContinue)
        {
            if (_advancing)
                throw new InvalidOperationException("Clock advancement cannot be reentered.");
            if (dt < 0 || float.IsNaN(dt) || float.IsInfinity(dt))
                throw new ArgumentOutOfRangeException(nameof(dt));
            _advancing = true;
            try
            {
                if (canContinue != null && !canContinue()) return;
                _accumulator += dt;
                while (_accumulator >= TickInterval)
                {
                    if (canContinue != null && !canContinue()) return;
                    _accumulator -= TickInterval;
                    AdvanceTick(canContinue);
                }
            }
            finally { _advancing = false; }
        }

        /// <summary>直接推进逻辑 tick；禁止在逻辑帧或定时器回调中重入推进。</summary>
        public void Advance(int tick)
        {
            if (_advancing)
                throw new InvalidOperationException("Clock advancement cannot be reentered.");
            if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
            _advancing = true;
            try
            {
                for (int i = 0; i < tick; i++) AdvanceTick(null);
            }
            finally { _advancing = false; }
        }

        private void AdvanceTick(Func<bool>? canContinue)
        {
            Tick++;
            _dispatcher.DispatchLogic(canContinue);
            UpdateTimers(canContinue);
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
        private void UpdateTimers(Func<bool>? canContinue)
        {
            for (int i = 0; i < _entries.Count;)
            {
                if (canContinue != null && !canContinue()) return;
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
