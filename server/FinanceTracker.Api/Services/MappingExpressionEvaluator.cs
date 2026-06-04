using System.Globalization;

namespace FinanceTracker.Api.Services;

internal sealed class MappingExpressionEvaluator
{
    private readonly IReadOnlyDictionary<string, string> _row;

    private MappingExpressionEvaluator(IReadOnlyDictionary<string, string> row)
    {
        _row = row;
    }

    internal static object? Evaluate(string expression, IReadOnlyDictionary<string, string> row)
    {
        return new MappingExpressionEvaluator(row).EvaluateExpression(expression.Trim());
    }

    private object? EvaluateExpression(string expression)
    {
        expression = StripOuterParens(expression.Trim());
        var comparison = FindComparison(expression);
        if (comparison is not null)
        {
            var left = EvaluateExpression(expression[..comparison.Value.Index]);
            var right = EvaluateExpression(expression[(comparison.Value.Index + comparison.Value.Operator.Length)..]);
            return Compare(left, right, comparison.Value.Operator);
        }

        var split = SplitTopLevel(expression, ['+', '-']);
        if (split is not null)
        {
            var left = EvaluateExpression(split.Value.Left);
            var right = EvaluateExpression(split.Value.Right);
            return split.Value.Operator == '+'
                ? ToDecimal(left) + ToDecimal(right)
                : ToDecimal(left) - ToDecimal(right);
        }

        if ((expression.StartsWith('\'') && expression.EndsWith('\'')) || (expression.StartsWith('"') && expression.EndsWith('"')))
        {
            return expression[1..^1];
        }

        if (decimal.TryParse(expression, NumberStyles.Number, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric;
        }

        var function = ParseFunction(expression);
        if (function is not null)
        {
            return EvaluateFunction(function.Value.Name, function.Value.Args);
        }

        return ReadValue(expression);
    }

    private object? EvaluateFunction(string name, IReadOnlyList<string> args)
    {
        return name.ToUpperInvariant() switch
        {
            "COALESCE" => args.Select(EvaluateExpression).FirstOrDefault(value => !IsBlank(value)),
            "IF" => ToBool(EvaluateExpression(args[0])) ? EvaluateExpression(args[1]) : EvaluateExpression(args[2]),
            "CONCAT" => string.Concat(args.Select(arg => Convert.ToString(EvaluateExpression(arg), CultureInfo.InvariantCulture))),
            "ABS" => Math.Abs(ToDecimal(EvaluateExpression(args[0]))),
            "ROUND" => Math.Round(ToDecimal(EvaluateExpression(args[0])), args.Count > 1 ? (int)ToDecimal(EvaluateExpression(args[1])) : 2),
            "TRIM" => Convert.ToString(EvaluateExpression(args[0]), CultureInfo.InvariantCulture)?.Trim(),
            "UPPER" => Convert.ToString(EvaluateExpression(args[0]), CultureInfo.InvariantCulture)?.ToUpperInvariant(),
            "LOWER" => Convert.ToString(EvaluateExpression(args[0]), CultureInfo.InvariantCulture)?.ToLowerInvariant(),
            _ => throw new ArgumentException($"Expression function '{name}' is not supported.")
        };
    }

    private string? ReadValue(string name)
    {
        return _row.TryGetValue(name, out var value)
            ? value
            : _row.TryGetValue(CsvParserService.NormalizeColumnReference(name), out var normalized)
                ? normalized
                : null;
    }

    private static bool Compare(object? left, object? right, string op)
    {
        if (left is null || right is null)
        {
            return op == "==" ? left == right : op == "!=" && left != right;
        }

        var leftDecimal = TryDecimal(left);
        var rightDecimal = TryDecimal(right);
        if (leftDecimal.HasValue && rightDecimal.HasValue)
        {
            return op switch
            {
                ">" => leftDecimal.Value > rightDecimal.Value,
                "<" => leftDecimal.Value < rightDecimal.Value,
                ">=" => leftDecimal.Value >= rightDecimal.Value,
                "<=" => leftDecimal.Value <= rightDecimal.Value,
                "==" => leftDecimal.Value == rightDecimal.Value,
                "!=" => leftDecimal.Value != rightDecimal.Value,
                _ => false
            };
        }

        var comparison = string.Compare(Convert.ToString(left, CultureInfo.InvariantCulture), Convert.ToString(right, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            "==" => comparison == 0,
            "!=" => comparison != 0,
            _ => false
        };
    }

    private static decimal ToDecimal(object? value)
    {
        return TryDecimal(value) ?? 0m;
    }

    private static decimal? TryDecimal(object? value)
    {
        if (value is decimal d)
        {
            return d;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Replace("$", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            text = $"-{text[1..^1]}";
        }

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static bool ToBool(object? value)
    {
        return value is bool b
            ? b
            : bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed) && parsed;
    }

    private static bool IsBlank(object? value)
    {
        return value is null || string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    private static string StripOuterParens(string expression)
    {
        while (expression.StartsWith('(') && expression.EndsWith(')') && IsBalanced(expression[1..^1]))
        {
            expression = expression[1..^1].Trim();
        }

        return expression;
    }

    private static (string Name, IReadOnlyList<string> Args)? ParseFunction(string expression)
    {
        var open = expression.IndexOf('(');
        if (open <= 0 || !expression.EndsWith(')'))
        {
            return null;
        }

        return (expression[..open].Trim(), SplitArguments(expression[(open + 1)..^1]));
    }

    private static (int Index, string Operator)? FindComparison(string expression)
    {
        foreach (var op in new[] { ">=", "<=", "==", "!=", ">", "<" })
        {
            var index = FindTopLevel(expression, op);
            if (index >= 0)
            {
                return (index, op);
            }
        }

        return null;
    }

    private static (string Left, string Right, char Operator)? SplitTopLevel(string expression, char[] operators)
    {
        var depth = 0;
        var quote = '\0';
        for (var i = expression.Length - 1; i >= 0; i--)
        {
            var ch = expression[i];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
            }
            else if (ch == ')')
            {
                depth++;
            }
            else if (ch == '(')
            {
                depth--;
            }
            else if (depth == 0 && operators.Contains(ch) && i > 0)
            {
                return (expression[..i], expression[(i + 1)..], ch);
            }
        }

        return null;
    }

    private static int FindTopLevel(string expression, string op)
    {
        var depth = 0;
        var quote = '\0';
        for (var i = 0; i <= expression.Length - op.Length; i++)
        {
            var ch = expression[i];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
            }
            else if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                depth--;
            }
            else if (depth == 0 && string.Equals(expression.Substring(i, op.Length), op, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> SplitArguments(string input)
    {
        var args = new List<string>();
        var depth = 0;
        var quote = '\0';
        var start = 0;
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
            }
            else if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                depth--;
            }
            else if (ch == ',' && depth == 0)
            {
                args.Add(input[start..i].Trim());
                start = i + 1;
            }
        }

        args.Add(input[start..].Trim());
        return args;
    }

    private static bool IsBalanced(string input)
    {
        var depth = 0;
        foreach (var ch in input)
        {
            if (ch == '(')
            {
                depth++;
            }
            else if (ch == ')')
            {
                depth--;
                if (depth < 0)
                {
                    return false;
                }
            }
        }

        return depth == 0;
    }
}
