using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

/// <summary>
/// Represents plugin route provider
/// </summary>
public class RouteProvider : IRouteProvider
{
    /// <summary>
    /// Register routes
    /// </summary>
    /// <param name="endpointRouteBuilder">Route builder</param>
    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        //Admin
        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.ConfigurationRouteName,
            pattern: "Admin/AIInterview/Configure",
            defaults: new { controller = "MockAiInterviewAdmin", action = "Configure", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.AdminAiServiceRouteName,
            pattern: "Admin/AIInterview/AiService",
            defaults: new { controller = "AIInterviewAdmin", action = "AiService", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.AdminSponsorInvitesRouteName,
            pattern: "Admin/AIInterview/SponsorInvites",
            defaults: new { controller = "AIInterviewAdmin", action = "SponsorInvites", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.AdminVendorCreditsRouteName,
            pattern: "Admin/AIInterview/VendorCredits",
            defaults: new { controller = "AIInterviewAdmin", action = "VendorCredits", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.AdminApplicantCreditsRouteName,
            pattern: "Admin/AIInterview/ApplicantCredits",
            defaults: new { controller = "AIInterviewAdmin", action = "ApplicantCredits", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.AdminScoreboardRouteName,
            pattern: "Admin/AIInterview/Scoreboard",
            defaults: new { controller = "AIInterviewAdmin", action = "Scoreboard", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.AdminScoreboardExportRouteName,
            pattern: "Admin/AIInterview/ScoreboardExportCsv",
            defaults: new { controller = "AIInterviewAdmin", action = "ScoreboardExportCsv", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.AdminMockConfigureRouteName,
            pattern: "Admin/MockAiInterview/Configure",
            defaults: new { controller = "MockAiInterviewAdmin", action = "MockConfigure", area = AreaNames.ADMIN });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.AdminMockReportRouteName,
            pattern: "Admin/MockAiInterview/Report",
            defaults: new { controller = "MockAiInterviewAdmin", action = "Report", area = AreaNames.ADMIN });

        //Public AIInterview
        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.IndexRouteName,
            pattern: "aiinterview",
            defaults: new { controller = "AIInterview", action = "Index" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.ApplyRouteName,
            pattern: "aiinterview/apply",
            defaults: new { controller = "AIInterview", action = "Apply" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.ApplyInlineRouteName,
            pattern: "aiinterview/apply-inline",
            defaults: new { controller = "AIInterview", action = "ApplyInline" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.MyApplicationsRouteName,
            pattern: "aiinterview/my-applications",
            defaults: new { controller = "AIInterview", action = "MyApplications" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.EmployerApplicationsRouteName,
            pattern: "aiinterview/employer-applications",
            defaults: new { controller = "AIInterview", action = "EmployerApplications" });

        endpointRouteBuilder.MapControllerRoute(name: "Plugin.Misc.AIInterview.UpdateStatus",
            pattern: "aiinterview/update-status",
            defaults: new { controller = "AIInterview", action = "UpdateStatus" });

        endpointRouteBuilder.MapControllerRoute(name: "Plugin.Misc.AIInterview.ExportCsv",
            pattern: "aiinterview/export-csv",
            defaults: new { controller = "AIInterview", action = "ExportCsv" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.ReportRouteName,
            pattern: "aiinterview/report/{sessionId}",
            defaults: new { controller = "AIInterview", action = "Report" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.RecordingRouteName,
            pattern: "aiinterview/recording/{sessionId}",
            defaults: new { controller = "AIInterview", action = "Recording" });

        //MockAiInterview
        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.MockStartRouteName,
            pattern: "mockaiinterview/start",
            defaults: new { controller = "MockAiInterview", action = "Start" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.MockRuntimeRouteName,
            pattern: "mockaiinterview/runtime",
            defaults: new { controller = "MockAiInterview", action = "Runtime" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.MockSubmitAnswerRouteName,
            pattern: "mockaiinterview/submit-answer",
            defaults: new { controller = "MockAiInterview", action = "SubmitAnswer" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.MockStopRouteName,
            pattern: "mockaiinterview/stop",
            defaults: new { controller = "MockAiInterview", action = "Stop" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.MockRefreshTokenRouteName,
            pattern: "mockaiinterview/refresh-token",
            defaults: new { controller = "MockAiInterview", action = "RefreshToken" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.MockSpeechTokenRouteName,
            pattern: "mockaiinterview/speech-token",
            defaults: new { controller = "MockAiInterview", action = "SpeechToken" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.MockAgoraTokenRouteName,
            pattern: "mockaiinterview/agora-token",
            defaults: new { controller = "MockAiInterview", action = "AgoraToken" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.MockRecordingUploadRouteName,
            pattern: "mockaiinterview/upload-recording",
            defaults: new { controller = "MockAiInterview", action = "UploadRecording" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.MockHistoryRouteName,
            pattern: "mockaiinterview/history",
            defaults: new { controller = "MockAiInterview", action = "History" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.MockReportRouteName,
            pattern: "mockaiinterview/report/{sessionId}",
            defaults: new { controller = "MockAiInterview", action = "Report" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.MockEmployerManageRouteName,
            pattern: "mockaiinterview/employer-manage",
            defaults: new { controller = "MockAiInterview", action = "EmployerManage" });

        endpointRouteBuilder.MapControllerRoute(name: "Plugin.Misc.AIInterview.Mock.CreateInvite",
            pattern: "mockaiinterview/create-invite",
            defaults: new { controller = "MockAiInterview", action = "CreateInvite" });

        endpointRouteBuilder.MapControllerRoute(name: "Plugin.Misc.AIInterview.Mock.DeactivateInvite",
            pattern: "mockaiinterview/deactivate-invite",
            defaults: new { controller = "MockAiInterview", action = "DeactivateInvite" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.VendorScoreboardRouteName,
            pattern: "aiinterview/vendor-scoreboard",
            defaults: new { controller = "AIInterview", action = "VendorScoreboard" });

        endpointRouteBuilder.MapControllerRoute(name: AIInterviewDefaults.VendorJobCreationRouteName,
            pattern: "aiinterview/vendor-job-creation",
            defaults: new { controller = "AIInterview", action = "VendorJobCreation" });
    }

    /// <summary>
    /// Gets a priority of route provider
    /// </summary>
    public int Priority => 0;
}
