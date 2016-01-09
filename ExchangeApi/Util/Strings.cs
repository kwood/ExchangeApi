using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Util
{
    public static class Strings
    {
        enum SymbolType
        {
            Lower,
            Upper,
            Digit,
            Other,
        }

        static SymbolType GetSymbolType(char c)
        {
            if (Char.IsLower(c)) return SymbolType.Lower;
            if (Char.IsUpper(c)) return SymbolType.Upper;
            if (Char.IsDigit(c)) return SymbolType.Digit;
            return SymbolType.Other;
        }

        // "FooBar" => "Foo_Bar"
        // "HTTPServer" => "HTTP_Server"
        // "RFC822" => "RFC_822"
        // "Foo_Bar" => "Foo_Bar"
        // "Foo.Bar" => "Foo.Bar"
        // "ПриветПока" => "Привет_Пока"
        //
        // Warning: doesn't support surrogate pairs. I haven't tested it, so don't really know
        // what happens if the input contains surrogate pairs.
        public static string CamelCaseToUnderscores(string s)
        {
            var res = new StringBuilder();
            SymbolType? last = null;
            bool needSep = false;
            foreach (char c in s.Reverse())
            {
                SymbolType sym = GetSymbolType(c);
                if (!last.HasValue)
                {
                    if (needSep && sym != SymbolType.Other) res.Append('_');
                    res.Append(c);
                    last = sym;
                    continue;
                }
                switch (last.Value)
                {
                    case SymbolType.Lower:
                        if (sym == SymbolType.Digit) res.Append('_');
                        res.Append(c);
                        if (sym == SymbolType.Upper)
                        {
                            needSep = true;
                            last = null;
                        }
                        else
                        {
                            last = sym;
                        }
                        break;
                    case SymbolType.Upper:
                    case SymbolType.Digit:
                        if (sym != last.Value && sym != SymbolType.Other) res.Append('_');
                        res.Append(c);
                        last = sym;
                        break;
                    case SymbolType.Other:
                        res.Append(c);
                        last = sym;
                        break;
                }
            }
            return new string(res.ToString().Reverse().ToArray());
        }

        // "foo_bar" => "FooBar"
        // "__yo__" => "Yo"
        // "FOO" => "FOO"
        // "1x" => "1x"
        // "hello world" => "Hello World"
        // "привет_пока" => "ПриветПока"
        //
        // Warning: doesn't support surrogate pairs. I haven't tested it, so don't really know
        // what happens if the input contains surrogate pairs.
        public static string UnderscoresToCamelCase(string s)
        {
            var res = new StringBuilder();
            bool newWord = true;
            foreach (char c in s)
            {
                SymbolType sym = GetSymbolType(c);
                if (c != '_')
                {
                    res.Append(newWord && sym == SymbolType.Lower ? Char.ToUpper(c) : c);
                }
                newWord = sym == SymbolType.Other;
            }
            return res.ToString();
        }
    }
}
