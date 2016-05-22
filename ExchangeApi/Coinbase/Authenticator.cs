using Conditions;
using ExchangeApi.Util;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Coinbase
{
    class Authenticator
    {
        static readonly Logger _log = LogManager.GetCurrentClassLogger();

        readonly string _key = null;
        readonly string _passphrase = null;
        readonly HMACSHA256 _hmac = null;

        public Authenticator(Keys keys)
        {
            if (keys == null)
            {
                _log.Info("No API keys have been specified. Authenticated requests can't be sent.");
                return;
            }
            Condition.Requires(keys.Key).IsNotNull();
            Condition.Requires(keys.Secret).IsNotNull();
            Condition.Requires(keys.Passphrase).IsNotNull();
            _key = keys.Key;
            _passphrase = keys.Passphrase;
            byte[] secret = Convert.FromBase64String(keys.Secret);
            Condition.Requires(secret, "secret").HasLength(64);
            _hmac = new HMACSHA256(secret);
        }

        public void Sign(HttpMethod method, string relativeUrl, string body, HttpHeaders headers)
        {
            // No keys specified, can't sign.
            if (_key == null) return;
            string timestamp = Time.ToUnixSeconds(DateTime.UtcNow).ToString();
            headers.Add("CB-ACCESS-KEY", _key);
            headers.Add("CB-ACCESS-SIGN", Signature(timestamp, method.ToString(), relativeUrl, body));
            headers.Add("CB-ACCESS-TIMESTAMP", timestamp);
            headers.Add("CB-ACCESS-PASSPHRASE", _passphrase);
            
        }

        string Signature(string timestamp, string method, string relativeUrl, string body)
        {
            Condition.Requires(timestamp, "timestamp").IsNotNull();
            Condition.Requires(method, "method").IsNotNull();
            Condition.Requires(relativeUrl, "relativeUrl").IsNotNull();
            string str = String.Format("{0}{1}{2}{3}", timestamp, method, relativeUrl, body ?? "");
            return Convert.ToBase64String(_hmac.ComputeHash(Encoding.UTF8.GetBytes(str)));
        }
    }
}
