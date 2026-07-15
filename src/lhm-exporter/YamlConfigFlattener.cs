using YamlDotNet.RepresentationModel;

namespace LhmExporter;

public static class YamlConfigFlattener
{
    public static Dictionary<string, string> FlattenFile(string path)
    {
        using var reader = new StreamReader(path);
        var yaml = new YamlStream();
        yaml.Load(reader);

        if (yaml.Documents.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (yaml.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidOperationException($"Configuration file root must be a mapping: {path}");

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        FlattenMapping("", root, result);
        return result;
    }

    private static void FlattenMapping(string prefix, YamlMappingNode node, Dictionary<string, string> result)
    {
        foreach (var entry in node.Children)
        {
            if (entry.Key is not YamlScalarNode keyNode || keyNode.Value is null)
                continue;

            var key = string.IsNullOrEmpty(prefix) ? keyNode.Value : $"{prefix}.{keyNode.Value}";
            key = NormalizeKey(key);

            switch (entry.Value)
            {
                case YamlMappingNode mapping:
                    FlattenMapping(key, mapping, result);
                    break;
                case YamlSequenceNode sequence:
                    result[key] = string.Join(",", sequence.Children.Select(ScalarToString));
                    break;
                default:
                    result[key] = ScalarToString(entry.Value);
                    break;
            }
        }
    }

    private static string ScalarToString(YamlNode node) =>
        node switch
        {
            YamlScalarNode scalar => scalar.Value ?? "",
            _ => node.ToString(),
        };

    /// <summary>Align YAML snake_case keys with windows_exporter CLI hyphen keys.</summary>
    private static string NormalizeKey(string key) =>
        key.Replace('_', '-');
}
