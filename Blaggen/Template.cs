using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Web;
[assembly: InternalsVisibleTo("BlaggenTest")]

namespace Blaggen;

/*

  API:
    var (generator, error) = Template.Parse(...);
    string ret = generator(myClass);

  Template syntax:
    {{ prop }} {{- "also prop, trim printable spaces" -}}
    {{prop | function | function(with_arguments)}}
    {{include file}} {{include "file/with.extension"}}
    {{#list}}repeated{{/list}} {{range also_list}}repeated{{end}}
    {{if bool_prop}}perhaps{{end}}

*/

// todo(Gustav): add foo.bar and .foobar accessors to access subdicts and global dicts
// todo(Gustav): add documentation to properties and functions so error messages can be more helpful, this also means we can generate documentation for the current build
// todo(Gustav): should return values always be strings? properties return datetime that format functions could format
internal static class Template
{
    internal record Str(string Value, bool IsEscaped);
    internal delegate string EscapeFunction(Str s);
    internal static string Escape_NoEscape(Str s) => s.Value;
    internal static string Escape_Html(Str s) => s.IsEscaped ? s.Value : HttpUtility.HtmlEncode(s.Value);


    static Str JoinStr(IEnumerable<Str> str, EscapeFunction escape)
    {
        var ss = str.Select(x => escape(x));
        var combined = string.Join("", ss);
        return new Str(combined, true);
    }

    internal record Location(FileInfo File, int Line, int Offset);
    internal record Error(Location Location, string Message);
    internal record FuncArgument(Location Location, Str Argument);

    // parse arguments, return function call or error
    internal delegate (Func, ImmutableArray<Error>) FuncGenerator(Location call, ImmutableArray<FuncArgument> arguments);

    // apply dynamic string function and return result
    internal delegate Str Func(Str arg);

    
    internal class Definition<TParent>
    {
        private readonly Dictionary<string, Func<TParent, string>> attributes = new ();
        private readonly Dictionary<string, Func<TParent, bool>> bools = new();
        private readonly Dictionary<string, Func<Node, EscapeFunction, (Func<TParent, Str>, ImmutableArray<Error>)>> children = new();

        internal Definition<TParent> AddVar(string name, Func<TParent, string> getter)
        {
            attributes.Add(name, getter);
            return this;
        }

        internal Definition<TParent> AddBool(string name, Func<TParent, bool> getter)
        {
            bools.Add(name, getter);
            return this;
        }

        internal Definition<TParent> AddList<TChild>(string name, Func<TParent, IEnumerable<TChild>> childSelector, Definition<TChild> childDef)
        {
            children.Add(name, (node, esc) =>
            {
                var (getter, errors) = childDef.Validate(node, esc);
                if (errors.Length > 0) { return (SyntaxError, errors); }

                return (parent => JoinStr(childSelector(parent).Select(getter), esc), NoErrors);
            });
            return this;
        }

        public Definition<TParent> Add(Action<Definition<TParent>> cb)
        {
            cb(this);
            return this;
        }

        private static Str SyntaxError(TParent _) => new("Syntax error", true);

        internal (Func<TParent, Str>, ImmutableArray<Error>) Validate(Node node, EscapeFunction escape)
        {
            switch (node)
            {
                case Node.Text text:
                    return (_ => new Str(text.Value, true), NoErrors);
                case Node.Attribute attribute:
                {
                    if (false == attributes.TryGetValue(attribute.Name, out var getter))
                    {
                        return (SyntaxError, [
                            new Error(
                                attribute.Location,
                                $"Missing attribute {attribute.Name}: {MatchStrings(attribute.Name, attributes.Keys)}"
                            )
                        ]);
                    }
                    return (parent => new Str(getter(parent), false), NoErrors);
                }
                case Node.If check:
                {
                    if (false == bools.TryGetValue(check.Name, out var getter))
                    {
                        return (SyntaxError, [
                            new Error(
                                check.Location,
                                $"Missing bool {check.Name}: {MatchStrings(check.Name, bools.Keys)}"
                            )
                        ]);
                    }

                    var (body, errors) = Validate(check.Body, escape);
                    if (errors.Length > 0) { return (SyntaxError, errors); }

                    return (parent => getter(parent) ? body(parent) : new Str(string.Empty, true), NoErrors);
                }
                case Node.Iterate iterate:
                {
                    if (false == children.TryGetValue(iterate.Name, out var validator))
                    {
                        return (SyntaxError, [
                            new Error(
                                iterate.Location,
                                $"Missing array {iterate.Name}: {MatchStrings(iterate.Name, children.Keys)}"
                            )
                        ]);
                    }
                    return validator(iterate.Body, escape);
                }
                case Node.FunctionCall fc:
                {
                    var (getter, errors) = Validate(fc.Arg, escape);
                    if (errors.Length > 0) { return (SyntaxError, errors); }

                    return (parent => fc.Function(getter(parent)), NoErrors);
                }
                case Node.Group gr:
                {
                    var validatedArgs = gr.Nodes.Select(x => Validate(x, escape)).ToImmutableArray();
                    var errors = validatedArgs.SelectMany(x => x.Item2).Distinct().ToImmutableArray();
                    if (errors.Length > 0) { return (SyntaxError, errors); }

                    var getters = validatedArgs.Select(x => x.Item1).ToImmutableArray();
                    return (parent => JoinStr(getters.Select(x => x(parent)), escape), NoErrors);
                }

                default:
                    throw new Exception("Unhandled state");
            }
        }
    }

