using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Mappers.DapperUtils;

// Simplified Attributes (removed InverseProperty, IsCollection, UniqueAttribute, NullableAttribute)
[AttributeUsage(AttributeTargets.Class)]
public class TableNameAttribute : Attribute
{
    public string Name { get; }
    public TableNameAttribute(string name) => Name = name;
}

public enum DbNamingConvention
{
    SnakeCase,
    PascalCase,
    CamelCase
}

public sealed class OrmMapperOptions
{
    public DbNamingConvention TableStyle { get; set; } = DbNamingConvention.SnakeCase;
    public DbNamingConvention ColumnStyle { get; set; } = DbNamingConvention.SnakeCase;
    public bool PluralizeTableNames { get; set; } = true;
    public IsolationLevel DefaultTransactionIsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

    public OrmMapperOptions Clone()
    {
        return new OrmMapperOptions
        {
            TableStyle = TableStyle,
            ColumnStyle = ColumnStyle,
            PluralizeTableNames = PluralizeTableNames,
            DefaultTransactionIsolationLevel = DefaultTransactionIsolationLevel
        };
    }
}

[AttributeUsage(AttributeTargets.Class)]
public class DbNamingConventionAttribute : Attribute
{
    public DbNamingConvention Convention { get; }
    public DbNamingConventionAttribute(DbNamingConvention convention) => Convention = convention;
}

public enum RlsScope
{
    Project,
    Organization
}

[AttributeUsage(AttributeTargets.Class)]
public class RLSAttribute : Attribute
{
    public RlsScope Scope { get; }
    public bool AllowUnscopedRows { get; }

    public RLSAttribute(RlsScope scope = RlsScope.Project, bool allowUnscopedRows = false)
    {
        Scope = scope;
        AllowUnscopedRows = allowUnscopedRows;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class PrimaryKeyAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public class ColumnAttribute : Attribute
{
    public string Name { get; }
    public ColumnAttribute(string name) => Name = name;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public class ForeignKeyAttribute : Attribute
{
    public string ColumnName { get; }
    public ForeignKeyAttribute(string columnName) => ColumnName = columnName;
}

[AttributeUsage(AttributeTargets.Property)]
public class SoftDeleteAttribute : Attribute
{
    public string ColumnName { get; }
    public SoftDeleteAttribute(string columnName = "is_deleted") => ColumnName = columnName;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Enum)]
public class DBTypeAttribute : Attribute
{
    public string ColumnName { get; }
    public FieldType ColumnType { get; }

    public DBTypeAttribute(string fieldType)
    {
        ColumnName = fieldType;
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Enum)]
public class ConverterAttribute : Attribute
{
    public Type ConverterType { get; }
    public ConverterAttribute(Type converterType)
    {
        if (!typeof(IFieldConverter).IsAssignableFrom(converterType))
            throw new ArgumentException($"{converterType} must implement IFieldConverter");
        ConverterType = converterType;
    }
}


[AttributeUsage(AttributeTargets.Class)]
public class NoUpsertAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public class FlattenInheritedPropertiesAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public class NotLoadableAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public class NotSavableAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public class NotDeletableAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Property)]
public class NoDBAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public class EncryptedAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public class AuditableAttribute : Attribute
{
    public AuditableAttribute()
    {
    }

    public AuditableAttribute(string auditAs)
    {
        AuditAs = auditAs;
    }

    public string? AuditAs { get; }
    public string? Alias { get; init; }
    public bool IncludeValue { get; init; } = true;
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class AuditSummaryAttribute : Attribute
{
    public string? Create { get; init; }
    public string? Update { get; init; }
    public string? Delete { get; init; }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class ResourcePermissionAttribute : Attribute
{
    public string? View { get; init; }
    public string? Save { get; init; }
    public string? Delete { get; init; }
    public RlsScope Scope { get; init; } = RlsScope.Project;
    public string ResourceIdProperty { get; init; } = "ResourceId";
    public string ProjectIdProperty { get; init; } = "ProjectId";
    public string OrganizationIdProperty { get; init; } = "OrganizationId";
}

public enum AuditContextKind
{
    OrganizationId,
    ProjectId,
    ActorUserId,
    TargetUserId,
    EntityLabel
}

[AttributeUsage(AttributeTargets.Property)]
public class AuditContextAttribute : Attribute
{
    public AuditContextAttribute(AuditContextKind kind)
    {
        Kind = kind;
    }

    public AuditContextKind Kind { get; }
}

public interface ISystemOwnedRecord
{
    bool IsSystem { get; }
}

public interface ISoftDelete
{
    bool IsDeleted { get; set; }
}

public sealed class SystemOwnedRecordWriteScope : IDisposable
{
    private static readonly AsyncLocal<int> CurrentDepth = new();
    private bool _disposed;

    private SystemOwnedRecordWriteScope()
    {
        CurrentDepth.Value = CurrentDepth.Value + 1;
    }

    public static bool IsActive => CurrentDepth.Value > 0;

    public static SystemOwnedRecordWriteScope Allow() => new();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CurrentDepth.Value = Math.Max(0, CurrentDepth.Value - 1);
        _disposed = true;
    }
}



public class SelectQuery()
{
    public bool LockRow { get; set; } = false;
    public bool NoWait { get; set; } = false;
    public bool IncludeSoftDeleted { get; set; } = false;
    public IDbConnection? CurrentConnection { get; set; }
    public IDbTransaction? CurrentTransaction { get; set; }
    public bool UseTransaction { get; set; }
    // When true, query execution opens/disposes CurrentConnection automatically.
    public bool ManageConnectionLifecycle { get; set; } = false;
    public IReadOnlyList<IDbInterceptor>? Interceptors { get; set; }
    public CancellationToken CancellationToken { get; set; } = default;
}


public interface IFieldConverter
{
    object? FromProvider(object? value);
    object ToProvider(object value, Type t);
}


public sealed class DelegateType
{
    public Action<object, object?> Setter { get; init; } = null!;
    public Func<object, object?> Getter { get; init; } = null!;
    public Type TargetType { get; init; } = null!;
}



// Simplified Metadata classes
public class PropertyMetadata
{
    public PropertyInfo Property { get; set; }
    public string ColumnName { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsLoadable { get; set; } = true;
    public bool IsSavable { get; set; } = true;
    public bool IsDeletable { get; set; } = true;
    public bool IsAuditable { get; set; } = false;
    public string AuditName { get; set; }
    public bool IncludeAuditValue { get; set; } = true;
    public AuditContextKind? AuditContextKind { get; set; }
    public bool IsInherited { get; set; }
    public bool IsForeignKey { get; set; }
    public string ForeignKeyColumn { get; set; }
    public Type ForeignKeyType { get; set; }
    public Type TargetType { get; set; }

    // for things like jsonb, this is really meant for changing the value on inserts/updates, using this on selects is more difficult
    // to use on both select & update/insert, make a class and put the FieldTypeAttribute on the class and it will register that to dapper and parse it
    // the limitation of that way is for things like a string, it wont insert correctly or return correctly. At least this way, it will insert correctly and
    // not error out. We can also make it so it will deserialize on select via splitmapping by defining a special type kind of thing, but i dont see me using that rn
    public string? CustomField { get; set; }
    public IFieldConverter? Converter { get; set; }


    public string ColName  {
        get  => IsForeignKey ? ForeignKeyColumn : ColumnName;
    }
}

public class ClassMetadata
{
    public Type Type { get; set; }
    public string TableName { get; set; }
    public DbNamingConvention NamingConvention { get; set; } = DbNamingConvention.SnakeCase;
    public DbNamingConvention TableNamingConvention { get; set; } = DbNamingConvention.SnakeCase;
    public string? SoftDeleteField { get; set; }
    public bool SupportsSoftDelete { get; set; }
    public PropertyMetadata? SoftDeleteProperty { get; set; }
    public PropertyMetadata PrimaryKey { get; set; }
    public PropertyMetadata BaseMetaData { get; set; }
    public List<PropertyMetadata> Properties { get; set; } = new();
    public List<PropertyMetadata> LoadableProperties { get; set; } = new();
    public List<PropertyMetadata> SavableProperties { get; set; } = new();
    public List<PropertyMetadata> DeletableProperties { get; set; } = new();
    public List<PropertyMetadata> AuditableProperties { get; set; } = new();
    public List<PropertyMetadata> ForeignKeys { get; set; } = new();
    public bool Upsertable { get; set; } = new();
    public ResourcePermissionAttribute? ResourcePermission { get; set; }
    public AuditSummaryAttribute? AuditSummary { get; set; }
}

public enum FieldType
{
    Jsonb,
    Json,
    Hstore,
    MacAddr,
    TsVector,
    TsQuery,
    Custom // uses the table name as the field
}

public class OrmConfiguration
{
    internal static readonly ConcurrentDictionary<Type, IFieldConverter> _handlers= new ();
    internal static IFieldConverter _encryptor = null!;
    public static void AddType<T>(IFieldConverter item)
    {
        _handlers.TryAdd(typeof(T), item);
    }

