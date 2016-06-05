using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase
{
    public enum Side
    {
        Buy = 1,
        Sell = -1,
    }

    // One price level of an order book.
    public class PriceLevel
    {
        public decimal Price { get; set; }
        public decimal SizeDelta { get; set; }
    }

    // All prices are distinct. All sizes are non-zero (can be negative).
    // Includes our own orders if any.
    public class OrderBookDelta : Util.Printable<OrderBookDelta>
    {
        // Server time. Coinbase doesn't give us server time together with the full order book,
        // so we retrieve it with a separate request BEFORE requesting the order book.
        public DateTime Time { get; set; }
        // Not null. Sorted by price in descending order (the highest price first).
        public List<PriceLevel> Bids { get; set; }
        // Not null. Sorted by price in ascending order (the lowest price first).
        public List<PriceLevel> Asks { get; set; }
        // This is for debugging only. Don't use it in production code.
        public long Sequence;
    }

    // A.K.A. fill, or match.
    public class Trade : Util.Printable<Trade>
    {
        // Server time.
        public DateTime Time { get; set; }
        public decimal Price { get; set; }
        public decimal Size { get; set; }
        // Maker order side.
        public Side Side { get; set; }
    }

    public class NewOrder : Util.Printable<NewOrder>
    {
        public Side Side { get; set; }
        // See https://api.exchange.coinbase.com/products for the full list of products.
        // One example is "BTC-USD".
        public string ProductId { get; set; }
        // Price per bitcoin.
        public decimal Price { get; set; }
        // Amount of BTC to buy or sell.
        public decimal Size { get; set; }
    }

    public class CancelOrder : Util.Printable<CancelOrder>
    {
        public string OrderId { get; set; }
    }

    public class Fill
    {
        public decimal Size { get; set; }
        public decimal Price { get; set; }
    }

    public class OrderUpdate : Util.Printable<OrderUpdate>
    {
        public string OrderId { get; set; }
        public DateTime Time { get; set; }
        // If Fill is present, the Unfilled size is *after* the fill is taken into account.
        public decimal Unfilled { get; set; }
        public bool Finished { get; set; }
        // May be null.
        public Fill Fill { get; set; }
    }
}
