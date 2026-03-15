using System.Text.Json.Serialization;

namespace Databento.Client.Models.Metadata;

/// <summary>
/// Time range for dataset availability
/// </summary>
public class DatasetRange
{
    /// <summary>
    /// Start time of available data
    /// </summary>
    public DateTimeOffset Start { get; set; }

    /// <summary>
    /// End time of available data
    /// </summary>
    public DateTimeOffset End { get; set; }

    /// <summary>
    /// Per-schema date ranges (optional)
    /// Maps schema name to its specific start and end timestamps
    /// </summary>
    public Dictionary<string, SchemaDateRange>? RangeBySchema { get; set; }

    /// <summary>
    /// Duration of available data
    /// </summary>
    [JsonIgnore]
    public TimeSpan Duration => End - Start;

    public override string ToString()
    {
        return $"{Start:yyyy-MM-dd HH:mm:ss} to {End:yyyy-MM-dd HH:mm:ss} ({Duration.TotalDays:F1} days)";
    }
}
