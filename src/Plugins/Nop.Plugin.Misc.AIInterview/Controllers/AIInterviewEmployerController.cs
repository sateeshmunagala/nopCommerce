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
    private readonly ISponsorInviteService _inviteService;
    private readonly ICreditService _creditService;

    public AIInterviewEmployerController(IApplicationService applicationService,
        IInterviewSessionService interviewSessionService,
        ICustomerService customerService,
        IWorkContext workContext,
        INotificationService notificationService,
        ILocalizationService localizationService,
        IProductService productService,
        ISponsorInviteService inviteService,
        ICreditService creditService)
    {
        _applicationService = applicationService;
        _interviewSessionService = interviewSessionService;
        _customerService = customerService;
        _workContext = workContext;
        _notificationService = notificationService;
        _localizationService = localizationService;
        _productService = productService;
        _inviteService = inviteService;
        _creditService = creditService;
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
                CandidateName = appCustomer != null ? (appCustomer.FirstName + " " + appCustomer.LastName).Trim() : await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown"),
                CandidateEmail = appCustomer?.Email ?? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown"),
                Status = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{a.Status?.Replace(" ", "")}") ?? a.Status,
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
        var idHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.ID");
        var candidateHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Candidate");
        var emailHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Applications.Email");
        var statusHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.History.Status");
        var scoreHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.History.Score");
        var dateHeader = await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.History.Date");
        sb.AppendLine($"{idHeader},{candidateHeader},{emailHeader},{statusHeader},{scoreHeader},{dateHeader}");

        foreach (var a in applications)
        {
            var appCustomer = await _customerService.GetCustomerByIdAsync(a.CustomerId);
            var session = await _interviewSessionService.GetLatestCompletedSessionByCustomerIdAsync(a.CustomerId);

            var candidateName = appCustomer != null ? (appCustomer.FirstName + " " + appCustomer.LastName).Trim() : await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown");
            var email = appCustomer?.Email ?? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.Unknown");
            var status = await _localizationService.GetResourceAsync($"{AIInterviewDefaults.LocalizationPrefix}.Status.{a.Status?.Replace(" ", "")}") ?? a.Status;
            var score = session?.Score.ToString() ?? await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Common.None");

            // Simple CSV escaping: wrap in quotes and escape existing quotes
            var candidateNameCsv = $"\"{candidateName.Replace("\"", "\"\"")}\"";
            var emailCsv = $"\"{email.Replace("\"", "\"\"")}\"";
            var statusCsv = $"\"{status?.Replace("\"", "\"\"")}\"";

            sb.AppendLine($"{a.Id},{candidateNameCsv},{emailCsv},{statusCsv},{score},{a.CreatedOnUtc:yyyy-MM-dd HH:mm:ss}");
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "applications.csv");
    }

    public async Task<IActionResult> SponsorInvites()
    {
        if (!await IsAuthorizedAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        var invites = await _inviteService.GetSponsorInvitesAsync(customer.Id);
        var wallet = await _creditService.GetOrCreateWalletAsync(customer.Id);

        ViewBag.CreditBalance = wallet.Balance;

        return View("~/Plugins/Misc.AIInterview/Views/AIInterviewEmployer/SponsorInvites.cshtml", invites);
    }

    [HttpPost]
    public async Task<IActionResult> CreateInvite(string email, int productId, int maxAttempts, DateTime? expiryDateUtc)
    {
        if (!await IsAuthorizedAsync())
            return Challenge();

        try
        {
            var customer = await _workContext.GetCurrentCustomerAsync();
            await _inviteService.CreateInviteAsync(customer.Id, email, productId, maxAttempts, expiryDateUtc);
            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Invite.Success"));
        }
        catch (NopException ex)
        {
            _notificationService.ErrorNotification(ex.Message);
        }
        catch (Exception)
        {
            _notificationService.ErrorNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Invite.Error"));
        }

        return RedirectToAction("SponsorInvites");
    }

    [HttpPost]
    public async Task<IActionResult> DeactivateInvite(int id)
    {
        if (!await IsAuthorizedAsync())
            return Challenge();

        var customer = await _workContext.GetCurrentCustomerAsync();
        await _inviteService.DeactivateInviteAsync(id, customer.Id);
        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Plugins.Misc.AIInterview.Employer.Invite.Deactivated"));

        return RedirectToAction("SponsorInvites");
    }
}
