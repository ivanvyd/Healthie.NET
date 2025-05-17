using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Healthie.StateProviding.SqlServer.Serialization;

internal class ExceptionJsonConverter : JsonConverter<Exception>
{
    public override Exception? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token for Exception deserialization.");
        }

        string? message = null;
        string? typeName = null;
        string? source = null;
        int hResult = 0; // Default HResult
        string? stackTrace = null;
        IDictionary? data = new Dictionary<object, object?>(); // Use a temporary flexible dictionary
        Exception? innerException = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                // Create the exception instance
                var ex = new Exception(message ?? "Deserialized exception: Message not found.", innerException);

                // Set properties that are settable
                if (source != null) ex.Source = source;
                ex.HResult = hResult;

                // Populate the Data dictionary
                if (data != null)
                {
                    foreach (DictionaryEntry entry in data)
                    {
                        if (entry.Key != null && !ex.Data.Contains(entry.Key))
                        {
                             ex.Data[entry.Key] = entry.Value;
                        }
                    }
                }
                if (typeName != null && !ex.Data.Contains("OriginalTypeName")) ex.Data["OriginalTypeName"] = typeName;
                if (stackTrace != null && !ex.Data.Contains("OriginalStackTrace")) ex.Data["OriginalStackTrace"] = stackTrace;
                
                return ex;
            }

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string? propertyName = reader.GetString();
                reader.Read(); // Move to the value token

                switch (propertyName)
                {
                    case "TypeName":
                        typeName = reader.GetString();
                        break;
                    case "Message":
                        message = reader.GetString();
                        break;
                    case "Source":
                        source = reader.GetString();
                        break;
                    case "HResult":
                        hResult = reader.GetInt32();
                        break;
                    case "StackTrace":
                        stackTrace = reader.GetString();
                        break;
                    case "InnerException":
                        innerException = JsonSerializer.Deserialize<Exception>(ref reader, options);
                        break;
                    case "Data":
                        if (reader.TokenType == JsonTokenType.StartObject)
                        {
                            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                            {
                                if (reader.TokenType == JsonTokenType.PropertyName)
                                {
                                    string? key = reader.GetString();
                                    reader.Read(); // Move to value
                                    object? value = JsonSerializer.Deserialize<object>(ref reader, options);
                                    if (key != null) data[key] = value;
                                }
                            }
                        }
                        else if (reader.TokenType == JsonTokenType.Null)
                        {
                            data = null; // Explicitly set to null if JSON value is null
                        }
                        break;
                    default:
                        reader.Skip(); // Skip unknown properties
                        break;
                }
            }
        }
        throw new JsonException("Error reading Exception JSON: Unexpected end of data.");
    }

    public override void Write(Utf8JsonWriter writer, Exception value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();

        writer.WriteString("TypeName", value.GetType().AssemblyQualifiedName);
        writer.WriteString("Message", value.Message);
        if (value.Source != null) writer.WriteString("Source", value.Source);
        writer.WriteNumber("HResult", value.HResult);
        if (value.StackTrace != null) writer.WriteString("StackTrace", value.StackTrace);

        if (value.InnerException != null)
        {
            writer.WritePropertyName("InnerException");
            JsonSerializer.Serialize(writer, value.InnerException, options);
        }
        else
        {
            writer.WriteNull("InnerException");
        }
        
        writer.WritePropertyName("Data");
        if (value.Data != null && value.Data.Count > 0)
        {
            writer.WriteStartObject();
            foreach (DictionaryEntry entry in value.Data)
            {
                if (entry.Key is string keyString)
                {
                    writer.WritePropertyName(keyString);
                    JsonSerializer.Serialize(writer, entry.Value, options);
                }
                else if (entry.Key != null) // Handle non-string keys by converting to string
                {
                     writer.WritePropertyName(entry.Key.ToString() ?? Guid.NewGuid().ToString()); // Fallback key
                     JsonSerializer.Serialize(writer, entry.Value, options);
                }
            }
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNullValue(); // Write null if Data is null or empty
        }

        writer.WriteEndObject();
    }
}

