using Moq;
using Nop.Data;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Services;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class ServiceTests
{
    private Mock<IRepository<JobApplication>> _applicationRepository;
    private ApplicationService _applicationService;

    [SetUp]
    public void SetUp()
    {
        _applicationRepository = new Mock<IRepository<JobApplication>>();
        _applicationService = new ApplicationService(_applicationRepository.Object);
    }

    [Test]
    public async Task CanInsertJobApplication()
    {
        var application = new JobApplication { JobTitle = "Software Engineer" };
        await _applicationService.InsertJobApplicationAsync(application);
        _applicationRepository.Verify(r => r.InsertAsync(application, true), Times.Once);
    }
}