    internal static async Task<(Func<T, string>, ImmutableArray<Error>)> Parse<T>(FileInfo path, VfsRead vfs, Dictionary<string, FuncGenerator> functions, DirectoryInfo include_dir, Definition<T> definition, EscapeFunction escape)
    {
        var source = await vfs.ReadAllTextAsync(path);
        var (tokens, lexerErrors) = Scanner(path, source);
        if (lexerErrors.Length > 0)
        { return (_ => "Lexing failed", lexerErrors); }

        var (node, parseErrors) = await Parse(tokens, functions, include_dir, path.Extension, vfs);
        if (parseErrors.Length > 0)
        { return (_ => "Parsing failed", parseErrors); }

        var (func, err) = definition.Validate(node, escape);

        return (t => escape(func(t)), err);
    }

    public enum EscapeString
    {
        Yes,
        No
    }

    public static FuncGenerator NoArguments(Func<string, string> f, EscapeString escape_string = EscapeString.Yes)
    {
        return (location, args) =>
        {
            if (args.IsEmpty == false)
            {
                return (
                    _ => new Str("syntax error", false),
                    [new Error(location, "Expected zero arguments")]);
            }
            return (arg => new Str(f(arg.Value), escape_string == EscapeString.No), NoErrors);
        };
    }

    internal static Dictionary<string, FuncGenerator> DefaultFunctions()
    {
        var culture = new CultureInfo("en-US", false);

        var t = new Dictionary<string, FuncGenerator>();
        t.Add("capitalize", NoArguments(args => Capitalize(args, true)));
        t.Add("lower", NoArguments(args => culture.TextInfo.ToLower(args)));
        t.Add("upper", NoArguments(args => culture.TextInfo.ToUpper(args)));
        t.Add("title", NoArguments(args => culture.TextInfo.ToTitleCase(args)));
        t.Add("safeHTML", NoArguments(args => args, EscapeString.No));

        t.Add("rtrim", OptionalStringArgument((str, spaceChars) => str.TrimEnd(spaceChars.ToCharArray()), " \t\n\r"));
        t.Add("ltrim", OptionalStringArgument((str, spaceChars) => str.TrimStart(spaceChars.ToCharArray()), " \t\n\r"));
        t.Add("trim", OptionalStringArgument((str, spaceChars) => str.Trim(spaceChars.ToCharArray()), " \t\n\r"));
        t.Add("zfill", OptionalIntArgument((str, count) => str.PadLeft(count, '0'), 3));

        static FuncGenerator OptionalStringArgument(Func<string, string, string> f, string missing)
        {
            return (location, args) =>
            {
                return args.Length switch
                {
                    0 => (arg => new Str(f(arg.Value, missing), false), NoErrors),
                    1 => (arg => new Str(f(arg.Value, args[0].Argument.Value), false), NoErrors),
                    _ => (_ => new Str("syntax error", false),
                    [
                        new Error(
                            location,
                            "Expected zero or one string argument")
                    ])
                };
            };
        }

        static FuncGenerator OptionalIntArgument(Func<string, int, string> f, int missing)
        {
            return (location, args) =>
            {
                return args.Length switch
                {
                    0 => (arg => new Str(f(arg.Value, missing), false), NoErrors),
                    1 => int.TryParse(args[0].Argument.Value, out var number)
                        ? (arg => new Str(f(arg.Value, number), false), NoErrors)
                        : (_ => new Str("syntax error", false),
                        [
                            new Error(location, "This function takes zero or one int argument"),
                                new Error(args[0].Location, "this is not a int")
                        ])
                    ,
                    _ => (_ => new Str("syntax error", false),
                    [
                        new Error(
                            location,
                            "Expected zero or one int argument")
                    ])
                };
            };
        }

        t.Add("replace", StringStringArgument((arg, lhs, rhs) => arg.Replace(lhs, rhs)));
        t.Add("substr", IntIntArgument((arg, lhs, rhs)=> arg.Substring(lhs, rhs)));
        return t;

        static (Func, ImmutableArray<Error>) SyntaxError(params Error[] errors)
            => (_ => new Str("syntax error", false), [..errors]);

        static FuncGenerator StringStringArgument(Func<string, string, string, string> f)
        {
            return (location, args) =>
            {
                if (args.Length != 2)
                {
                    return SyntaxError(new Error(location, "Expected two arguments"));
                }
                return (arg => new Str(f(arg.Value, args[0].Argument.Value, args[1].Argument.Value), false), NoErrors);
            };
        }

        static FuncGenerator IntIntArgument(Func<string, int, int, string> f)
        {
            return (location, args) =>
            {
                if (args.Length != 2)
                {
                    return SyntaxError(new Error(location, "Expected two arguments"));
                }

                if (int.TryParse(args[0].Argument.Value, out var lhs) == false)
                {
                    return SyntaxError(new Error(args[0].Location, "Not a integer"));
                }

                if (int.TryParse(args[1].Argument.Value, out var rhs) == false)
                {
                    return SyntaxError(new Error(args[1].Location, "Not a integer"));
                }

                return (arg => new Str(f(arg.Value, lhs, rhs), false), NoErrors);
            };
        }

        static string Capitalize(string p, bool alsoFirstChar)
        {
            var cap = alsoFirstChar;
            var sb = new StringBuilder();
            foreach (var h in p.ToLower())
            {
                var c = h;
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
    }


    // --------------------------------------------------------------------------------------------

    internal abstract record Node
    {
        private Node() { }

        internal record Text(string Value, Location Location) : Node();
        internal record Attribute(string Name, Location Location) : Node();
        internal record Iterate(string Name, Node Body, Location Location) : Node();
        internal record If(string Name, Node Body, Location Location) : Node();
        internal record FunctionCall(string Name, Func Function, Node Arg, Location Location) : Node();
        internal record Group(List<Node> Nodes, Location Location) : Node();
    }

    private static ImmutableArray<Error> NoErrors => ImmutableArray<Error>.Empty;
    private static Location UnknownLocation => new(new FileInfo("unknown-file.txt"), -1, -1);

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
        KeywordIf, KeywordRange, KeywordEnd, KeywordInclude
    }
    private record Token(TokenType Type, string Lexeme, Location Location, string Value);
    
    
    private record ScannerLocation(int Line, int Offset, int Index);
    private static (ImmutableArray<Token>, ImmutableArray<Error>) Scanner(FileInfo file, string source)
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
        ret.Add(new Token(TokenType.Eof, "", new Location(file, current.Line, current.Offset), string.Empty));

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
                            case "if": return ident with { Type = TokenType.KeywordIf };
                            case "range": return ident with { Type = TokenType.KeywordRange };
                            case "end": return ident with { Type = TokenType.KeywordEnd };
                            case "include": return ident with { Type = TokenType.KeywordInclude };
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

        static bool IsDigit(char c) => c is >= '0' and <= '9';
        static bool IsAlpha(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_';
        static bool IsAlphaNumeric(char c) => IsAlpha(c) || IsDigit(c);
        ScannerLocation NextChar() => current with { Index = current.Index + 1, Offset = current.Offset + 1 };

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
            var suggestedIndex = current.Index + 1;
            return suggestedIndex >= source.Length ? '\0' : source[suggestedIndex];
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
            return new Location(file, st.Line, st.Offset);
        }

        Token CreateToken(TokenType tt, string? value = null, ScannerLocation? begin = null, ScannerLocation? end = null)
        {
            var st = begin ?? start;
            var cu = end ?? current;
            var text = source.Substring(st.Index, cu.Index-st.Index);
            return new Token(tt, text, GetStartLocation(st), value ?? text);
        }

        bool IsAtEnd() => current.Index >= source.Length;
    }


