using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Plugin.Misc.AIInterview.Domain;
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

    public static async Task<IList<SelectListItem>> BuildResumeSelectListAsync(IEnumerable<JobApplication> applications,
        IDownloadService downloadService,
        int selectedResumeDownloadId = 0)
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
            var createdLabel = application.CreatedOnUtc == default
                ? string.Empty
                : application.CreatedOnUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
}
