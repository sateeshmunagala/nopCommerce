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

    /// <summary>
    /// Gets or sets a value indicating whether a resume is required for application
    /// </summary>
    public bool ResumeRequired { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an interview is required before application
    /// </summary>
    public bool InterviewRequired { get; set; }

    /// <summary>
    /// Gets or sets the minimum score required in an interview to apply
    /// </summary>
    public decimal MinimumScore { get; set; }
}
