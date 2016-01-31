using ExchangeApi.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.OkCoin
{
    class Authenticator
    {
        // Elements with null values are ignored.
        public static string Sign(Keys keys, IEnumerable<KeyValuePair<string, string>> data)
        {
            string s = String.Join("&", data.Where(p => p.Value != null)
                                            .OrderBy(p => p.Key)
                                            .Append(new KeyValuePair<string, string>("secret_key", keys.SecretKey))
                                            .Select(p => String.Format("{0}={1}", p.Key, p.Value)));
            return Md5Hex(s);
        }

        static string Md5Hex(string s)
        {
            byte[] hash = MD5.Create().ComputeHash(Encoding.ASCII.GetBytes(s));
            StringBuilder res = new StringBuilder(2 * hash.Length);
            foreach (byte x in hash)
            {
                res.AppendFormat("{0:X2}", x);
            }
            return res.ToString();
        }
    }
}
