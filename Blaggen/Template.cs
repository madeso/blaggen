using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Blaggen;

// todo(Gustav): improve error reporting in definition
// todo(Gustav): read from files
// todo(Gustav): import statement
// todo(Gustav): change function API to only allow constants or implement dynamic arguments in parser
// todo(Gustav): figure out how to handle type safety in functions...? reflection/cmd parser?

public static class Template
{
    public delegate string Func(List<string> args);

    public record Location(int Line, int Offset);
    public record Error(Location Location, string Message);
    
    public class Definition<TParent>
    {
        private readonly Dictionary<string, Func<TParent, string>> attributes = new ();
        private readonly Dictionary<string, Func<Node, (Func<TParent, string>, ImmutableArray<Error>)>> children = new();

        public Definition<TParent> AddVar(string name, Func<TParent, string> getter)
        {
            attributes.Add(name, getter);
            return this;
        }

        public Definition<TParent> AddList<TChild>(string name, Func<TParent, IEnumerable<TChild>> childSelector, Definition<TChild> childDef)
        {
            // todo(Gustav): implement Add
            children.Add(name, node =>
            {
                var (getter, errors) = childDef.Validate(node);
                if (errors.Length > 0) { return (SyntaxError, errors); }

                return (parent => string.Join("", childSelector(parent).Select(getter)), NoErrors());
            });
            return this;
        }

        private static string SyntaxError(TParent _) => "Syntax error";
        private static ImmutableArray<Error> NoErrors() => ImmutableArray<Error>.Empty;

        public (Func<TParent, string>, ImmutableArray<Error>) Validate(Node node)
        {
            // todo(Gustav): replace location with actual location
            var unknownLocation = new Location(-1, -1);

            switch (node)
            {
                case Node.Text text:
                    return (_ => text.Value, NoErrors());
                case Node.Attribute attribute:
                {
                    if (false == attributes.TryGetValue(attribute.Name, out var getter))
                    {
                        return (SyntaxError, ImmutableArray.Create(new Error(
                                unknownLocation,
                                $"Missing attribute ${attribute.Name}: {MatchStrings(attribute.Name, attributes.Keys)}"
                            )));
                    }
                    return (parent => getter(parent), NoErrors());
                }
                case Node.Iterate iterate:
                {
                    if (false == children.TryGetValue(iterate.Name, out var validator))
                    {
                        return (SyntaxError, ImmutableArray.Create(new Error(
                                unknownLocation,
                                $"Missing array {iterate.Name}: {MatchStrings(iterate.Name, children.Keys)}"
                            )));
                    }
                    return validator(iterate.Body);
                }
                case Node.FunctionCall fc:
                {
                    var validatedArgs = fc.Args.Select(Validate).ToImmutableArray();
                    var errors = validatedArgs.SelectMany(x => x.Item2).Distinct().ToImmutableArray();
                    if (errors.Length > 0) { return (SyntaxError, errors); }

                    var getters = validatedArgs.Select(x => x.Item1).ToImmutableArray();
                    return (parent => fc.Function(getters.Select(x => x(parent)).ToList()), NoErrors());
                }
                case Node.Group gr:
                {
                    var validatedArgs = gr.Nodes.Select(Validate).ToImmutableArray();
                    var errors = validatedArgs.SelectMany(x => x.Item2).Distinct().ToImmutableArray();
                    if (errors.Length > 0) { return (SyntaxError, errors); }

                    var getters = validatedArgs.Select(x => x.Item1).ToImmutableArray();
                    return (parent => string.Join("", getters.Select(x => x(parent)).ToList()), NoErrors());
                }

                default:
                    throw new Exception("Unhandled state");
            }
        }
    }

    public abstract record Node
    {
        private Node() {}

        internal record Text(string Value) : Node();
        internal record Attribute(string Name) : Node();
        internal record Iterate(string Name, Node Body) : Node();
        internal record FunctionCall(string Name, Func Function, List<Node> Args) : Node();
        internal record Group(List<Node> Nodes) : Node();
    }


    private static string MatchStrings(string name, IEnumerable<string> candidates)
    {
        var all = candidates.ToImmutableArray();

        var matches = EditDistance.ClosestMatches(name, 10, all).ToImmutableArray();

        return matches.Length > 0
            ? $"did you mean {ToArrayString(matches)} of {all.Length}: {ToArrayString(all)}"
            : $"No match in {all.Length}: {ToArrayString(all)}"
            ;

        static string ToArrayString(IEnumerable<string> candidates)
        {
            var s = string.Join(", ", candidates);
            return $"[{s}]";
        }
    }
    

