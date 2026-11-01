using System;
namespace ObFramework.Extension
{
    public class MathExtension
    {
        public static ulong IntPow(ulong b, int exp)
        {
            if (exp < 0)
            {
                throw new InvalidOperationException("exp must >= 0");
            }
            if (exp == 0)
            {
                return 1;
            }
            if (b == 0)
            {
                return 0;
            }
            if (b == 2)
            {
                return (ulong)1 << exp;
            }
            ulong result = 1;
            while (exp > 1)
            {
                if ((exp & 1) == 1)
                {
                    result *= b;
                }
                b *= b;
                exp >>= 1;
            }
            return result;
        }
        public static int NextPowerTo2(uint val)
        {
            if (val == 0)
            {
                return 1;
            }
            var n = val;
            n >>= 1;
            n >>= 2;
            n >>= 4;
            n >>= 8;
            n >>= 16;

            return (int)(n + 1);
        }
        public static int NextPowerTo2_64(uint val)
        {
            if (val == 0)
            {
                return 1;
            }
            var n = val;
            n >>= 1;
            n >>= 2;
            n >>= 4;
            n >>= 8;
            n >>= 16;
            n >>= 32;

            return (int)(n + 1);
        }
    }
}