    public static void RegisterDefaultEncryptor(IFieldConverter encryption)
    {
        if (_encryptor == null) _encryptor = encryption;
    }

    internal static IFieldConverter? GetType<T>()
    {
        return GetType(typeof(T));
    }

    internal static IFieldConverter? GetType(Type type)
    {
        var val =  _handlers.GetValueOrDefault(type);
        if(val == null && type.IsEnum) // idk maybe make more generic if possible
        {
            return _handlers.GetValueOrDefault(typeof(Enum));
        }
        return val;
    }


}



// Main ORM Mapper - Optimized for bulk operations
public static partial class OrmMapper
{
    private static readonly ConcurrentDictionary<Type, ClassMetadata> _metadataCache = new();
    private static readonly ConcurrentDictionary<PropertyInfo, DelegateType> _delegateCache = new();
    private static readonly object OptionsLock = new();
    private static OrmMapperOptions _options = new();
    public static ClassMetadata GetMetadata<T>() => GetOrCreateMetadata(typeof(T));
    public static ClassMetadata GetMetadata(Type type) => GetOrCreateMetadata(type);

    public static OrmMapperOptions Options
    {
        get
        {
            lock (OptionsLock)
            {
                return _options.Clone();
            }
        }
    }

    public static void Configure(OrmMapperOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        lock (OptionsLock)
        {
            _options = options.Clone();
            _metadataCache.Clear();
        }
    }

    public static void Configure(Action<OrmMapperOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = Options;
        configure(options);
        Configure(options);
    }

    internal static OrmMapperOptions GetOptionsSnapshot()
    {
        return Options;
    }
    public static DelegateType GetDelegate(PropertyInfo prop)
    {
        return _delegateCache.GetOrAdd(prop, p =>
        {
            var targetType =
                Nullable.GetUnderlyingType(p.PropertyType)
                ?? p.PropertyType;

            return new DelegateType
            {
                Setter = CreateSetter(p),
                Getter = CreateGetter(p),
                TargetType = targetType
            };
        });
    }


