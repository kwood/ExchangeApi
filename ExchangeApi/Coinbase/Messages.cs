using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase
{
    // One price level of an order book.
    class PriceLevel
    {
        public decimal Price;
        // Includes our own orders if any.
        public decimal Size;
    }

    // Either a full snapshot or a delta, depending on the context where it's used.
    //
    // All prices are distinct. All sizes are non-zero. When representing a full snapshot,
    // all sizes are positive. When represending a delta, sizes may be negative or positive.
    class OrderBook : Util.Printable<OrderBook>
    {
        // Not null. Sorted by price in descending order (the biggest price first).
        public List<PriceLevel> Bids;
        // Not null. Sorted by price in ascending order (the lowest price first).
        public List<PriceLevel> Asks;
    }
}
