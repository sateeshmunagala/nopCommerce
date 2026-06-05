using Moq;
using NUnit.Framework;
using Nop.Plugin.Misc.AIInterview;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Helpers;
using System.Threading.Tasks;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class PluginDefaultsTests
{
    [Test]
    public async Task InstallAsync_SetsApplicantFlagsToTrue()
    {
        // Arrange
        var settingService = new Mock<ISettingService>();
        var localizationService = new Mock<ILocalizationService>();
        var webHelper = new Mock<IWebHelper>();
        var messageTemplateService = new Mock<IMessageTemplateService>();

        AIInterviewSettings savedSettings = null;
        settingService.Setup(s => s.SaveSettingAsync<AIInterviewSettings>(It.IsAny<AIInterviewSettings>(), It.IsAny<int>()))
            .Callback<Nop.Core.Configuration.ISettings, int>((settings, storeId) => {
                if (settings is AIInterviewSettings aiSettings) {
                    savedSettings = aiSettings;
                }
            })
            .Returns(Task.CompletedTask);

        var plugin = new AIInterviewPlugin(localizationService.Object, settingService.Object, webHelper.Object, messageTemplateService.Object);

        // Act
        try {
            await plugin.InstallAsync();
        } catch (System.Exception ex) when (ex is not NUnit.Framework.AssertionException) {
            // we catch other exceptions because InstallAsync attempts to insert message templates which we did not fully mock.
            // but we ensure the setup correctly sets the flags.
        }

        // Assert
        Assert.That(savedSettings, Is.Not.Null);
        Assert.That(savedSettings.Enabled, Is.True);
        Assert.That(savedSettings.ResumeRequired, Is.True);
        Assert.That(savedSettings.InterviewRequired, Is.True);
    }
}
