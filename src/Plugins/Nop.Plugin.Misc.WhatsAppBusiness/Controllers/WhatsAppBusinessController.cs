using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Services.ScheduleTasks;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Models;
using SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Services;

namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Controllers;

[AuthorizeAdmin(false)]
[Area("Admin")]
[AutoValidateAntiforgeryToken]
public class WhatsAppBusinessController : BasePluginController
{
	private readonly WhatsAppBusinessSettings _settings;

	private readonly ISettingService _settingService;

	private readonly ILocalizationService _localizationService;

	private readonly IPermissionService _permissionService;

	private readonly INotificationService _notificationService;

	private readonly IWhatsAppBusinessService _whatsAppService;

	private readonly IWebHelper _webHelper;

	private readonly IScheduleTaskService _scheduleTaskService;

	public WhatsAppBusinessController(WhatsAppBusinessSettings settings, ISettingService settingService, ILocalizationService localizationService, IPermissionService permissionService, INotificationService notificationService, IWhatsAppBusinessService whatsAppService, IWebHelper webHelper, IScheduleTaskService scheduleTaskService)
	{
		_settings = settings;
		_settingService = settingService;
		_localizationService = localizationService;
		_permissionService = permissionService;
		_notificationService = notificationService;
		_whatsAppService = whatsAppService;
		_webHelper = webHelper;
		_scheduleTaskService = scheduleTaskService;
	}

	public async Task<IActionResult> Configure()
	{
		if (!(await _permissionService.AuthorizeAsync("Configuration.ManagePlugins")))
		{
			return AccessDeniedView();
		}
		ConfigurationModel configurationModel = new ConfigurationModel
		{
			ApiKey = _settings.ApiKey,
			PhoneNumberId = _settings.PhoneNumberId,
			BusinessAccountId = _settings.BusinessAccountId,
			AppId = _settings.AppId,
			AppSecret = _settings.AppSecret,
			ApiVersion = _settings.ApiVersion,
			IsEnabled = _settings.IsEnabled,
			EnableOrderPlaced = _settings.EnableOrderPlaced,
			EnableOrderProcessing = _settings.EnableOrderProcessing,
			EnableShipmentCreated = _settings.EnableShipmentCreated,
			EnableShipmentDelivered = _settings.EnableShipmentDelivered,
			EnableOrderCancelled = _settings.EnableOrderCancelled,
			EnableRefundIssued = _settings.EnableRefundIssued,
			UseTemplateMessages = _settings.UseTemplateMessages,
			DefaultLanguageCode = _settings.DefaultLanguageCode,
			OrderConfirmationTemplateName = _settings.OrderConfirmationTemplateName,
			ShipmentTrackingTemplateName = _settings.ShipmentTrackingTemplateName,
			DeliveryConfirmationTemplateName = _settings.DeliveryConfirmationTemplateName,
			ApplicantInterviewCompletionTemplateName = _settings.ApplicantInterviewCompletionTemplateName,
			VendorInterviewCompletionTemplateName = _settings.VendorInterviewCompletionTemplateName,
			InterviewReportSharingTemplateName = _settings.InterviewReportSharingTemplateName,
			OtpTemplateName = _settings.OtpTemplateName,
			PasswordRecoveryTemplateName = _settings.PasswordRecoveryTemplateName,
			PollingIntervalSeconds = _settings.PollingIntervalSeconds,
			MinDelayBetweenSendsSeconds = _settings.MinDelayBetweenSendsSeconds,
			MaxDelayBetweenSendsSeconds = _settings.MaxDelayBetweenSendsSeconds,
			MaxMessagesPerBatch = _settings.MaxMessagesPerBatch,
			LookbackWindowDays = _settings.LookbackWindowDays,
			WebhookVerifyToken = _settings.WebhookVerifyToken,
			WebhookUrl = _webHelper.GetStoreLocation((bool?)null) + WhatsAppBusinessDefaults.WebhookPath,
			DefaultTrackingUrlPattern = _settings.DefaultTrackingUrlPattern,
			CarrierTrackingUrls = _settings.CarrierTrackingUrls,
			ShowOptInOnCheckoutCompleted = _settings.ShowOptInOnCheckoutCompleted,
			ShowTrackingOnOrderDetails = _settings.ShowTrackingOnOrderDetails,
			RequireCustomerAccount = _settings.RequireCustomerAccount
		};
		ConfigurationModel configurationModel2 = configurationModel;
		configurationModel2.RecentLogs = await _whatsAppService.GetRecentLogsAsync();
		ConfigurationModel configurationModel3 = configurationModel;
		configurationModel3.BlacklistedNumbers = await _whatsAppService.GetBlacklistAsync();
		return ((Controller)(object)this).View("~/Plugins/Misc.WhatsAppBusiness/Views/Configure.cshtml", (object?)configurationModel);
	}

