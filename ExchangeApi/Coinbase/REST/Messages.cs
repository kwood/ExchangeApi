using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase.REST
{
    public class Order : Util.Printable<Order>
    {
        public string Id { get; set; }
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
    }

    public class FullOrderBook : Util.Printable<FullOrderBook>
    {
        // Server time. Coinbase doesn't give us server time together with the full order book,
        // so we retrieve it with a separate request BEFORE requesting the order book.
        public DateTime Time { get; set; }
        public long Sequence { get; set; }
        public List<Order> Bids { get; set; }
        public List<Order> Asks { get; set; }
    }
}
