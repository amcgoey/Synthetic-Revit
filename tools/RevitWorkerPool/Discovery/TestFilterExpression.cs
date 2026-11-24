using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace RevitWorkerPool.Discovery;

/// <summary>
/// Parses and evaluates <c>--filter</c> fragments and VSTest-style filter expressions
/// (<c>FullyQualifiedName~</c>, <c>FullyQualifiedName=</c>, <c>TestCategory=</c>,
/// <c>&amp;</c>, <c>|</c>, <c>!</c>, parentheses, and wildcards) against discovered test methods.
/// </summary>
internal static class TestFilterExpression
{
    public static Func<DiscoveredTestMethod, bool> Compile(IReadOnlyList<string>? filters)
    {
        if (filters is null || filters.Count == 0)
        {
            return static _ => true;
        }

        var predicates = new List<Func<DiscoveredTestMethod, bool>>(filters.Count);
        foreach (string raw in filters)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            predicates.Add(CompileSingle(raw.Trim()));
        }

        if (predicates.Count == 0)
        {
            return static _ => true;
        }

        if (predicates.Count == 1)
        {
            return predicates[0];
        }

        return method =>
        {
            for (int i = 0; i < predicates.Count; i++)
            {
                if (predicates[i](method))
                {
                    return true;
                }
            }

            return false;
        };
    }

    private static Func<DiscoveredTestMethod, bool> CompileSingle(string expression)
    {
        if (!LooksLikeCompositeExpression(expression))
        {
            return CompileSimpleTerm(expression);
        }

        List<Token> tokens = Tokenize(expression);
        if (tokens.Count == 0)
        {
            return static _ => true;
        }

        var parser = new Parser(tokens);
        return parser.Parse();
    }

    private static bool LooksLikeCompositeExpression(string expression)
    {
        if (expression.StartsWith('(') || expression.StartsWith('!'))
        {
            return true;
        }

        for (int i = 0; i < expression.Length; i++)
        {
            char ch = expression[i];
            if (ch is '&' or '|' or '~' or '=')
            {
                return true;
            }
        }

        return false;
    }

    private static Func<DiscoveredTestMethod, bool> CompileSimpleTerm(string term)
    {
        if (term.Contains('*') || term.Contains('?'))
        {
            Regex regex = BuildWildcardRegex(term);
            return method => regex.IsMatch(method.FullName);
        }

        return method => method.FullName.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private static Regex BuildWildcardRegex(string wildcard)
    {
        string escaped = Regex.Escape(wildcard)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal);

        return new Regex(
            $"^{escaped}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    private enum TokenKind
    {
        And,
        Or,
        Not,
        LParen,
        RParen,
        Atom
    }

    private readonly record struct Token(TokenKind Kind, string Value);

    private static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        int i = 0;
        int n = input.Length;

        while (i < n)
        {
            char ch = input[i];
            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            if (ch == '&')
            {
                if (i + 1 < n && input[i + 1] == '&')
                {
                    i += 2;
                }
                else
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.And, "&"));
                continue;
            }

            if (ch == '|')
            {
                if (i + 1 < n && input[i + 1] == '|')
                {
                    i += 2;
                }
                else
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Or, "|"));
                continue;
            }

            if (ch == '(')
            {
                tokens.Add(new Token(TokenKind.LParen, "("));
                i++;
                continue;
            }

            if (ch == ')')
            {
                tokens.Add(new Token(TokenKind.RParen, ")"));
                i++;
                continue;
            }

            if (ch == '!')
            {
                tokens.Add(new Token(TokenKind.Not, "!"));
                i++;
                continue;
            }

            var sb = new StringBuilder();
            bool inQuotes = false;
            char quoteChar = '\0';
            int innerParenDepth = 0;

            while (i < n)
            {
                char c = input[i];
                if (inQuotes)
                {
                    if (c == '\\' && i + 1 < n)
                    {
                        sb.Append(input[i + 1]);
                        i += 2;
                        continue;
                    }

                    if (c == quoteChar)
                    {
                        inQuotes = false;
                        i++;
                        continue;
                    }

                    sb.Append(c);
                    i++;
                    continue;
                }

                if (c is '"' or '\'')
                {
                    inQuotes = true;
                    quoteChar = c;
                    i++;
                    continue;
                }

                if (c is '&' or '|')
                {
                    break;
                }

                if (c == '(')
                {
                    innerParenDepth++;
                    sb.Append(c);
                    i++;
                    continue;
                }

                if (c == ')')
                {
                    if (innerParenDepth > 0)
                    {
                        innerParenDepth--;
                        sb.Append(c);
                        i++;
                        continue;
                    }

                    break;
                }

                if (char.IsWhiteSpace(c))
                {
                    break;
                }

                sb.Append(c);
                i++;
            }

            string atom = sb.ToString();
            if (!string.IsNullOrEmpty(atom))
            {
                tokens.Add(new Token(TokenKind.Atom, atom));
            }
        }

        return tokens;
    }

    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private int _pos;

        public Parser(List<Token> tokens)
        {
            _tokens = tokens;
        }

        public Func<DiscoveredTestMethod, bool> Parse()
        {
            Func<DiscoveredTestMethod, bool> expr = ParseOr();
            if (_pos < _tokens.Count)
            {
                throw new FormatException($"Unexpected token '{_tokens[_pos].Value}' in test filter expression.");
            }

            return expr;
        }

        private Func<DiscoveredTestMethod, bool> ParseOr()
        {
            Func<DiscoveredTestMethod, bool> left = ParseAnd();
            while (Match(TokenKind.Or))
            {
                Func<DiscoveredTestMethod, bool> right = ParseAnd();
                Func<DiscoveredTestMethod, bool> capturedLeft = left;
                left = method => capturedLeft(method) || right(method);
            }

            return left;
        }

        private Func<DiscoveredTestMethod, bool> ParseAnd()
        {
            Func<DiscoveredTestMethod, bool> left = ParseUnary();
            while (Match(TokenKind.And))
            {
                Func<DiscoveredTestMethod, bool> right = ParseUnary();
                Func<DiscoveredTestMethod, bool> capturedLeft = left;
                left = method => capturedLeft(method) && right(method);
            }

            return left;
        }

        private Func<DiscoveredTestMethod, bool> ParseUnary()
        {
            if (Match(TokenKind.Not))
            {
                Func<DiscoveredTestMethod, bool> operand = ParseUnary();
                return method => !operand(method);
            }

            return ParsePrimary();
        }

        private Func<DiscoveredTestMethod, bool> ParsePrimary()
        {
            if (Match(TokenKind.LParen))
            {
                Func<DiscoveredTestMethod, bool> inner = ParseOr();
                if (!Match(TokenKind.RParen))
                {
                    throw new FormatException("Unbalanced '(' in test filter expression.");
                }

                return inner;
            }

            if (_pos < _tokens.Count && _tokens[_pos].Kind == TokenKind.Atom)
            {
                string atom = _tokens[_pos++].Value;
                return CompilePredicateAtom(atom);
            }

            throw new FormatException("Invalid or incomplete test filter expression.");
        }

        private bool Match(TokenKind kind)
        {
            if (_pos < _tokens.Count && _tokens[_pos].Kind == kind)
            {
                _pos++;
                return true;
            }

            return false;
        }
    }

    private static Func<DiscoveredTestMethod, bool> CompilePredicateAtom(string atom)
    {
        if (TrySplitOperator(atom, "!~", out string keyNotContains, out string valNotContains))
        {
            Func<DiscoveredTestMethod, bool> inner = CompileKeyContains(keyNotContains, valNotContains);
            return method => !inner(method);
        }

        if (TrySplitOperator(atom, "!=", out string keyNotEq, out string valNotEq))
        {
            Func<DiscoveredTestMethod, bool> inner = CompileKeyEquals(keyNotEq, valNotEq);
            return method => !inner(method);
        }

        if (TrySplitOperator(atom, "~", out string keyContains, out string valContains))
        {
            return CompileKeyContains(keyContains, valContains);
        }

        if (TrySplitOperator(atom, "=", out string keyEq, out string valEq))
        {
            return CompileKeyEquals(keyEq, valEq);
        }

        return CompileSimpleTerm(atom);
    }

    private static bool TrySplitOperator(string atom, string op, out string key, out string value)
    {
        int idx = atom.IndexOf(op, StringComparison.Ordinal);
        if (idx <= 0)
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        key = atom[..idx].Trim();
        value = atom[(idx + op.Length)..].Trim();
        return true;
    }

    private static Func<DiscoveredTestMethod, bool> CompileKeyContains(string key, string value)
    {
        if (string.Equals(key, "FullyQualifiedName", StringComparison.OrdinalIgnoreCase))
        {
            return method => method.FullName.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase))
        {
            return method => method.MethodName.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(key, "ClassName", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "Fixture", StringComparison.OrdinalIgnoreCase))
        {
            return method => method.FixtureFullName.Contains(value, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(key, "TestCategory", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "Category", StringComparison.OrdinalIgnoreCase))
        {
            return method =>
            {
                foreach (string cat in method.Categories)
                {
                    if (cat.Contains(value, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            };
        }

        throw new FormatException($"Unsupported test filter property '{key}' in '{key}~{value}'.");
    }

    private static Func<DiscoveredTestMethod, bool> CompileKeyEquals(string key, string value)
    {
        if (string.Equals(key, "FullyQualifiedName", StringComparison.OrdinalIgnoreCase))
        {
            return method => string.Equals(method.FullName, value, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(key, "Name", StringComparison.OrdinalIgnoreCase))
        {
            return method => string.Equals(method.MethodName, value, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(key, "ClassName", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "Fixture", StringComparison.OrdinalIgnoreCase))
        {
            return method => string.Equals(method.FixtureFullName, value, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(key, "TestCategory", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "Category", StringComparison.OrdinalIgnoreCase))
        {
            return method =>
            {
                foreach (string cat in method.Categories)
                {
                    if (string.Equals(cat, value, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            };
        }

        throw new FormatException($"Unsupported test filter property '{key}' in '{key}={value}'.");
    }
}
