using Nop.Core.Domain.Affiliates;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Services.Affiliates;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial class JobSupportAffiliateService : IJobSupportAffiliateService
{
    private readonly IAffiliateService _affiliateService;
    private readonly ICustomerService _customerService;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly ILogger _logger;

    public JobSupportAffiliateService(IAffiliateService affiliateService,
        ICustomerService customerService,
        IGenericAttributeService genericAttributeService,
        ILogger logger)
    {
        _affiliateService = affiliateService;
        _customerService = customerService;
        _genericAttributeService = genericAttributeService;
        _logger = logger;
    }

    public async Task EnsureAffiliateAsync(Customer customer, WorkflowExecutionMode mode)
    {
        ArgumentNullException.ThrowIfNull(customer);

        var affiliateId = await _genericAttributeService.GetAttributeAsync<int>(customer,
            JobSupportDefaults.AffiliateIdAttribute);
        if (affiliateId > 0 && await _affiliateService.GetAffiliateByIdAsync(affiliateId) != null)
            return;

        var friendlyUrlSeed = $"job-support-{customer.CustomerGuid:N}";
        var existingAffiliate = await _affiliateService.GetAffiliateByFriendlyUrlNameAsync(friendlyUrlSeed);
        if (existingAffiliate != null)
        {
            if (mode == WorkflowExecutionMode.Live)
            {
                await _genericAttributeService.SaveAttributeAsync(customer,
                    JobSupportDefaults.AffiliateIdAttribute,
                    existingAffiliate.Id);
            }

            return;
        }

        var billingAddress = await _customerService.GetCustomerBillingAddressAsync(customer);
        if (billingAddress == null)
            return;

        if (mode == WorkflowExecutionMode.Shadow)
        {
            await _logger.InformationAsync($"JobSupport shadow affiliate outcome: create affiliate for customer {customer.Id}.");
            return;
        }

        if (mode != WorkflowExecutionMode.Live)
            return;

        var affiliate = new Affiliate
        {
            Active = true,
            AddressId = billingAddress.Id,
            AdminComment = "Created by the JobSupport registration workflow"
        };
        affiliate.FriendlyUrlName = await _affiliateService.ValidateFriendlyUrlNameAsync(affiliate, friendlyUrlSeed);

        await _affiliateService.InsertAffiliateAsync(affiliate);
        await _genericAttributeService.SaveAttributeAsync(customer, JobSupportDefaults.AffiliateIdAttribute, affiliate.Id);
    }
}
