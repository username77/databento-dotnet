using System.Text.Json.Serialization;

namespace Databento.Client.Models.Metadata;

/// <summary>
/// Date range for a specific schema
/// </summary>
public class SchemaDateRange
{
    /// <summary>
    /// Start timestamp
    /// </summary>
    public DateTimeOffset Start { get; set; }

    /// <summary>
    /// End timestamp (exclusive)
    /// </summary>
    public DateTimeOffset End { get; set; }

    /// <summary>
    /// Duration of available data for this schema
    /// </summary>
    [JsonIgnore]
    public TimeSpan Duration => End - Start;

    public override string ToString()
    {
        return $"{Start:yyyy-MM-dd HH:mm:ss} to {End:yyyy-MM-dd HH:mm:ss}";
    }
}
