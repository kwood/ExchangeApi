using Conditions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ExchangeApi.Coinbase.WebSocket;

namespace ExchangeApi.Coinbase
{
    class Order
    {
        // Null for orders that haven't been acknowledged by the exchange.
        public string OrderId;
        // Not null.
        public string ClientOrderId;
        // OrderId == null => Unfilled == 0.
        public decimal Unfilled;
        // If not null, we are expecting the exchange to tell us the order is done.
        // We aren't sending cancellation requests in this state.
        public object Pending;
        // Null iff the order is done.
        public Action<TimestampedMsg<OrderUpdate>> Callback;
    }

    class OrderManager
    {
        // If an expected message doesn't arrive for this long, assume it'll never arrive.
        static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        readonly Scheduler _scheduler;
        // All orders have non-null OrderId and Callback.
        readonly Dictionary<string, Order> _openOrders = new Dictionary<string, Order>();
        // All orders have null OrderId and non-null Callback.
        readonly Dictionary<string, Order> _newOrders = new Dictionary<string, Order>();

        public OrderManager(Scheduler scheduler)
        {
            Condition.Requires(scheduler, "scheduler").IsNotNull();
            _scheduler = scheduler;
        }

        // Called from the scheduler thread.
        public void Add(string clientOrderId, Action<TimestampedMsg<OrderUpdate>> cb)
        {
            Condition.Requires(clientOrderId, "clientOrderId").IsNotNull();
            Condition.Requires(cb, "cb").IsNotNull();
            var order = new Order() { ClientOrderId = clientOrderId, Callback = cb };
            _newOrders.Add(order.ClientOrderId, order);
            // If we don't get OrderReceived for a long time, finish the order.
            _scheduler.Schedule(DateTime.UtcNow + Timeout, () =>
            {
                if (order.OrderId != null) return;
                _log.Warn("New order request timed out: ClientOrderID = {0}", order.ClientOrderId);
                _newOrders.Remove(order.ClientOrderId);
                order.Callback = null;
                try { cb(null); }
                catch (Exception e) { _log.Warn(e, "Ignoring exception from order callback"); }
            });
        }

        // Called when we try to create a new order and get "rejected" error from Coinbase.
        public void Reject(string clientOrderId)
        {
            Order order;
            if (!_newOrders.TryGetValue(clientOrderId, out order)) return;
            _log.Info("New order request rejected: ClientOrderID = {0}", clientOrderId);
            var cb = order.Callback;
            order.Callback = null;
            _newOrders.Remove(clientOrderId);
            try { cb(null); }
            catch (Exception e) { _log.Warn(e, "Ignoring exception from order callback"); }
        }

        // Called from the scheduler thread.
        public bool CanCancel(string orderId)
        {
            Order order;
            return _openOrders.TryGetValue(orderId, out order) && order.Pending == null;
        }

        // Requires: CanCancel(orderId).
        public void Cancel(string orderId)
        {
            Order order;
            if (!_openOrders.TryGetValue(orderId, out order)) throw new Exception("Invalid order: " + orderId);
            if (order.Pending != null) throw new Exception("Can't cancel: " + orderId);
            var token = new object();
            order.Pending = token;
            // Remove pending after a while whether the order actually gets cancelled or not.
            _scheduler.Schedule(DateTime.UtcNow + Timeout, () =>
            {
                if (order.Pending != token || order.Callback == null) return;
                _log.Warn("Cancel order request timed out: OrderID = {0}", orderId);
                order.Pending = null;
            });
        }

        // Called when we try to cancel an order and get "invalid order" error from Coinbase.
        public void Invalidate(string orderId)
        {
            Order order;
            if (!_openOrders.TryGetValue(orderId, out order)) return;
            // This pending bit is forever.
            order.Pending = new object();
            // If the order doesn't finish by itself, finish it forcefully after a while.
            _scheduler.Schedule(DateTime.UtcNow + Timeout, () =>
            {
                if (order.Callback == null) return;
                _log.Warn("Finishing stale order: OrderID = {0}", orderId);
                var cb = order.Callback;
                order.Callback = null;
                _openOrders.Remove(orderId);
                try { cb(null); }
                catch (Exception e) { _log.Warn(e, "Ignoring exception from order callback"); }
            });
        }

