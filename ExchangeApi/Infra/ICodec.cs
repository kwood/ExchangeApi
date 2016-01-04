using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi
{
    public interface ICodec<In, Out>
    {
        ArraySegment<byte> Serialize(Out msg);

        IEnumerable<In> Parse(ArraySegment<byte> msg);
    }
}