	[HttpPost]
	public async Task<IActionResult> Configure(ConfigurationModel model)
	{
		if (!(await _permissionService.AuthorizeAsync("Configuration.ManagePlugins")))
		{
			return ((ControllerBase)(object)this).Content("Access denied");
		}
		if (!((ControllerBase)this).ModelState.IsValid)
		{
			return await Configure();
		}
		_settings.ApiKey = model.ApiKey;
		_settings.PhoneNumberId = model.PhoneNumberId;
		_settings.BusinessAccountId = model.BusinessAccountId;
		_settings.AppId = model.AppId;
		_settings.AppSecret = model.AppSecret;
		_settings.ApiVersion = model.ApiVersion;
		_settings.IsEnabled = model.IsEnabled;
		_settings.EnableOrderPlaced = model.EnableOrderPlaced;
		_settings.EnableOrderProcessing = model.EnableOrderProcessing;
		_settings.EnableShipmentCreated = model.EnableShipmentCreated;
		_settings.EnableShipmentDelivered = model.EnableShipmentDelivered;
		_settings.EnableOrderCancelled = model.EnableOrderCancelled;
		_settings.EnableRefundIssued = model.EnableRefundIssued;
		_settings.UseTemplateMessages = model.UseTemplateMessages;
		_settings.DefaultLanguageCode = model.DefaultLanguageCode;
		_settings.OrderConfirmationTemplateName = model.OrderConfirmationTemplateName;
		_settings.ShipmentTrackingTemplateName = model.ShipmentTrackingTemplateName;
		_settings.DeliveryConfirmationTemplateName = model.DeliveryConfirmationTemplateName;
		_settings.ApplicantInterviewCompletionTemplateName = model.ApplicantInterviewCompletionTemplateName;
		_settings.VendorInterviewCompletionTemplateName = model.VendorInterviewCompletionTemplateName;
		_settings.InterviewReportSharingTemplateName = model.InterviewReportSharingTemplateName;
		_settings.OtpTemplateName = model.OtpTemplateName;
		_settings.PasswordRecoveryTemplateName = model.PasswordRecoveryTemplateName;
		_settings.PollingIntervalSeconds = model.PollingIntervalSeconds;
		_settings.MinDelayBetweenSendsSeconds = model.MinDelayBetweenSendsSeconds;
		_settings.MaxDelayBetweenSendsSeconds = model.MaxDelayBetweenSendsSeconds;
		_settings.MaxMessagesPerBatch = model.MaxMessagesPerBatch;
		_settings.LookbackWindowDays = model.LookbackWindowDays;
		_settings.WebhookVerifyToken = model.WebhookVerifyToken;
		_settings.DefaultTrackingUrlPattern = model.DefaultTrackingUrlPattern;
		_settings.CarrierTrackingUrls = model.CarrierTrackingUrls;
		_settings.ShowOptInOnCheckoutCompleted = model.ShowOptInOnCheckoutCompleted;
		_settings.ShowTrackingOnOrderDetails = model.ShowTrackingOnOrderDetails;
		_settings.RequireCustomerAccount = model.RequireCustomerAccount;
		await _settingService.SaveSettingAsync<WhatsAppBusinessSettings>(_settings, 0);

		var scheduleTasks = (await _scheduleTaskService.GetAllTasksAsync(showHidden: true) ?? new List<ScheduleTask>())
			.Where(task => task.Type == WhatsAppBusinessDefaults.ScheduleTask.Type ||
				task.Type == WhatsAppBusinessDefaults.LegacyScheduleTaskType)
			.OrderBy(task => task.Id)
			.ToList();
		var scheduleTask = scheduleTasks
			.FirstOrDefault(task => task.Type == WhatsAppBusinessDefaults.ScheduleTask.Type) ?? scheduleTasks.FirstOrDefault();
		if (scheduleTask != null)
		{
			scheduleTask.Type = WhatsAppBusinessDefaults.ScheduleTask.Type;
			scheduleTask.Seconds = _settings.PollingIntervalSeconds;
			await _scheduleTaskService.UpdateTaskAsync(scheduleTask);

			foreach (var duplicateTask in scheduleTasks.Where(task => !ReferenceEquals(task, scheduleTask)))
				await _scheduleTaskService.DeleteTaskAsync(duplicateTask);
		}

		INotificationService notificationService = _notificationService;
		notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"), true, 0);
		return await Configure();
	}

	[HttpPost]
	[IgnoreAntiforgeryToken]
	public async Task<IActionResult> TestConnection()
	{
		if (!(await _permissionService.AuthorizeAsync("Configuration.ManagePlugins")))
		{
			return ((Controller)(object)this).Json((object?)new
			{
				success = false,
				error = "Access denied"
			});
		}
		if (string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(_settings.PhoneNumberId))
		{
			return ((Controller)(object)this).Json((object?)new
			{
				success = false,
				error = "API Key and Phone Number ID are required."
			});
		}
		await _whatsAppService.SendMessageAsync(0, 0, "test", "ConnectionTest", "ping");
		return ((Controller)(object)this).Json((object?)new
		{
			success = false,
			error = "API credentials are configured. Note: a real test message requires a valid phone number."
		});
	}

	[HttpPost]
	public async Task<IActionResult> RemoveBlacklist(int id)
	{
		if (!(await _permissionService.AuthorizeAsync("Configuration.ManagePlugins")))
		{
			return AccessDeniedView();
		}
		await _whatsAppService.RemoveFromBlacklistAsync(id);
		_notificationService.SuccessNotification("Number removed from blacklist.", true, 0);
		return ((ControllerBase)(object)this).RedirectToAction("Configure");
	}
}
