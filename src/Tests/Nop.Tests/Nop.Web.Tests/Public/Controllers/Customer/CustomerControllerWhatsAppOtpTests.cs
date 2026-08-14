using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
using System.Text.Encodings.Web;
using Nop.Core;
using Nop.Core.Domain;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Gdpr;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Security;
using Nop.Core.Domain.Tax;
using Nop.Core.Events;
using Nop.Services.Attributes;
using Nop.Services.Authentication.External;
using Nop.Services.Authentication.MultiFactor;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.ExportImport;
using Nop.Services.Gdpr;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Services.Tax;
using Nop.Web.Controllers;
using Nop.Web.Factories;
using Nop.Web.Models.Customer;
using NUnit.Framework;

namespace Nop.Tests.Nop.Web.Tests.Public.Controllers.CustomerOtp;

[TestFixture]
public class CustomerControllerWhatsAppOtpTests
{
    private Mock<ICustomerService> _customerService;
    private Mock<IGenericAttributeService> _genericAttributeService;
    private Mock<ISmsService> _smsService;
    private Mock<IWhatsAppNotificationService> _whatsAppService;
    private Mock<IServiceProvider> _optionalServiceProvider;
    private Mock<ILocalizationService> _localizationService;
    private Mock<ILogger> _logger;
    private Mock<IWorkflowMessageService> _workflowMessageService;
    private Mock<IWorkContext> _workContext;
    private Mock<ICustomerModelFactory> _customerModelFactory;
    private Mock<INotificationService> _notificationService;
    private Customer _customer;

