using System.Globalization;

namespace Calculator;

// The code that does the tool's actual work. No UI and no framework dependencies, so it is easy to test.
// Supports + - * / % ^, parentheses, unary minus, the constants pi and e, and a few functions.
public static class CalculatorEngine
{
    public static bool TryEvaluate(string input, out double result, out string error)
    {
        result = 0;
        error = "";
        try
        {
            result = new Parser(input).Parse();
            if (double.IsNaN(result) || double.IsInfinity(result))
                throw new FormatException("Result is not a number");
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string Format(double value) => value.ToString("G15", CultureInfo.InvariantCulture);

    // Returns the result as text, or throws a FormatException that says what is wrong
    public static string Run(string input)
    {
        if (!TryEvaluate(input, out var result, out var error)) throw new FormatException(error);
        return Format(result);
    }

    private sealed class Parser(string text)
    {
        private int _pos;

        public double Parse()
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Type an expression");
            var value = Sum();
            SkipSpaces();
            if (_pos < text.Length) throw new FormatException($"Unexpected '{text[_pos]}'");
            return value;
        }

        private double Sum()
        {
            var value = Product();
            while (true)
            {
                if (Take('+')) value += Product();
                else if (Take('-')) value -= Product();
                else return value;
            }
        }

        private double Product()
        {
            var value = Unary();
            while (true)
            {
                if (Take('*')) value *= Unary();
                else if (Take('/'))
                {
                    var divisor = Unary();
                    if (divisor == 0) throw new FormatException("Cannot divide by zero");
                    value /= divisor;
                }
                else if (Take('%'))
                {
                    var divisor = Unary();
                    if (divisor == 0) throw new FormatException("Cannot divide by zero");
                    value %= divisor;
                }
                else return value;
            }
        }

        private double Unary()
        {
            if (Take('-')) return -Unary();
            if (Take('+')) return Unary();
            return Power();
        }

        // Right associative: 2^3^2 is 2^(3^2)
        private double Power()
        {
            var baseValue = Atom();
            return Take('^') ? Math.Pow(baseValue, Unary()) : baseValue;
        }

        private double Atom()
        {
            SkipSpaces();
            if (_pos >= text.Length) throw new FormatException("Expression ends too soon");

            if (Take('('))
            {
                var inner = Sum();
                if (!Take(')')) throw new FormatException("Missing )");
                return inner;
            }

            var c = text[_pos];
            if (char.IsDigit(c) || c == '.') return Number();
            if (char.IsLetter(c)) return Name();
            throw new FormatException($"Unexpected '{c}'");
        }

        private double Number()
        {
            var start = _pos;
            while (_pos < text.Length && (char.IsDigit(text[_pos]) || text[_pos] == '.')) _pos++;
            var slice = text[start.._pos];
            if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new FormatException($"'{slice}' is not a number");
            return value;
        }

        private double Name()
        {
            var start = _pos;
            while (_pos < text.Length && char.IsLetter(text[_pos])) _pos++;
            var name = text[start.._pos].ToLowerInvariant();

            switch (name)
            {
                case "pi": return Math.PI;
                case "e": return Math.E;
            }

            if (!Take('(')) throw new FormatException($"Unknown name '{name}'");
            var arg = Sum();
            if (!Take(')')) throw new FormatException("Missing )");

            return name switch
            {
                "sqrt" => arg < 0 ? throw new FormatException("Square root of a negative number") : Math.Sqrt(arg),
                "abs" => Math.Abs(arg),
                "sin" => Math.Sin(arg),
                "cos" => Math.Cos(arg),
                "tan" => Math.Tan(arg),
                "ln" => arg <= 0 ? throw new FormatException("Logarithm needs a positive number") : Math.Log(arg),
                "log" => arg <= 0 ? throw new FormatException("Logarithm needs a positive number") : Math.Log10(arg),
                "round" => Math.Round(arg, MidpointRounding.AwayFromZero),
                "floor" => Math.Floor(arg),
                "ceil" => Math.Ceiling(arg),
                _ => throw new FormatException($"Unknown function '{name}'"),
            };
        }

        private bool Take(char c)
        {
            SkipSpaces();
            if (_pos < text.Length && text[_pos] == c)
            {
                _pos++;
                return true;
            }
            return false;
        }

        private void SkipSpaces()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++;
        }
    }
}