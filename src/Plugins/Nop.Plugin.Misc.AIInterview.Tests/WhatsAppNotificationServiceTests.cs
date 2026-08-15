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