    public static (Func<T, string>, ImmutableArray<Error>) Parse<T>(string source, Dictionary<string, Func> functions, Definition<T> definition)
    {
        var (tokens, lexerErrors) = Scanner(source);
        if (lexerErrors.Length > 0)
        { return (_ => "Lexing failed", lexerErrors); }

        var (node, parseErrors) = Parse(tokens, functions);
        if (parseErrors.Length > 0)
        { return (_ => "Parsing failed", parseErrors); }

        return definition.Validate(node);
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
        BeginCode, EndCode,
        BeginCodeTrim, EndCodeTrim,
        Ident,
        Dot,
        Comma,
        Pipe,
        LeftParen, RightParen,
        Hash,
        Slash,
        QuestionMark,
        Eof,
        KeywordIf, KeywordRange, KeywordEnd
    }
    private record Token(TokenType Type, string Lexeme, Location Location, string Value);
    
    
    private record ScannerLocation(int Line, int Offset, int Index);
    private static (ImmutableArray<Token>, ImmutableArray<Error>) Scanner(string source)
    {
        var start = new ScannerLocation(1, 0, 0);
        var current = start;
        var insideCodeBlock = false;

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
            if (insideCodeBlock)
            {
                var tok = ScanCodeToken();
                if (tok != null) yield return tok;
            }
            else
            {
                while (insideCodeBlock == false && false == IsAtEnd())
                {
                    var beforeStart = current;
                    var c = Advance();
                    if (c == '{' && Match('{'))
                    {
                        var beginType = TokenType.BeginCode;
                        if (Peek() == '-')
                        {
                            Advance();
                            beginType = TokenType.BeginCodeTrim;
                        }
                        var afterStart = current;
                        var text = CreateToken(TokenType.Text, null, start, beforeStart);
                        if(text.Value.Length > 0)
                        {
                            yield return text;
                        }
                        insideCodeBlock = true;

                        yield return CreateToken(beginType, null, beforeStart, current);
                    }
                }

                if (IsAtEnd())
                {
                    var text = CreateToken(TokenType.Text);
                    if (text.Value.Length > 0)
                    {
                        yield return text;
                    }
                }
            }
        }

