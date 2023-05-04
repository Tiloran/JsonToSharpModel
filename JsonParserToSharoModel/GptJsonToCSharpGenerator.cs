using System.Text.Json;
using System.Text.RegularExpressions;

namespace JsonParserToSharoModel;

public static class GptJsonToCSharpGenerator
{
    public static string Generate(List<string> jsonStrings = null, List<string> filesPath = null)
    {
        if ((jsonStrings?.Any() == true && filesPath?.Any() == true) ||
            (jsonStrings?.Any() != true && filesPath?.Any() != true))
        {
            throw new Exception("set jsonStrings or filesPath");
        }

        string rootName = "Root";

        // Разобрать все JSON-строки и объединить их свойства
        JsonElement tempJsonElement = jsonStrings?.Any() == true
            ? JsonDocument.Parse(jsonStrings[0]).RootElement
            : JsonDocument.Parse(File.ReadAllText(filesPath[0])).RootElement;

        var combinedJsonElement = tempJsonElement.Clone();

        // Обработка ситуации, когда JSON на верхнем уровне является массивом
        if (combinedJsonElement.ValueKind == JsonValueKind.Array)
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                using (Utf8JsonWriter jsonUtf8Writer = new Utf8JsonWriter(memoryStream))
                {
                    jsonUtf8Writer.WriteStartObject();
                    jsonUtf8Writer.WritePropertyName("Items");
                    combinedJsonElement.WriteTo(jsonUtf8Writer);
                    jsonUtf8Writer.WriteEndObject();
                }

                memoryStream.Seek(0, SeekOrigin.Begin);

                using (JsonDocument jsonDocument = JsonDocument.Parse(memoryStream))
                {
                    combinedJsonElement = jsonDocument.RootElement.Clone();
                }
            }
        }

        if (jsonStrings?.Any() == true)
        {
            for (var i = 1;
                 i < jsonStrings.Count;
                 i++)
            {
                string jsonString = jsonStrings[i];
                JsonDocument jsonDoc = JsonDocument.Parse(jsonString);
                JsonElement jsonRoot = jsonDoc.RootElement;
                combinedJsonElement = MergeJsonElements(combinedJsonElement, jsonRoot);
            }
        }
        else
        {
            for (var i = 1;
                 i < filesPath.Count;
                 i++)
            {
                string jsonString = File.ReadAllText(filesPath[i]);
                JsonDocument jsonDoc = JsonDocument.Parse(jsonString);
                JsonElement jsonRoot = jsonDoc.RootElement;
                combinedJsonElement = MergeJsonElements(combinedJsonElement, jsonRoot);
            }
        }

        // Генерировать классы на основе объединенных свойств
        HashSet<string> classNames = new HashSet<string>();
        HashSet<string> classes = new HashSet<string>();
        GenerateClass(combinedJsonElement, rootName, classes, classNames);

        // Вывести сгенерированные классы
        string classString = string.Join(Environment.NewLine + Environment.NewLine, classes);
        return classString;
    }

    static JsonElement MergeJsonElements(JsonElement element1, JsonElement element2)
    {
        Dictionary<string, JsonElement> combinedProperties = new Dictionary<string, JsonElement>();

        if (element1.ValueKind == JsonValueKind.Object)
        {
            // Добавить свойства из первого элемента
            foreach (JsonProperty prop in element1.EnumerateObject())
            {
                combinedProperties[prop.Name] = prop.Value;
            }
        }
        else if (element1.ValueKind == JsonValueKind.Array)
        {
            combinedProperties["Items"] = element1;
        }

        if (element2.ValueKind == JsonValueKind.Object)
        {
            // Добавить свойства из второго элемента
            foreach (JsonProperty prop in element2.EnumerateObject())
            {
                // Если свойство уже существует и является объектом, объединить вложенные объекты
                if (combinedProperties.TryGetValue(prop.Name, out JsonElement existingValue) &&
                    existingValue.ValueKind == JsonValueKind.Object &&
                    prop.Value.ValueKind == JsonValueKind.Object)
                {
                    combinedProperties[prop.Name] = MergeJsonElements(existingValue, prop.Value);
                }
                else
                {
                    combinedProperties[prop.Name] = prop.Value;
                }
            }
        }
        else if (element2.ValueKind == JsonValueKind.Array)
        {
            combinedProperties["Items"] = element2;
        }

        // Создать JsonElement из объединенных свойств
        using MemoryStream memoryStream = new MemoryStream();
        using (Utf8JsonWriter jsonUtf8Writer = new Utf8JsonWriter(memoryStream))
        {
            jsonUtf8Writer.WriteStartObject();

            foreach (var property in combinedProperties)
            {
                jsonUtf8Writer.WritePropertyName(property.Key);
                property.Value.WriteTo(jsonUtf8Writer);
            }

            jsonUtf8Writer.WriteEndObject();
        }

        memoryStream.Seek(0, SeekOrigin.Begin);

        using (JsonDocument jsonDocument = JsonDocument.Parse(memoryStream))
        {
            return jsonDocument.RootElement.Clone();
        }
    }


    static void GenerateClass(JsonElement jsonElement, string className, HashSet<string> classes,
        HashSet<string> classNames, int indentation = 0)
    {
        string indent = new string(' ', indentation * 4);
        className = EnsureUniqueClassName(classNames, className);

        if (ClassExists(classNames, className))
        {
            return;
        }
        else
        {
            classNames.Add(className);
        }

        string classCode = $"{indent}public class {className}\n{indent}{{\n";

        foreach (JsonProperty prop in jsonElement.EnumerateObject())
        {
            string propName = prop.Name;
            JsonElement propValue = prop.Value;

            if (propValue.ValueKind == JsonValueKind.Object)
            {
                string nestedClassName = GetCSharpPropertyName(propName);
                GenerateClass(propValue, nestedClassName, classes, classNames, indentation + 1);
                classCode += $"{indent}    [JsonPropertyName(\"{propName}\")]\n";
                classCode += $"{indent}    public {nestedClassName}? {nestedClassName} {{ get; set; }}\n";
            }
            else if (propValue.ValueKind == JsonValueKind.Array)
            {
                JsonElement firstElem = propValue.EnumerateArray().FirstOrDefault();

                if (firstElem.ValueKind == JsonValueKind.Object)
                {
                    string nestedClassName = GetCSharpPropertyName(propName);
                    GenerateClass(firstElem, nestedClassName, classes, classNames, indentation + 1);
                    classCode += $"{indent}    [JsonPropertyName(\"{propName}\")]\n";
                    classCode += $"{indent}    public List<{nestedClassName}> {nestedClassName}List {{ get; set; }}\n";
                }
                else
                {
                    string csharpType = GetCSharpType(firstElem);
                    string csharpPropName = GetCSharpPropertyName(propName, className);
                    classCode += $"{indent} [JsonPropertyName(\"{propName}\")]\n";
                    classCode += $"{indent} public List<{csharpType}> {csharpPropName}List {{ get; set; }}\n";
                }
            }
            else
            {
                string csharpType = GetCSharpType(propValue);
                string csharpPropName = GetCSharpPropertyName(propName, className);
                classCode += $"{indent} [JsonPropertyName(\"{propName}\")]\n";
                classCode += $"{indent} public {csharpType} {csharpPropName} {{ get; set; }}\n";
            }
        }

        classCode += $"{indent}}}";

        string generatedClassName = GetClassName(classCode) ?? string.Empty;
        if (!string.IsNullOrEmpty(generatedClassName))
        {
            classNames.Add(generatedClassName);
            classes.Add(classCode);
        }
    }

    static bool ClassExists(HashSet<string> classNames, string className)
    {
        return classNames.Contains(className);
    }

    static string GetCSharpType(JsonElement jsonElement)
    {
        switch (jsonElement.ValueKind)
        {
            case JsonValueKind.Number:
                if (jsonElement.TryGetInt32(out int intValue)) return "int?";
                if (jsonElement.TryGetInt64(out long longValue)) return "long?";
                if (jsonElement.TryGetDouble(out double doubleValue)) return "double?";
                return "decimal?";
            case JsonValueKind.String:
                if (jsonElement.TryGetDateTime(out DateTime _)) return "DateTime?";
                return "string?";
            case JsonValueKind.True:
            case JsonValueKind.False:
                return "bool?";
            case JsonValueKind.Object:
                return "object?";
            case JsonValueKind.Array:
                return "List<object>?";
            default:
                return "object?";
        }
    }


    static string GetCSharpPropertyName(string propName, string enclosingClassName = null)
    {
        string[] words = propName.Split('_');
        string csharpPropName = string.Concat(words.Select(w =>
            string.IsNullOrEmpty(w) ? w : char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant()));

        if (csharpPropName == enclosingClassName)
        {
            csharpPropName += "Property";
        }

        return csharpPropName;
    }


    static string EnsureUniqueClassName(HashSet<string> classNames, string className)
    {
        string originalClassName = className;
        int index = 1;

        while (classNames.Contains(className))
        {
            className = originalClassName + index;
            index++;
        }

        return className;
    }

    static string? GetClassName(string classCode)
    {
        Match match = Regex.Match(classCode, @"public class (\w+)", RegexOptions.Compiled);

        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return null;
    }
}