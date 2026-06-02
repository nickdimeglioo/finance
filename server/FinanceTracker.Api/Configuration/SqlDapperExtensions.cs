using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Npgsql;
using NpgsqlTypes;

public class EnumHandler : IFieldConverter
{
    private static readonly Regex _snakeRegex =
        new Regex("(?<=[a-z0-9])([A-Z])", RegexOptions.Compiled);

    public object? FromProvider(object value)
    {
        return ToSnakeCase(value.ToString());
        
    }

    public object ToProvider(object value, Type t)
    {
        string name = value.ToString()!;
        string pascal = ToPascalCase(name);

        return Enum.Parse(t, pascal, ignoreCase: true);
    }

    private static string ToSnakeCase(string name)
    {
        // OfflineReview -> Offline_Review -> offline_review
        return _snakeRegex.Replace(name, "_$1").ToLowerInvariant();
    }

    private static string ToPascalCase(string snake)
    {
        // offline_review -> OfflineReview
        return string.Concat(
            snake.Split('_', StringSplitOptions.RemoveEmptyEntries)
                 .Select(s => char.ToUpperInvariant(s[0]) + s[1..])
        );
    }
}


public class DBSerializer : IFieldConverter
{
    public object? FromProvider(object value)
    {
        return JsonSerializer.Serialize(value);

    }

    public object? ToProvider(object value, Type t)
    {
        return JsonSerializer.Deserialize((string)value, t);
    }
}



public class DateTimeOffsetHandler : IFieldConverter
{

    public object? FromProvider(object value)
    {
        return value;

    }

    public object ToProvider(object value, Type t)
    {
        if (value == null) return null;
       if(t.IsAssignableFrom(value.GetType()))
        {
            return (DateTimeOffset)value;
        }
        else if(value is DateTime dt)
        {
            return new DateTimeOffset(dt, TimeSpan.Zero); // return in utc/no offset
        }

        return (DateTimeOffset)value; // try to convert
    }
}


public class DateOnlyHandler : IFieldConverter
{

    public object? FromProvider(object? value)
    {
        return value;

    }

    public object ToProvider(object value, Type t)
    {
        if (value is null) return null;
        return value switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            string s => DateOnly.Parse(s, CultureInfo.InvariantCulture),
            _ => throw new DataException($"Cannot convert {value.GetType()} to DateOnly")
        };
    }
}


public sealed class DateOnlyDapperTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        if (parameter is NpgsqlParameter npgsqlParameter)
        {
            npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Date;
            npgsqlParameter.Value = value;
            return;
        }

        parameter.DbType = DbType.Date;
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
    }

    public override DateOnly Parse(object value)
    {
        return value switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            string s => DateOnly.Parse(s, CultureInfo.InvariantCulture),
            _ => throw new DataException($"Cannot convert {value.GetType()} to DateOnly")
        };
    }
}