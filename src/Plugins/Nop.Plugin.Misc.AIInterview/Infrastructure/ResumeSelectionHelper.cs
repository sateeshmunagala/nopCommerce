using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Services.Helpers;
using Nop.Services.Media;

namespace Nop.Plugin.Misc.AIInterview.Infrastructure;

internal static class ResumeSelectionHelper
{
    public static HashSet<int> GetOwnedResumeDownloadIds(IEnumerable<JobApplication> applications)
    {
        return applications?
            .Where(application => application.ResumeDownloadId > 0)
            .Select(application => application.ResumeDownloadId)
            .ToHashSet()
            ?? new HashSet<int>();
    }

    public static HashSet<int> GetOwnedResumeDownloadIds(IEnumerable<JobApplication> applications, IEnumerable<InterviewSession> sessions)
    {
        var ownedResumeIds = GetOwnedResumeDownloadIds(applications);
        foreach (var session in sessions ?? Enumerable.Empty<InterviewSession>())
        {
            if (session?.ResumeDownloadId > 0)
                ownedResumeIds.Add(session.ResumeDownloadId);
        }

        return ownedResumeIds;
    }

    public static async Task<IList<SelectListItem>> BuildResumeSelectListAsync(IEnumerable<JobApplication> applications,
        IDownloadService downloadService,
        int selectedResumeDownloadId = 0,
        IDateTimeHelper dateTimeHelper = null)
    {
        var items = new List<SelectListItem>();
        if (applications == null || downloadService == null)
            return items;

        var recentResumeApplications = applications
            .Where(application => application.ResumeDownloadId > 0)
            .OrderByDescending(application => application.CreatedOnUtc)
            .ThenByDescending(application => application.Id)
            .GroupBy(application => application.ResumeDownloadId)
            .Select(group => group.First())
            .ToList();

        foreach (var application in recentResumeApplications)
        {
            var downloadTask = downloadService.GetDownloadByIdAsync(application.ResumeDownloadId);
            var download = downloadTask == null ? null : await downloadTask;
            if (download == null)
                continue;

            var fileName = string.IsNullOrWhiteSpace(download.Filename)
                ? $"Resume #{application.ResumeDownloadId.ToString(CultureInfo.InvariantCulture)}"
                : download.Filename;
            var createdLabel = await FormatResumeCreatedLabelAsync(application.CreatedOnUtc, dateTimeHelper);
            var text = string.IsNullOrWhiteSpace(createdLabel)
                ? fileName
                : $"{fileName} ({createdLabel})";

            items.Add(new SelectListItem
            {
                Value = application.ResumeDownloadId.ToString(CultureInfo.InvariantCulture),
                Text = text,
                Selected = application.ResumeDownloadId == selectedResumeDownloadId
            });
        }

        return items;
    }

    public static async Task<IList<SelectListItem>> BuildResumeSelectListAsync(IEnumerable<JobApplication> applications,
        IEnumerable<InterviewSession> sessions,
        IDownloadService downloadService,
        int selectedResumeDownloadId = 0,
        IDateTimeHelper dateTimeHelper = null)
    {
        var items = new List<SelectListItem>();
        if (downloadService == null)
            return items;

        var orderedEntries = new List<(int DownloadId, DateTime CreatedOnUtc, string DefaultLabel)>();

        orderedEntries.AddRange((applications ?? Enumerable.Empty<JobApplication>())
            .Where(application => application.ResumeDownloadId > 0)
            .Select(application => (
                application.ResumeDownloadId,
                application.CreatedOnUtc,
                "Application resume")));

        orderedEntries.AddRange((sessions ?? Enumerable.Empty<InterviewSession>())
            .Where(session => session.ResumeDownloadId > 0)
            .Select(session => (
                session.ResumeDownloadId,
                session.CreatedOnUtc,
                "Practice resume")));

        foreach (var entry in orderedEntries
                     .OrderByDescending(entry => entry.CreatedOnUtc)
                     .ThenByDescending(entry => entry.DownloadId)
                     .GroupBy(entry => entry.DownloadId)
                     .Select(group => group.First()))
        {
            var download = await downloadService.GetDownloadByIdAsync(entry.DownloadId);
            if (download == null)
                continue;

            var fileName = string.IsNullOrWhiteSpace(download.Filename)
                ? $"{entry.DefaultLabel} #{entry.DownloadId.ToString(CultureInfo.InvariantCulture)}"
                : download.Filename;
            var createdLabel = await FormatResumeCreatedLabelAsync(entry.CreatedOnUtc, dateTimeHelper);
            var text = string.IsNullOrWhiteSpace(createdLabel)
                ? fileName
                : $"{fileName} ({createdLabel})";

            items.Add(new SelectListItem
            {
                Value = entry.DownloadId.ToString(CultureInfo.InvariantCulture),
                Text = text,
                Selected = entry.DownloadId == selectedResumeDownloadId
            });
        }

        return items;
    }

    private static async Task<string> FormatResumeCreatedLabelAsync(DateTime createdOnUtc, IDateTimeHelper dateTimeHelper)
    {
        if (createdOnUtc == default)
            return string.Empty;

        if (dateTimeHelper == null)
            return createdOnUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var userDateTime = await dateTimeHelper.ConvertToUserTimeAsync(createdOnUtc, DateTimeKind.Utc);
        return userDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
