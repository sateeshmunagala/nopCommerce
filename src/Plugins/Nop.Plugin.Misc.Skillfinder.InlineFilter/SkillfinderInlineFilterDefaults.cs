namespace Nop.Plugin.Misc.Skillfinder.InlineFilter;

public static class SkillfinderInlineFilterDefaults
{
    public const string SystemName = "Misc.Skillfinder.InlineFilter";
    public const string RouteName = "Plugin.Misc.Skillfinder.InlineFilter.GetFilteredResults";
    public const string ResultsRoutePattern = "skillfinder/inline-filter/results/{categorySeName?}";
    public const string AiInterviewSystemName = "Misc.AIInterview";
    public const string AiInterviewJobTemplateName = "AI Interview Job Details";
    public const string AiInterviewJobTemplateViewPath = "~/Plugins/Misc.AIInterview/Views/ProductTemplate.JobDetails.cshtml";
    public const int ResultCount = 6;
    public const int SearchPageSize = 24;
    public const string LocalizationPrefix = "Plugins.Misc.Skillfinder.InlineFilter";
}
