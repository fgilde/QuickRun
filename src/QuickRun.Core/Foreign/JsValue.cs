using System.Globalization;
using System.Text;

namespace QuickRun.Core.Foreign;

/// <summary>
/// A value read out of a JSON document or a JavaScript object literal.
/// <para>
/// Foreign formats are not always JSON: a Pinokio script is a JavaScript module whose exported
/// literal has unquoted keys, comments, single quotes and - in the middle of it - functions this
/// reader cannot make sense of. Those become <see cref="Unknown"/> instead of failing the whole
/// file, because the parts around them are still worth reading.
/// </para>
/// </summary>
public abstract record JsValue
{
    public sealed record Str(string Value) : JsValue;

    public sealed record Num(double Value) : JsValue;

    public sealed record Bool(bool Value) : JsValue;

    /// <summary><c>null</c>, <c>undefined</c>, or nothing at all.</summary>
    public sealed record Nothing : JsValue;

    /// <summary>Something this reader stepped over - a function, a call, an expression.</summary>
    public sealed record Unknown : JsValue;

    public sealed record Arr(IReadOnlyList<JsValue> Values) : JsValue;

    public sealed record Obj(IReadOnlyDictionary<string, JsValue> Fields) : JsValue;

    public static readonly JsValue None = new Nothing();

    public JsValue? Field(string name) =>
        this is Obj o && o.Fields.TryGetValue(name, out var value) ? value : null;

    public string? Text => this is Str s ? s.Value : null;

    /// <summary>An array's items, or this value on its own - a scalar is a list of one.</summary>
    public IReadOnlyList<JsValue> Items => this switch
    {
        Arr a => a.Values,
        Nothing or Unknown => Array.Empty<JsValue>(),
        _ => new[] { this },
    };

    /// <summary>The strings in this value. Pinokio's <c>message</c> is either one or a list.</summary>
    public IReadOnlyList<string> Strings =>
        Items.Select(i => i.Text).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).ToList();

    /// <summary>The value as text, the way JavaScript would print it in a template.</summary>
    public string AsText => this switch
    {
        Str s => s.Value,
        Bool b => b.Value ? "true" : "false",
        Num n => n.Value == Math.Floor(n.Value) && Math.Abs(n.Value) < 1e15
            ? ((long)n.Value).ToString(CultureInfo.InvariantCulture)
            : n.Value.ToString(CultureInfo.InvariantCulture),
        _ => "",
    };

    public bool Truthy => this switch
    {
        Str s => s.Value.Length > 0,
        Num n => n.Value != 0,
        Bool b => b.Value,
        Nothing or Unknown => false,
        _ => true,
    };
}

public sealed class JsParseException(string message) : Exception(message);

