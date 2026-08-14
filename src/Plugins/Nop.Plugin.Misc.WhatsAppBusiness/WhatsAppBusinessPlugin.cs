using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nop.Core;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Services.Cms;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;
using Nop.Web.Framework.Infrastructure;
using SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Components;

namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness;

public class WhatsAppBusinessPlugin : BasePlugin, IWidgetPlugin, IPlugin
{
	private readonly ILocalizationService _localizationService;

	private readonly IScheduleTaskService _scheduleTaskService;

	private readonly ISettingService _settingService;

	private readonly IWebHelper _webHelper;

	public bool HideInWidgetList => false;

	public WhatsAppBusinessPlugin(ILocalizationService localizationService, IScheduleTaskService scheduleTaskService, ISettingService settingService, IWebHelper webHelper)
	{
		_localizationService = localizationService;
		_scheduleTaskService = scheduleTaskService;
		_settingService = settingService;
		_webHelper = webHelper;
	}

	public Task<IList<string>> GetWidgetZonesAsync()
	{
		return Task.FromResult((IList<string>)new List<string>
		{
			PublicWidgetZones.CheckoutCompletedBottom,
			PublicWidgetZones.OrderDetailsPageAfterproducts
		});
	}

	public Type GetWidgetViewComponent(string widgetZone)
	{
		if (widgetZone.Equals(PublicWidgetZones.CheckoutCompletedBottom, StringComparison.InvariantCultureIgnoreCase))
		{
			return typeof(WhatsAppOptInViewComponent);
		}
		if (widgetZone.Equals(PublicWidgetZones.OrderDetailsPageAfterproducts, StringComparison.InvariantCultureIgnoreCase))
		{
			return typeof(WhatsAppTrackingViewComponent);
		}
		return typeof(WhatsAppOptInViewComponent);
	}

	public override string GetConfigurationPageUrl()
	{
		return _webHelper.GetStoreLocation((bool?)null) + "Admin/WhatsAppBusiness/Configure";
	}