        // Called from the scheduler thread. Doesn't throw.
        // Returns true if the message indicates a fill of one of our orders.
        public bool OnMessage(TimestampedMsg<IMessageIn> msg)
        {
            try
            {
                return msg.Value.Visit(new MessageHandler(this, msg.Received));
            }
            catch (Exception e)
            {
                _log.Error(e, "Unable to handle incoming message. Expect inconsistencies in order states.");
                return false;
            }
        }

        class MessageHandler : WebSocket.IVisitorIn<bool>
        {
            static readonly Logger _log = LogManager.GetCurrentClassLogger();
            readonly OrderManager _manager;
            readonly DateTime _received;

            public MessageHandler(OrderManager manager, DateTime received)
            {
                _manager = manager;
                _received = received;
            }

            public bool Visit(OrderReceived msg)
            {
                if (msg.ClientOrderId == null) return false;
                Order order;
                if (!_manager._newOrders.TryGetValue(msg.ClientOrderId, out order)) return false;
                Condition.Requires(msg.Size, "msg.Size").IsNotNull();
                Condition.Requires(msg.OrderId, "msg.OrderId").IsNotNull();
                _manager._openOrders.Add(msg.OrderId, order);
                Condition.Requires(_manager._newOrders.Remove(msg.ClientOrderId)).IsTrue();
                order.OrderId = msg.OrderId;
                order.Unfilled = msg.Size.Value;
                PublishUpdate(msg, order, fill: null, finished: false);
                return false;
            }

            public bool Visit(OrderOpen msg)
            {
                // Don't care.
                return false;
            }

            public bool Visit(OrderChange msg)
            {
                Condition.Requires(msg.OrderId, "msg.OrderId").IsNotNull();
                Order order;
                if (!_manager._openOrders.TryGetValue(msg.OrderId, out order)) return false;
                Condition.Requires(msg.NewSize, "msg.NewSize").IsNotNull();
                order.Unfilled = msg.NewSize.Value;
                PublishUpdate(msg, order, fill: null, finished: false);
                return false;
            }

            public bool Visit(OrderDone msg)
            {
                Condition.Requires(msg.OrderId, "msg.OrderId").IsNotNull();
                Order order;
                if (!_manager._openOrders.TryGetValue(msg.OrderId, out order)) return false;
                Condition.Requires(_manager._openOrders.Remove(msg.OrderId)).IsTrue();
                PublishUpdate(msg, order, fill: null, finished: true);
                return false;
            }

            public bool Visit(OrderMatch msg)
            {
                Condition.Requires(msg.MakerOrderId, "msg.MakerOrderId").IsNotNull();
                Condition.Requires(msg.TakerOrderId, "msg.TakerOrderId").IsNotNull();
                Order order;
                if (_manager._openOrders.TryGetValue(msg.TakerOrderId, out order))
                {
                    if (_manager._openOrders.ContainsKey(msg.MakerOrderId)) throw new Exception("Self trade");
                }
                else if (!_manager._openOrders.TryGetValue(msg.MakerOrderId, out order))
                {
                    return false;
                }
                Condition.Requires(msg.Size, "msg.Size").IsLessOrEqual(order.Unfilled);
                order.Unfilled -= msg.Size;
                PublishUpdate(msg, order, new Fill() { Size = msg.Size, Price = msg.Price }, finished: false);
                // This is the only place where any overload of Visit() in MessageHandler returns true.
                return true;
            }

            void PublishUpdate(IMessageIn msg, Order order, Fill fill, bool finished)
            {
                Condition.Requires(order.Callback, "order.Callback").IsNotNull();
                var update = new TimestampedMsg<OrderUpdate>()
                {
                    Received = _received,
                    Value = new OrderUpdate()
                    {
                        Time = msg.Time,
                        OrderId = order.OrderId,
                        Unfilled = order.Unfilled,
                        Fill = fill,
                        Finished = finished,
                    }
                };
                var cb = order.Callback;
                if (finished) order.Callback = null;
                try { cb(update); }
                catch (Exception e) { _log.Warn(e, "Ignoring exception from order callback"); }
            }
        }
    }
}
