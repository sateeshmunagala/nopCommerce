using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.AIInterview;

/// <summary>
/// Represents AI Interview settings
/// </summary>
public class AIInterviewSettings : ISettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the AI Interview is enabled
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the API key for AI Interview
    /// </summary>
    public string ApiKey { get; set; }
}
