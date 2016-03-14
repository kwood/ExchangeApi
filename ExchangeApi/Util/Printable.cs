using StatePrinter;
using StatePrinter.Configurations;
using StatePrinter.FieldHarvesters;
using StatePrinter.Introspection;
using StatePrinter.OutputFormatters;
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
            tokens = UnfuckupSimpleDicts(tokens);
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

        // Stateprinter has special logic to handle dictinaries with simple key types.
        // It forces you to print such dictionaries as dict[key] = value, dict[key] = value, etc.
        // We want it to look like dict = ([key] = value, [key] = value), just like lists.
        public List<Token> UnfuckupSimpleDicts(List<Token> tokens)
        {
            var res = new List<Token>();
            for (int i = 0; i != tokens.Count; ++i)
            {
                if (tokens[i].Tokenkind == TokenType.StartEnumeration &&
                    i != 0 && i != tokens.Count - 1 &&
                    tokens[i - 1].Tokenkind != TokenType.FieldnameWithTypeAndReference)
                {
                    if (tokens[i + 1].Tokenkind == TokenType.EndEnumeration)
                    {
                        ++i;
                        continue;
                    }
                    Field next = tokens[i + 1].Field;
                    if (next != null && next.SimpleKeyInArrayOrDictionary != null && next.Name != null)
                    {
                        res.Add(new Token(TokenType.FieldnameWithTypeAndReference, new Field(next.Name)));
                    }
                }
                res.Add(tokens[i]);
            }
            return res;
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

                case TokenType.StartEnumeration:
                    output.Append("(");
                    break;
                case TokenType.EndEnumeration:
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
            if (field.SimpleKeyInArrayOrDictionary != null)
                output.AppendFormat("[{0}]", field.SimpleKeyInArrayOrDictionary);
            else if (field.Name != null)
                output.Append(field.Name);
            else
                return;
            output.Append(" = ");
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
