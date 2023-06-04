using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Blaggen;

/*
API GOAL:
var file = ParseFile(...);
vqr generator = file.ForType(
    new Definition<Foo>()
        .addVar("x", foo=>foo.x)
        .addBool("b", foo=>foo.b)
        .addList<Bar>("l", foo=>foo.bar, new Definition<Bar>());
);

var str = generator(foo);
*/


public static class Template
{
    public delegate string Func(List<string> args);

    public record Location(int Line, int Offset);
    public record Error(Location Location, string Message);

    public record Node
    {
        private Node() {}

        internal record Text(string Value) : Node();
        internal record Attribute(string Name) : Node();
        internal record FunctionCall(string Name, Func Function, List<Node> Args) : Node();
        internal record Group(List<Node> Nodes) : Node();

        public string Evaluate(Dictionary<string, string> data)
        {
            return this switch
            {
                Text text => text.Value,
                Attribute attribute => data.TryGetValue(attribute.Name, out var value)
                    ? value
                    : string.Empty,
                FunctionCall fc => fc.Function(fc.Args.Select(a => a.Evaluate(data)).ToList()),
                Group gr => string.Join("", gr.Nodes.Select(n => n.Evaluate(data))),
            };
        }
    }
    

    public static (Node, ImmutableArray<Error>) Parse(string source, Dictionary<string, Func> functions)
    {
        var (tokens, lexerErrors) = Scanner(source);
        if (lexerErrors.Length > 0)
        {
            return (new Node.Text("Lexing failed"), lexerErrors);
        }

        return Parse(tokens, functions);
    }


    public static Dictionary<string, Func> DefaultFunctions()
    {
        var culture = new CultureInfo("en-US", false);

        var t = new Dictionary<string, Func>();
        t.Add("capitalize", args => Capitalize(args[0], true));
        t.Add("lower", args => culture.TextInfo.ToLower(args[0]));
        t.Add("upper", args => culture.TextInfo.ToUpper(args[0]));
        t.Add("title", args => culture.TextInfo.ToTitleCase(args[0]));
        t.Add("rtrim", args => args[0].TrimEnd(GetOptionalValue(args, 1).ToCharArray()));
        t.Add("ltrim", args => args[0].TrimStart(GetOptionalValue(args, 1).ToCharArray()));
        t.Add("trim", args => args[0].Trim(GetOptionalValue(args, 1).ToCharArray()));
        t.Add("zfill", args => zfill(args[0], GetOptionalValue(args, 1, "3")));
        t.Add("replace", args => args[0].Replace(args[1], args[2]));
        t.Add("substr", args => args[0].Substring(int.Parse(args[1]), int.Parse(GetOptionalValue(args, 2))));
        return t;

        static string GetOptionalValue(List<string> args, int i, string d = "")
        {
            if (args.Count > i)
            {
                return args[i];
            }

            return d;
        }

        static string Capitalize(string p, bool alsoFirstChar)
        {
            bool cap = alsoFirstChar;
            StringBuilder sb = new StringBuilder();
            foreach (char h in p.ToLower())
            {
                char c = h;
                if (char.IsLetter(c) && cap)
                {
                    c = char.ToUpper(c);
                    cap = false;
                }
                if (char.IsWhiteSpace(c)) cap = true;
                sb.Append(c);
            }
            return sb.ToString();
        }

        static string zfill(string str, string scount)
        {
            int i = int.Parse(scount);
            return str.PadLeft(i, '0');
        }
    }


    // --------------------------------------------------------------------------------------------


