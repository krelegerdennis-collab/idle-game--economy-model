using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Playtest
{
    public sealed class JsonValue
    {
        public enum Kind
        {
            Null,
            Bool,
            Number,
            String,
            Array,
            Object,
        }

        public Kind Type;
        public bool Bool;
        public double Number;
        public string Text;
        public List<JsonValue> Items = new List<JsonValue>();
        public Dictionary<string, JsonValue> Map = new Dictionary<string, JsonValue>();

        public static JsonValue Null()
        {
            return new JsonValue { Type = Kind.Null };
        }

        public static JsonValue From(bool value)
        {
            return new JsonValue { Type = Kind.Bool, Bool = value };
        }

        public static JsonValue From(double value)
        {
            return new JsonValue { Type = Kind.Number, Number = value };
        }

        public static JsonValue From(string value)
        {
            return new JsonValue { Type = Kind.String, Text = value ?? "" };
        }

        public static JsonValue MakeArray()
        {
            return new JsonValue { Type = Kind.Array };
        }

        public static JsonValue MakeObject()
        {
            return new JsonValue { Type = Kind.Object };
        }

        public bool Has(string key)
        {
            return Type == Kind.Object && Map.ContainsKey(key) && Map[key].Type != Kind.Null;
        }

        public JsonValue Require(string key)
        {
            if (Type != Kind.Object || !Map.ContainsKey(key) || Map[key].Type == Kind.Null)
                throw new InvalidOperationException("economy_config.json missing " + key);
            return Map[key];
        }

        public double RequiredNumber(string key)
        {
            JsonValue value = Require(key);
            if (value.Type != Kind.Number)
                throw new InvalidOperationException("economy_config.json " + key + " is not a number");
            return value.Number;
        }

        public int RequiredInt(string key)
        {
            double value = RequiredNumber(key);
            double rounded = Math.Round(value);
            if (double.IsNaN(value) || Math.Abs(value - rounded) > 1e-6)
                throw new InvalidOperationException("economy_config.json " + key + " is not an integer");
            return (int)rounded;
        }

        public string RequiredString(string key)
        {
            JsonValue value = Require(key);
            if (value.Type != Kind.String)
                throw new InvalidOperationException("economy_config.json " + key + " is not a string");
            return value.Text;
        }

        public string Write()
        {
            StringBuilder builder = new StringBuilder();
            WriteInto(builder);
            return builder.ToString();
        }

        void WriteInto(StringBuilder builder)
        {
            switch (Type)
            {
                case Kind.Null:
                    builder.Append("null");
                    break;
                case Kind.Bool:
                    builder.Append(Bool ? "true" : "false");
                    break;
                case Kind.Number:
                    builder.Append(Number.ToString("G17", CultureInfo.InvariantCulture));
                    break;
                case Kind.String:
                    builder.Append('"');
                    AppendEscaped(builder, Text ?? "");
                    builder.Append('"');
                    break;
                case Kind.Array:
                    builder.Append('[');
                    for (int index = 0; index < Items.Count; index++)
                    {
                        if (index > 0)
                            builder.Append(',');
                        Items[index].WriteInto(builder);
                    }
                    builder.Append(']');
                    break;
                case Kind.Object:
                    builder.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, JsonValue> pair in Map)
                    {
                        if (!first)
                            builder.Append(',');
                        first = false;
                        builder.Append('"');
                        AppendEscaped(builder, pair.Key);
                        builder.Append("\":");
                        pair.Value.WriteInto(builder);
                    }
                    builder.Append('}');
                    break;
            }
        }

        static void AppendEscaped(StringBuilder builder, string value)
        {
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 32)
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(character);
                        break;
                }
            }
        }
    }

    public static class JsonMini
    {
        public static JsonValue Parse(string text)
        {
            if (text == null)
                throw new FormatException("economy_config.json is empty");
            int index = 0;
            if (text.Length > 0 && text[0] == '\uFEFF')
                index = 1;
            JsonValue value = ParseValue(text, ref index);
            Skip(text, ref index);
            if (index != text.Length)
                throw new FormatException("economy_config.json has trailing data");
            return value;
        }

        static JsonValue ParseValue(string text, ref int index)
        {
            Skip(text, ref index);
            if (index >= text.Length)
                throw new FormatException("economy_config.json ended early");
            char character = text[index];
            if (character == '{')
                return ParseObject(text, ref index);
            if (character == '[')
                return ParseArray(text, ref index);
            if (character == '"')
                return JsonValue.From(ParseString(text, ref index));
            if (character == 't')
            {
                Expect(text, ref index, "true");
                return JsonValue.From(true);
            }
            if (character == 'f')
            {
                Expect(text, ref index, "false");
                return JsonValue.From(false);
            }
            if (character == 'n')
            {
                Expect(text, ref index, "null");
                return JsonValue.Null();
            }
            return JsonValue.From(ParseNumber(text, ref index));
        }

        static JsonValue ParseObject(string text, ref int index)
        {
            Expect(text, ref index, "{");
            JsonValue value = JsonValue.MakeObject();
            Skip(text, ref index);
            if (index < text.Length && text[index] == '}')
            {
                index++;
                return value;
            }
            while (true)
            {
                Skip(text, ref index);
                string key = ParseString(text, ref index);
                Skip(text, ref index);
                Expect(text, ref index, ":");
                value.Map[key] = ParseValue(text, ref index);
                Skip(text, ref index);
                if (index >= text.Length)
                    throw new FormatException("economy_config.json object ended early");
                if (text[index] == ',')
                {
                    index++;
                    continue;
                }
                if (text[index] == '}')
                {
                    index++;
                    break;
                }
                throw new FormatException("economy_config.json object is missing a comma");
            }
            return value;
        }

        static JsonValue ParseArray(string text, ref int index)
        {
            Expect(text, ref index, "[");
            JsonValue value = JsonValue.MakeArray();
            Skip(text, ref index);
            if (index < text.Length && text[index] == ']')
            {
                index++;
                return value;
            }
            while (true)
            {
                value.Items.Add(ParseValue(text, ref index));
                Skip(text, ref index);
                if (index >= text.Length)
                    throw new FormatException("economy_config.json array ended early");
                if (text[index] == ',')
                {
                    index++;
                    continue;
                }
                if (text[index] == ']')
                {
                    index++;
                    break;
                }
                throw new FormatException("economy_config.json array is missing a comma");
            }
            return value;
        }

        static string ParseString(string text, ref int index)
        {
            if (index >= text.Length || text[index] != '"')
                throw new FormatException("economy_config.json expected a string");
            index++;
            StringBuilder builder = new StringBuilder();
            while (index < text.Length)
            {
                char character = text[index++];
                if (character == '"')
                    return builder.ToString();
                if (character != '\\')
                {
                    builder.Append(character);
                    continue;
                }
                if (index >= text.Length)
                    throw new FormatException("economy_config.json string ended early");
                char escaped = text[index++];
                switch (escaped)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escaped);
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (index + 4 > text.Length)
                            throw new FormatException("economy_config.json unicode escape ended early");
                        int code = int.Parse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        builder.Append((char)code);
                        index += 4;
                        break;
                    default:
                        throw new FormatException("economy_config.json bad string escape");
                }
            }
            throw new FormatException("economy_config.json string ended early");
        }

        static double ParseNumber(string text, ref int index)
        {
            int start = index;
            if (index < text.Length && (text[index] == '-' || text[index] == '+'))
                index++;
            while (index < text.Length && "0123456789.eE+-".IndexOf(text[index]) >= 0)
                index++;
            string token = text.Substring(start, index - start);
            double value;
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException("economy_config.json bad number");
            return value;
        }

        static void Skip(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;
        }

        static void Expect(string text, ref int index, string token)
        {
            Skip(text, ref index);
            if (index + token.Length > text.Length || text.Substring(index, token.Length) != token)
                throw new FormatException("economy_config.json expected " + token);
            index += token.Length;
        }
    }
}
