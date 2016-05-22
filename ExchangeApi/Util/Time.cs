using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Util
{
    public static class Time
    {
        static readonly DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static long ToUnixSeconds(DateTime t)
        {
            return (long)(t - _epoch).TotalSeconds;
        }

        public static DateTime FromUnixMillis(long millis)
        {
            return _epoch + TimeSpan.FromMilliseconds(millis);
        }

        public static DateTime FromDayTime(TimeSpan time, TimeSpan tz)
        {
            // Note that the second argument of Combine() may end up being negative.
            // It's OK because it's not too negative.
            return Combine(DateTime.UtcNow, time - tz);
        }

        // Returns the closes timestamp to `baseTimestamp` that has time portion equal to `timeOnly`.
        //
        //   Combine("2015-01-04 23:59", "23:55") => "2015-01-04 23:55"
        //   Combine("2015-01-04 23:59", "00:03") => "2015-01-05 00:03"
        static DateTime Combine(DateTime baseTimestamp, TimeSpan timeOnly)
        {
            return new int[] { -1, 0, 1 }
                .Select(n => baseTimestamp.Date + TimeSpan.FromDays(n) + timeOnly)
                .OrderBy(d => Math.Abs(d.Ticks - baseTimestamp.Ticks))
                .First();
        }
    }
}
