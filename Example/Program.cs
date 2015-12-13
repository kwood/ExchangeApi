using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OkCoinApi.Example
{
    class Program
    {
        static void Main(string[] args)
        {
            var depth = new ProductDepth()
            {
                Product = new Future()
                {
                    CoinType = CoinType.Btc,
                    FutureType = FutureType.NextWeek,
                },
                Orders = new List<Amount>()
                {
                    new Amount() { Price = 1.23m, Quantity = 42.5m, Side = Side.Sell },
                    new Amount() { Price = 1.23m, Quantity = 42.5m, Side = Side.Sell },
                },
            };
            Console.WriteLine(depth);
        }
    }
}
