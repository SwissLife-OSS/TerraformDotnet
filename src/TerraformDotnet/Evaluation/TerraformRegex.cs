using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Evaluation;

/// <summary>
/// Evaluates Terraform's RE2-based <c>regex</c>, <c>regexall</c> and regular expression
/// <c>replace</c> with the .NET regex engine. The pattern is translated to preserve RE2 semantics
/// (ASCII-only shorthand classes, <c>$</c> only at the end of the text, <c>(?P&lt;name&gt;</c>, POSIX classes);
/// anything that cannot be translated faithfully makes the call "unsupported" (<c>null</c>).
/// </summary>
internal static class TerraformRegex
{
    private const int CacheLimit = 512;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly ConcurrentDictionary<string, Regex?> Cache = new(StringComparer.Ordinal);

    public static HclValue? Regex(FunctionCall call)
    {
        var (text, expression) = Prepare(call);
        if (expression is null)
        {
            return null;
        }

        try
        {
            var match = expression.Match(text);
            if (!match.Success)
            {
                throw call.Error("pattern did not match any part of the given string.");
            }

            return Describe(call, expression, match);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    public static HclValue? RegexAll(FunctionCall call)
    {
        var (text, expression) = Prepare(call);
        if (expression is null)
        {
            return null;
        }

        try
        {
            var results = new List<HclValue>();
            foreach (var match in FindAll(expression, text))
            {
                results.Add(Describe(call, expression, match));
            }

            return HclValue.FromTuple(results);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    public static HclValue? Replace(FunctionCall call, string text, string pattern, string replacement)
    {
        if (replacement.Contains('$', StringComparison.Ordinal) || HasSurrogates(text) || HasSurrogates(replacement))
        {
            return null;
        }

        var expression = Compile(pattern);
        if (expression is null)
        {
            return null;
        }

        try
        {
            var builder = new StringBuilder();
            var position = 0;
            foreach (var match in FindAll(expression, text))
            {
                builder.Append(text, position, match.Index - position).Append(replacement);
                position = match.Index + match.Length;
            }

            builder.Append(text, position, text.Length - position);

            return HclValue.FromString(builder.ToString());
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static (string Text, Regex? Expression) Prepare(FunctionCall call)
    {
        var pattern = call.String(0);
        var text = call.String(1);

        return (text, HasSurrogates(text) ? null : Compile(pattern));
    }

    private static bool HasSurrogates(string text) => text.Any(char.IsSurrogate);

    private static IEnumerable<Match> FindAll(Regex expression, string text)
    {
        var previousEnd = -1;
        foreach (Match match in expression.Matches(text))
        {
            var abutting = match.Length == 0 && match.Index == previousEnd;
            previousEnd = match.Index + match.Length;
            if (!abutting)
            {
                yield return match;
            }
        }
    }

    private static HclValue Describe(FunctionCall call, Regex expression, Match match)
    {
        var numbers = expression.GetGroupNumbers().Where(n => n != 0).ToList();
        if (numbers.Count == 0)
        {
            return HclValue.FromString(match.Value);
        }

        var named = numbers.Select(n => expression.GroupNameFromNumber(n)).Where(name => !int.TryParse(name, out _)).ToList();
        if (named.Count == 0)
        {
            return HclValue.FromTuple(numbers.Select(n => Capture(match.Groups[n])).ToList());
        }

        if (named.Count != numbers.Count)
        {
            throw call.Error("the pattern mixes named and unnamed capture groups.");
        }

        return HclValue.FromObject(named.ToDictionary(name => name, name => Capture(match.Groups[name]), StringComparer.Ordinal));
    }

    private static HclValue Capture(Group group) => group.Success ? HclValue.FromString(group.Value) : HclValue.Null;

    private static Regex? Compile(string pattern)
    {
        if (Cache.TryGetValue(pattern, out var cached))
        {
            return cached;
        }

        var compiled = Build(pattern);
        if (Cache.Count >= CacheLimit)
        {
            Cache.Clear();
        }

        Cache[pattern] = compiled;

        return compiled;
    }

    private static Regex? Build(string pattern)
    {
        if (HasSurrogates(pattern))
        {
            return null;
        }

        var translated = new Translator(pattern).Run();
        if (translated is null)
        {
            return null;
        }

        try
        {
            return new Regex(translated, RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private const string WordClass = "0-9A-Za-z_";

    private const string SpaceClass = "\\t\\n\\f\\r ";

    /// <summary>Rewrites an RE2 pattern into an equivalent .NET pattern, or reports it as unsupported.</summary>
    private sealed class Translator
    {
        private readonly string _pattern;
        private readonly StringBuilder _output = new();
        private int _index;
        private bool _unsupported;

        public Translator(string pattern) => _pattern = pattern;

        public string? Run()
        {
            while (_index < _pattern.Length && !_unsupported)
            {
                switch (_pattern[_index])
                {
                    case '\\':
                        Escape(inClass: false);
                        break;
                    case '[':
                        CharacterClass();
                        break;
                    case '(':
                        Group();
                        break;
                    case '$':
                        _output.Append("\\z");
                        _index++;
                        break;
                    default:
                        _output.Append(_pattern[_index]);
                        _index++;
                        break;
                }
            }

            return _unsupported ? null : _output.ToString();
        }

        private void Group()
        {
            if (!Peek("(?"))
            {
                _output.Append('(');
                _index++;
                return;
            }

            _index += 2;
            if (Peek("P<") || (Peek("<") && !Peek("<=") && !Peek("<!")))
            {
                _index += _pattern[_index] == 'P' ? 2 : 1;
                var close = _pattern.IndexOf('>', _index);
                if (close < 0 || !IsGroupName(_pattern.AsSpan(_index, close - _index)))
                {
                    _unsupported = true;
                    return;
                }

                _output.Append("(?<").Append(_pattern, _index, close - _index + 1);
                _index = close + 1;
                return;
            }

            if (Peek(":"))
            {
                _output.Append("(?:");
                _index++;
                return;
            }

            var start = _index;
            while (_index < _pattern.Length && _pattern[_index] is 'i' or 's' or '-')
            {
                _index++;
            }

            if (_index == start || _index >= _pattern.Length || _pattern[_index] is not (':' or ')'))
            {
                _unsupported = true;
                return;
            }

            _output.Append("(?").Append(_pattern, start, _index - start + 1);
            _index++;
        }

        private static bool IsGroupName(ReadOnlySpan<char> name)
        {
            if (name.IsEmpty)
            {
                return false;
            }

            foreach (var ch in name)
            {
                if (!(char.IsAsciiLetterOrDigit(ch) || ch == '_'))
                {
                    return false;
                }
            }

            return !char.IsAsciiDigit(name[0]);
        }

        private bool Peek(string text) => string.CompareOrdinal(_pattern, _index, text, 0, text.Length) == 0;

        private void CharacterClass()
        {
            _output.Append('[');
            _index++;
            if (_index < _pattern.Length && _pattern[_index] == '^')
            {
                _output.Append('^');
                _index++;
            }

            var first = true;
            while (_index < _pattern.Length && !_unsupported)
            {
                var ch = _pattern[_index];
                if (ch == ']' && !first)
                {
                    _output.Append(']');
                    _index++;
                    return;
                }

                first = false;
                if (ch == '[' && Peek("[:"))
                {
                    PosixClass();
                }
                else if (ch == '[' || ch == ']')
                {
                    _output.Append('\\').Append(ch);
                    _index++;
                }
                else if (ch == '\\')
                {
                    Escape(inClass: true);
                }
                else
                {
                    _output.Append(ch);
                    _index++;
                }
            }

            _unsupported = true;
        }

        private void PosixClass()
        {
            var close = _pattern.IndexOf(":]", _index, StringComparison.Ordinal);
            var name = close < 0 ? string.Empty : _pattern[(_index + 2)..close];
            var members = name switch
            {
                "alpha" => "a-zA-Z",
                "digit" => "0-9",
                "alnum" => "0-9A-Za-z",
                "upper" => "A-Z",
                "lower" => "a-z",
                "space" => "\\t\\n\\v\\f\\r ",
                "punct" => "!-/:-@\\[-`{-~",
                "word" => WordClass,
                "xdigit" => "0-9A-Fa-f",
                "blank" => "\\t ",
                "cntrl" => "\\x00-\\x1f\\x7f",
                "print" => " -~",
                "graph" => "!-~",
                "ascii" => "\\x00-\\x7f",
                _ => null,
            };

            if (members is null)
            {
                _unsupported = true;
                return;
            }

            _output.Append(members);
            _index = close + 2;
        }

        private void Escape(bool inClass)
        {
            if (_index + 1 >= _pattern.Length)
            {
                _unsupported = true;
                return;
            }

            var ch = _pattern[_index + 1];
            _index += 2;
            switch (ch)
            {
                case 'd':
                    _output.Append(inClass ? "0-9" : "[0-9]");
                    break;
                case 'w':
                    _output.Append(inClass ? WordClass : $"[{WordClass}]");
                    break;
                case 's':
                    _output.Append(inClass ? SpaceClass : $"[{SpaceClass}]");
                    break;
                case 'D' when !inClass:
                    _output.Append("[^0-9]");
                    break;
                case 'W' when !inClass:
                    _output.Append($"[^{WordClass}]");
                    break;
                case 'S' when !inClass:
                    _output.Append($"[^{SpaceClass}]");
                    break;
                case 'b' when !inClass:
                    _output.Append($"(?:(?<=[{WordClass}])(?![{WordClass}])|(?<![{WordClass}])(?=[{WordClass}]))");
                    break;
                case 'B' when !inClass:
                    _output.Append($"(?:(?<=[{WordClass}])(?=[{WordClass}])|(?<![{WordClass}])(?![{WordClass}]))");
                    break;
                case 'A' or 'z' when !inClass:
                    _output.Append('\\').Append(ch);
                    break;
                case 'n' or 'r' or 't' or 'f' or 'v' or 'a':
                    _output.Append('\\').Append(ch);
                    break;
                case 'p' or 'P':
                    UnicodeCategory(ch);
                    break;
                case 'Q' when !inClass:
                    Quoted();
                    break;
                case 'x':
                    HexEscape();
                    break;
                case '_':
                    _output.Append('_');
                    break;
                default:
                    if (ch < 0x80 && !char.IsAsciiLetterOrDigit(ch) && ch != ' ' && !char.IsControl(ch))
                    {
                        _output.Append('\\').Append(ch);
                    }
                    else
                    {
                        _unsupported = true;
                    }

                    break;
            }
        }

        private void UnicodeCategory(char kind)
        {
            string name;
            if (_index < _pattern.Length && _pattern[_index] == '{')
            {
                var close = _pattern.IndexOf('}', _index);
                if (close < 0)
                {
                    _unsupported = true;
                    return;
                }

                name = _pattern[(_index + 1)..close];
                _index = close + 1;
            }
            else if (_index < _pattern.Length)
            {
                name = _pattern[_index].ToString();
                _index++;
            }
            else
            {
                _unsupported = true;
                return;
            }

            var valid = name.Length is 1 or 2 && char.IsAsciiLetterUpper(name[0]) && (name.Length == 1 || char.IsAsciiLetterLower(name[1]));
            if (!valid)
            {
                _unsupported = true;
                return;
            }

            _output.Append('\\').Append(kind).Append('{').Append(name).Append('}');
        }

        private void Quoted()
        {
            var end = _pattern.IndexOf("\\E", _index, StringComparison.Ordinal);
            var literal = end < 0 ? _pattern[_index..] : _pattern[_index..end];
            _index = end < 0 ? _pattern.Length : end + 2;
            _output.Append(System.Text.RegularExpressions.Regex.Escape(literal));
        }

        private void HexEscape()
        {
            if (_index + 2 > _pattern.Length || !char.IsAsciiHexDigit(_pattern[_index]) || !char.IsAsciiHexDigit(_pattern[_index + 1]))
            {
                _unsupported = true;
                return;
            }

            _output.Append("\\x").Append(_pattern, _index, 2);
            _index += 2;
        }
    }
}