    private enum TokenType
    {
        Text,
        Begin, End,
        BeginTrim, EndTrim,
        Ident,
        Dot,
        Comma,
        Pipe,
        LeftParen, RightParen,
        Hash,
        Slash,
        Eof,
    }
    private record Token(TokenType Type, string Lexeme, Location Location, string Value);
    
    
    private record ScannerLocation(int Line, int Offset, int Index);
    private static (ImmutableArray<Token>, ImmutableArray<Error>) Scanner(string source)
    {
        var start = new ScannerLocation(1, 0, 0);
        var current = start;
        var inside = false;

        var errors = new List<Error>();
        var ret = new List<Token>();

        while (false == IsAtEnd())
        {
            start = current;
            ret.AddRange(ScanToken());
        }
        ret.Add(new Token(TokenType.Eof, "", new Location(current.Line, current.Offset), string.Empty));

        if (errors.Count != 0)
        {
            ret.Clear();
        }
        return (ret.ToImmutableArray(), errors.ToImmutableArray());

        void ReportError(Location loc, string message)
        {
            errors.Add(new Error(loc, message));
        }

        IEnumerable<Token> ScanToken()
        {
            if (inside)
            {
                var tok = ScanInsideToken();
                if (tok != null) yield return tok;
            }
            else
            {
                while (inside == false && false == IsAtEnd())
                {
                    var beforeStart = current;
                    var c = Advance();
                    if (c == '{' && Match('{'))
                    {
                        var beginType = TokenType.Begin;
                        if (Peek() == '-')
                        {
                            Advance();
                            beginType = TokenType.BeginTrim;
                        }
                        var afterStart = current;
                        var text = AddToken(TokenType.Text, null, start, beforeStart);
                        if(text.Value.Length > 0)
                        {
                            yield return text;
                        }
                        inside = true;

                        yield return AddToken(beginType, null, beforeStart, current);
                    }
                }

                if (IsAtEnd())
                {
                    var text = AddToken(TokenType.Text);
                    if (text.Value.Length > 0)
                    {
                        yield return text;
                    }
                }
            }
        }

        Token? ScanInsideToken()
        {
            var c = Advance();
            switch (c)
            {
                case '-':
                    if (!Match('}'))
                    {
                        ReportError(StartLocation(), "Detected rouge -");
                        return null;
                    }

                    if (!Match('}'))
                    {
                        ReportError(StartLocation(), "Detected rouge -}");
                        return null;
                    }

                    inside = false;
                    return AddToken(TokenType.EndTrim);
                case '}':
                    if (!Match('}'))
                    {
                        ReportError(StartLocation(), "Detected rouge {");
                        return null;
                    }

                    inside = false;
                    return AddToken(TokenType.End);
                case '|': return AddToken(TokenType.Pipe);
                case ',': return AddToken(TokenType.Comma);
                case '(': return AddToken(TokenType.LeftParen);
                case ')': return AddToken(TokenType.RightParen);
                case '#': return AddToken(TokenType.Hash);
                case '.': return AddToken(TokenType.Dot);

                case '/':
                    if(!Match('*')) {return AddToken(TokenType.Slash);}
                    while (!(Peek() == '*' && PeekNext() == '/')  && !IsAtEnd())
                    {
                        Advance();
                    }

                    // skip * and /
                    if (!IsAtEnd())
                    {
                        Advance();
                        Advance();
                    }

                    return null;

                case '"':
                    while (Peek() != '"' && !IsAtEnd())
                    {
                        Advance();
                    }

                    if (IsAtEnd())
                    {
                        ReportError(StartLocation(), "Unterminated string.");
                        return null;
                    }

                    // The closing ".
                    Advance();

                    // Trim the surrounding quotes.
                    var value = source.Substring(start.Index + 1, current.Index - start.Index - 2);
                    return AddToken(TokenType.Ident, value);

                case ' ':
                case '\r':
                case '\n':
                case '\t':
                    return null;

                default:
                    if (IsDigit(c))
                    {
                        while (IsDigit(Peek())) Advance();

                        // Look for a fractional part.
                        if (Peek() == '.' && IsDigit(PeekNext()))
                        {
                            // Consume the "."
                            Advance();

                            while (IsDigit(Peek())) Advance();
                        }

                        return AddToken(TokenType.Ident);
                    }
                    else if (IsAlpha(c))
                    {
                        while (IsAlphaNumeric(Peek())) Advance();

                        return AddToken(TokenType.Ident);
                    }
                    else
                    {
                        ReportError(StartLocation(), $"Unexpected character {c}");
                        return null;
                    }
            }
        }

        static bool IsDigit(char c)
        {
            return c is >= '0' and <= '9';
        }

        static bool IsAlpha(char c)
        {
            return c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_';
        }

        static bool IsAlphaNumeric(char c)
        {
            return IsAlpha(c) || IsDigit(c);
        }

        ScannerLocation NextChar()
        {
            return current with { Index = current.Index + 1, Offset = current.Offset + 1 };
        }

        bool Match(char expected)
        {
            if (IsAtEnd()) { return false; }
            if (source[current.Index] != expected) { return false; }

            current = NextChar();
            return true;
        }

        char Peek()
        {
            return IsAtEnd() ? '\0' : source[current.Index];
        }

        char PeekNext()
        {
            if (current.Index + 1 >= source.Length) return '\0';
            return source[current.Index + 1];
        }

        char Advance()
        {
            var ret = source[current.Index];
            current = NextChar();
            if (ret == '\n')
            {
                current = current with { Offset = 0, Line = current.Line + 1 };
            }
            return ret;
        }

        Location StartLocation(ScannerLocation? stt = null)
        {
            var st = stt ?? start;
            return new Location(st.Line, st.Offset);
        }

        Token AddToken(TokenType tt, string? value = null, ScannerLocation? begin = null, ScannerLocation? end = null)
        {
            var st = begin ?? start;
            var cu = end ?? current;
            var text = source.Substring(st.Index, cu.Index-st.Index);
            return new Token(tt, text, StartLocation(st), value ?? text);
        }

        bool IsAtEnd()
        {
            return current.Index >= source.Length;
        }
    }


