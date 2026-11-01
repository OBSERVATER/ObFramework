using System;

namespace ObFramework.Core.Async
{
    public class ObLifetime
    {
        private readonly object _gate = new object();

        private int _version = 1;
        private bool _alive = true;

        public int Capture()
        {
            lock (_gate)
            {
                if (!_alive)
                    throw new InvalidOperationException("Lifetime is inactive.");

                return _version;
            }
        }

        public bool IsCurrent(int version)
        {
            lock (_gate)
            {
                return _alive && _version == version;
            }
        }

        public void Invalidate()
        {
            lock (_gate)
            {
                _alive = false;
            }
        }

        public void Renew()
        {
            lock (_gate)
            {
                if (_alive)
                    throw new InvalidOperationException("Lifetime is still active.");

                checked
                {
                    _version++;
                }

                _alive = true;
            }
        }
    }
}
