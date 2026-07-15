using System.Globalization;
using System.Text;

namespace LhmExporter;

public static class MetricsFormatter
{
    private static readonly Regex Invalid = new(@"[^a-zA-Z0-9_]", RegexOptions.Compiled);

    public sealed record MetricSample(
        string Name,
        double Value,
        string? Help = null,
        IReadOnlyDictionary<string, string>? Labels = null);

    public static string Sanitize(string s)
    {
        s = (s ?? "")
            .ToLowerInvariant()
            .Replace(" ", "_")
            .Replace("-", "_")
            .Replace(".", "_")
            .Replace("/", "_")
            .Replace("\\", "_")
            .Replace("(", "_")
            .Replace(")", "_");

        s = Invalid.Replace(s, "");
        return string.IsNullOrWhiteSpace(s) ? "unknown" : s;
    }

    public static bool IsValidNumber(float v) => !(float.IsNaN(v) || float.IsInfinity(v));

    public static bool IsValidNumber(double v) => !(double.IsNaN(v) || double.IsInfinity(v));

    public static string FormatMetrics(IEnumerable<MetricSample> samples)
    {
        var sb = new StringBuilder(128 * 1024);
        var seenHelp = new HashSet<string>(StringComparer.Ordinal);
        var deduplicated = new Dictionary<string, MetricSample>();

        foreach (var s in samples)
        {
            if (!IsValidNumber(s.Value))
                continue;

            deduplicated[BuildMetricKey(s.Name, s.Labels)] = s;
        }

        foreach (var s in deduplicated.Values)
        {
            if (s.Help != null && !seenHelp.Contains(s.Name))
            {
                sb.Append("# HELP ").Append(s.Name).Append(' ').Append(s.Help).Append('\n');

                var metricType = s.Name.EndsWith("_total", StringComparison.Ordinal) ? "counter" : "gauge";
                sb.Append("# TYPE ").Append(s.Name).Append(' ').Append(metricType).Append('\n');
                seenHelp.Add(s.Name);
            }

            sb.Append(s.Name);

            if (s.Labels is { Count: > 0 })
            {
                sb.Append('{');
                var first = true;
                foreach (var kv in s.Labels)
                {
                    if (!first)
                        sb.Append(',');
                    first = false;
                    sb.Append(kv.Key).Append("=\"").Append(EscapeLabelValue(kv.Value)).Append('"');
                }
                sb.Append('}');
            }

            sb.Append(' ')
                .Append(s.Value.ToString("0.############", CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return sb.ToString();
    }

    private static string BuildMetricKey(string name, IReadOnlyDictionary<string, string>? labels)
    {
        if (labels is not { Count: > 0 })
            return name;

        var sortedLabels = labels.OrderBy(kv => kv.Key)
            .Select(kv => $"{kv.Key}={kv.Value}")
            .ToArray();

        return $"{name}{{{string.Join(",", sortedLabels)}}}";
    }

    private static string EscapeLabelValue(string v) =>
        (v ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
