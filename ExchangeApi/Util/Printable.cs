using StatePrinting;
using StatePrinting.FieldHarvesters;
using StatePrinting.Introspection;
using StatePrinting.OutputFormatters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExchangeApi.Util
{
    public class CompactFormatter : IOutputFormatter
    {
        public string Print(List<Token> tokens)
        {
            tokens = new UnusedReferencesTokenFilter().FilterUnusedReferences(tokens);
            // Remove fields with null values. They aren't interesting.
            tokens = tokens
                .Where(token => token.Tokenkind != TokenType.SimpleFieldValue || token.Value != "null")
                .ToList();
            var output = new StringBuilder();
            for (int i = 0; i != tokens.Count; ++i)
            {
                if (i + 1 == tokens.Count)
                    WriteToken(tokens[i], null, output);
                else
                    WriteToken(tokens[i], tokens[i + 1], output);
            }
            return output.ToString();
        }

        void WriteToken(Token token, Token next, StringBuilder output)
        {
            switch (token.Tokenkind)
            {
                case TokenType.StartScope:
                    output.Append("{");
                    break;

                case TokenType.EndScope:
                    output.Append("}");
                    MaybeWriteComma(next, output);
                    break;

                case TokenType.StartList:
                case TokenType.StartDict:
                    output.Append("(");
                    break;

                case TokenType.EndList:
                case TokenType.EndDict:
                    output.Append(")");
                    MaybeWriteComma(next, output);
                    break;

                case TokenType.SimpleFieldValue:
                    WriteDeclarator(token.Field, output);
                    output.Append(token.Value);
                    MaybeWriteComma(next, output);
                    break;

                case TokenType.SeenBeforeWithReference:
                    WriteDeclarator(token.Field, output);
                    output.AppendFormat("-> {0}", token.ReferenceNo.Number);
                    MaybeWriteComma(next, output);
                    break;

                case TokenType.FieldnameWithTypeAndReference:
                    WriteDeclarator(token.Field, output);
                    if (token.ReferenceNo != null)
                    {
                        output.AppendFormat("(ref {0}) ", token.ReferenceNo);
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException("token.TokenKind", token.Tokenkind, ":-(");
            }
        }

        void WriteDeclarator(Field field, StringBuilder output)
        {
            if (field == null)
                return;
            if (field.Key != null)
                output.AppendFormat("[{0}] = ", field.Key);
            else if (field.Name != null && !field.Index.HasValue)
                output.AppendFormat("{0} = ", field.Name);
        }

        void MaybeWriteComma(Token next, StringBuilder output)
        {
            if (next == null) return;
            switch (next.Tokenkind)
            {
                case TokenType.SimpleFieldValue:
                case TokenType.SeenBeforeWithReference:
                case TokenType.FieldnameWithTypeAndReference:
                    output.Append(", ");
                    break;
            }
        }
    }

    public class Printable<T>
    {
        static readonly Stateprinter _printer = new Stateprinter();

        static Printable()
        {
            _printer.Configuration
                .SetOutputFormatter(new CompactFormatter())
                .Add(new AllFieldsAndPropertiesHarvester());
        }

        public override string ToString()
        {
            return _printer.PrintObject(this);
        }
    }
}