/// <summary>
/// Reads one JSON or JavaScript literal. Deliberately tolerant: comments, trailing commas,
/// unquoted keys and single or backtick quotes are all accepted, because that is what the files
/// in the wild look like.
/// </summary>
public static class JsLiteral
{
    public static JsValue Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new JsParseException("empty document");
        return new Reader(Body(text)).Value();
    }

    public static JsValue? TryParse(string text)
    {
        try { return Parse(text); }
        catch (JsParseException) { return null; }
    }

    /// <summary>Finds the exported literal in a module, or takes the document as it is.</summary>
    private static string Body(string text)
    {
        var exports = text.IndexOf("module.exports", StringComparison.Ordinal);
        if (exports >= 0)
        {
            var equals = text.IndexOf('=', exports + "module.exports".Length);
            if (equals >= 0) return text[(equals + 1)..];
        }

        var def = text.IndexOf("export default", StringComparison.Ordinal);
        return def >= 0 ? text[(def + "export default".Length)..] : text;
    }

    private sealed class Reader(string text)
    {
        private int _at;

        private bool Eof => _at >= text.Length;

        public JsValue Value()
        {
            Skippable();
            if (Eof) return JsValue.None;

            return text[_at] switch
            {
                '{' => Object(),
                '[' => Array(),
                '"' or '\'' or '`' => new JsValue.Str(String()),
                '-' or '+' or '.' or (>= '0' and <= '9') => Number(),
                _ => Word(),
            };
        }

        private JsValue Object()
        {
            _at++;
            var fields = new Dictionary<string, JsValue>(StringComparer.Ordinal);

            while (true)
            {
                Skippable();
                if (Eof) throw new JsParseException("unterminated object");
                if (text[_at] == '}') { _at++; return new JsValue.Obj(fields); }
                if (text[_at] == ',') { _at++; continue; }

                var key = Key();
                Skippable();
                if (Eof || text[_at] != ':') throw new JsParseException($"expected ':' after '{key}'");
                _at++;

                fields[key] = Value();
            }
        }

        private JsValue Array()
        {
            _at++;
            var items = new List<JsValue>();

            while (true)
            {
                Skippable();
                if (Eof) throw new JsParseException("unterminated array");
                if (text[_at] == ']') { _at++; return new JsValue.Arr(items); }
                if (text[_at] == ',') { _at++; continue; }

                items.Add(Value());
            }
        }

        private string Key()
        {
            if (text[_at] is '"' or '\'' or '`') return String();

            var start = _at;
            while (!Eof && (char.IsLetterOrDigit(text[_at]) || text[_at] is '_' or '$')) _at++;
            if (_at == start) throw new JsParseException($"unreadable key at {_at}");
            return text[start.._at];
        }

        private string String()
        {
            var quote = text[_at++];
            var builder = new StringBuilder();

            while (!Eof)
            {
                var c = text[_at++];
                if (c == quote) return builder.ToString();

                if (c != '\\') { builder.Append(c); continue; }
                if (Eof) break;

                var escape = text[_at++];
                builder.Append(escape switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'b' => '\b',
                    'f' => '\f',
                    '0' => '\0',
                    'u' => Unicode(),
                    _ => escape,
                });
            }

            throw new JsParseException("unterminated string");
        }

        private char Unicode()
        {
            if (_at + 4 > text.Length) throw new JsParseException("truncated unicode escape");
            var hex = text.Substring(_at, 4);
            _at += 4;
            return (char)Convert.ToInt32(hex, 16);
        }

        private JsValue Number()
        {
            var start = _at;
            while (!Eof && (char.IsAsciiDigit(text[_at]) || text[_at] is '-' or '+' or '.' or 'e' or 'E')) _at++;

            return double.TryParse(text[start.._at], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? new JsValue.Num(value)
                : Skip();
        }

        /// <summary>A bare word: a literal if we know it, otherwise something to step over.</summary>
        private JsValue Word()
        {
            var start = _at;
            while (!Eof && (char.IsLetterOrDigit(text[_at]) || text[_at] is '_' or '$')) _at++;

            return text[start.._at] switch
            {
                "true" => new JsValue.Bool(true),
                "false" => new JsValue.Bool(false),
                "null" or "undefined" => JsValue.None,
                _ => Skip(),
            };
        }

        /// <summary>
        /// Steps over a value this reader cannot represent, stopping where the enclosing object or
        /// array continues. Strings and nesting are respected, so a function body cannot swallow
        /// the rest of the file.
        /// </summary>
        private JsValue Skip()
        {
            var depth = 0;

            while (!Eof)
            {
                var c = text[_at];

                if (c is '"' or '\'' or '`') { String(); continue; }
                if (c == '/' && Comment()) continue;

                if (c is '{' or '[' or '(') { depth++; _at++; continue; }
                if (c is '}' or ']' or ')')
                {
                    if (depth == 0) break;
                    depth--;
                    _at++;
                    continue;
                }

                if (c == ',' && depth == 0) break;
                _at++;
            }

            return new JsValue.Unknown();
        }

        private void Skippable()
        {
            while (!Eof)
            {
                if (char.IsWhiteSpace(text[_at])) { _at++; continue; }
                if (text[_at] == '/' && Comment()) continue;
                return;
            }
        }

        private bool Comment()
        {
            if (_at + 1 >= text.Length) return false;

            if (text[_at + 1] == '/')
            {
                while (!Eof && text[_at] != '\n') _at++;
                return true;
            }

            if (text[_at + 1] != '*') return false;

            var end = text.IndexOf("*/", _at + 2, StringComparison.Ordinal);
            _at = end < 0 ? text.Length : end + 2;
            return true;
        }
    }
}