    private class ParseError : Exception { }
    private static async Task<(Node, ImmutableArray<Error>)> Parse(IEnumerable<Token> itok, Dictionary<string, FuncGenerator> functions, DirectoryInfo includeDir, string defaultExtension, VfsRead vfs)
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

        var rootNode = await ParseGroup();

        if (!IsAtEnd())
        {
            ReportError(Peek().Location, ExpectedMessage("EOF"));
        }

        if (errors.Count == 0)
        {
            return (rootNode, errors.ToImmutableArray());
        }
        return (new Node.Text("Parsing failed", UnknownLocation),  errors.ToImmutableArray());

        async Task<Node> ParseGroup()
        {
            var start = Peek().Location;
            var nodes = new List<Node>();
            while (!IsAtEnd() && !(Peek().Type == TokenType.BeginCode && PeekNext() == TokenType.KeywordEnd))
            {
                try
                {
                    await ParseNode(nodes);
                }
                catch (ParseError)
                {
                    Synchronize();
                }
            }

            return new Node.Group(nodes, start);
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

        FuncArgument ParseFunctionArg()
        {
            if(Peek().Type != TokenType.Ident)
            {
                throw ReportError(Peek().Location, ExpectedMessage("identifier"));
            }

            var arg = Advance();
            return new FuncArgument(arg.Location, new Str(arg.Value, true));
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

        async Task ParseNode(List<Node> nodes)
        {
            switch (Peek().Type)
            {
                case TokenType.BeginCode:
                    var start = Peek().Location;
                    Advance();

                    if (Match(TokenType.KeywordRange))
                    {
                        var attribute = ExtractAttributeName();
                        Consume(TokenType.EndCode, ExpectedMessage("}}"));

                        var group = await ParseGroup();
                        Consume(TokenType.BeginCode, ExpectedMessage("{{"));
                        Consume(TokenType.KeywordEnd, ExpectedMessage("keyword end"));
                        Consume(TokenType.EndCode, ExpectedMessage("}}"));

                        nodes.Add(new Node.Iterate(attribute, group, start));
                    }
                    else if (Match(TokenType.KeywordIf))
                    {
                        var attribute = ExtractAttributeName();
                        Consume(TokenType.EndCode, ExpectedMessage("}}"));

                        var group = await ParseGroup();
                        Consume(TokenType.BeginCode, ExpectedMessage("{{"));
                        Consume(TokenType.KeywordEnd, ExpectedMessage("keyword end"));
                        Consume(TokenType.EndCode, ExpectedMessage("}}"));

                        nodes.Add(new Node.If(attribute, group, start));
                    }
                    else if (Match(TokenType.KeywordInclude))
                    {
                        var name = Consume(TokenType.Ident, ExpectedMessage("IDENT"));
                        var includeLocation = Peek().Location;
                        Consume(TokenType.EndCode, ExpectedMessage("}}"));

                        var firstFile = includeDir.GetFile(name.Value);
                        var file = firstFile;
                        var secondFile = firstFile;
                        if (vfs.Exists(file) == false)
                        {
                            secondFile = includeDir.GetFile(name.Value + defaultExtension);
                            file = secondFile;
                        }

                        if (vfs.Exists(file) == false)
                        {
                            ReportError(includeLocation, $"Unable to open file: tried {firstFile} and {secondFile}");
                        }
                        else
                        {
                            var source = await vfs.ReadAllTextAsync(file);
                            var (scannerTokens, lexerErrors) = Scanner(file, source);
                            if (lexerErrors.Length > 0)
                            {
                                ReportError(includeLocation, "included from here...");
                                foreach (var e in lexerErrors)
                                {
                                    ReportError(e.Location, e.Message);
                                }
                                return;
                            }

                            var (node, parseErrors) = await Parse(scannerTokens, functions, includeDir, defaultExtension, vfs);
                            if (parseErrors.Length > 0)
                            {
                                ReportError(includeLocation, "included from here...");
                                foreach (var e in parseErrors)
                                {
                                    ReportError(e.Location, e.Message);
                                }

                                return;
                            }

                            nodes.Add(node);
                        }
                    }
                    else
                    {
                        ParseAttributeToEnd(nodes);
                    }
                    break;
                case TokenType.Text:
                    var text = Advance();
                    nodes.Add(new Node.Text(text.Value, text.Location));
                    break;
                default:
                    throw ReportError(Peek().Location, $"Unexpected token {TokenToMessage(Peek())}");
            }
        }

        void ParseAttributeToEnd(List<Node> nodes)
        {
            var start = Peek().Location;
            Node node = new Node.Attribute(ExtractAttributeName(), start);

            while (Peek().Type == TokenType.Pipe)
            {
                Advance();
                var name = Consume(TokenType.Ident, ExpectedMessage("function name"));
                var arguments = new List<FuncArgument>();

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

                if (functions.TryGetValue(name.Value, out var funcGenerator))
                {
                    var (func, funcParseErrors) = funcGenerator(name.Location, arguments.ToImmutableArray());
                    if (funcParseErrors.IsEmpty == false)
                    {
                        foreach (var err in funcParseErrors)
                        {
                            ReportError(err.Location, err.Message);
                        }
                    }
                    node = new Node.FunctionCall(name.Value, func, node, name.Location);
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
