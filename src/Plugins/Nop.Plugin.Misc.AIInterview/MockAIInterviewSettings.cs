using Nop.Core.Configuration;

namespace Nop.Plugin.Misc.AIInterview;

/// <summary>
/// Represents Mock AI Interview settings
/// </summary>
public class MockAIInterviewSettings : ISettings
{
    /// <summary>
    /// Gets or sets a value indicating whether to use mock AI interview responses
    /// </summary>
    public bool UseMockResponses { get; set; }
}
