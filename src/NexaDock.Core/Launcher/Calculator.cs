using System.Globalization;

namespace NexaDock.Core.Launcher;

/// <summary>
/// A small, safe expression evaluator for the launcher: + − × ÷ ^ %, parentheses, decimals with a
/// dot or a comma, constants (pi, e) and common functions (sqrt, sin, cos, tan, log, ln, abs,
/// round). Plain numbers are not calculations; "=" in front is allowed.
/// </summary>
public static class Calculator
{
    public static bool TryEvaluate(string input, out double value)
    {
        value = 0;
        var text = input.Trim();
        if (text.StartsWith('='))
        {
            text = text[1..];
        }

        text = text.Replace('×', '*').Replace('÷', '/').Replace('−', '-').Replace(" ", string.Empty, StringComparison.Ordinal);
        if (text.Length == 0 || !text.Any(char.IsDigit) && !text.Contains("pi", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Something to calculate: an operator between terms or a function, not just "42" or "-5".
        var body = text.TrimStart('-', '+');
        if (!body.Any(c => c is '+' or '-' or '*' or '/' or '^' or '%' or '(') && !body.Any(char.IsLetter))
        {
            return false;
        }

        try
        {
            var parser = new Parser(text);
            value = parser.ParseExpression();
            if (!parser.AtEnd || double.IsNaN(value) || double.IsInfinity(value))
            {
                return false;
            }

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string Format(double value, CultureInfo culture)
    {
        var rounded = Math.Round(value, 10);
        return Math.Abs(rounded) >= 1e15 || (rounded != 0 && Math.Abs(rounded) < 1e-6)
            ? rounded.ToString("G10", culture)
            : rounded.ToString("#,0.##########", culture);
    }

    private sealed class Parser(string text)
    {
        private int _pos;

        public bool AtEnd => _pos >= text.Length;

        // expression := term (('+' | '-') term)*
        public double ParseExpression()
        {
            var value = ParseTerm();
            while (!AtEnd && (Peek == '+' || Peek == '-'))
            {
                var op = text[_pos++];
                var right = ParseTerm();
                value = op == '+' ? value + right : value - right;
            }

            return value;
        }

        // term := power (('*' | '/' | '%') power)*
        private double ParseTerm()
        {
            var value = ParsePower();
            while (!AtEnd && (Peek == '*' || Peek == '/' || Peek == '%'))
            {
                var op = text[_pos++];

                // "50%" at the end of a term is a percentage: 200*15% = 30.
                if (op == '%' && (AtEnd || Peek is '+' or '-' or ')' or '*' or '/'))
                {
                    value /= 100;
                    continue;
                }

                var right = ParsePower();
                value = op switch
                {
                    '*' => value * right,
                    '/' => right == 0 ? throw new FormatException("division by zero") : value / right,
                    _ => value % right,
                };
            }

            return value;
        }

        // power := unary ('^' power)?
        private double ParsePower()
        {
            var value = ParseUnary();
            if (!AtEnd && Peek == '^')
            {
                _pos++;
                value = Math.Pow(value, ParsePower());
            }

            return value;
        }

        private double ParseUnary()
        {
            if (!AtEnd && (Peek == '-' || Peek == '+'))
            {
                var negative = text[_pos++] == '-';
                var v = ParseUnary();
                return negative ? -v : v;
            }

            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            if (AtEnd)
            {
                throw new FormatException("unexpected end");
            }

            if (Peek == '(')
            {
                _pos++;
                var inner = ParseExpression();
                Expect(')');
                return inner;
            }

            if (char.IsLetter(Peek))
            {
                var start = _pos;
                while (!AtEnd && char.IsLetter(Peek))
                {
                    _pos++;
                }

                var name = text[start.._pos].ToLowerInvariant();
                switch (name)
                {
                    case "pi":
                        return Math.PI;
                    case "e":
                        return Math.E;
                }

                Expect('(');
                var arg = ParseExpression();
                Expect(')');
                return name switch
                {
                    "sqrt" or "korijen" => arg < 0 ? throw new FormatException("negative root") : Math.Sqrt(arg),
                    "sin" => Math.Sin(arg * Math.PI / 180),
                    "cos" => Math.Cos(arg * Math.PI / 180),
                    "tan" => Math.Tan(arg * Math.PI / 180),
                    "log" => Math.Log10(arg),
                    "ln" => Math.Log(arg),
                    "abs" => Math.Abs(arg),
                    "round" => Math.Round(arg),
                    _ => throw new FormatException("unknown function " + name),
                };
            }

            return ParseNumber();
        }

        private double ParseNumber()
        {
            var start = _pos;
            while (!AtEnd && (char.IsDigit(Peek) || Peek is '.' or ','))
            {
                _pos++;
            }

            if (start == _pos)
            {
                throw new FormatException("number expected");
            }

            var raw = text[start.._pos];

            // "1.234,5" / "1,234.5": the last separator is the decimal one; "2,5" and "2.5" both mean 2.5.
            var lastDot = raw.LastIndexOf('.');
            var lastComma = raw.LastIndexOf(',');
            var decimalAt = Math.Max(lastDot, lastComma);
            string normalized;
            if (decimalAt < 0)
            {
                normalized = raw;
            }
            else if (lastDot >= 0 && lastComma >= 0)
            {
                normalized = new string(raw[..decimalAt].Where(char.IsDigit).ToArray()) + "." + raw[(decimalAt + 1)..];
            }
            else
            {
                normalized = raw.Replace(',', '.');
            }

            return double.Parse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private char Peek => text[_pos];

        private void Expect(char c)
        {
            if (AtEnd || text[_pos] != c)
            {
                throw new FormatException($"'{c}' expected");
            }

            _pos++;
        }
    }
}
