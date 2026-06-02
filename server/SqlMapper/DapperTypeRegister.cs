using System;
using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using System.Text.Json;
using Dapper;
namespace Mappers.DapperUtils
{

    public static class DapperFieldType
    {

        // ============================
        // THREAD-SAFE CACHES
        // ============================

        private static readonly ConcurrentDictionary<Type, bool> _handlerRegistry = new();

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
            }

            public override T Parse(object value)
            {
                if (value is null || value is DBNull)
                    return default!;

                return JsonSerializer.Deserialize<T>(value.ToString()!)!;
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