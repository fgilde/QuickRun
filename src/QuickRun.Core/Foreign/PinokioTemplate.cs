using System.Globalization;
using System.Text;

namespace QuickRun.Core.Foreign;

/// <summary>
/// Resolves the <c>{{ ... }}</c> holes in a Pinokio script.
/// <para>
/// Pinokio puts real JavaScript in there, and a lot of it decides the command itself:
/// <c>{{platform === 'win32' &amp;&amp; gpu === 'amd' ? 'python main.py --directml' : 'python main.py'}}</c>.
/// Ignoring the expression would leave a command that cannot run, so this evaluates the small
/// subset those scripts actually use - variables, member access, comparison, and/or/not, ternary
/// and string concatenation - and refuses anything else rather than guessing.
/// </para>
/// </summary>
public static class PinokioTemplate
{
    /// <summary>Expands every hole. Throws <see cref="JsParseException"/> if one cannot be read.</summary>
    public static string Expand(string template, IReadOnlyDictionary<string, JsValue> vars)
    {
        if (!template.Contains("{{", StringComparison.Ordinal)) return template;

        var builder = new StringBuilder();
        var at = 0;

        while (at < template.Length)
        {
            var open = template.IndexOf("{{", at, StringComparison.Ordinal);
            if (open < 0) break;

            var close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0) break;

            builder.Append(template, at, open - at);
            builder.Append(Evaluate(template[(open + 2)..close], vars).AsText);
            at = close + 2;
        }

        builder.Append(template, at, template.Length - at);
        return builder.ToString();
    }

    public static JsValue Evaluate(string expression, IReadOnlyDictionary<string, JsValue> vars)
    {
        var parser = new Parser(expression, vars);
        var value = parser.Ternary();
        parser.ExpectEnd();
        return value;
    }

    private sealed class Parser(string text, IReadOnlyDictionary<string, JsValue> vars)
    {
        private int _at;

        public void ExpectEnd()
        {
            Space();
            if (_at < text.Length) throw new JsParseException($"cannot read expression '{text.Trim()}'");
        }

        public JsValue Ternary()
        {
            var condition = Or();
            Space();
            if (!Take("?")) return condition;

            var yes = Ternary();
            Space();
            if (!Take(":")) throw new JsParseException($"missing ':' in '{text.Trim()}'");
            var no = Ternary();

            return condition.Truthy ? yes : no;
        }

        private JsValue Or()
        {
            var left = And();
            while (true)
            {
                Space();
                if (!Take("||")) return left;
                var right = And();
                left = left.Truthy ? left : right;
            }
        }

        private JsValue And()
        {
            var left = Equality();
            while (true)
            {
                Space();
                if (!Take("&&")) return left;
                var right = Equality();
                left = left.Truthy ? right : left;
            }
        }

        private JsValue Equality()
        {
            var left = Additive();

            while (true)
            {
                Space();
                var negate = false;

                if (Take("===") || Take("==")) { }
                else if (Take("!==") || Take("!=")) negate = true;
                else return left;

                var right = Additive();
                var same = Same(left, right);
                left = new JsValue.Bool(negate ? !same : same);
            }
        }

        private JsValue Additive()
        {
            var left = Unary();

            while (true)
            {
                Space();
                if (!Take("+")) return left;

                var right = Unary();
                left = left is JsValue.Num a && right is JsValue.Num b
                    ? new JsValue.Num(a.Value + b.Value)
                    : new JsValue.Str(left.AsText + right.AsText);
            }
        }

        private JsValue Unary()
        {
            Space();
            return Take("!") ? new JsValue.Bool(!Unary().Truthy) : Primary();
        }

        private JsValue Primary()
        {
            Space();
            if (_at >= text.Length) throw new JsParseException($"expression ends early: '{text.Trim()}'");

            var c = text[_at];

            if (c == '(')
            {
                _at++;
                var inner = Ternary();
                Space();
                if (!Take(")")) throw new JsParseException($"missing ')' in '{text.Trim()}'");
                return inner;
            }

            if (c is '"' or '\'' or '`') return new JsValue.Str(Quoted());
            if (char.IsAsciiDigit(c)) return Number();

            return Path();
        }

        /// <summary>A variable and whatever is read off it: <c>input.event[1]</c>.</summary>
        private JsValue Path()
        {
            var name = Word();

            var value = name switch
            {
                "true" => new JsValue.Bool(true),
                "false" => new JsValue.Bool(false),
                "null" or "undefined" => JsValue.None,
                _ => vars.TryGetValue(name, out var known) ? known : JsValue.None,
            };

            while (true)
            {
                if (Take("."))
                {
                    value = value.Field(Word()) ?? JsValue.None;
                    continue;
                }

                if (!Take("[")) return value;

                var index = Ternary();
                Space();
                if (!Take("]")) throw new JsParseException($"missing ']' in '{text.Trim()}'");

                value = index is JsValue.Num n
                    ? At(value, (int)n.Value)
                    : value.Field(index.AsText) ?? JsValue.None;
            }
        }

        private static JsValue At(JsValue value, int index)
        {
            var items = value.Items;
            return index >= 0 && index < items.Count ? items[index] : JsValue.None;
        }

        private string Word()
        {
            Space();
            var start = _at;
            while (_at < text.Length && (char.IsLetterOrDigit(text[_at]) || text[_at] is '_' or '$')) _at++;
            if (_at == start) throw new JsParseException($"expected a name in '{text.Trim()}'");
            return text[start.._at];
        }

        private string Quoted()
        {
            var quote = text[_at++];
            var builder = new StringBuilder();

            while (_at < text.Length)
            {
                var c = text[_at++];
                if (c == quote) return builder.ToString();
                if (c == '\\' && _at < text.Length) { builder.Append(text[_at++]); continue; }
                builder.Append(c);
            }

            throw new JsParseException($"unterminated string in '{text.Trim()}'");
        }

        private JsValue Number()
        {
            var start = _at;
            while (_at < text.Length && (char.IsAsciiDigit(text[_at]) || text[_at] == '.')) _at++;
            return new JsValue.Num(double.Parse(text[start.._at], CultureInfo.InvariantCulture));
        }

        private static bool Same(JsValue left, JsValue right) =>
            (left, right) switch
            {
                (JsValue.Num a, JsValue.Num b) => a.Value == b.Value,
                (JsValue.Nothing, JsValue.Nothing) => true,
                (JsValue.Nothing, _) or (_, JsValue.Nothing) => false,
                _ => string.Equals(left.AsText, right.AsText, StringComparison.Ordinal),
            };

        private bool Take(string token)
        {
            Space();
            if (!text.AsSpan(_at).StartsWith(token, StringComparison.Ordinal)) return false;

            var next = _at + token.Length < text.Length ? text[_at + token.Length] : '\0';

            // '!=' is not a negation, and '?.' is optional chaining rather than a ternary.
            if (token == "!" && next == '=') return false;
            if (token == "?" && next == '.') return false;

            _at += token.Length;
            return true;
        }

        private void Space()
        {
            while (_at < text.Length && char.IsWhiteSpace(text[_at])) _at++;
        }
    }
}
