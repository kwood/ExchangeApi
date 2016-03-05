using Conditions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Util
{
    public static class JTokenExtension
    {
        static readonly char[] E = new char[] { 'e', 'E' };

        // Unlike regular decimal.Parse(), this one supports scientific notation.
        //
        // "-12.34" => -12.34
        // "-12.34e-1" => -1.234
        public static decimal AsDecimal(this Newtonsoft.Json.Linq.JToken token)
        {
            string s = (string)token;
            Condition.Requires(s, "s").IsNotNull();
            int pos = s.IndexOfAny(E);
            if (pos == -1) return decimal.Parse(s);
            decimal res = decimal.Parse(s.Substring(0, pos));
            int e = int.Parse(s.Substring(pos + 1));
            decimal mul = e > 0 ? 10m : 0.1m;
            for (int i = 0; i != Math.Abs(e); ++i)
                res *= mul;
            return res;
        }
    }
}