    [SetUp]
    public void SetUp()
    {
        _customerService = new Mock<ICustomerService>();
        _genericAttributeService = new Mock<IGenericAttributeService>();
        _smsService = new Mock<ISmsService>();
        _whatsAppService = new Mock<IWhatsAppNotificationService>();
        _optionalServiceProvider = new Mock<IServiceProvider>();
        _localizationService = new Mock<ILocalizationService>();
        _logger = new Mock<ILogger>();
        _workflowMessageService = new Mock<IWorkflowMessageService>();
        _workContext = new Mock<IWorkContext>();
        _customerModelFactory = new Mock<ICustomerModelFactory>();
        _notificationService = new Mock<INotificationService>();
        _customer = new Customer
        {
            Id = 42,
            Email = "customer@example.com",
            Phone = "+14155552671",
            Active = true
        };

        _localizationService.Setup(x => x.GetResourceAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => key == "PhoneVerification.OtpCode.Message" ? "Your OTP is {0}" : key);
        _customerService.Setup(x => x.GetCustomerByPhoneAsync(It.IsAny<string>())).ReturnsAsync(_customer);
        _genericAttributeService.Setup(x => x.GetAttributeAsync<string>(
                It.IsAny<BaseEntity>(),
                NopCustomerDefaults.OtpContextAttribute,
                0,
                default))
            .ReturnsAsync(string.Empty);
        _genericAttributeService.Setup(x => x.SaveAttributeAsync(
                It.IsAny<BaseEntity>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                0))
            .Returns(Task.CompletedTask);
        _smsService.Setup(x => x.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        _optionalServiceProvider.Setup(x => x.GetService(typeof(IWhatsAppNotificationService))).Returns(_whatsAppService.Object);
        _whatsAppService.SetupGet(x => x.IsEnabled).Returns(true);
        _whatsAppService.Setup(x => x.SendNotificationAsync(It.IsAny<WhatsAppNotificationRequest>())).ReturnsAsync(false);
    }

    [Test]
    public async Task SendOtp_NormalizesPhone_AndFallsBackToSms_WhenWhatsAppReturnsFalse()
    {
        var controller = CreateController(new OtpSettings
        {
            LoginByPhoneEnabled = true,
            WhatsAppOtpEnabled = true,
            OtpLength = 6,
            OtpTimeLife = 300,
            OtpTimeToRepeat = 5,
            OtpCountAttemptsToSendCode = 3
        });

        var result = (JsonResult)await controller.SendOtp("+1 (415) 555-2671");

        Assert.That(GetJsonProperty<bool>(result, "success"), Is.True);
        _whatsAppService.Verify(x => x.SendNotificationAsync(
            It.Is<WhatsAppNotificationRequest>(request => request.PhoneNumber == "+14155552671")), Times.Once);
        _smsService.Verify(x => x.SendSmsAsync("+14155552671", It.IsAny<string>()), Times.Once);
        _genericAttributeService.Verify(x => x.SaveAttributeAsync(
            _customer,
            NopCustomerDefaults.OtpContextAttribute,
            It.IsAny<string>(),
            0), Times.Once);
        _logger.Verify(x => x.WarningAsync(
            It.Is<string>(message => message.Contains("customer 42") && !message.Contains("+14155552671")),
            null,
            null), Times.Once);
    }

    [Test]
    public async Task SendOtp_UsesSmsFallback_WhenWhatsAppProviderResolvesToNull()
    {
        _optionalServiceProvider.Setup(x => x.GetService(typeof(IWhatsAppNotificationService))).Returns(null);
        var controller = CreateController(CreateEnabledOtpSettings());

        var result = (JsonResult)await controller.SendOtp("+14155552671");

        Assert.That(GetJsonProperty<bool>(result, "success"), Is.True);
        _smsService.Verify(x => x.SendSmsAsync("+14155552671", It.IsAny<string>()), Times.Once);
        _whatsAppService.Verify(x => x.SendNotificationAsync(It.IsAny<WhatsAppNotificationRequest>()), Times.Never);
        _genericAttributeService.Verify(x => x.SaveAttributeAsync(
            _customer,
            NopCustomerDefaults.OtpContextAttribute,
            It.IsAny<string>(),
            0), Times.Once);
    }

    [Test]
    public async Task SendOtp_UsesSmsFallback_WhenWhatsAppThrows()
    {
        _whatsAppService.Setup(x => x.SendNotificationAsync(It.IsAny<WhatsAppNotificationRequest>()))
            .ThrowsAsync(new InvalidOperationException("provider failure"));
        var controller = CreateController(CreateEnabledOtpSettings());

        var result = (JsonResult)await controller.SendOtp("+14155552671");

        Assert.That(GetJsonProperty<bool>(result, "success"), Is.True);
        _smsService.Verify(x => x.SendSmsAsync("+14155552671", It.IsAny<string>()), Times.Once);
        _logger.Verify(x => x.WarningAsync(
            It.Is<string>(message => message.Contains("customer 42") && !message.Contains("+14155552671")),
            It.IsAny<Exception>(),
            null), Times.Once);
    }

    [Test]
    public async Task SendOtp_DoesNotSend_WhenOtpIsStillWithinThrottleWindow()
    {
        SetOtpContext(new OtpContext
        {
            Code = "123456",
            CodeGeneratedAtUtc = DateTime.UtcNow,
            LastAttemptAtUtc = DateTime.UtcNow,
            SentCount = 1
        });
        var controller = CreateController(CreateEnabledOtpSettings());

        var result = (JsonResult)await controller.SendOtp("+14155552671");

        Assert.That(GetJsonProperty<bool>(result, "success"), Is.False);
        _smsService.Verify(x => x.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _whatsAppService.Verify(x => x.SendNotificationAsync(It.IsAny<WhatsAppNotificationRequest>()), Times.Never);
    }

    [Test]
    public async Task SendOtp_DoesNotSend_WhenAttemptLimitIsReached()
    {
        SetOtpContext(new OtpContext
        {
            LastAttemptAtUtc = DateTime.UtcNow,
            SentCount = 3
        });
        var controller = CreateController(CreateEnabledOtpSettings());

        var result = (JsonResult)await controller.SendOtp("+14155552671");

        Assert.That(GetJsonProperty<bool>(result, "success"), Is.False);
        _smsService.Verify(x => x.SendSmsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _whatsAppService.Verify(x => x.SendNotificationAsync(It.IsAny<WhatsAppNotificationRequest>()), Times.Never);
    }

    [Test]
    public async Task PasswordRecovery_SendsEmailWithoutResolvingWhatsApp()
    {
        _customerService.Setup(x => x.GetCustomerByEmailAsync(_customer.Email)).ReturnsAsync(_customer);
        _workContext.Setup(x => x.GetWorkingLanguageAsync()).ReturnsAsync(new Language { Id = 1 });
        _workflowMessageService.Setup(x => x.SendCustomerPasswordRecoveryMessageAsync(_customer, 1))
            .ReturnsAsync(new List<int> { 1 });
        _customerModelFactory.Setup(x => x.PreparePasswordRecoveryModelAsync(It.IsAny<PasswordRecoveryModel>()))
            .ReturnsAsync((PasswordRecoveryModel model) => model);
        var controller = CreateController(CreateEnabledOtpSettings());

        await controller.PasswordRecoverySend(new PasswordRecoveryModel { Email = _customer.Email }, true);

        _workflowMessageService.Verify(x => x.SendCustomerPasswordRecoveryMessageAsync(_customer, 1), Times.Once);
        _optionalServiceProvider.Verify(x => x.GetService(typeof(IWhatsAppNotificationService)), Times.Never);
        _whatsAppService.Verify(x => x.SendNotificationAsync(It.IsAny<WhatsAppNotificationRequest>()), Times.Never);
    }

    private CustomerController CreateController(OtpSettings otpSettings)
    {
        var controller = new CustomerController(
            new AddressSettings(),
            new CaptchaSettings { Enabled = false },
            new CustomerSettings { PhoneNumberValidationEnabled = false },
            new DateTimeSettings(),
            new GdprSettings(),
            HtmlEncoder.Default,
            Mock.Of<IAddressModelFactory>(),
            Mock.Of<IAddressService>(),
            Mock.Of<IAttributeParser<AddressAttribute, AddressAttributeValue>>(),
            Mock.Of<IAttributeParser<CustomerAttribute, CustomerAttributeValue>>(),
            Mock.Of<IAttributeService<CustomerAttribute, CustomerAttributeValue>>(),
            Mock.Of<global::Nop.Services.Authentication.IAuthenticationService>(),
            Mock.Of<ICountryService>(),
            Mock.Of<ICurrencyService>(),
            Mock.Of<ICustomerActivityService>(),
            _customerModelFactory.Object,
            Mock.Of<ICustomerRegistrationService>(),
            _customerService.Object,
            Mock.Of<IDownloadService>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<IExportManager>(),
            Mock.Of<IExternalAuthenticationService>(),
            Mock.Of<IGdprService>(),
            _genericAttributeService.Object,
            Mock.Of<IGiftCardService>(),
            _localizationService.Object,
            _logger.Object,
            Mock.Of<IMultiFactorAuthenticationPluginManager>(),
            Mock.Of<INewsLetterSubscriptionService>(),
            _notificationService.Object,
            Mock.Of<IOrderService>(),
            Mock.Of<IPermissionService>(),
            Mock.Of<IPictureService>(),
            Mock.Of<IPriceFormatter>(),
            Mock.Of<IProductService>(),
            _smsService.Object,
            Mock.Of<IStateProvinceService>(),
            Mock.Of<IStoreContext>(),
            Mock.Of<ITaxService>(),
            _workContext.Object,
            _workflowMessageService.Object,
            new LocalizationSettings(),
            new MediaSettings(),
            new MultiFactorAuthenticationSettings(),
            otpSettings,
            new StoreInformationSettings(),
            new TaxSettings(),
            _optionalServiceProvider.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static OtpSettings CreateEnabledOtpSettings()
    {
        return new OtpSettings
        {
            LoginByPhoneEnabled = true,
            WhatsAppOtpEnabled = true,
            OtpLength = 6,
            OtpTimeLife = 300,
            OtpTimeToRepeat = 5,
            OtpCountAttemptsToSendCode = 3
        };
    }

    private void SetOtpContext(OtpContext context)
    {
        _genericAttributeService.Setup(x => x.GetAttributeAsync<string>(
                It.IsAny<BaseEntity>(),
                NopCustomerDefaults.OtpContextAttribute,
                0,
                default))
            .ReturnsAsync(JsonConvert.SerializeObject(context));
    }

    private static T GetJsonProperty<T>(JsonResult result, string propertyName)
    {
        var property = result.Value.GetType().GetProperty(propertyName);
        return (T)property.GetValue(result.Value);
    }
}
