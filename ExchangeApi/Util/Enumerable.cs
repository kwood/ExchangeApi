using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Util
{
    class Appended<T> : IEnumerable<T>
    {
        readonly IEnumerable<T> _seq;
        readonly T _last;

        public Appended(IEnumerable<T> seq, T last)
        {
            _seq = seq;
            _last = last;
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (T elem in _seq)
            {
                yield return elem;
            }
            yield return _last;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public static class EnumerableExtension
    {
        public static IEnumerable<T> Append<T>(this IEnumerable<T> seq, T last)
        {
            return new Appended<T>(seq, last);
        }

        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> seq)
        {
            return new HashSet<T>(seq);
        }

        public static IEnumerable<T> DedupBy<T, U>(this IEnumerable<T> seq, Func<T, U> by)
        {
            return seq.GroupBy(by).Select(g => g.First());
        }

        public static IEnumerable<T> Dedup<T>(this IEnumerable<T> seq)
        {
            return seq.DedupBy(x => x);
        }
    }
}