    private static ClassMetadata GetOrCreateMetadata(Type type)
    {
        return _metadataCache.GetOrAdd(type, t =>
        {
            var metadata = new ClassMetadata { Type = t };
            var options = GetOptionsSnapshot();


            var classNamingConvention = t.GetCustomAttribute<DbNamingConventionAttribute>(inherit: false)?.Convention;
            var tableNamingConvention = classNamingConvention ?? options.TableStyle;
            var columnNamingConvention = classNamingConvention ?? options.ColumnStyle;
            metadata.NamingConvention = columnNamingConvention;
            metadata.TableNamingConvention = tableNamingConvention;

            // Get table name
            var tableAttr = t.GetCustomAttribute<TableNameAttribute>(inherit: false);
            metadata.TableName = tableAttr?.Name ?? GetDefaultTableName(t, tableNamingConvention, options.PluralizeTableNames);
            var softDeleteAttr = t.GetCustomAttribute<SoftDeleteAttribute>();
            //FieldType? tableField = t.GetCustomAttribute<DBTypeAttribute>(inherit: false)?.ColumnType; // Is this right, idk
            metadata.SoftDeleteField = softDeleteAttr?.ColumnName;
            metadata.SupportsSoftDelete = softDeleteAttr != null || typeof(ISoftDelete).IsAssignableFrom(t);
            metadata.Upsertable = t.GetCustomAttribute<NoUpsertAttribute>() == null;
            metadata.ResourcePermission = t.GetCustomAttribute<ResourcePermissionAttribute>(inherit: true);
            metadata.AuditSummary = t.GetCustomAttribute<AuditSummaryAttribute>(inherit: true);
            var flattenInheritedProperties = t.GetCustomAttribute<FlattenInheritedPropertiesAttribute>(inherit: false) != null;

            // register if needed, this basically is for defining a table as a type to get converted to (such as jsonb). You can also define on individual fields
            // but it wont register and instead, conversion is kind of done manually for inserts and selects, can can even lead to weird behavior, but idrk yet.
            //if (tableField.HasValue) DapperFieldType.TryRegisterHandler(type, tableField.Value); // idk if we need to actually store this anywhere though, not rn at least

            PropertyMetadata? pk = null;
            string primaryKeyId = $"{ToPascalCase(metadata.TableName)}Id";

            // Process properties
            foreach (var prop in GetRelevantProperties(t, flattenInheritedProperties))
            {
                bool isCustomPrimaryKey = false;
                if (pk == null && prop.PropertyType == typeof(int) && prop.Name == primaryKeyId)
                {
                    isCustomPrimaryKey = true;
                }
                var targetType = Nullable.GetUnderlyingType(prop.PropertyType)
                     ?? prop.PropertyType;
                var hasDb = prop.GetCustomAttribute<NoDBAttribute>() == null;
                var converterAttribute = prop.GetCustomAttribute<ConverterAttribute>();
                var auditableAttribute = prop.GetCustomAttribute<AuditableAttribute>();
                var auditContextAttribute = prop.GetCustomAttribute<AuditContextAttribute>();
                var softDeletePropertyAttribute = prop.GetCustomAttribute<SoftDeleteAttribute>();
                var propMetadata = new PropertyMetadata
                {
                    Property = prop,
                    ColumnName = prop.GetCustomAttribute<ColumnAttribute>()?.Name ?? GetDefaultColumnName(prop.Name, columnNamingConvention),
                    IsPrimaryKey = prop.GetCustomAttribute<PrimaryKeyAttribute>() != null || isCustomPrimaryKey,
                    IsLoadable = hasDb && prop.GetCustomAttribute<NotLoadableAttribute>() == null,
                    IsSavable = hasDb && prop.GetCustomAttribute<NotSavableAttribute>() == null,
                    IsDeletable = hasDb && prop.GetCustomAttribute<NotDeletableAttribute>() == null,
                    IsAuditable = auditableAttribute != null,
                    AuditName = !string.IsNullOrWhiteSpace(auditableAttribute?.AuditAs)
                        ? auditableAttribute.AuditAs!
                        : !string.IsNullOrWhiteSpace(auditableAttribute?.Alias)
                            ? auditableAttribute.Alias!
                            : prop.Name,
                    IncludeAuditValue = auditableAttribute?.IncludeValue ?? true,
                    AuditContextKind = auditContextAttribute?.Kind,
                    CustomField = prop.GetCustomAttribute<DBTypeAttribute>()?.ColumnName,
                    Converter = converterAttribute == null
                        ? null
                        : (IFieldConverter?)Activator.CreateInstance(converterAttribute.ConverterType),
                    IsInherited = false,
                    TargetType = targetType,
                };

                GetDelegate(prop); // set the cache for the delegate

                bool hasEncyption = prop.GetCustomAttribute<EncryptedAttribute>() != null;

                // Check for foreign key
                var fkAttr = prop.GetCustomAttribute<ForeignKeyAttribute>();
                if (hasDb && (fkAttr != null || (!IsSimpleType(prop.PropertyType) && !IsCollection(prop.PropertyType) && propMetadata.CustomField == null)))
                { // if we use a class and specify a custom field type (like jsonb) it will only allow for FK if FK attrb is used (not that this should ever happen tbh)
                    propMetadata.IsForeignKey = true;
                    propMetadata.ForeignKeyColumn = fkAttr?.ColumnName ?? GetDefaultForeignKeyColumnName(prop.Name, columnNamingConvention);
                    propMetadata.ForeignKeyType = prop.PropertyType;
                }

                metadata.Properties.Add(propMetadata);

                if (softDeletePropertyAttribute != null ||
                    (metadata.SupportsSoftDelete &&
                     string.Equals(prop.Name, nameof(ISoftDelete.IsDeleted), StringComparison.OrdinalIgnoreCase)))
                {
                    metadata.SupportsSoftDelete = true;
                    metadata.SoftDeleteField = softDeletePropertyAttribute?.ColumnName ?? propMetadata.ColumnName;
                    metadata.SoftDeleteProperty = propMetadata;
                }

                if (hasEncyption)
                {
                    propMetadata.Converter = OrmConfiguration._encryptor; // later can add it so that can be user defined, but rn its one encryptor
                }
                else if(propMetadata.Converter == null)
                {
                    propMetadata.Converter = OrmConfiguration.GetType(propMetadata.Property.PropertyType);
                }

                if (propMetadata.IsPrimaryKey)
                {
                    metadata.PrimaryKey = propMetadata;
                    if (!isCustomPrimaryKey && pk != null)
                        pk.IsPrimaryKey = false;
                    else
                        pk = propMetadata;
                }

                if (propMetadata.IsLoadable)
                    metadata.LoadableProperties.Add(propMetadata);
                if (propMetadata.IsSavable)
                    metadata.SavableProperties.Add(propMetadata);
                if (propMetadata.IsDeletable)
                    metadata.DeletableProperties.Add(propMetadata);
                if (propMetadata.IsForeignKey)
                    metadata.ForeignKeys.Add(propMetadata);
                if (propMetadata.IsAuditable)
                    metadata.AuditableProperties.Add(propMetadata);
            }

            if (metadata.SupportsSoftDelete)
            {
                metadata.SoftDeleteField ??= metadata.SoftDeleteProperty?.ColumnName ?? ApplyNamingConvention(nameof(ISoftDelete.IsDeleted), columnNamingConvention);
            }

            // Handle inheritance
            Type? inherited = flattenInheritedProperties ? null : InheritedConcreteClass(t);
            if (inherited != null)
            {
                var inheritedMetadata = GetOrCreateMetadata(inherited);
                var propData = inheritedMetadata.PrimaryKey;
                if (propData != null)
                {
                    var prop = propData.Property;
                    var name = t.GetCustomAttribute<ForeignKeyAttribute>()?.ColumnName ?? propData.ColumnName;
                    var propMetadata = new PropertyMetadata
                    {
                        Property = prop,
                        IsPrimaryKey = false,
                        ColumnName = name,
                        IsLoadable = propData.IsLoadable,
                        IsSavable = propData.IsSavable,
                        IsDeletable = propData.IsDeletable,
                        IsForeignKey = true,
                        ForeignKeyColumn = name, // change here if 
                        IsInherited = true,
                        ForeignKeyType = inherited,
                        Converter = propData.Converter,
                        CustomField = propData.CustomField,
                        //CustomField = propData.CustomField // idk if this should be added, i dont really care
                    };

                    if (propMetadata.IsLoadable)
                        metadata.LoadableProperties.Add(propMetadata);
                    if (propMetadata.IsSavable)
                        metadata.SavableProperties.Add(propMetadata);
                    if (propMetadata.IsDeletable)
                        metadata.DeletableProperties.Add(propMetadata);
                    if (propMetadata.IsForeignKey)
                        metadata.ForeignKeys.Add(propMetadata);

                    metadata.BaseMetaData = propMetadata;
                }
            }

            return metadata;
        });
    }


    static Action<object, object?> CreateSetter(PropertyInfo prop)
    {
        var targetParam = Expression.Parameter(typeof(object), "target");
        var valueParam = Expression.Parameter(typeof(object), "value");

        var castTarget = Expression.Convert(targetParam, prop.DeclaringType!);
        var castValue = Expression.Convert(valueParam, prop.PropertyType);

        var body = Expression.Assign(
            Expression.Property(castTarget, prop),
            castValue
        );

        return Expression.Lambda<Action<object, object?>>(body, targetParam, valueParam)
                         .Compile();
    }

    private static Func<object, object?> CreateGetter(PropertyInfo prop)
    {
        var target = Expression.Parameter(typeof(object), "target");

        var body = Expression.Convert(
            Expression.Property(
                Expression.Convert(target, prop.DeclaringType!),
                prop
            ),
            typeof(object)
        );

        return Expression
            .Lambda<Func<object, object?>>(body, target)
            .Compile();
    }





    public static PropertyInfo[] GetRelevantProperties(Type originalType, bool flattenConcreteInheritance = false)
    {
        var result = new List<PropertyInfo>();

        // Get interface properties
        var interfaceProps = new List<PropertyInfo>();
        foreach (var iface in originalType.GetInterfaces())
        {
            var ifaceProps = iface.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            interfaceProps.AddRange(ifaceProps);
        }

        // Get properties from abstract base classes and original type
        var baseProps = new List<PropertyInfo>();
        var type = originalType;
        while (type != null && type != typeof(object))
        {
            bool isOriginal = type == originalType;
            bool isAbstract = type.IsAbstract;
            bool isConcreteBase = !isOriginal && !isAbstract;

            if (!isConcreteBase || flattenConcreteInheritance)
            {
                var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                baseProps.AddRange(props);
            }
            type = type.BaseType;
        }

        result.AddRange(interfaceProps);
        result.AddRange(baseProps);

        var concreteProps = originalType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        var concretePropNames = new HashSet<string>(concreteProps.Select(p => p.Name));

        result = result.Where(p => !concretePropNames.Contains(p.Name)).ToList();
        result.AddRange(concreteProps);

        return result.ToArray();
    }

