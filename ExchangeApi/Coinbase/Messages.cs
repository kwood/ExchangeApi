using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase
{
    public interface IMessageOut
    {
        T Visit<T>(IVisitorOut<T> v);
    }

    public interface IVisitorOut<T>
    {
    }

    public interface IMessageIn
    {
        T Visit<T>(IVisitorIn<T> v);
    }

    public interface IVisitorIn<T>
    {
    }

    public enum Side
    {
        Buy = 1,
        Sell = -1,
    }

    public class Amount : Util.Printable<Amount>
    {
        public Side Side { get; set; }
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
    }

    public class Order : Util.Printable<Order>
    {
        public string Id { get; set; }
        public Amount Amount { get; set; }
    }

    public class OrderBook : Util.Printable<OrderBook>
    {
        public long Sequence { get; set; }
        public List<Order> Orders { get; set; }
    }
}
