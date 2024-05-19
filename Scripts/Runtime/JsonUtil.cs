using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DoubTech.AI.Art
{

    public class CaseInsensitiveStringEnumConverter : StringEnumConverter
    {
        public CaseInsensitiveStringEnumConverter()
        {
            AllowIntegerValues = true;
            CamelCaseText = true;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            bool isNullable = Nullable.GetUnderlyingType(objectType) != null;
            Type enumType = isNullable ? Nullable.GetUnderlyingType(objectType) : objectType;

            if (reader.TokenType == JsonToken.String)
            {
                string enumText = reader.Value.ToString();

                if (!string.IsNullOrEmpty(enumText))
                {
                    return Enum.Parse(enumType, enumText, ignoreCase: true);
                }
            }

            return base.ReadJson(reader, objectType, existingValue, serializer);
        }
    }
}