    public static bool IsSimpleType(Type type)
    {
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type.IsArray ||
               type == typeof(DateTime) || type == typeof(decimal) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(Guid) ||
               (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                IsSimpleType(type.GetGenericArguments()[0]));
    }

    public static Type? InheritedConcreteClass(Type type)
    {
        var baseType = type.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (!baseType.IsAbstract)
                return baseType;
            baseType = baseType.BaseType;
        }
        return null;
    }

    private static bool IsCollection(Type type)
    {
        return type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
    }

    public static string ToPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (!value.Contains('_'))
        {
            return char.ToUpperInvariant(value[0]) + value[1..];
        }

        string[] parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        TextInfo textInfo = CultureInfo.InvariantCulture.TextInfo;
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = textInfo.ToTitleCase(parts[i].ToLowerInvariant());
        }

        return string.Concat(parts);
    }

    public static string ToSnakeCase(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var result = new StringBuilder();
        result.Append(char.ToLowerInvariant(value[0]));

        for (int i = 1; i < value.Length; i++)
        {
            if (char.IsUpper(value[i]))
            {
                result.Append('_');
                result.Append(char.ToLowerInvariant(value[i]));
            }
            else
            {
                result.Append(value[i]);
            }
        }
        return result.ToString();
    }

    public static string ToCamelCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var pascal = ToPascalCase(value);
        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    public static string ApplyNamingConvention(string value, DbNamingConvention namingConvention)
    {
        return namingConvention switch
        {
            DbNamingConvention.PascalCase => ToPascalCase(value),
            DbNamingConvention.CamelCase => ToCamelCase(value),
            _ => ToSnakeCase(value)
        };
    }

    private static string GetDefaultTableName(Type type, DbNamingConvention namingConvention, bool pluralize)
    {
        var tableName = pluralize ? $"{type.Name}s" : type.Name;
        return ApplyNamingConvention(tableName, namingConvention);
    }

    public static string GetDefaultColumnName(string propertyName, DbNamingConvention namingConvention)
    {
        return ApplyNamingConvention(propertyName, namingConvention);
    }

    public static string GetDefaultColumnName(ClassMetadata metadata, string propertyName)
    {
        return GetDefaultColumnName(propertyName, metadata.NamingConvention);
    }

    private static string GetDefaultForeignKeyColumnName(string propertyName, DbNamingConvention namingConvention)
    {
        return namingConvention switch
        {
            DbNamingConvention.PascalCase => $"{propertyName}Id",
            DbNamingConvention.CamelCase => $"{ToCamelCase(propertyName)}Id",
            _ => $"{ToSnakeCase(propertyName)}_id"
        };
    }

    internal static bool ShouldFilterSoftDeleted(ClassMetadata metadata, SelectQuery? selectQuery)
    {
        return metadata.SupportsSoftDelete &&
               !string.IsNullOrWhiteSpace(metadata.SoftDeleteField) &&
               selectQuery?.IncludeSoftDeleted != true;
    }

    internal static string BuildSoftDeletePredicate(string tableReference, ClassMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.SoftDeleteField))
        {
            throw new InvalidOperationException($"No soft delete field configured for {metadata.Type.Name}.");
        }

        return $"COALESCE({tableReference}.\"{metadata.SoftDeleteField}\", FALSE) = FALSE";
    }


    private static Dictionary<ClassMetadata, List<PropertyMetadata>> LoadAllForeignKeysDict(ClassMetadata? classMetadata, bool includeInheritance = false)
    {
        var result = new Dictionary<ClassMetadata, List<PropertyMetadata>>();
        var current = classMetadata;
        while (current != null)
        {
            if (!result.ContainsKey(current)) result[current] = [];

            if (includeInheritance)
                result[current].AddRange(current.ForeignKeys.Where(x => x.IsLoadable));
            else
                result[current].AddRange(current.ForeignKeys.Where(x => !x.IsInherited && x.IsLoadable));

            if (current.BaseMetaData?.ForeignKeyType != null && current.BaseMetaData?.IsLoadable == true)
            {
                current = GetOrCreateMetadata(current.BaseMetaData.ForeignKeyType);
            }
            else
            {
                current = null;
            }
        }
        return result;
    }

    // OPTIMIZED BULK LOADING - Main improvement here
    public static async Task<IEnumerable<T>> GetAllAsync<T>(
    IEnumerable<object>? ids = null,
    int depth = -1,
    HashSet<Type>? ignoredTypes = null,
    int batchSize = 1000,
    SelectQuery? selectQuery = null) where T : class, new()
    {
        Type type = typeof(T);
        var metadata = GetOrCreateMetadata(type);
        ignoredTypes ??= [];

        if (ignoredTypes.Contains(type))
        {
            depth = 0;
        }
        ignoredTypes.Add(type);

        // Decide who owns the connection and transaction
        var externalConnection = selectQuery?.CurrentConnection != null;
        var externalTransaction = selectQuery?.CurrentTransaction != null;
        var connection = selectQuery?.CurrentConnection ?? DbConnection.UsersConnection;

        try
        {
            if (!externalConnection)
                connection.Open();

            // Handle transaction setup
            if (selectQuery?.UseTransaction == true && !externalTransaction)
            {
                using var transaction = connection.BeginTransaction();
                try
                {
                    // Create a modified query with our transaction for internal use
                    selectQuery.CurrentTransaction = transaction; // ik this is bad practice but its honestly expected by the caller for selectquery to change maybe?

                    var results = await GetAllInternalAsync<T>(
                        connection,
                        metadata,
                        ids,
                        depth,
                        ignoredTypes,
                        batchSize,
                        selectQuery);

                    transaction.Commit();
                    return results;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            else
            {
                // Use existing transaction or no transaction
                return await GetAllInternalAsync<T>(
                    connection,
                    metadata,
                    ids,
                    depth,
                    ignoredTypes,
                    batchSize,
                    selectQuery);
            }
        }
        finally
        {
            // Only dispose if we created the connection
            if (!externalConnection)
                connection.Dispose();
        }
    }

    private static async Task<IEnumerable<T>> GetAllInternalAsync<T>(
        IDbConnection connection,
        ClassMetadata metadata,
        IEnumerable<object>? ids,
        int depth,
        HashSet<Type> ignoredTypes,
        int batchSize,
        SelectQuery? selectQuery) where T : class, new()
    {
        var idList = ids?.ToList();
        var allResults = new List<T>();

        if (idList?.Any() == true)
        {
            for (int i = 0; i < idList.Count; i += batchSize)
            {
                var batch = idList.Skip(i).Take(batchSize).ToList();
                var batchResults = await GetBatchWithJoinsAsync<T>(
                    connection,
                    metadata,
                    batch,
                    depth,
                    ignoredTypes,
                    selectQuery);
                allResults.AddRange(batchResults);
            }
        }
        else
        {
            var batchResults = await GetBatchWithJoinsAsync<T>(
                connection,
                metadata,
                null,
                depth,
                ignoredTypes,
                selectQuery);
            allResults.AddRange(batchResults);
        }

        return allResults;
    }

    // NEW: Optimized batch loading with JOINs instead of N+1 queries
    private static async Task<IEnumerable<T>> GetBatchWithJoinsAsync<T>(IDbConnection connection, ClassMetadata metadata, List<object>? ids, int depth, HashSet<Type> ignoredTypes, SelectQuery? selectQuery) where T : class, new()
    {
        if (depth == 0)
        {
            // Simple load without FKs for performance
            return await GetBatchSimpleAsync<T>(connection, metadata, ids, selectQuery);
        }

        // Build complex query with all FK joins for efficient loading
        var (sql, splitMapping, _) = BuildOptimizedSelectWithJoins(metadata, depth, ignoredTypes, selectQuery);

        object parameters;
        if(ids?.Any() == true) //TODO: really need to break parameter building into its own function, this is dumb
        {
            if(metadata.PrimaryKey.Property.PropertyType == typeof(string))
            {
                parameters = new { ids = ids.Cast<string>().ToArray() };
            }else if (metadata.PrimaryKey.Property.PropertyType == typeof(int))
            {
                parameters = new { ids = ids.Cast<int>().ToArray() };
            }else if (metadata.PrimaryKey.Property.PropertyType == typeof(Guid))
            {
                parameters = new { ids = ids.Cast<Guid>().ToArray() };
            }
            else if (metadata.PrimaryKey.Property.PropertyType == typeof(long))
            {
                parameters = new { ids = ids.Cast<long>().ToArray() };
            }
            else
            {
                parameters = new { ids = ids.Cast<object>().ToArray() };
            }
        }
        else
        {
            parameters = new { };
        }

        if (ids != null && ids.Count != 0)
        {
            sql = sql.Replace("WHERE 1=1", $"WHERE \"{metadata.TableName}\".\"{metadata.PrimaryKey.ColumnName}\" = ANY(@ids)");

        }

        var context = new SelectContext(connection, selectQuery?.CurrentTransaction, metadata, selectQuery)
        {
            Sql = sql,
            Parameters = parameters
        };
        var cancellationToken = selectQuery?.CancellationToken ?? default;
        await RunBeforeSelectInterceptorsAsync(context, selectQuery?.Interceptors, cancellationToken);

        // Execute with multi-mapping for all joined tables
        var results = (await ExecuteMultiMappingQuery<T>(connection, sql, parameters, splitMapping, metadata, selectQuery?.CurrentTransaction, cancellationToken)).ToList();
        context.Result = results;
        await RunAfterSelectInterceptorsAsync(context, selectQuery?.Interceptors, cancellationToken);

        return results;
    }

    private static async Task<IEnumerable<T>> GetBatchSimpleAsync<T>(IDbConnection connection, ClassMetadata metadata, List<object>? ids, SelectQuery? selectQuery) where T : class, new()
    {
        var columns = metadata.LoadableProperties
            .Where(p => !p.IsForeignKey && IsSimpleType(p.Property.PropertyType))
            .Select(p => $"\"{p.ColumnName}\" as \"{p.Property.Name}\"");

        string sql;
        object parameters;

        if (ids?.Any() == true)
        {
            sql = $"SELECT {string.Join(", ", columns)} FROM \"{metadata.TableName}\" WHERE \"{metadata.PrimaryKey.ColumnName}\" = ANY(@ids)";
            parameters = new { ids = ids.ToArray() };
        }
        else
        {
            sql = $"SELECT {string.Join(", ", columns)} FROM \"{metadata.TableName}\"";
            parameters = new { };
        }
        if (ShouldFilterSoftDeleted(metadata, selectQuery))
        {
            sql += ids?.Any() == true
                ? $" AND {BuildSoftDeletePredicate($"\"{metadata.TableName}\"", metadata)}"
                : $" WHERE {BuildSoftDeletePredicate($"\"{metadata.TableName}\"", metadata)}";
        }
        if(selectQuery != null && selectQuery.LockRow)
        {
            sql = sql + " FOR UPDATE" + (selectQuery.NoWait ? " NOWAIT" : string.Empty);
        }
        var context = new SelectContext(connection, selectQuery?.CurrentTransaction, metadata, selectQuery)
        {
            Sql = sql,
            Parameters = parameters
        };
        var cancellationToken = selectQuery?.CancellationToken ?? default;
        await RunBeforeSelectInterceptorsAsync(context, selectQuery?.Interceptors, cancellationToken);
        var results = (await connection.QueryAsync<T>(new CommandDefinition(sql, parameters, selectQuery?.CurrentTransaction, cancellationToken: cancellationToken))).ToList();
        context.Result = results;
        await RunAfterSelectInterceptorsAsync(context, selectQuery?.Interceptors, cancellationToken);
        return results;
    }

    private static string mapPropertySingle(string fieldName, string fieldType)
    {
        return $"{fieldName}::{fieldType}";
        //return $"CAST(@{fieldName} AS {fieldType})";
    }

    private static string mapProperty(ClassMetadata table, PropertyMetadata prop, string fieldOverride = null)
    {
        string field = fieldOverride ?? prop.Property.Name; //field override will just be for inherited tables on initial gather
        if (prop.CustomField != null)
        {
            field = '@'+mapPropertySingle(field, prop.CustomField);
        }
        return $"\"{table.TableName}\".\"{prop.ColumnName}\" as \"{field}\"";
    }

    // NEW: Build optimized query with strategic JOINs
    private static (string Sql, List<SplitMapping> SplitMapping, List<JoinTableInfo> JoinTables)
