using System;
using System.IO;
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
                Formatting = Formatting.Indented,
            };
            settings.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy(), allowIntegerValues: false));
            settings.Converters.Add(new Fix64JsonConverter());
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

        /// <summary>Marks every property as required; nullable value types must be present but may be null.</summary>
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
                property.Required = nullableValueType ? Required.AllowNull : Required.Always;
                return property;
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