	public override async Task InstallAsync()
	{
		await _settingService.SaveSettingAsync<WhatsAppBusinessSettings>(new WhatsAppBusinessSettings(), 0);
		var (taskName, taskType, taskSeconds) = WhatsAppBusinessDefaults.ScheduleTask;
		var scheduleTasks = (await _scheduleTaskService.GetAllTasksAsync(showHidden: true) ?? new List<ScheduleTask>())
			.Where(IsWhatsAppScheduleTask)
			.OrderBy(task => task.Id)
			.ToList();
		var scheduleTask = scheduleTasks.FirstOrDefault(task => task.Type == taskType) ?? scheduleTasks.FirstOrDefault();
		if (scheduleTask != null)
		{
			if (scheduleTask.Type != taskType)
			{
				scheduleTask.Type = taskType;
				await _scheduleTaskService.UpdateTaskAsync(scheduleTask);
			}

			foreach (var duplicateTask in scheduleTasks.Where(task => !ReferenceEquals(task, scheduleTask)))
				await _scheduleTaskService.DeleteTaskAsync(duplicateTask);
		}
		else
		{
			await _scheduleTaskService.InsertTaskAsync(new ScheduleTask
			{
				Enabled = true,
				StopOnError = false,
				LastEnabledUtc = DateTime.UtcNow,
				Seconds = taskSeconds,
				Name = taskName,
				Type = taskType
			});
		}
		await _localizationService.AddOrUpdateLocaleResourceAsync((IDictionary<string, string>)new Dictionary<string, string>
		{
			["Plugins.Misc.WhatsAppBusiness.Fields.ApiKey"] = "API Key (permanent access token)",
			["Plugins.Misc.WhatsAppBusiness.Fields.ApiKey.Hint"] = "Enter the permanent access token from Meta Business Manager.",
			["Plugins.Misc.WhatsAppBusiness.Fields.PhoneNumberId"] = "Phone Number ID",
			["Plugins.Misc.WhatsAppBusiness.Fields.PhoneNumberId.Hint"] = "The numeric Phone Number ID from Meta Business Manager.",
			["Plugins.Misc.WhatsAppBusiness.Fields.BusinessAccountId"] = "Business Account ID",
			["Plugins.Misc.WhatsAppBusiness.Fields.BusinessAccountId.Hint"] = "The WhatsApp Business Account ID from Meta Business Manager.",
			["Plugins.Misc.WhatsAppBusiness.Fields.AppId"] = "App ID",
			["Plugins.Misc.WhatsAppBusiness.Fields.AppId.Hint"] = "The App ID from Meta Developer portal.",
			["Plugins.Misc.WhatsAppBusiness.Fields.AppSecret"] = "App Secret",
			["Plugins.Misc.WhatsAppBusiness.Fields.AppSecret.Hint"] = "The App Secret used for webhook signature verification.",
			["Plugins.Misc.WhatsAppBusiness.Fields.ApiVersion"] = "API Version",
			["Plugins.Misc.WhatsAppBusiness.Fields.ApiVersion.Hint"] = "The Graph API version (e.g. v23.0).",
			["Plugins.Misc.WhatsAppBusiness.Fields.IsEnabled"] = "Enabled",
			["Plugins.Misc.WhatsAppBusiness.Fields.IsEnabled.Hint"] = "Enable or disable WhatsApp notifications globally.",
			["Plugins.Misc.WhatsAppBusiness.Fields.EnableOrderPlaced"] = "Order Placed",
			["Plugins.Misc.WhatsAppBusiness.Fields.EnableOrderProcessing"] = "Order Processing",
			["Plugins.Misc.WhatsAppBusiness.Fields.EnableShipmentCreated"] = "Shipment Created",
			["Plugins.Misc.WhatsAppBusiness.Fields.EnableShipmentDelivered"] = "Shipment Delivered",
			["Plugins.Misc.WhatsAppBusiness.Fields.EnableOrderCancelled"] = "Order Cancelled",
			["Plugins.Misc.WhatsAppBusiness.Fields.EnableRefundIssued"] = "Refund Issued",
			["Plugins.Misc.WhatsAppBusiness.Fields.UseTemplateMessages"] = "Use Template Messages",
			["Plugins.Misc.WhatsAppBusiness.Fields.UseTemplateMessages.Hint"] = "Send pre-approved template messages instead of plain text.",
			["Plugins.Misc.WhatsAppBusiness.Fields.DefaultLanguageCode"] = "Default Language Code",
			["Plugins.Misc.WhatsAppBusiness.Fields.DefaultLanguageCode.Hint"] = "Language code for templates (e.g. pt_BR, en_US).",
			["Plugins.Misc.WhatsAppBusiness.Fields.OrderConfirmationTemplateName"] = "Order Confirmation Template",
			["Plugins.Misc.WhatsAppBusiness.Fields.ShipmentTrackingTemplateName"] = "Shipment Tracking Template",
			["Plugins.Misc.WhatsAppBusiness.Fields.DeliveryConfirmationTemplateName"] = "Delivery Confirmation Template",
			["Plugins.Misc.WhatsAppBusiness.Fields.PollingIntervalSeconds"] = "Polling Interval (seconds)",
			["Plugins.Misc.WhatsAppBusiness.Fields.MinDelayBetweenSendsSeconds"] = "Min Delay Between Sends (seconds)",
			["Plugins.Misc.WhatsAppBusiness.Fields.MaxDelayBetweenSendsSeconds"] = "Max Delay Between Sends (seconds)",
			["Plugins.Misc.WhatsAppBusiness.Fields.MaxMessagesPerBatch"] = "Max Messages Per Batch",
			["Plugins.Misc.WhatsAppBusiness.Fields.LookbackWindowDays"] = "Lookback Window (days)",
			["Plugins.Misc.WhatsAppBusiness.Fields.WebhookVerifyToken"] = "Webhook Verify Token",
			["Plugins.Misc.WhatsAppBusiness.Fields.WebhookVerifyToken.Hint"] = "The token Meta sends to verify webhook ownership.",
			["Plugins.Misc.WhatsAppBusiness.Fields.DefaultTrackingUrlPattern"] = "Default Tracking URL Pattern",
			["Plugins.Misc.WhatsAppBusiness.Fields.DefaultTrackingUrlPattern.Hint"] = "URL pattern with {tracking} placeholder for tracking number.",
			["Plugins.Misc.WhatsAppBusiness.Fields.CarrierTrackingUrls"] = "Carrier Tracking URLs (JSON)",
			["Plugins.Misc.WhatsAppBusiness.Fields.CarrierTrackingUrls.Hint"] = "JSON dictionary of carrier name to tracking URL pattern.",
			["Plugins.Misc.WhatsAppBusiness.Fields.ShowOptInOnCheckoutCompleted"] = "Show Opt-In on Checkout",
			["Plugins.Misc.WhatsAppBusiness.Fields.ShowTrackingOnOrderDetails"] = "Show Tracking on Order Details",
			["Plugins.Misc.WhatsAppBusiness.Fields.RequireCustomerAccount"] = "Require Customer Account",
			["Plugins.Misc.WhatsAppBusiness.Fields.RequireCustomerAccount.Hint"] = "Only show opt-in to registered customers (not guests).",
			["Plugins.Misc.WhatsAppBusiness.Configuration"] = "Configuration",
			["Plugins.Misc.WhatsAppBusiness.Configuration.ApiCredentials"] = "API Credentials",
			["Plugins.Misc.WhatsAppBusiness.Configuration.EventToggles"] = "Event Notifications",
			["Plugins.Misc.WhatsAppBusiness.Configuration.TemplateSettings"] = "Template Messages",
			["Plugins.Misc.WhatsAppBusiness.Configuration.DispatchSettings"] = "Anti-Ban Dispatch Queue",
			["Plugins.Misc.WhatsAppBusiness.Configuration.TrackingSettings"] = "Carrier Tracking",
			["Plugins.Misc.WhatsAppBusiness.Configuration.WidgetSettings"] = "Customer Widget Settings",
			["Plugins.Misc.WhatsAppBusiness.Configuration.WebhookSettings"] = "Webhook",
			["Plugins.Misc.WhatsAppBusiness.Dashboard"] = "Message Dashboard",
			["Plugins.Misc.WhatsAppBusiness.MessageLog"] = "Recent Message Log",
			["Plugins.Misc.WhatsAppBusiness.MessageLog.OrderId"] = "Order",
			["Plugins.Misc.WhatsAppBusiness.MessageLog.PhoneNumber"] = "Phone",
			["Plugins.Misc.WhatsAppBusiness.MessageLog.MessageType"] = "Type",
			["Plugins.Misc.WhatsAppBusiness.MessageLog.Status"] = "Status",
			["Plugins.Misc.WhatsAppBusiness.MessageLog.SentAt"] = "Sent at",
			["Plugins.Misc.WhatsAppBusiness.MessageLog.Error"] = "Error",
			["Plugins.Misc.WhatsAppBusiness.Blacklist"] = "Blacklisted Numbers",
			["Plugins.Misc.WhatsAppBusiness.TestConnection"] = "Test Connection",
			["Plugins.Misc.WhatsAppBusiness.SendTestMessage"] = "Send Test Message"
		}, (int?)null);
		Dictionary<string, string> dictionary = new Dictionary<string, string>
		{
			["Plugins.Misc.WhatsAppBusiness.Fields.ApiKey"] = "Chave de API (token de acesso permanente)",
			["Plugins.Misc.WhatsAppBusiness.Fields.ApiKey.Hint"] = "Insira o token de acesso permanente do Meta Business Manager.",
			["Plugins.Misc.WhatsAppBusiness.Fields.PhoneNumberId"] = "ID do Número de Telefone",
			["Plugins.Misc.WhatsAppBusiness.Fields.PhoneNumberId.Hint"] = "O ID numérico do número de telefone do Meta Business Manager.",
			["Plugins.Misc.WhatsAppBusiness.Fields.BusinessAccountId"] = "ID da Conta Business",
			["Plugins.Misc.WhatsAppBusiness.Fields.BusinessAccountId.Hint"] = "O ID da conta WhatsApp Business do Meta Business Manager.",
			["Plugins.Misc.WhatsAppBusiness.Fields.AppId"] = "ID do App",
			["Plugins.Misc.WhatsAppBusiness.Fields.AppSecret"] = "Segredo do App",
			["Plugins.Misc.WhatsAppBusiness.Fields.ApiVersion"] = "Versão da API",
			["Plugins.Misc.WhatsAppBusiness.Fields.IsEnabled"] = "Habilitado",
			["Plugins.Misc.WhatsAppBusiness.Fields.EnableOrderPlaced"] = "Pedido Criado",
			["Plugins.Misc.WhatsAppBusiness.Fields.EnableOrderProcessing"] = "Pedido em Processamento",
			["Plugins.Misc.WhatsAppBusiness.Fields.EnableShipmentCreated"] = "Envio Criado",
			["Plugins.Misc.WhatsAppBusiness.Fields.EnableShipmentDelivered"] = "Envio Entregue",
			["Plugins.Misc.WhatsAppBusiness.Fields.EnableOrderCancelled"] = "Pedido Cancelado",
			["Plugins.Misc.WhatsAppBusiness.Fields.EnableRefundIssued"] = "Reembolso Emitido",
			["Plugins.Misc.WhatsAppBusiness.Fields.UseTemplateMessages"] = "Usar Mensagens de Modelo",
			["Plugins.Misc.WhatsAppBusiness.Fields.DefaultLanguageCode"] = "Código de Idioma Padrão",
			["Plugins.Misc.WhatsAppBusiness.Fields.OrderConfirmationTemplateName"] = "Modelo de Confirmação de Pedido",
			["Plugins.Misc.WhatsAppBusiness.Fields.ShipmentTrackingTemplateName"] = "Modelo de Rastreio de Envio",
			["Plugins.Misc.WhatsAppBusiness.Fields.DeliveryConfirmationTemplateName"] = "Modelo de Confirmação de Entrega",
			["Plugins.Misc.WhatsAppBusiness.Fields.PollingIntervalSeconds"] = "Intervalo de Polling (segundos)",
			["Plugins.Misc.WhatsAppBusiness.Fields.MinDelayBetweenSendsSeconds"] = "Atraso Mínimo Entre Envios (segundos)",
			["Plugins.Misc.WhatsAppBusiness.Fields.MaxDelayBetweenSendsSeconds"] = "Atraso Máximo Entre Envios (segundos)",
			["Plugins.Misc.WhatsAppBusiness.Fields.MaxMessagesPerBatch"] = "Máximo de Mensagens por Lote",
			["Plugins.Misc.WhatsAppBusiness.Fields.LookbackWindowDays"] = "Janela de Retroatividade (dias)",
			["Plugins.Misc.WhatsAppBusiness.Fields.WebhookVerifyToken"] = "Token de Verificação do Webhook",
			["Plugins.Misc.WhatsAppBusiness.Fields.DefaultTrackingUrlPattern"] = "Padrão de URL de Rastreio",
			["Plugins.Misc.WhatsAppBusiness.Fields.CarrierTrackingUrls"] = "URLs de Rastreio por Transportadora (JSON)",
			["Plugins.Misc.WhatsAppBusiness.Fields.ShowOptInOnCheckoutCompleted"] = "Mostrar Opt-In no Checkout",
			["Plugins.Misc.WhatsAppBusiness.Fields.ShowTrackingOnOrderDetails"] = "Mostrar Rastreio nos Detalhes do Pedido",
			["Plugins.Misc.WhatsAppBusiness.Fields.RequireCustomerAccount"] = "Exigir Conta de Cliente",
			["Plugins.Misc.WhatsAppBusiness.Configuration"] = "Configuração",
			["Plugins.Misc.WhatsAppBusiness.Configuration.ApiCredentials"] = "Credenciais da API",
			["Plugins.Misc.WhatsAppBusiness.Configuration.EventToggles"] = "Notificações de Eventos",
			["Plugins.Misc.WhatsAppBusiness.Configuration.TemplateSettings"] = "Mensagens de Modelo",
			["Plugins.Misc.WhatsAppBusiness.Configuration.DispatchSettings"] = "Fila de Despacho Anti-Ban",
			["Plugins.Misc.WhatsAppBusiness.Configuration.TrackingSettings"] = "Rastreio de Transportadora",
			["Plugins.Misc.WhatsAppBusiness.Configuration.WidgetSettings"] = "Configurações do Widget do Cliente",
			["Plugins.Misc.WhatsAppBusiness.Configuration.WebhookSettings"] = "Webhook",
			["Plugins.Misc.WhatsAppBusiness.Dashboard"] = "Painel de Mensagens",
			["Plugins.Misc.WhatsAppBusiness.MessageLog"] = "Registro Recente de Mensagens",
			["Plugins.Misc.WhatsAppBusiness.MessageLog.OrderId"] = "Pedido",
			["Plugins.Misc.WhatsAppBusiness.MessageLog.PhoneNumber"] = "Telefone",
			["Plugins.Misc.WhatsAppBusiness.MessageLog.MessageType"] = "Tipo",
			["Plugins.Misc.WhatsAppBusiness.MessageLog.Status"] = "Status",
			["Plugins.Misc.WhatsAppBusiness.MessageLog.SentAt"] = "Enviado em",
			["Plugins.Misc.WhatsAppBusiness.MessageLog.Error"] = "Erro",
			["Plugins.Misc.WhatsAppBusiness.Blacklist"] = "Números Bloqueados",
			["Plugins.Misc.WhatsAppBusiness.TestConnection"] = "Testar Conexão",
			["Plugins.Misc.WhatsAppBusiness.SendTestMessage"] = "Enviar Mensagem de Teste"
		};
		foreach (var (text3, text4) in dictionary)
		{
			await _localizationService.AddOrUpdateLocaleResourceAsync(text3, text4, "pt-BR");
		}
		await base.InstallAsync();
	}

	public override async Task UninstallAsync()
	{
		await _settingService.DeleteSettingAsync<WhatsAppBusinessSettings>();
		var scheduleTasks = (await _scheduleTaskService.GetAllTasksAsync(showHidden: true) ?? new List<ScheduleTask>())
			.Where(IsWhatsAppScheduleTask)
			.ToList();
		foreach (var scheduleTask in scheduleTasks)
			await _scheduleTaskService.DeleteTaskAsync(scheduleTask);
		await _localizationService.DeleteLocaleResourcesAsync("Plugins.Misc.WhatsAppBusiness", (int?)null);
		await base.UninstallAsync();
	}

	private static bool IsWhatsAppScheduleTask(ScheduleTask task)
	{
		return task.Type == WhatsAppBusinessDefaults.ScheduleTask.Type ||
			task.Type == WhatsAppBusinessDefaults.LegacyScheduleTaskType;
	}
}
