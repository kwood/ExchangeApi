using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConcurrentHttpListener
{
    class Program
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        static readonly int Parallelism = 32;
        static readonly int Port = 8087;
        static readonly Semaphore _inflight = new Semaphore(Parallelism, Parallelism);

        static void OnRequest(Task<HttpListenerContext> t)
        {
            try
            {
                HttpListenerContext ctx = t.Result;
                HttpListenerRequest request = ctx.Request;
                var content = new MemoryStream();
                request.InputStream.CopyTo(content);
                var bytes = content.ToArray();
                _log.Info("{0} {1} {2}", request.HttpMethod, request.Url, Encoding.UTF8.GetString(bytes));
                HttpListenerResponse response = ctx.Response;
                response.StatusCode = 200;
                response.OutputStream.Write(bytes, 0, bytes.Length);
                response.OutputStream.Close();
            }
            catch (Exception e)
            {
                _log.Error(e, "Error while handling an incoming HTTP request");
            }
            finally
            {
                _inflight.Release();
            }
        }

        static void Listen()
        {
            using (HttpListener listener = new HttpListener())
            {
                listener.Prefixes.Add(String.Format("http://+:{0}/", Port));
                listener.Start();
                _log.Info("Listening...");
                while (true)
                {
                    _inflight.WaitOne();
                    listener.GetContextAsync().ContinueWith(OnRequest);
                }
            }
        }

        static int Main(string[] args)
        {
            try
            {
                Listen();
                return 0;
            }
            catch (Exception e)
            {
                _log.Error(e, "Unhandled exception");
                return 1;
            }
        }
    }
}
