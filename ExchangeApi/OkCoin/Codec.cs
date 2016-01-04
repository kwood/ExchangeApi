using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    public class Codec : ICodec<IMessageIn, IMessageOut>
    {
        public IEnumerable<IMessageIn> Parse(ArraySegment<byte> msg)
        {
            return Parser.Parse(Decode(msg));
        }

        public ArraySegment<byte> Serialize(IMessageOut msg)
        {
            return Encode(msg.Visit(new Serializer()));
        }

        static ArraySegment<byte> Encode(string s)
        {
            return new ArraySegment<byte>(Encoding.ASCII.GetBytes(s));
        }

        static string Decode(ArraySegment<byte> bytes)
        {
            return Encoding.ASCII.GetString(bytes.Array, bytes.Offset, bytes.Count);
        }
    }
}
