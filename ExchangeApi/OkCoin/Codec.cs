using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    public class Codec : ICodec<IMessageIn, IMessageOut>
    {
        public IMessageIn Parse(ArraySegment<byte> msg)
        {
            return Parser.Parse(msg);
        }

        public ArraySegment<byte> Serialize(IMessageOut msg)
        {
            return Encode(msg.Visit(new Serializer()));
        }

        static ArraySegment<byte> Encode(string s)
        {
            return new ArraySegment<byte>(Encoding.ASCII.GetBytes(s));
        }
    }
}
