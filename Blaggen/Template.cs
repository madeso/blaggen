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


public class Template
{
    public delegate string Func(List<string> args);

    // ---------------------------------------------------------------------------

    public class MissingFunction : Exception
    {
        public MissingFunction(string name)
            : base($"Missing function {name}")
        {
        }
    }

    public class SyntaxError : Exception
    {
        public SyntaxError(string name)
            : base($"Syntax error: {name}")
        {
        }
    }

    public class InvalidState : Exception
    {
        public InvalidState()
            : base("invalid state")
        {
        }
    }

    // --------------------------------------------------------------------------------------------

    private abstract class Node
    {
        public abstract string Evaluate(Dictionary<string, Func> functions, Dictionary<string, string> data);
    }

    private class Text : Node
    {
        private readonly string value;
        public Text(string value)
        {
            this.value = value;
        }

        public override string Evaluate(Dictionary<string, Func> functions, Dictionary<string, string> data)
        {
            return value;
        }

        public override string ToString()
        {
            return value;
        }
    }

    private class Attribute : Node
    {
        private readonly string name;
        public Attribute(string name)
        {
            this.name = name;
        }

        public override string Evaluate(Dictionary<string, Func> functions, Dictionary<string, string> data)
        {
            return data.TryGetValue(name, out var value) ? value : string.Empty;
        }

        public override string ToString()
        {
            return "%" + name + "%";
        }
    }

    private class FunctionCall : Node
    {
        private readonly string name;
        readonly List<Node> args;

        public FunctionCall(string name, IEnumerable<Node> arguments)
        {
            this.name = name;
            args = arguments.ToList();
        }

        public override string Evaluate(Dictionary<string, Func> functions, Dictionary<string, string> data)
        {
            if (functions.ContainsKey(name))
            {
                var values = args.Select(a => a.Evaluate(functions, data)).ToList();
                return functions[name](values);
            }

            throw new MissingFunction(name);
        }

        public override string ToString()
        {
            return $"${name}({string.Join(",", args.Select(x => x.ToString()))})";
        }
    }

    private class Group : Node
    {
        readonly List<Node> nodes;
        public Group(List<Node> nodes)
        {
            this.nodes = nodes;

        }

        public override string Evaluate(Dictionary<string, Func> functions, Dictionary<string, string> data)
        {
            return string.Join("", nodes.Select(n => n.Evaluate(functions, data)));
        }
    }

    private static class Syntax
    {
        public const char VAR_SIGN = '%';
        public const char FUNC_SIGN = '$';
        public const char BEGIN_SIGN = '(';
        public const char END_SIGN = ')';
        public const char SEP_SIGN = ',';
    }

    private static class Parser
    {
        private enum State
        {
            Text, Var, Func
        }

        public static List<Node> Parse(string pattern)
        {
            var mem = "";
            var nodes = new List<Node>();
            var state = State.Text;

            var i = 0;
            while (i < pattern.Length)
            {
                var c = pattern[i];
                i += 1;
                switch (state)
                {
                    case State.Text:
                        switch (c)
                        {
                            case Syntax.VAR_SIGN:
                                AddText();
                                state = State.Var;
                                break;
                            case Syntax.FUNC_SIGN:
                                AddText();
                                state = State.Func;
                                break;
                            default:
                                mem += c;
                                break;
                        }
                        break;
                    case State.Var:
                        if (c == Syntax.VAR_SIGN)
                        {
                            AddVar();
                            state = State.Text;
                        }
                        else
                        {
                            mem += c;
                        }
                        break;
                    case State.Func:
                        if (mem == "")
                        {
                            if (char.IsLetter(c))
                                mem += c;
                            else
                                throw new SyntaxError("function name is empty");
                        }
                        else
                        {
                            if (char.IsLetterOrDigit(c))
                            {
                                mem += c;
                            }
                            else if (c == Syntax.BEGIN_SIGN)
                            {
                                var args = ParseArguments(ref i, pattern);
                                i += 1;
                                AddFunc(args);
                                state = State.Text;
                            }
                            else
                                throw new SyntaxError("function calls must end with () and, mus begin with a letter and can only continue with alphanumerics");
                        }
                        break;
                    default:
                        throw new InvalidState();
                        break;
                }
            }

            if (mem == "") return nodes;
            
            switch (state)
            {
                case State.Text:
                    AddText();
                    break;
                case State.Var:
                    AddVar();
                    break;
                case State.Func:
                    throw new SyntaxError("Can't end a function without ending it");
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return nodes;

            void AddText()
            {
                if (mem != "")
                    nodes.Add(new Text(mem));
                mem = "";
            }

            void AddVar()
            {
                if (mem != "")
                    nodes.Add(new Attribute(mem));
                else
                    nodes.Add(new Text(Syntax.VAR_SIGN.ToString()));
                mem = "";
            }

            void AddFunc(List<string> arguments)
            {
                if (arguments == null)
                    throw new SyntaxError("weird func call");
                nodes.Add(new FunctionCall(mem, arguments.Select(CompilePatternToNode)));
                mem = "";
            }

            static List<string> ParseArguments(ref int start, string pattern)
            {
                // return new index, and a list of string arguments that need to be parsed
                List<string> args = new List<string>();
                var state = 0;
                var mem = "";
                for (int i = start; i < pattern.Length; ++i)
                {
                    char c = pattern[i];
                    switch (c)
                    {
                        case Syntax.BEGIN_SIGN:
                            mem += c;
                            state += 1;
                            break;
                        case Syntax.END_SIGN:
                            if (state == 0)
                            {
                                if (mem != "")
                                {
                                    args.Add(mem);
                                }
                                start = i;
                                return args;
                            }

                            mem += c;
                            state -= 1;
                            break;
                        case Syntax.SEP_SIGN:
                            if (state == 0)
                            {
                                args.Add(mem);
                                mem = "";
                            }
                            else
                            {
                                mem += c;
                            }
                            break;
                        default:
                            mem += c;
                            break;
                    }
                }
                throw new SyntaxError("should have detected an end before eos");
            }
        }
    }

    private static Node CompilePatternToNode(string pattern)
    {
        return new Group(Parser.Parse(pattern));
    }

    readonly Node list;
    private Template(string pattern)
    {
        list = CompilePatternToNode(pattern);
    }
    public string Evaluate(Dictionary<string, Func> funcs, Dictionary<string, string> data)
    {
        return list.Evaluate(funcs, data);
    }

    public static Template Compile(string pattern)
    {
        return new Template(pattern);
    }

    public static Dictionary<string, Func> DefaultFunctions()
    {
        var culture = new CultureInfo("en-US", false);

        var t = new Dictionary<string, Func>();
        //t.Add("title", args => args[0].title());
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
}