        Token? ScanCodeToken()
        {
            var c = Advance();
            switch (c)
            {
                case '-':
                    if (!Match('}'))
                    {
                        ReportError(GetStartLocation(), "Detected rouge -");
                        return null;
                    }

                    if (!Match('}'))
                    {
                        ReportError(GetStartLocation(), "Detected rouge -}");
                        return null;
                    }

                    insideCodeBlock = false;
                    return CreateToken(TokenType.EndCodeTrim);
                case '}':
                    if (!Match('}'))
                    {
                        ReportError(GetStartLocation(), "Detected rouge {");
                        return null;
                    }

                    insideCodeBlock = false;
                    return CreateToken(TokenType.EndCode);
                case '|': return CreateToken(TokenType.Pipe);
                case ',': return CreateToken(TokenType.Comma);
                case '(': return CreateToken(TokenType.LeftParen);
                case ')': return CreateToken(TokenType.RightParen);
                case '#': return CreateToken(TokenType.Hash);
                case '.': return CreateToken(TokenType.Dot);
                case '?': return CreateToken(TokenType.QuestionMark);

                case '/':
                    if(!Match('*')) {return CreateToken(TokenType.Slash);}
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
                        ReportError(GetStartLocation(), "Unterminated string.");
                        return null;
                    }

                    // The closing ".
                    Advance();

                    // Trim the surrounding quotes.
                    var value = source.Substring(start.Index + 1, current.Index - start.Index - 2);
                    return CreateToken(TokenType.Ident, value);

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

                        return CreateToken(TokenType.Ident);
                    }
                    else if (IsAlpha(c))
                    {
                        while (IsAlphaNumeric(Peek())) Advance();
                        var ident = CreateToken(TokenType.Ident);

                        // check keywords
                        switch (ident.Value)
                        {
                            case "has": return ident with { Type = TokenType.KeywordIf };
                            case "range": return ident with { Type = TokenType.KeywordRange };
                            case "end": return ident with { Type = TokenType.KeywordEnd };
                        }

                        return ident;
                    }
                    else
                    {
                        ReportError(GetStartLocation(), $"Unexpected character {c}");
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

        Location GetStartLocation(ScannerLocation? stt = null)
        {
            var st = stt ?? start;
            return new Location(st.Line, st.Offset);
        }

        Token CreateToken(TokenType tt, string? value = null, ScannerLocation? begin = null, ScannerLocation? end = null)
        {
            var st = begin ?? start;
            var cu = end ?? current;
            var text = source.Substring(st.Index, cu.Index-st.Index);
            return new Token(tt, text, GetStartLocation(st), value ?? text);
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
                    case TokenType.BeginCodeTrim:
                        if (lastToken is { Type: TokenType.Text })
                        {
                            yield return lastToken with { Value = lastToken.Value.TrimEnd() };
                        }

                        lastToken = tok with { Type = TokenType.BeginCode };
                        break;
                    case TokenType.Text when lastToken is { Type: TokenType.EndCodeTrim }:
                        yield return lastToken with { Type = TokenType.EndCode };
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
                if (lastToken is { Type: TokenType.BeginCode} && tok.Type == TokenType.EndCode)
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

        static IEnumerable<Token> TransformSingleCharsToKeywords(IEnumerable<Token> tokens)
        {
            var eatIdent = false;
            Token? lastToken = null;
            foreach (var tok in tokens)
            {
                if (tok.Type == TokenType.Ident && eatIdent)
                {
                    eatIdent = false;
                    continue;
                }

                switch (tok.Type)
                {
                    case TokenType.Slash when lastToken is { Type: TokenType.BeginCode }:
                        yield return lastToken;
                        lastToken = tok with { Type = TokenType.KeywordEnd };
                        eatIdent = true;
                        break;
                    case TokenType.Hash when lastToken is { Type:TokenType.BeginCode }:
                        yield return lastToken;
                        lastToken = tok with { Type = TokenType.KeywordRange };
                        break;
                    case TokenType.QuestionMark when lastToken is { Type: TokenType.BeginCode }:
                        yield return lastToken;
                        lastToken = tok with { Type = TokenType.KeywordIf };
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

        var tokens = TransformSingleCharsToKeywords(TrimEmptyStartEnd(TrimTextTokens(itok))).ToImmutableArray();

        var current = 0;

        var errors = new List<Error>();

        var rootNode = ParseGroup();

        if (!IsAtEnd())
        {
            ReportError(Peek().Location, ExpectedMessage("EOF"));
        }

        if (errors.Count == 0)
        {
            return (rootNode, errors.ToImmutableArray());
        }
        return (new Node.Text("Parsing failed"),  errors.ToImmutableArray());

        Node ParseGroup()
        {
            var nodes = new List<Node>();
            while (!IsAtEnd() && !(Peek().Type == TokenType.BeginCode && PeekNext() == TokenType.KeywordEnd))
            {
                try
                {
                    ParseNode(nodes);
                }
                catch (ParseError)
                {
                    Synchronize();
                }
            }

            return new Node.Group(nodes);
        }

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

        TokenType PeekNext()
        {
            if (current + 1 >= tokens.Length) return TokenType.Eof;
            return tokens[current + 1].Type;
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
                if (Previous().Type == TokenType.EndCode) return;

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
                throw ReportError(Peek().Location, ExpectedMessage("identifier"));
            }

            return new Node.Text(Advance().Value);
        }

        string ExtractAttributeName()
        {
            var ident = Consume(TokenType.Ident, ExpectedMessage("IDENT"));
            return ident.Value;
        }

        string ExpectedMessage(string what)
        {
            return $"Expected {what} but found {TokenToMessage(Peek())}";
        }

        void ParseNode(List<Node> nodes)
        {
            switch (Peek().Type)
            {
                case TokenType.BeginCode:
                    Advance();

                    if (Match(TokenType.KeywordRange))
                    {
                        var attribute = ExtractAttributeName();
                        Consume(TokenType.EndCode, ExpectedMessage("}}"));

                        var group = ParseGroup();
                        Consume(TokenType.BeginCode, ExpectedMessage("{{"));
                        Consume(TokenType.KeywordEnd, ExpectedMessage("keyword end"));
                        Consume(TokenType.EndCode, ExpectedMessage("}}"));

                        nodes.Add(new Node.Iterate(attribute, group));
                    }
                    else
                    {
                        ParseAttributeToEnd(nodes);
                    }
                    break;
                case TokenType.Text:
                    var text = Advance();
                    nodes.Add(new Node.Text(text.Value));
                    break;
                default:
                    throw ReportError(Peek().Location, $"Unexpected token {TokenToMessage(Peek())}");
            }
        }

        void ParseAttributeToEnd(List<Node> nodes)
        {
            Node node = new Node.Attribute(ExtractAttributeName());

            while (Peek().Type == TokenType.Pipe)
            {
                Advance();
                var name = Consume(TokenType.Ident, ExpectedMessage("function name"));
                var arguments = new List<Node> { node };

                if (Match(TokenType.LeftParen))
                {
                    while (Peek().Type != TokenType.RightParen && !IsAtEnd())
                    {
                        arguments.Add(ParseFunctionArg());

                        if (Peek().Type != TokenType.RightParen)
                        {
                            Consume(TokenType.Comma, ExpectedMessage("comma for the next function argument"));
                        }
                    }

                    Consume(TokenType.RightParen, ExpectedMessage(") to end function"));
                }

                if (functions.TryGetValue(name.Value, out var f))
                {
                    node = new Node.FunctionCall(name.Value, f, arguments);
                }
                else
                {
                    ReportError(name.Location, $"Unknown function named {name.Value}: {MatchStrings(name.Value, functions.Keys)}");
                }
            }
            nodes.Add(node);

            Consume(TokenType.EndCode, ExpectedMessage("end token"));
        }
    }
}
