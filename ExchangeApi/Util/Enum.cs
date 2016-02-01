using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Util
{
    public static class Enum
    {
        public static IEnumerable<T> Values<T>()
        {
            return System.Enum.GetValues(typeof(T)).Cast<T>();
        }
    }
}
