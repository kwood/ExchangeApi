using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConcurrentHttpClient
{
    class Program
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        static readonly int Parallelism = 64;
        static readonly int Port = 8087;
        static readonly Semaphore _inflight = new Semaphore(Parallelism, Parallelism);

        static async Task SendRequest(HttpClient client, long n)
        {
            try
            {
                var param = new KeyValuePair<string, string>[] { new KeyValuePair<string, string>("n", n.ToString()) };
                var form = new FormUrlEncodedContent(param);
                var req = new HttpRequestMessage();
                req.Method = n % 2 == 0 ? HttpMethod.Get : HttpMethod.Post;
                string uri = String.Format("/n/{0}", n);
                if (req.Method == HttpMethod.Get)
                {
                    string query = await form.ReadAsStringAsync();
                    if (query.Length > 0)
                    {
                        uri = String.Format("{0}?{1}", uri, query);
                    }
                }
                else
                {
                    req.Content = form;
                }
                req.RequestUri = new Uri(uri, UriKind.Relative);
                HttpResponseMessage resp = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead);
                string content = await resp.EnsureSuccessStatusCode().Content.ReadAsStringAsync();
                _log.Info("{0} {1}", n, content);
            }
            catch (Exception e)
            {
                _log.Error(e, "Error while sending an HTTP request");
            }
            finally
            {
                _inflight.Release();
            }
        }

        static void Send()
        {
            using (HttpClient client = new HttpClient())
            {
                client.BaseAddress = new Uri(String.Format("http://127.0.0.1:{0}/", Port));
                client.Timeout = TimeSpan.FromSeconds(10);
                _log.Info("Sending...");
                for (long i = 0; true; ++i)
                {
                    _inflight.WaitOne();
                    long n = i;
                    Task.Run(() => SendRequest(client, n));
                }
            }
        }

        static int Main(string[] args)
        {
            try
            {
                Send();
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
