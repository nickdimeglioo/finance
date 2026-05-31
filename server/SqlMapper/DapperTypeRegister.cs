using System;
using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace Mappers.DapperUtils
{

    public static class DapperFieldType
    {

        // ============================
        // THREAD-SAFE CACHES
        // ============================

        private static readonly ConcurrentDictionary<Type, bool> _handlerRegistry = new();
        private static int _defaultHandlersRegistered;

        // ============================
        // HANDLER REGISTRATION
        // ============================

        public static void TryRegisterHandler(Type propertyType, FieldType fieldType)
        {
            // Prevent global duplicate registration
            if (!_handlerRegistry.TryAdd(propertyType, true))
                return;

            switch (fieldType)
            {
                case FieldType.Jsonb:
                    RegisterJsonB(propertyType);
                    break;

                case FieldType.Json:
                    RegisterJson(propertyType);
                    break;

                //case FieldType.EncryptedJson:
                //    RegisterEncryptedJson(propertyType);
                //    break;

                default:
                    break;
                    //throw new NotSupportedException($"Unsupported FieldType: {fieldType}");
            }
        }

        public static void RegisterDefaultHandlers()
        {
            if (Interlocked.Exchange(ref _defaultHandlersRegistered, 1) == 1)
                return;

            DefaultTypeMap.MatchNamesWithUnderscores = true;
            SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
            SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
            TryRegisterHandler(typeof(Dictionary<string, object?>), FieldType.Jsonb);
            TryRegisterHandler(typeof(Dictionary<string, string>), FieldType.Jsonb);
            TryRegisterHandler(typeof(JsonDocument), FieldType.Jsonb);
        }

        private static void RegisterJsonB(Type type)
        {
            var handlerType = typeof(JsonBTypeHandler<>).MakeGenericType(type);
            var handler = (SqlMapper.ITypeHandler)Activator.CreateInstance(handlerType)!;
            SqlMapper.AddTypeHandler(type, handler);
        }

        private static void RegisterJson(Type type)
        {
            var handlerType = typeof(JsonTypeHandler<>).MakeGenericType(type);
            var handler = (SqlMapper.ITypeHandler)Activator.CreateInstance(handlerType)!;
            SqlMapper.AddTypeHandler(type, handler);
        }

        private static void RegisterEncryptedJson(Type type)
        {
            var handlerType = typeof(EncryptedJsonTypeHandler<>).MakeGenericType(type);
            var handler = (SqlMapper.ITypeHandler)Activator.CreateInstance(handlerType)!;
            SqlMapper.AddTypeHandler(type, handler);
        }

        // ============================
        // GENERIC TYPE HANDLERS
        // ============================

        private class JsonBTypeHandler<T> : SqlMapper.TypeHandler<T>
        {
            public override void SetValue(IDbDataParameter parameter, T value)
            {
                parameter.Value = value == null
                    ? DBNull.Value
                    : JsonSerializer.Serialize(value);
                if (parameter is NpgsqlParameter npgsqlParameter)
                {
                    npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
                }
            }

            public override T Parse(object value)
            {
                if (value is null || value is DBNull)
                    return default!;

                return JsonSerializer.Deserialize<T>(value.ToString()!)!;
            }
        }

        private class JsonTypeHandler<T> : SqlMapper.TypeHandler<T>
        {
            public override void SetValue(IDbDataParameter parameter, T value)
            {
                parameter.Value = value == null
                    ? DBNull.Value
                    : JsonSerializer.Serialize(value);
                if (parameter is NpgsqlParameter npgsqlParameter)
                {
                    npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Json;
                }
            }

            public override T Parse(object value)
            {
                if (value is null || value is DBNull)
                    return default!;

                return JsonSerializer.Deserialize<T>(value.ToString()!)!;
            }
        }

        private sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
        {
            public override void SetValue(IDbDataParameter parameter, DateOnly value)
            {
                parameter.Value = value.ToDateTime(TimeOnly.MinValue);
                if (parameter is NpgsqlParameter npgsqlParameter)
                {
                    npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Date;
                }
            }

            public override DateOnly Parse(object value)
            {
                return value switch
                {
                    DateOnly dateOnly => dateOnly,
                    DateTime dateTime => DateOnly.FromDateTime(dateTime),
                    string text => DateOnly.Parse(text),
                    _ => DateOnly.FromDateTime(Convert.ToDateTime(value))
                };
            }
        }

        private sealed class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
        {
            public override void SetValue(IDbDataParameter parameter, TimeOnly value)
            {
                parameter.Value = value.ToTimeSpan();
                if (parameter is NpgsqlParameter npgsqlParameter)
                {
                    npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Time;
                }
            }

            public override TimeOnly Parse(object value)
            {
                return value switch
                {
                    TimeOnly timeOnly => timeOnly,
                    TimeSpan timeSpan => TimeOnly.FromTimeSpan(timeSpan),
                    DateTime dateTime => TimeOnly.FromDateTime(dateTime),
                    string text => TimeOnly.Parse(text),
                    _ => TimeOnly.FromTimeSpan((TimeSpan)value)
                };
            }
        }

        private class EncryptedJsonTypeHandler<T> : SqlMapper.TypeHandler<T>
        {
            public override void SetValue(IDbDataParameter parameter, T value)
            {
                if (value == null)
                {
                    parameter.Value = DBNull.Value;
                    return;
                }

                var json = JsonSerializer.Serialize(value);
                var encrypted = Encrypt(json);
                parameter.Value = encrypted;
            }

            public override T Parse(object value)
            {
                if (value is null || value is DBNull)
                    return default!;

                var decrypted = Decrypt(value.ToString()!);
                return JsonSerializer.Deserialize<T>(decrypted)!;
            }

            // Replace with your real crypto
            private static string Encrypt(string input) => input;
            private static string Decrypt(string input) => input;
        }

        // ============================
        // ATTRIBUTE + ENUM
        // ============================

        [AttributeUsage(AttributeTargets.Property)]
        public sealed class FieldTypeAttribute : Attribute
        {
            public FieldType FieldType { get; }

            public FieldTypeAttribute(FieldType fieldType)
            {
                FieldType = fieldType;
            }
        }

    }

}
