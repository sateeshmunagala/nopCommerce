using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Messages;
using Nop.Core.Events;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Messages;
using Nop.Services.Seo;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial class JobSupportNotificationService : IJobSupportNotificationService
{
    private readonly ICustomerService _customerService;
    private readonly IEmailAccountService _emailAccountService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly IMessageTemplateService _messageTemplateService;
    private readonly IMessageTokenProvider _messageTokenProvider;
    private readonly ISpecificationAttributeService _specificationAttributeService;
    private readonly IStoreContext _storeContext;
    private readonly IUrlRecordService _urlRecordService;
    private readonly IWorkflowMessageService _workflowMessageService;
    private readonly EmailAccountSettings _emailAccountSettings;
    private readonly JobSupportSettings _settings;

    public JobSupportNotificationService(ICustomerService customerService,
        IEmailAccountService emailAccountService,
        IEventPublisher eventPublisher,
        IGenericAttributeService genericAttributeService,
        IMessageTemplateService messageTemplateService,
        IMessageTokenProvider messageTokenProvider,
        ISpecificationAttributeService specificationAttributeService,
        IStoreContext storeContext,
        IUrlRecordService urlRecordService,
        IWorkflowMessageService workflowMessageService,
        EmailAccountSettings emailAccountSettings,
        JobSupportSettings settings)
    {
        _customerService = customerService;
        _emailAccountService = emailAccountService;
        _eventPublisher = eventPublisher;
        _genericAttributeService = genericAttributeService;
        _messageTemplateService = messageTemplateService;
        _messageTokenProvider = messageTokenProvider;
        _specificationAttributeService = specificationAttributeService;
        _storeContext = storeContext;
        _urlRecordService = urlRecordService;
        _workflowMessageService = workflowMessageService;
        _emailAccountSettings = emailAccountSettings;
        _settings = settings;
    }

    public async Task<bool> QueueProfileAvailableNotificationAsync(Product profile, Customer recipient)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(recipient);

        if (string.IsNullOrWhiteSpace(recipient.Email))
            return false;

        var store = await _storeContext.GetCurrentStoreAsync();
        var template = (await _messageTemplateService.GetMessageTemplatesByNameAsync(
                JobSupportDefaults.CustomerAvailableMessageTemplateSystemName,
                store.Id))
            .FirstOrDefault(messageTemplate => messageTemplate.IsActive);
        if (template == null)
            return false;

        var emailAccountId = template.EmailAccountId > 0
            ? template.EmailAccountId
            : _emailAccountSettings.DefaultEmailAccountId;
        var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(emailAccountId);
        if (emailAccount == null)
            return false;

        var profileCustomer = profile.VendorId > 0
            ? await _customerService.GetCustomerByIdAsync(profile.VendorId)
            : null;
        var availability = profileCustomer == null
            ? string.Empty
            : await _genericAttributeService.GetAttributeAsync<string>(profileCustomer,
                JobSupportDefaults.CurrentAvailabilityAttribute,
                store.Id);
        var recipientName = await _customerService.GetCustomerFullNameAsync(recipient);
        var profileSlug = await _urlRecordService.GetSeNameAsync(profile);
        var profileUrl = string.IsNullOrWhiteSpace(profileSlug)
            ? store.Url
            : $"{store.Url.TrimEnd('/')}/{profileSlug}";
        var skills = await GetSkillsAsync(profile.Id);

        var tokens = new List<Token>
        {
            new("JobSupport.ProfileId", profile.Id),
            new("JobSupport.ProfileName", profile.Name),
            new("JobSupport.ProfileUrl", profileUrl, true),
            new("JobSupport.ProfileShortDescription", profile.ShortDescription, true),
            new("JobSupport.ProfileSkills", skills),
            new("JobSupport.CustomerFullName", recipientName),
            new("JobSupport.Availability", availability)
        };
        await _messageTokenProvider.AddStoreTokensAsync(tokens, store, emailAccount);
        await _eventPublisher.PublishAsync(new MessageTokensAddedEvent<Token>(template, tokens));

        var isPremium = !string.IsNullOrWhiteSpace(_settings.PaidCustomerRoleSystemName) &&
                        await _customerService.IsInCustomerRoleAsync(recipient,
                            _settings.PaidCustomerRoleSystemName,
                            false);
        var queuedTemplate = CopyForRecipient(template, isPremium ? null : 1);
        var languageId = recipient.LanguageId.GetValueOrDefault() > 0
            ? recipient.LanguageId.Value
            : store.DefaultLanguageId;
        var queuedEmailId = await _workflowMessageService.SendNotificationAsync(queuedTemplate,
            emailAccount,
            languageId,
            tokens,
            recipient.Email,
            recipientName);
        return queuedEmailId > 0;
    }

    private async Task<string> GetSkillsAsync(int productId)
    {
        var mappings = await _specificationAttributeService.GetProductSpecificationAttributesAsync(productId);
        var optionIds = mappings.Select(mapping => mapping.SpecificationAttributeOptionId).Distinct().ToArray();
        if (optionIds.Length == 0)
            return string.Empty;

        var options = await _specificationAttributeService.GetSpecificationAttributeOptionsByIdsAsync(optionIds);
        return string.Join(", ", options.Select(option => option.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static MessageTemplate CopyForRecipient(MessageTemplate template, int? delayBeforeSend)
    {
        return new MessageTemplate
        {
            Id = template.Id,
            Name = template.Name,
            BccEmailAddresses = template.BccEmailAddresses,
            Subject = template.Subject,
            Body = template.Body,
            IsActive = template.IsActive,
            DelayBeforeSend = delayBeforeSend,
            DelayPeriod = MessageDelayPeriod.Hours,
            AttachedDownloadId = template.AttachedDownloadId,
            AllowDirectReply = template.AllowDirectReply,
            EmailAccountId = template.EmailAccountId,
            LimitedToStores = template.LimitedToStores
        };
    }
}
