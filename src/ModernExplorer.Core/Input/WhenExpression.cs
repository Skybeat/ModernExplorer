namespace ModernExplorer.Core.Input;

/// <summary>
/// Minimal expression parser for <c>when</c> conditions:
/// identifiers, <c>!</c>, <c>&amp;&amp;</c>, <c>||</c>, and parentheses.
/// Example: <c>fileList &amp;&amp; (hasSelection || hasFocusedItem)</c>.
/// </summary>
public sealed class WhenExpression
{
    private readonly Node _root;

    /// <summary>
    /// Specificity = number of positive identifiers (excluding "global").
    /// More specific bindings win when multiple bindings match.
    /// </summary>
    public int Specificity { get; }

    public string Source { get; }

    public static readonly WhenExpression Always = Parse("global");

    private WhenExpression(Node root, string source)
    {
        _root = root;
        Source = source;
        Specificity = root.CountIdentifiers();
    }

    public bool Evaluate(FocusContext ctx) => _root.Eval(ctx);

    public static WhenExpression Parse(string? text)
    {
        var src = string.IsNullOrWhiteSpace(text) ? "global" : text.Trim();
        var parser = new Parser(src);
        var node = parser.ParseOr();
        parser.ExpectEnd();
        return new WhenExpression(node, src);
    }

    public override string ToString() => Source;

    private abstract class Node
    {
        public abstract bool Eval(FocusContext ctx);
        public abstract int CountIdentifiers();
    }

    private sealed class Ident(string name) : Node
    {
        public override bool Eval(FocusContext ctx) => ctx.Has(name);
        public override int CountIdentifiers() => name.Equals("global", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    private sealed class Not(Node inner) : Node
    {
        public override bool Eval(FocusContext ctx) => !inner.Eval(ctx);
        public override int CountIdentifiers() => 0;
    }

    private sealed class And(Node l, Node r) : Node
    {
        public override bool Eval(FocusContext ctx) => l.Eval(ctx) && r.Eval(ctx);
        public override int CountIdentifiers() => l.CountIdentifiers() + r.CountIdentifiers();
    }

    private sealed class Or(Node l, Node r) : Node
    {
        public override bool Eval(FocusContext ctx) => l.Eval(ctx) || r.Eval(ctx);
        public override int CountIdentifiers() => Math.Min(l.CountIdentifiers(), r.CountIdentifiers());
    }

    private sealed class Parser(string s)
    {
        private int _pos;

        public Node ParseOr()
        {
            var left = ParseAnd();
            while (TryConsume("||")) left = new Or(left, ParseAnd());
            return left;
        }

        private Node ParseAnd()
        {
            var left = ParseUnary();
            while (TryConsume("&&")) left = new And(left, ParseUnary());
            return left;
        }

        private Node ParseUnary()
        {
            if (TryConsume("!")) return new Not(ParseUnary());
            if (TryConsume("("))
            {
                var n = ParseOr();
                if (!TryConsume(")")) throw Error("')' erwartet");
                return n;
            }
            return ParseIdent();
        }

        private Node ParseIdent()
        {
            SkipWs();
            int start = _pos;
            while (_pos < s.Length && (char.IsLetterOrDigit(s[_pos]) || s[_pos] is '.' or '_' or '-')) _pos++;
            if (start == _pos) throw Error("Bezeichner erwartet");
            return new Ident(s[start.._pos]);
        }

        private bool TryConsume(string token)
        {
            SkipWs();
            if (string.CompareOrdinal(s, _pos, token, 0, token.Length) != 0) return false;
            _pos += token.Length;
            return true;
        }

        public void ExpectEnd()
        {
            SkipWs();
            if (_pos != s.Length) throw Error("Unerwartetes Zeichen");
        }

        private void SkipWs() { while (_pos < s.Length && char.IsWhiteSpace(s[_pos])) _pos++; }

        private FormatException Error(string msg) => new($"when-Ausdruck '{s}', Position {_pos}: {msg}");
    }
}

