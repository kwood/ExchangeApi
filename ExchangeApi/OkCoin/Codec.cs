using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    public class Codec : ICodec<IMessageIn, IMessageOut>
    {
        readonly Serializer _serializer;

        public Codec(Keys keys)
        {
            _serializer = new Serializer(keys);
        }

        public IEnumerable<IMessageIn> Parse(ArraySegment<byte> msg)
        {
            return ResponseParser.Parse(Decode(msg));
        }

        public ArraySegment<byte> Serialize(IMessageOut msg)
        {
            return Encode(msg.Visit(_serializer));
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
