using System.Text;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Misc.AIInterview.Models;
using Nop.Plugin.Misc.AIInterview.Services;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Misc.AIInterview.Controllers;

public class AIInterviewEmployerController : BasePluginController
{
    private readonly IApplicationService _applicationService;
    private readonly IInterviewSessionService _interviewSessionService;
    private readonly ICustomerService _customerService;
    private readonly IWorkContext _workContext;
    private readonly INotificationService _notificationService;
    private readonly ILocalizationService _localizationService;
    private readonly IProductService _productService;

    public AIInterviewEmployerController(IApplicationService applicationService,
        IInterviewSessionService interviewSessionService,
        ICustomerService customerService,
        IWorkContext workContext,
        INotificationService notificationService,
        ILocalizationService localizationService,
        IProductService productService)
    {
        _applicationService = applicationService;
        _interviewSessionService = interviewSessionService;
        _customerService = customerService;
        _workContext = workContext;
        _notificationService = notificationService;
        _localizationService = localizationService;
        _productService = productService;
    }

    protected async Task<bool> IsAuthorizedAsync()
    {
        var customer = await _workContext.GetCurrentCustomerAsync();
        return customer != null && (await _customerService.IsAdminAsync(customer) || customer.VendorId > 0);
    }

    public async Task<IActionResult> List(ApplicationListModel model, int pageIndex = 0, int pageSize = 10)
    {
        if (!await IsAuthorizedAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var isEmployer = !await _customerService.IsAdminAsync(customer) && customer.VendorId > 0;

        var applications = await _applicationService.GetApplicationsAsync(
            candidateNameOrEmail: model.CandidateNameOrEmail,
            status: model.Status,
            minScore: model.MinScore,
            maxScore: model.MaxScore,
            startDate: model.StartDate,
            endDate: model.EndDate,
            vendorId: isEmployer ? customer.VendorId : 0,
            pageIndex: pageIndex,
            pageSize: pageSize,
            sortByScore: model.SortByScore);

        model.Applications = await Task.WhenAll(applications.Select(async a =>
        {
            var appCustomer = await _customerService.GetCustomerByIdAsync(a.CustomerId);
            var session = await _interviewSessionService.GetLatestCompletedSessionByCustomerIdAsync(a.CustomerId);

            return new ApplicationModel
            {
                Id = a.Id,
                CandidateName = appCustomer != null ? (appCustomer.FirstName + " " + appCustomer.LastName).Trim() : "Unknown",
                CandidateEmail = appCustomer?.Email ?? "Unknown",
                Status = a.Status,
                StatusComment = a.StatusComment,
                InterviewScore = session?.Score,
                InterviewReportUrl = session != null ? Url.Action("Report", "AIInterview", new { sessionId = session.Id }) : null,
                CreatedOn = a.CreatedOnUtc
            };
        }));

        return View("~/Plugins/Misc.AIInterview/Views/AIInterviewEmployer/List.cshtml", model);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStatus(UpdateStatusModel model)
    {
        if (!await IsAuthorizedAsync())
            return Challenge();

        var application = await _applicationService.GetJobApplicationByIdAsync(model.Id);
        if (application == null)
            return NotFound();

        var customer = await _workContext.GetCurrentCustomerAsync();
        if (!await _customerService.IsAdminAsync(customer) && customer.VendorId > 0)
        {
            var product = await _productService.GetProductByIdAsync(application.ProductId);
            if (product == null || product.VendorId != customer.VendorId)
                return Challenge();
        }

        application.Status = model.Status;
        application.StatusComment = model.StatusComment;

        await _applicationService.UpdateJobApplicationAsync(application);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.UpdateStatus.Success"));

        return RedirectToAction("List");
    }

    public async Task<IActionResult> ExportCsv(ApplicationListModel model)
    {
        if (!await IsAuthorizedAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var isEmployer = !await _customerService.IsAdminAsync(customer) && customer.VendorId > 0;

        var applications = await _applicationService.GetApplicationsAsync(
            candidateNameOrEmail: model.CandidateNameOrEmail,
            status: model.Status,
            minScore: model.MinScore,
            maxScore: model.MaxScore,
            startDate: model.StartDate,
            endDate: model.EndDate,
            vendorId: isEmployer ? customer.VendorId : 0,
            sortByScore: model.SortByScore);

        var sb = new StringBuilder();
        sb.AppendLine("ID,Candidate,Email,Status,Score,Date");

        foreach (var a in applications)
        {
            var appCustomer = await _customerService.GetCustomerByIdAsync(a.CustomerId);
            var session = await _interviewSessionService.GetLatestCompletedSessionByCustomerIdAsync(a.CustomerId);

            var candidateName = appCustomer != null ? (appCustomer.FirstName + " " + appCustomer.LastName).Trim() : "Unknown";
            var email = appCustomer?.Email ?? "Unknown";
            var score = session?.Score.ToString() ?? "N/A";

            // Simple CSV escaping: wrap in quotes and escape existing quotes
            var candidateNameCsv = $"\"{candidateName.Replace("\"", "\"\"")}\"";
            var emailCsv = $"\"{email.Replace("\"", "\"\"")}\"";
            var statusCsv = $"\"{a.Status?.Replace("\"", "\"\"")}\"";

            sb.AppendLine($"{a.Id},{candidateNameCsv},{emailCsv},{statusCsv},{score},{a.CreatedOnUtc:yyyy-MM-dd HH:mm:ss}");
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "applications.csv");
    }
}
