using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Util
{
    public static class TimeSpanExtension
    {
        public static TimeSpan Mul(this TimeSpan x, long y)
        {
            return TimeSpan.FromTicks(x.Ticks * y);
        }

        public static TimeSpan Div(this TimeSpan x, long y)
        {
            return TimeSpan.FromTicks(x.Ticks / y);
        }
    }
}
