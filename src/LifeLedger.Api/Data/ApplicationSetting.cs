namespace LifeLedger.Api.Data;

/// <summary>Stores a small piece of application metadata that is local to one LifeLedger database.</summary>
public sealed class ApplicationSetting
{
    /// <summary>Stable setting identifier, for example <c>data-schema-version</c>.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>String value so future settings can use a common local table.</summary>
    public string Value { get; set; } = string.Empty;
    /// <summary>UTC timestamp of the last write to this setting.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
