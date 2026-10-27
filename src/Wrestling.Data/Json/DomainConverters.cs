using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Wrestling.Core.Calendar;
using Wrestling.Core.Wrestlers;

namespace Wrestling.Data.Json
{
    /// <summary>Reads and writes <see cref="GameDate"/> as an ISO "yyyy-MM-dd" string.</summary>
    public sealed class GameDateJsonConverter : JsonConverter<GameDate>
    {
        public override GameDate ReadJson(JsonReader reader, Type objectType, GameDate existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.String)
            {
                throw new JsonSerializationException($"Expected a yyyy-MM-dd date string at '{reader.Path}'.");
            }

            try
            {
                return GameDate.Parse((string)reader.Value!);
            }
            catch (Exception exception) when (exception is FormatException || exception is ArgumentOutOfRangeException)
            {
                throw new JsonSerializationException($"Invalid date at '{reader.Path}': {exception.Message}", exception);
            }
        }

        public override void WriteJson(JsonWriter writer, GameDate value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString());
        }
    }

    /// <summary>
    /// Reads and writes an <see cref="AttributeSet"/> as an object with one camelCase property per
    /// attribute. Every attribute is required and unknown names are rejected.
    /// </summary>
    public sealed class AttributeSetJsonConverter : JsonConverter<AttributeSet>
    {
        private static readonly NamingStrategy Naming = new CamelCaseNamingStrategy();
        private static readonly Dictionary<string, WrestlerAttribute> ByName = BuildNames();

        public override AttributeSet ReadJson(JsonReader reader, Type objectType, AttributeSet? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.StartObject)
            {
                throw new JsonSerializationException($"Expected an attribute object at '{reader.Path}'.");
            }

            string path = reader.Path;
            var values = new Dictionary<WrestlerAttribute, int>();
            while (reader.Read() && reader.TokenType != JsonToken.EndObject)
            {
                if (reader.TokenType != JsonToken.PropertyName)
                {
                    throw new JsonSerializationException($"Unexpected token {reader.TokenType} at '{reader.Path}'.");
                }

                string name = (string)reader.Value!;
                if (!ByName.TryGetValue(name, out WrestlerAttribute attribute))
                {
                    throw new JsonSerializationException($"Unknown attribute '{name}' at '{reader.Path}'.");
                }

                if (values.ContainsKey(attribute))
                {
                    throw new JsonSerializationException($"Attribute '{name}' appears twice at '{path}'.");
                }

                if (!reader.Read() || reader.TokenType != JsonToken.Integer)
                {
                    throw new JsonSerializationException($"Attribute '{name}' must be an integer at '{reader.Path}'.");
                }

                long value = (long)reader.Value!;
                if (value < WrestlerAttributes.Minimum || value > WrestlerAttributes.Maximum)
                {
                    throw new JsonSerializationException($"Attribute '{name}' is {value}; expected {WrestlerAttributes.Minimum}..{WrestlerAttributes.Maximum} at '{reader.Path}'.");
                }

                values[attribute] = (int)value;
            }

            foreach (WrestlerAttribute attribute in WrestlerAttributes.All)
            {
                if (!values.ContainsKey(attribute))
                {
                    throw new JsonSerializationException($"Attribute '{NameOf(attribute)}' is missing at '{path}'.");
                }
            }

            return new AttributeSet(values);
        }

        public override void WriteJson(JsonWriter writer, AttributeSet? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            writer.WriteStartObject();
            foreach (WrestlerAttribute attribute in WrestlerAttributes.All)
            {
                writer.WritePropertyName(NameOf(attribute));
                writer.WriteValue(value[attribute]);
            }

            writer.WriteEndObject();
        }

        public static string NameOf(WrestlerAttribute attribute) => Naming.GetPropertyName(attribute.ToString(), false);

        private static Dictionary<string, WrestlerAttribute> BuildNames()
        {
            var names = new Dictionary<string, WrestlerAttribute>(StringComparer.Ordinal);
            foreach (WrestlerAttribute attribute in WrestlerAttributes.All)
            {
                names[NameOf(attribute)] = attribute;
            }

            return names;
        }
    }
}
