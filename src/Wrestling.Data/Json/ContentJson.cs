using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Wrestling.Core.Numerics;

namespace Wrestling.Data.Json
{
    /// <summary>
    /// Strict JSON settings for content files: camelCase names, every property required,
    /// unknown properties rejected, and numbers read as decimals so fixed-point values are exact.
    /// </summary>
    public static class ContentJson
    {
        public static JsonSerializerSettings CreateSettings()
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new StrictContractResolver(),
                MissingMemberHandling = MissingMemberHandling.Error,
                FloatParseHandling = FloatParseHandling.Decimal,
                DateParseHandling = DateParseHandling.None,
                NullValueHandling = NullValueHandling.Include,
                // Always build new objects: populating a property's default instance would write into
                // shared defaults (static "empty" values) and leak one file's content into another.
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                Formatting = Formatting.Indented,
            };
            settings.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy(), allowIntegerValues: false));
            settings.Converters.Add(new Fix64JsonConverter());
            settings.Converters.Add(new GameDateJsonConverter());
            settings.Converters.Add(new AttributeSetJsonConverter());
            return settings;
        }

        /// <summary>Deserializes <paramref name="json"/>; errors name the source and the JSON path.</summary>
        public static T Deserialize<T>(string json, string sourceName)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            try
            {
                var serializer = JsonSerializer.Create(CreateSettings());
                using var reader = new JsonTextReader(new StringReader(json));
                T? result = serializer.Deserialize<T>(reader);
                if (result == null)
                {
                    throw new ContentLoadException(sourceName, "File contains no content.");
                }

                if (reader.Read() && reader.TokenType != JsonToken.Comment)
                {
                    throw new ContentLoadException(sourceName, "Unexpected content after the end of the root object.");
                }

                return result;
            }
            catch (JsonException exception)
            {
                throw new ContentLoadException(sourceName, exception.Message, exception);
            }
        }

        public static string Serialize<T>(T value) => JsonConvert.SerializeObject(value, CreateSettings());

        /// <summary>Marks every property as required; nullable value types and nullable-annotated references must be present but may be null.</summary>
        private sealed class StrictContractResolver : CamelCasePropertyNamesContractResolver
        {
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                JsonProperty property = base.CreateProperty(member, memberSerialization);
                if (!property.Writable)
                {
                    // Computed, read-only properties are not part of the content format.
                    property.Ignored = true;
                    return property;
                }

                bool nullableValueType = property.PropertyType != null && Nullable.GetUnderlyingType(property.PropertyType) != null;
                bool nullableReference = property.PropertyType != null && !property.PropertyType.IsValueType && IsNullableReference(member);
                property.Required = nullableValueType || nullableReference ? Required.AllowNull : Required.Always;
                return property;
            }

            /// <summary>
            /// Reads the compiler's nullable annotation (C# 8+): a <c>string?</c> property carries
            /// NullableAttribute with first flag 2, or inherits flag 2 from its type's NullableContextAttribute.
            /// </summary>
            private static bool IsNullableReference(MemberInfo member)
            {
                byte? own = Flag(member.CustomAttributes, "System.Runtime.CompilerServices.NullableAttribute");
                if (own.HasValue)
                {
                    return own.Value == 2;
                }

                for (Type? type = member.DeclaringType; type != null; type = type.DeclaringType)
                {
                    byte? context = Flag(type.CustomAttributes, "System.Runtime.CompilerServices.NullableContextAttribute");
                    if (context.HasValue)
                    {
                        return context.Value == 2;
                    }
                }

                return false;
            }

            private static byte? Flag(IEnumerable<CustomAttributeData> attributes, string name)
            {
                foreach (CustomAttributeData attribute in attributes)
                {
                    if (attribute.AttributeType.FullName != name || attribute.ConstructorArguments.Count != 1)
                    {
                        continue;
                    }

                    object? value = attribute.ConstructorArguments[0].Value;
                    if (value is byte single)
                    {
                        return single;
                    }

                    if (value is IReadOnlyCollection<CustomAttributeTypedArgument> flags && flags.Count > 0)
                    {
                        return (byte)flags.First().Value!;
                    }
                }

                return null;
            }
        }
    }

    /// <summary>Reads and writes <see cref="Fix64"/> as a plain JSON number, exactly.</summary>
    public sealed class Fix64JsonConverter : JsonConverter<Fix64>
    {
        public override Fix64 ReadJson(JsonReader reader, Type objectType, Fix64 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Integer:
                case JsonToken.Float:
                    return Fix64.FromDecimal(Convert.ToDecimal(reader.Value, System.Globalization.CultureInfo.InvariantCulture));
                default:
                    throw new JsonSerializationException($"Expected a number at '{reader.Path}', found {reader.TokenType}.");
            }
        }

        public override void WriteJson(JsonWriter writer, Fix64 value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToDecimal());
        }
    }

    /// <summary>A content file could not be read or failed validation.</summary>
    public sealed class ContentLoadException : Exception
    {
        public ContentLoadException(string sourceName, string message, Exception? innerException = null)
            : base($"{sourceName}: {message}", innerException)
        {
            SourceName = sourceName;
        }

        public string SourceName { get; }
    }
}