    private class ParseError : Exception { }
    private static (Node, ImmutableArray<Error>) Parse(IEnumerable<Token> itok, Dictionary<string, Func> functions)
    {
        static IEnumerable<Token> TrimTextTokens(IEnumerable<Token> tokens)
        {
            Token? lastToken = null;
            foreach (var tok in tokens)
            {
                switch (tok.Type)
                {
                    case TokenType.BeginTrim:
                        if (lastToken is { Type: TokenType.Text })
                        {
                            yield return lastToken with { Value = lastToken.Value.TrimEnd() };
                        }

                        lastToken = tok with { Type = TokenType.Begin };
                        break;
                    case TokenType.Text when lastToken is { Type: TokenType.EndTrim }:
                        yield return lastToken with { Type = TokenType.End };
                        lastToken = tok with { Value = tok.Value.TrimStart() };
                        break;
                    default:
                        if (lastToken != null)
                        {
                            yield return lastToken;
                        }

                        lastToken = tok;
                        break;
                }
            }

            if (lastToken != null)
            {
                yield return lastToken;
            }
        }

        static IEnumerable<Token> TrimEmptyStartEnd(IEnumerable<Token> tokens)
        {
            Token? lastToken = null;
            foreach (var tok in tokens)
            {
                if (lastToken is { Type: TokenType.Begin} && tok.Type == TokenType.End)
                {
                    lastToken = null;
                    continue;
                }

                if (lastToken != null)
                {
                    yield return lastToken;
                }

                lastToken = tok;
            }

            if (lastToken != null)
            {
                yield return lastToken;
            }
        }

        var tokens = TrimEmptyStartEnd(TrimTextTokens(itok)).ToImmutableArray();

        var current = 0;

        var nodes = new List<Node>();
        var errors = new List<Error>();

        while (!IsAtEnd())
        {
            try
            {
                ParseNode();
            }
            catch (ParseError)
            {
                Synchronize();
            }
        }

        if(errors.Count == 0) {
            return (new Node.Group(nodes), errors.ToImmutableArray());
        }
        return (new Node.Text("Parsing failed"),  errors.ToImmutableArray());

        ParseError ReportError(Location loc, string message)
        {
            errors.Add(new Error(loc, message));
            return new ParseError();
        }

        bool Match(params TokenType[] types)
        {
            if (!types.Any(Check)) return false;

            Advance();
            return true;
        }

        bool Check(TokenType type)
        {
            if (IsAtEnd()) return false;
            return Peek().Type == type;
        }

        Token Advance()
        {
            if (!IsAtEnd()) current++;
            return Previous();
        }

        bool IsAtEnd()
        {
            return Peek().Type == TokenType.Eof;
        }

        Token Peek()
        {
            return tokens[current];
        }

        Token Previous()
        {
            return tokens[current - 1];
        }

        void Synchronize()
        {
            Advance();

            while (!IsAtEnd())
            {
                if (Previous().Type == TokenType.End) return;

                switch (Peek().Type)
                {
                    case TokenType.Text:
                        return;
                }

                Advance();
            }
        }

        static string TokenToMessage(Token token)
        {
            var value = token.Type == TokenType.Text ? "" : $": {token.Lexeme}";
            return $"{token.Type}{value}";
        }

        Token Consume(TokenType type, string message)
        {
            if (Check(type)) return Advance();

            throw ReportError(Peek().Location, message);
        }

        Node ParseFunctionArg()
        {
            if(Peek().Type != TokenType.Ident)
            {
                throw ReportError(Peek().Location, $"Expected identifier but found {TokenToMessage(Peek())}");
            }

            return new Node.Text(Advance().Value);
        }

        void ParseNode()
        {
            switch (Peek().Type)
            {
                case TokenType.Begin:
                    Advance();
                    if (Peek().Type != TokenType.Ident)
                    {
                        throw ReportError(Peek().Location, $"Expected IDENT, found {TokenToMessage(Peek())}");
                    }

                    Node node = new Node.Attribute(Advance().Value);

                    while (Peek().Type == TokenType.Pipe)
                    {
                        Advance();
                        var name = Consume(TokenType.Ident, $"Expected function name but found {TokenToMessage(Peek())}");
                        var arguments = new List<Node> { node };

                        if (Match(TokenType.LeftParen))
                        {
                            while (Peek().Type != TokenType.RightParen && !IsAtEnd())
                            {
                                arguments.Add(ParseFunctionArg());

                                if (Peek().Type != TokenType.RightParen)
                                {
                                    Consume(TokenType.Comma, $"Expected comma for the next function argument but found {TokenToMessage(Peek())}");
                                }
                            }

                            Consume(TokenType.RightParen, $"Expected ) to end function but found {TokenToMessage(Peek())}");
                        }

                        if (functions.TryGetValue(name.Value, out var f))
                        {
                            node = new Node.FunctionCall(name.Value, f, arguments);
                        }
                        else
                        {
                            ReportError(name.Location, $"Unknown function named {name.Value}");
                        }
                    }
                    nodes.Add(node);

                    Consume(TokenType.End, $"Expected end token but found {TokenToMessage(Peek())}");
                    break;
                case TokenType.Text:
                    var text = Advance();
                    nodes.Add(new Node.Text(text.Value));
                    break;
                default:
                    throw ReportError(Peek().Location, $"Unexpected token {TokenToMessage(Peek())}");
            }
        }
    }
}