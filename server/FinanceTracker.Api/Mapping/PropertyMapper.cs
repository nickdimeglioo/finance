using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace FinanceTracker.Api.Mapping;

[AttributeUsage(AttributeTargets.Property)]
public sealed class MapFromAttribute : Attribute
{
    public MapFromAttribute(string propertyName)
    {
        PropertyName = propertyName;
    }

    public string PropertyName { get; }
}

public sealed class MappingOptions
{
    public int MaxDepth { get; set; } = int.MaxValue;
    public int CurrentDepth { get; internal set; }
}

public static class PropertyMapper<TSource, TTarget>
    where TTarget : new()
{
    private static readonly ConcurrentDictionary<string, PropertyMappingInfo[]> PropertyMappingCache = new();
    private static readonly ConcurrentDictionary<Type, bool> SimpleTypeCache = new();
    private static readonly ConcurrentDictionary<Type, bool> CollectionTypeCache = new();

    public static TTarget? Map(TSource source, MappingOptions? options = null)
    {
        if (source is null)
        {
            return default;
        }

        options ??= new MappingOptions();
        if (options.CurrentDepth >= options.MaxDepth)
        {
            return default;
        }

        var target = new TTarget();
        MapProperties(source, target, options);
        return target;
    }

    public static List<TTarget> MapList(IEnumerable<TSource> sourceList, MappingOptions? options = null)
    {
        return sourceList.Select(item => Map(item, options)!).ToList();
    }

    public static TTarget[] MapArray(TSource[] sourceArray, MappingOptions? options = null)
    {
        return sourceArray.Select(item => Map(item, options)!).ToArray();
    }

    private static void MapProperties(TSource source, TTarget target, MappingOptions options)
    {
        foreach (var mapping in GetPropertyMappings())
        {
            try
            {
                var sourceValue = mapping.SourceProperty.GetValue(source);
                if (sourceValue is null)
                {
                    mapping.TargetProperty.SetValue(target, null);
                    continue;
                }

                if (mapping.IsCollection)
                {
                    mapping.TargetProperty.SetValue(target, MapCollection(sourceValue, mapping, options));
                }
                else if (mapping.IsComplex)
                {
                    var nextOptions = new MappingOptions
                    {
                        MaxDepth = options.MaxDepth,
                        CurrentDepth = options.CurrentDepth + 1
                    };
                    mapping.TargetProperty.SetValue(target, MapComplexObject(sourceValue, mapping.TargetProperty.PropertyType, nextOptions));
                }
                else
                {
                    mapping.TargetProperty.SetValue(target, sourceValue);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to map property '{mapping.SourceProperty.Name}' to '{mapping.TargetProperty.Name}': {ex.Message}", ex);
            }
        }
    }

    private static object? MapCollection(object sourceCollection, PropertyMappingInfo mapping, MappingOptions options)
    {
        if (sourceCollection is not IEnumerable enumerable)
        {
            return null;
        }

        var sourceList = enumerable.Cast<object?>().ToList();
        if (sourceList.Count == 0)
        {
            return CreateEmptyCollection(mapping.TargetProperty.PropertyType, mapping.TargetElementType);
        }

        var targetList = new List<object?>();
        var nextOptions = new MappingOptions
        {
            MaxDepth = options.MaxDepth,
            CurrentDepth = options.CurrentDepth + 1
        };

        foreach (var item in sourceList)
        {
            targetList.Add(item is null || IsSimpleType(mapping.TargetElementType)
                ? item
                : MapComplexObject(item, mapping.TargetElementType, nextOptions));
        }

        return ConvertToTargetCollectionType(targetList, mapping.TargetProperty.PropertyType, mapping.TargetElementType);
    }

    private static object? MapComplexObject(object source, Type targetType, MappingOptions options)
    {
        var mapperType = typeof(PropertyMapper<,>).MakeGenericType(source.GetType(), targetType);
        var mapMethod = mapperType.GetMethod(nameof(Map), BindingFlags.Public | BindingFlags.Static);
        return mapMethod?.Invoke(null, [source, options]);
    }

    private static object? CreateEmptyCollection(Type collectionType, Type elementType)
    {
        if (collectionType.IsArray)
        {
            return Array.CreateInstance(elementType, 0);
        }

        if (collectionType.IsGenericType)
        {
            var genericTypeDef = collectionType.GetGenericTypeDefinition();
            if (genericTypeDef == typeof(List<>) || genericTypeDef == typeof(IList<>) || genericTypeDef == typeof(ICollection<>) || genericTypeDef == typeof(IEnumerable<>))
            {
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
            }
        }

        return Activator.CreateInstance(collectionType);
    }

    private static object? ConvertToTargetCollectionType(List<object?> sourceList, Type targetCollectionType, Type elementType)
    {
        if (targetCollectionType.IsArray)
        {
            var array = Array.CreateInstance(elementType, sourceList.Count);
            for (var i = 0; i < sourceList.Count; i++)
            {
                array.SetValue(sourceList[i], i);
            }

            return array;
        }

        if (targetCollectionType.IsGenericType)
        {
            var genericTypeDef = targetCollectionType.GetGenericTypeDefinition();
            if (genericTypeDef == typeof(List<>) || genericTypeDef == typeof(IList<>) || genericTypeDef == typeof(ICollection<>) || genericTypeDef == typeof(IEnumerable<>))
            {
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
                foreach (var item in sourceList)
                {
                    list.Add(item);
                }

                return list;
            }
        }

        var collection = Activator.CreateInstance(targetCollectionType);
        if (collection is IList targetList)
        {
            foreach (var item in sourceList)
            {
                targetList.Add(item);
            }
        }

        return collection;
    }

    private static PropertyMappingInfo[] GetPropertyMappings()
    {
        var cacheKey = $"{typeof(TSource).FullName}->{typeof(TTarget).FullName}";
        return PropertyMappingCache.GetOrAdd(cacheKey, _ =>
        {
            var sourceProperties = GetAllProperties(typeof(TSource))
                .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var mappings = new List<PropertyMappingInfo>();

            foreach (var targetProp in GetAllProperties(typeof(TTarget)))
            {
                if (!targetProp.CanWrite)
                {
                    continue;
                }

                var mapFrom = targetProp.GetCustomAttribute<MapFromAttribute>();
                var sourceName = mapFrom?.PropertyName ?? targetProp.Name;
                if (!sourceProperties.TryGetValue(sourceName, out var sourceProp) || !sourceProp.CanRead)
                {
                    continue;
                }

                var isCollection = IsCollectionType(sourceProp.PropertyType) && IsCollectionType(targetProp.PropertyType);
                var mapping = new PropertyMappingInfo
                {
                    SourceProperty = sourceProp,
                    TargetProperty = targetProp,
                    IsCollection = isCollection,
                    IsComplex = !IsSimpleType(targetProp.PropertyType) && !IsCollectionType(targetProp.PropertyType),
                    SourceElementType = isCollection ? GetElementType(sourceProp.PropertyType) : typeof(object),
                    TargetElementType = isCollection ? GetElementType(targetProp.PropertyType) : typeof(object)
                };

                mappings.Add(mapping);
            }

            return mappings.ToArray();
        });
    }

    private static IEnumerable<PropertyInfo> GetAllProperties(Type type)
    {
        var currentType = type;
        while (currentType is not null && currentType != typeof(object))
        {
            foreach (var property in currentType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                yield return property;
            }

            currentType = currentType.BaseType;
        }
    }

    private static bool IsSimpleType(Type type)
    {
        return SimpleTypeCache.GetOrAdd(type, static t =>
        {
            var underlyingType = Nullable.GetUnderlyingType(t) ?? t;
            return underlyingType.IsPrimitive
                || underlyingType == typeof(string)
                || underlyingType == typeof(decimal)
                || underlyingType == typeof(DateTime)
                || underlyingType == typeof(DateTimeOffset)
                || underlyingType == typeof(DateOnly)
                || underlyingType == typeof(TimeOnly)
                || underlyingType == typeof(TimeSpan)
                || underlyingType == typeof(Guid)
                || underlyingType.IsEnum;
        });
    }

    private static bool IsCollectionType(Type type)
    {
        return CollectionTypeCache.GetOrAdd(type, static t => t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t));
    }

    private static Type GetElementType(Type collectionType)
    {
        if (collectionType.IsArray)
        {
            return collectionType.GetElementType() ?? typeof(object);
        }

        return collectionType.IsGenericType ? collectionType.GetGenericArguments().FirstOrDefault() ?? typeof(object) : typeof(object);
    }

    private readonly struct PropertyMappingInfo
    {
        public PropertyInfo SourceProperty { get; init; }
        public PropertyInfo TargetProperty { get; init; }
        public bool IsCollection { get; init; }
        public bool IsComplex { get; init; }
        public Type SourceElementType { get; init; }
        public Type TargetElementType { get; init; }
    }
}

public static class DtoMapperExtensions
{
    public static TTarget MapTo<TSource, TTarget>(this TSource source, MappingOptions? options = null)
        where TTarget : new()
    {
        return PropertyMapper<TSource, TTarget>.Map(source, options)!;
    }

    public static List<TTarget> MapToList<TSource, TTarget>(this IEnumerable<TSource> source, MappingOptions? options = null)
        where TTarget : new()
    {
        return PropertyMapper<TSource, TTarget>.MapList(source, options);
    }

    public static TTarget[] MapToArray<TSource, TTarget>(this TSource[] source, MappingOptions? options = null)
        where TTarget : new()
    {
        return PropertyMapper<TSource, TTarget>.MapArray(source, options);
    }
}
