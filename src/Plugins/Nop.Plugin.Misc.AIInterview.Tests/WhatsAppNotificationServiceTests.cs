using Moq;
using Nop.Services.Logging;
using NUnit.Framework;
using Nop.Plugin.Misc.WhatsAppBusiness;
using Nop.Plugin.Misc.WhatsAppBusiness.Services;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class WhatsAppNotificationServiceTests
{
    private const string PhoneNumber = "+15550000005";

    [Test]
    public async Task SendNotificationAsync_ReturnsFalseWithoutProviderOrWarning_WhenCredentialsAreMissing()
    {
        var provider = new Mock<IWhatsAppBusinessService>();
        var logger = new Mock<ILogger>();
        var service = new WhatsAppNotificationService(
            provider.Object,
            new WhatsAppBusinessSettings
            {
                IsEnabled = true,
                ApiKey = string.Empty,
                PhoneNumberId = string.Empty,
                UseTemplateMessages = false
            },
            logger.Object);

        var result = await service.SendNotificationAsync(CreateRequest());

        Assert.That(result, Is.False);
        Assert.That(service.IsEnabled, Is.False);
        provider.VerifyNoOtherCalls();
        logger.Verify(x => x.WarningAsync(It.IsAny<string>(), null, null), Times.Never);
    }

    [Test]
    public async Task SendNotificationAsync_ReturnsFalseAndLogsRedactedWarning_WhenProviderReturnsFalse()
    {
        var provider = new Mock<IWhatsAppBusinessService>();
        var logger = new Mock<ILogger>();
        provider.Setup(x => x.SendMessageAsync(
                0,
                17,
                PhoneNumber,
                "AIInterview.ApplicantCompletion",
                "Completed",
                null))
            .ReturnsAsync(false);
        var service = CreateService(provider, logger);

        var result = await service.SendNotificationAsync(CreateRequest());

        Assert.That(result, Is.False);
        logger.Verify(x => x.WarningAsync(
            It.Is<string>(message => message.Contains("+15***05") && !message.Contains(PhoneNumber)),
            null,
            null), Times.Once);
    }

    [Test]
    public void SendNotificationAsync_ReturnsFalseAndLogsRedactedWarning_WhenProviderThrows()
    {
        var provider = new Mock<IWhatsAppBusinessService>();
        var logger = new Mock<ILogger>();
        provider.Setup(x => x.SendMessageAsync(
                0,
                17,
                PhoneNumber,
                "AIInterview.ApplicantCompletion",
                "Completed",
                null))
            .ThrowsAsync(new InvalidOperationException($"provider failed for {PhoneNumber}"));
        var service = CreateService(provider, logger);

        var result = false;
        Assert.DoesNotThrowAsync(async () => result = await service.SendNotificationAsync(CreateRequest()));

        Assert.That(result, Is.False);
        logger.Verify(x => x.WarningAsync(
            It.Is<string>(message => message.Contains("+15***05") &&
                message.Contains(nameof(InvalidOperationException)) &&
                !message.Contains(PhoneNumber)),
            null,
            null), Times.Once);
    }

    private static WhatsAppNotificationService CreateService(
        Mock<IWhatsAppBusinessService> provider,
        Mock<ILogger> logger)
    {
        return new WhatsAppNotificationService(
            provider.Object,
            new WhatsAppBusinessSettings
            {
                IsEnabled = true,
                ApiKey = "test-api-key",
                PhoneNumberId = "test-phone-number-id",
                UseTemplateMessages = false
            },
            logger.Object);
    }

    private static WhatsAppNotificationRequest CreateRequest()
    {
        return new WhatsAppNotificationRequest
        {
            CustomerId = 17,
            PhoneNumber = PhoneNumber,
            MessageType = "AIInterview.ApplicantCompletion",
            MessageBody = "Completed"
        };
    }
}