BuildOptimizedSelectWithJoins(ClassMetadata metadata, int depth, HashSet<Type> ignoredTypes, SelectQuery? selectQuery)
    {
        var tables = GetInheritanceChain(metadata);
        var joinTables = new List<JoinTableInfo>();
        var splitMappings = new List<SplitMapping>();
        var allColumns = new List<string>();
        var columnNames = new HashSet<string>();
        // 1. Add base entity columns (ONLY simple ones without converters)
        foreach (var table in tables)
        {
            foreach (var prop in table.LoadableProperties.Where(p => IsSimpleType(p.Property.PropertyType) && p.Converter == null))
            {
                if (columnNames.Contains(prop.Property.Name)) continue;
                allColumns.Add(mapProperty(table, prop));
                columnNames.Add(prop.Property.Name);
            }
        }
        // 2. Add properties with Converters (Encryption, JSON, etc) as separate segments
        foreach (var table in tables)
        {
            foreach (var prop in table.LoadableProperties.Where(p => p.Converter != null))
            {
                var splitAlias = $"conv_{prop.Property.Name}";
                allColumns.Add($"\"{table.TableName}\".\"{prop.ColumnName}\" as \"{splitAlias}\"");
                splitMappings.Add(new SplitMapping
                {
                    SplitOn = splitAlias,
                    PropertyName = prop.Property.Name,
                    TargetType = prop.Property.PropertyType,
                    IsConverter = true,
                    Converter = prop.Converter,
                    FieldProperty = prop.Property,
                    NavigationPath = new List<PropertyInfo> { prop.Property }
                });
            }
        }

        // 3. Recurse into foreign keys
        int joinIndex = 0;
        AddForeignKeysRecursive(metadata, ref joinIndex, allColumns, splitMappings, joinTables, ignoredTypes, null, new List<PropertyInfo>());
        var sql = BuildJoinSql(tables, joinTables, allColumns, metadata, selectQuery);
        return (sql, splitMappings, joinTables);
    }


    private static void PopulateSplitData(object rootEntity, dynamic data, SplitMapping mapping)
    {
        if (data == null || data is DBNull) return;

        if (mapping.IsConverter)
        {
            // Execute the specific conversion/decryption logic
            var dict = (IDictionary<string, object>)data;
            var converted = mapping.Converter!.ToProvider(dict[mapping.SplitOn], mapping.TargetType);
            SetNestedProperty(rootEntity, mapping.NavigationPath, converted);
        }
        else
        {
            // Standard Foreign Key mapping
            var fkEntity = CreateForeignKeyEntity((IDictionary<string, object>)data, mapping);
            if (fkEntity != null) SetNestedProperty(rootEntity, mapping.NavigationPath, fkEntity);
        }
    }

    private static void SetNestedProperty(
    object root,
    List<PropertyInfo> path,
    object? value)
    {
        object current = root;

        for (int i = 0; i < path.Count - 1; i++)
        {
            var prop = path[i];
            var del = GetDelegate(prop);

            var next = del.Getter(current);
            if (next == null)
            {
                next = Activator.CreateInstance(prop.PropertyType)!;
                del.Setter(current, next);
            }

            current = next;
        }

        var finalProp = path[^1];
        var finalDel = GetDelegate(finalProp);
        finalDel.Setter(current, value);
    }


    /// <summary>
    /// Handles foreign keys recursively
    /// </summary>
    private static void AddForeignKeysRecursive(ClassMetadata metadata, ref int joinIndex, List<string> allColumns, List<SplitMapping> splitMappings, List<JoinTableInfo> joinTables, HashSet<Type> ignoredTypes, string? parentAlias, List<PropertyInfo> currentPath)
    {
        foreach (var fks in LoadAllForeignKeysDict(metadata))
        {
            foreach (var fk in fks.Value)
            {
                if (!ignoredTypes.Add(fk.ForeignKeyType)) continue;

                var fkMetadata = GetOrCreateMetadata(fk.ForeignKeyType);
                var fkTables = GetInheritanceChain(fkMetadata);
                var fkAlias = $"fk{joinIndex++}";
                var baseTable = parentAlias ?? fks.Key.TableName;

                joinTables.Add(new JoinTableInfo
                {
                    BaseTable = baseTable,
                    JoinTable = fkTables[0].TableName,
                    BaseColumn = fk.ForeignKeyColumn,
                    JoinColumn = fkMetadata.PrimaryKey.ColumnName,
                    Alias = fkAlias,
                    Metadata = fkMetadata,
                    PropertyName = fk.Property.Name
                });

                // Add Standard Columns for the FK
                var fkColumns = new List<string>();
                for (int i = 0; i < fkTables.Count; i++)
                {
                    var fkTable = fkTables[i];
                    // Use base alias for inherited tables (fk6_base1, fk6_base2, etc.)
                    var tableAlias = i == 0 ? fkAlias : $"{fkAlias}_base{i}";

                    foreach (var prop in fkTable.LoadableProperties.Where(p => IsSimpleType(p.Property.PropertyType) && p.Converter == null))
                    {
                        var colAlias = $"{fkAlias}_{prop.Property.Name}";
                        fkColumns.Add($"\"{tableAlias}\".\"{prop.ColumnName}\" as \"{colAlias}\"");
                    }
                }

                allColumns.AddRange(fkColumns);
                var navPath = new List<PropertyInfo>(currentPath) { fk.Property };

                if (fkColumns.Any())
                {
                    splitMappings.Add(new SplitMapping
                    {
                        SplitOn = fkColumns.First().Split(" as ")[1],
                        PropertyName = fk.Property.Name,
                        TargetType = fk.ForeignKeyType,
                        ColumnPrefix = fkAlias,
                        FieldProperty = fk.Property,
                        NavigationPath = navPath
                    });
                }

                // NEW: Handle properties with converters INSIDE the joined table
                for (int i = 0; i < fkTables.Count; i++)
                {
                    var fkTable = fkTables[i];
                    var tableAlias = i == 0 ? fkAlias : $"{fkAlias}_base{i}";

                    foreach (var prop in fkTable.LoadableProperties.Where(p => p.Converter != null))
                    {
                        var convAlias = $"conv_{fkAlias}_{prop.Property.Name}";
                        allColumns.Add($"\"{tableAlias}\".\"{prop.ColumnName}\" as \"{convAlias}\"");

                        splitMappings.Add(new SplitMapping
                        {
                            SplitOn = convAlias,
                            PropertyName = prop.Property.Name,
                            TargetType = prop.Property.PropertyType,
                            IsConverter = true,
                            Converter = prop.Converter,
                            FieldProperty = prop.Property,
                            NavigationPath = new List<PropertyInfo>(navPath) { prop.Property }
                        });
                    }
                }

                AddForeignKeysRecursive(fkMetadata, ref joinIndex, allColumns, splitMappings, joinTables, ignoredTypes, fkAlias, navPath);
                ignoredTypes.Remove(fk.ForeignKeyType);
            }
        }
    }



    private static string BuildJoinSql(List<ClassMetadata> tables, List<JoinTableInfo> joinTables, List<string> allColumns, ClassMetadata metadata, SelectQuery? selectQuery)
    {
        var sql = new StringBuilder();
        sql.AppendLine($"SELECT {string.Join(", ", allColumns)}");
        sql.AppendLine($"FROM \"{tables[0].TableName}\"");

        // Add inheritance joins
        for (int i = 1; i < tables.Count; i++)
        {
            sql.AppendLine($"INNER JOIN \"{tables[i].TableName}\" ON \"{tables[i].TableName}\".\"{tables[i].PrimaryKey.ColumnName}\" = \"{tables[i - 1].TableName}\".\"{tables[i - 1].BaseMetaData.ColumnName}\"");
        }

        // Add FK joins
        foreach (var join in joinTables)
        {
            var joinPredicate = $"\"{join.BaseTable}\".\"{join.BaseColumn}\" = \"{join.Alias}\".\"{join.JoinColumn}\"";
            if (ShouldFilterSoftDeleted(join.Metadata, selectQuery))
            {
                joinPredicate += $" AND {BuildSoftDeletePredicate($"\"{join.Alias}\"", join.Metadata)}";
            }

            sql.AppendLine($"LEFT JOIN \"{join.JoinTable}\" \"{join.Alias}\" ON {joinPredicate}");

            // Add inheritance joins for FK tables
            var fkTables = GetInheritanceChain(join.Metadata);
            for (int i = 1; i < fkTables.Count; i++)
            {
                var parentAlias = i == 1 ? join.Alias : $"{join.Alias}_base{i - 1}";
                var currentAlias = $"{join.Alias}_base{i}";
                sql.AppendLine($"LEFT JOIN \"{fkTables[i].TableName}\" \"{currentAlias}\" ON \"{currentAlias}\".\"{fkTables[i].PrimaryKey.ColumnName}\" = \"{parentAlias}\".\"{fkTables[i - 1].BaseMetaData.ColumnName}\"");
            }
        }

        sql.AppendLine("WHERE 1=1"); // Placeholder for dynamic WHERE clauses
        if (ShouldFilterSoftDeleted(metadata, selectQuery))
        {
            sql.AppendLine($"AND {BuildSoftDeletePredicate($"\"{tables[0].TableName}\"", metadata)}");
        }
        if(selectQuery != null && selectQuery.LockRow)
        {
            var lockTarget = $"\"{tables[0].TableName}\"";
            sql.AppendLine($"FOR UPDATE OF {lockTarget}" + (selectQuery.NoWait ? " NOWAIT" : string.Empty));
        }
        return sql.ToString();
    }

    // NEW: Execute multi-mapping query efficiently
    public static async Task<IEnumerable<T>> ExecuteMultiMappingQuery<T>(IDbConnection connection, string sql, object parameters, List<SplitMapping> splitMappings, ClassMetadata metadata, IDbTransaction? transaction = null, CancellationToken cancellationToken = default) where T : class, new()
    {
        //if (!splitMappings.Any()) return await connection.QueryAsync<T>(sql, parameters);

        var splitOn = string.Join(",", splitMappings.Select(s => s.SplitOn.Trim('\"')));

        // Map using Dapper generics (up to 7 types)
        return await MapWithManyFks<T>(connection, sql, parameters, splitOn, splitMappings, transaction, cancellationToken); // Fallback for many splits
        //return splitMappings.Count switch
        //{
        //    1 => await connection.QueryAsync<T, dynamic, T>(sql, (e, d1) => { PopulateSplitData(e, d1, splitMappings[0]); return e; }, parameters, splitOn: splitOn),
        //    2 => await connection.QueryAsync<T, dynamic, dynamic, T>(sql, (e, d1, d2) => { PopulateSplitData(e, d1, splitMappings[0]); PopulateSplitData(e, d2, splitMappings[1]); return e; }, parameters, splitOn: splitOn),
        //    3 => await connection.QueryAsync<T, dynamic, dynamic, dynamic, T>(sql, (e, d1, d2, d3) => { PopulateSplitData(e, d1, splitMappings[0]); PopulateSplitData(e, d2, splitMappings[1]); PopulateSplitData(e, d3, splitMappings[2]); return e; }, parameters, splitOn: splitOn),
        //    _ => await MapWithManyFks<T>(connection, sql, parameters, splitOn, splitMappings) // Fallback for many splits
        //};
    }

    private static async Task<IEnumerable<T>> MapWithOneFk<T>(IDbConnection connection, string sql, object parameters, string splitOn, SplitMapping fk1) where T : class, new()
    {
        return await connection.QueryAsync<T, dynamic, T>(
            sql,
            (entity, fk1Data) =>
            {
                MapForeignKeyData(entity, fk1Data, fk1);
                return entity;
            },
            parameters,
            splitOn: splitOn
        );
    }

    private static async Task<IEnumerable<T>> MapWithTwoFks<T>(IDbConnection connection, string sql, object parameters, string splitOn, List<SplitMapping> mappings) where T : class, new()
    {
        return await connection.QueryAsync<T, dynamic, dynamic, T>(
            sql,
            (entity, fk1Data, fk2Data) =>
            {
                MapForeignKeyData(entity, fk1Data, mappings[0]);
                MapForeignKeyData(entity, fk2Data, mappings[1]);
                return entity;
            },
            parameters,
            splitOn: splitOn
        );
    }

    private static async Task<IEnumerable<T>> MapWithThreeFks<T>(IDbConnection connection, string sql, object parameters, string splitOn, List<SplitMapping> mappings) where T : class, new()
    {
        return await connection.QueryAsync<T, dynamic, dynamic, dynamic, T>(
            sql,
            (entity, fk1Data, fk2Data, fk3Data) =>
            {
                MapForeignKeyData(entity, fk1Data, mappings[0]);
                MapForeignKeyData(entity, fk2Data, mappings[1]);
                MapForeignKeyData(entity, fk3Data, mappings[2]);
                return entity;
            },
            parameters,
            splitOn: splitOn
        );
    }

    private static async Task<IEnumerable<T>> MapWithFourFks<T>(IDbConnection connection, string sql, object parameters, string splitOn, List<SplitMapping> mappings) where T : class, new()
    {
        return await connection.QueryAsync<T, dynamic, dynamic, dynamic, dynamic, T>(
            sql,
            (entity, fk1Data, fk2Data, fk3Data, fk4Data) =>
            {
                MapForeignKeyData(entity, fk1Data, mappings[0]);
                MapForeignKeyData(entity, fk2Data, mappings[1]);
                MapForeignKeyData(entity, fk3Data, mappings[2]);
                MapForeignKeyData(entity, fk4Data, mappings[3]);
                return entity;
            },
            parameters,
            splitOn: splitOn
        );
    }

    private static List<PropertyMetadata> GetLoadableFull(ClassMetadata metadata)
    {
        List<PropertyMetadata> data = new List<PropertyMetadata>(metadata.LoadableProperties ?? []);
        while(metadata.BaseMetaData != null && metadata.BaseMetaData.ForeignKeyType != null)
        {
            metadata = GetOrCreateMetadata(metadata.BaseMetaData.ForeignKeyType);
            data.AddRange(metadata.LoadableProperties);
        }
        return data;
    }

    private static async Task<IEnumerable<T>> MapWithManyFks<T>(
    IDbConnection connection,
    string sql,
    object parameters,
    string splitOn,
    List<SplitMapping> mappings,
    IDbTransaction? transaction = null,
    CancellationToken cancellationToken = default
) where T : class, new()
    {
        var results = await connection.QueryAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
        var entities = new List<T>();

        var metadata = GetOrCreateMetadata(typeof(T));
        var loadableProperties = GetLoadableFull(metadata)
            .Where(p => IsSimpleType(p.Property.PropertyType))
            .ToList();

        foreach (var row in results)
        {
            var entity = new T();
            var dict = (IDictionary<string, object>)row;

            // -----------------------------
            // Map scalar/simple properties
            // -----------------------------
            foreach (var propMeta in loadableProperties)
            {
                if (!dict.TryGetValue(propMeta.Property.Name, out var value) ||
                    value is null ||
                    value is DBNull)
                    continue;

                var del = GetDelegate(propMeta.Property);

                object? finalValue;

                if (propMeta.Converter != null)
                {
                    finalValue = propMeta.Converter.ToProvider(value, propMeta.TargetType);
                }
                else if (del.TargetType.IsAssignableFrom(value.GetType()))
                {
                    finalValue = value;
                }
                else
                {
                    finalValue = Convert.ChangeType(value, del.TargetType);
                }

                del.Setter(entity, finalValue);
            }

            // -----------------------------
            // Map FK / conversion mappings
            // -----------------------------
            foreach (var mapping in mappings)
            {
                if (mapping.IsConverter)
                {
                    var raw = dict[mapping.SplitOn];
                    if (raw is null || raw is DBNull) continue;

                    var converted = mapping.Converter!.ToProvider(raw, mapping.TargetType);
                    SetNestedProperty(entity, mapping.NavigationPath, converted);
                }
                else
                {
                    MapForeignKeyData(entity, dict, mapping);
                }
            }

            entities.Add(entity);
        }

        return entities;
    }


    private static void MapForeignKeyData(
    object entity,
    IDictionary<string, object> fkData,
    SplitMapping mapping)
    {
        if (fkData == null) return;

        var fkEntity = CreateForeignKeyEntity(fkData, mapping);
        if (fkEntity == null) return;

        object current = entity;

        for (int i = 0; i < mapping.NavigationPath.Count - 1; i++)
        {
            var prop = mapping.NavigationPath[i];
            var del = GetDelegate(prop);

            var next = del.Getter(current);
            if (next == null)
            {
                next = Activator.CreateInstance(prop.PropertyType)!;
                del.Setter(current, next);
            }

            current = next;
        }

        var finalProp = mapping.NavigationPath[^1];
        GetDelegate(finalProp).Setter(current, fkEntity);
    }


    public static List<PropertyMetadata> LoadAllForeignKeys(ClassMetadata? classMetadata, bool includeInheritance = false)
    {
        var result = new List<PropertyMetadata>();
        var current = classMetadata;
        while (current != null)
        {
            if (includeInheritance)
                result.AddRange(current.ForeignKeys.Where(x => x.IsLoadable));
            else
                result.AddRange(current.ForeignKeys.Where(x => !x.IsInherited && x.IsLoadable));

            if (current.BaseMetaData?.ForeignKeyType != null && current.BaseMetaData?.IsLoadable == true)
            {
                current = GetOrCreateMetadata(current.BaseMetaData.ForeignKeyType);
            }
            else
            {
                current = null;
            }
        }
        return result;
    }

    private static object CreateForeignKeyEntity(IDictionary<string, object> data, SplitMapping mapping)
    {
        var fkMetadata = GetOrCreateMetadata(mapping.TargetType);
        var fkEntity = Activator.CreateInstance(mapping.TargetType);
        bool hasValidData = false;

        // Get the full inheritance chain for this FK type
        var fkTables = GetInheritanceChain(fkMetadata);

        // Process all properties across the inheritance chain
        foreach (var fkTable in fkTables)
        {
            foreach (var propertyMeta in fkTable.LoadableProperties)
            {
                var columnKey = $"{mapping.ColumnPrefix}_{propertyMeta.Property.Name}";

                if (!data.TryGetValue(columnKey, out var rawValue) || rawValue == null || rawValue == DBNull.Value)
                    continue;

                try
                {
                    object convertedValue;

                    // Check if property has a converter (encryption, custom types, etc.)
                    if (propertyMeta.Converter != null)
                    {
                        convertedValue = propertyMeta.Converter.FromProvider(rawValue);
                    }
                    else if (IsSimpleType(propertyMeta.Property.PropertyType))
                    {
                        // Standard type conversion
                        var targetType = propertyMeta.Property.PropertyType;

                        // Handle Nullable<T> by unwrapping to the underlying type
                        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
                        {
                            targetType = Nullable.GetUnderlyingType(targetType);
                        }

                        convertedValue = Convert.ChangeType(rawValue, targetType);
                    }
                    else
                    {
                        // Skip non-simple types without converters (shouldn't happen in this context)
                        continue;
                    }

                    propertyMeta.Property.SetValue(fkEntity, convertedValue);
                    hasValidData = true;
                }
                catch
                {
                    // Log or handle conversion errors if needed
                }
            }
        }

        return hasValidData ? fkEntity : null;
    }


    private static List<ClassMetadata> GetInheritanceChain(ClassMetadata metadata)
    {
        var tables = new List<ClassMetadata> { metadata };
        var current = metadata;

        while (current.BaseMetaData?.ForeignKeyType != null)
        {
            current = GetOrCreateMetadata(current.BaseMetaData.ForeignKeyType);
            tables.Add(current);
        }

        return tables;
    }

    // Single entity loading (optimized)
    public static async Task<T?> GetByIdAsync<T>(object? id, int depth = -1, HashSet<Type>? ignoredTypes = null, SelectQuery? selectQuery = null) where T : class, new()
    {
        if (id == null) return null;
        var results = await GetAllAsync<T>(new[] { id }, depth, ignoredTypes, 1, selectQuery);
        return results.FirstOrDefault();
    }

    // Non-generic overloads
    public static async Task<object?> GetByIdAsync(Type type, object id, int depth = -1, HashSet<Type>? ignoredTypes = null, SelectQuery? selectQuery = null)
    {
        var method = typeof(OrmMapper).GetMethod(nameof(GetByIdAsync), new[] { typeof(object), typeof(int), typeof(HashSet<Type>), typeof(SelectQuery) });
        var genericMethod = method.MakeGenericMethod(type);
        var task = (Task)genericMethod.Invoke(null, new object[] { id, depth, ignoredTypes, selectQuery });
        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        return resultProperty.GetValue(task);
    }

    public static async Task<IEnumerable> GetAllAsync(Type type, IEnumerable<object>? ids = null, int depth = -1, HashSet<Type>? ignoredTypes = null, int batchSize = 1000, SelectQuery? selectQuery = null)
    {
        var method = typeof(OrmMapper).GetMethod(nameof(GetAllAsync), new[] { typeof(IEnumerable<object>), typeof(int), typeof(HashSet<Type>), typeof(int), typeof(SelectQuery) });
        var genericMethod = method.MakeGenericMethod(type);
        var task = (Task)genericMethod.Invoke(null, new object[] { ids, depth, ignoredTypes, batchSize, selectQuery });
        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result");
        return (IEnumerable)resultProperty.GetValue(task);
    }

    // Helper extension method
    public async static Task<IEnumerable<T>> ExecuteRawQuery<T>(string sql, object? parameters = null, IDbConnection? conn = null, IDbTransaction? tran = null)
    {
        var connection = conn ?? DbConnection.UsersConnection;
        return await connection.QueryAsync<T>(sql, parameters, tran);
    
    }

    public async static Task<T?> ExecuteRawScalar<T>(string sql, object? parameters = null, IDbConnection? conn = null, IDbTransaction? tran = null)
    {
        var connection = conn ?? DbConnection.UsersConnection;
        return await connection.ExecuteScalarAsync<T>(sql, parameters, tran);
    }

    public async static Task<int> Execute(string sql, object? parameters = null, IDbConnection? conn = null, IDbTransaction? tran = null)
    {
        var connection = conn ?? DbConnection.UsersConnection;
        return await connection.ExecuteAsync(sql, parameters, tran);
    }

    public async static Task<int> Execute<T>(string sql, object? parameters = null, IDbConnection? conn = null, IDbTransaction? tran = null)
    {
        var connection = conn ?? DbConnection.UsersConnection;
        return await connection.ExecuteAsync(sql, parameters, tran);
    }

    public async static Task<T> QuerySingle<T>(string sql, object? parameters = null, IDbConnection? conn = null, IDbTransaction? tran = null)
    {
        var connection = conn ?? DbConnection.UsersConnection;
        return await connection.QuerySingleAsync<T>(sql, parameters, tran);
    }

    // Helper classes for optimized loading
    public class JoinTableInfo
    {
        public string BaseTable { get; set; }
        public string JoinTable { get; set; }
        public string BaseColumn { get; set; }
        public string JoinColumn { get; set; }
        public string Alias { get; set; }
        public ClassMetadata Metadata { get; set; }
        public string PropertyName { get; set; }
        public List<ClassMetadata> FkTables { get; set; } = new();
    }

    public class SplitMapping
    {
        public string SplitOn { get; set; }
        public string PropertyName { get; set; }
        public Type TargetType { get; set; }
        public string ColumnPrefix { get; set; }
        public PropertyInfo FieldProperty { get; set; }
        public List<PropertyInfo> NavigationPath { get; set; } = new();
        public SplitMapping? Parent { get; set; }

        // NEW: Support for Converters/Encryption
        public bool IsConverter { get; set; }
        public IFieldConverter? Converter { get; set; }
    }

}
