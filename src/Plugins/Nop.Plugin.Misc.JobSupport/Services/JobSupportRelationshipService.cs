using LinqToDB;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Forums;
using Nop.Data;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Domain;
using Nop.Plugin.Misc.JobSupport.Domain.Enums;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Forums;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial class JobSupportRelationshipService : IJobSupportRelationshipService
{
    private readonly ICustomerService _customerService;
    private readonly IForumService _forumService;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly ILogger _logger;
    private readonly IStoreContext _storeContext;
    private readonly JobSupportSettings _settings;
    private readonly IRepository<JobSupportProfile> _profileRepository;
    private readonly IRepository<JobSupportProfileView> _profileViewRepository;
    private readonly IRepository<JobSupportRelationship> _relationshipRepository;

    public JobSupportRelationshipService(ICustomerService customerService,
        IForumService forumService,
        IGenericAttributeService genericAttributeService,
        ILogger logger,
        IStoreContext storeContext,
        JobSupportSettings settings,
        IRepository<JobSupportProfile> profileRepository,
        IRepository<JobSupportProfileView> profileViewRepository,
        IRepository<JobSupportRelationship> relationshipRepository)
    {
        _customerService = customerService;
        _forumService = forumService;
        _genericAttributeService = genericAttributeService;
        _logger = logger;
        _storeContext = storeContext;
        _settings = settings;
        _profileRepository = profileRepository;
        _profileViewRepository = profileViewRepository;
        _relationshipRepository = relationshipRepository;
    }

    public Task<RelationshipActionResult> ShortlistProfileAsync(int sourceCustomerId, int targetProfileId) =>
        ApplyRelationshipAsync(sourceCustomerId, targetProfileId, RelationshipType.ShortlistedByMe, false);

    public async Task<RelationshipActionResult> RemoveShortlistAsync(int sourceCustomerId, int targetProfileId)
    {
        var context = await ValidateAsync(sourceCustomerId, targetProfileId, RelationshipType.ShortlistedByMe);
        if (context.Result != null)
            return context.Result;

        if (_settings.ExecutionMode == WorkflowExecutionMode.Shadow)
        {
            await LogShadowAsync(RelationshipType.ShortlistedByMe, context, "remove");
            return Success(context, RelationshipType.ShortlistedByMe, false, "Removed");
        }

        if (_settings.ExecutionMode != WorkflowExecutionMode.Live)
            return Failure(sourceCustomerId, targetProfileId, RelationshipType.ShortlistedByMe, "WorkflowDisabled");

        var relationship = await _relationshipRepository.Table.FirstOrDefaultAsync(item =>
            item.SourceCustomerId == context.SourceCustomer.Id &&
            item.TargetCustomerId == context.TargetCustomer.Id &&
            item.RelationshipTypeId == 1);
        if (relationship == null || relationship.StatusId == (int)RelationshipStatus.Removed)
            return Success(context, RelationshipType.ShortlistedByMe, true, "Removed");

        relationship.StatusId = (int)RelationshipStatus.Removed;
        relationship.UpdatedOnUtc = DateTime.UtcNow;
        await _relationshipRepository.UpdateAsync(relationship, false);
        return Success(context, RelationshipType.ShortlistedByMe, false, "Removed");
    }

    public Task<RelationshipActionResult> SendInterestAsync(int sourceCustomerId, int targetProfileId) =>
        ApplyRelationshipAsync(sourceCustomerId, targetProfileId, RelationshipType.InterestSent, true);

    public Task<RelationshipActionResult> AcceptInterestAsync(int sourceCustomerId, int relationshipSourceCustomerId) =>
        ApplyCustomerRelationshipAsync(sourceCustomerId, relationshipSourceCustomerId, RelationshipType.AcceptedByMe);

    public Task<RelationshipActionResult> DeclineInterestAsync(int sourceCustomerId, int relationshipSourceCustomerId) =>
        ApplyCustomerRelationshipAsync(sourceCustomerId, relationshipSourceCustomerId, RelationshipType.DeclinedByMe);

    public Task<RelationshipActionResult> BlockProfileAsync(int sourceCustomerId, int targetProfileId) =>
        ApplyRelationshipAsync(sourceCustomerId, targetProfileId, RelationshipType.BlockedByMe, true, true);

    public Task<RelationshipActionResult> RecordProfileViewAsync(int sourceCustomerId, int targetProfileId) =>
        ApplyRelationshipAsync(sourceCustomerId, targetProfileId, RelationshipType.ViewedByMe, false);

    private async Task<RelationshipActionResult> ApplyCustomerRelationshipAsync(int sourceCustomerId,
        int relationshipSourceCustomerId,
        RelationshipType relationshipType)
    {
        var profile = await _profileRepository.Table.FirstOrDefaultAsync(item =>
            item.CustomerId == relationshipSourceCustomerId);
        if (profile == null)
            return Failure(sourceCustomerId, 0, relationshipType, "ProfileNotFound");

        return await ApplyRelationshipAsync(sourceCustomerId, profile.Id, relationshipType, true);
    }

    private async Task<RelationshipActionResult> ApplyRelationshipAsync(int sourceCustomerId,
        int targetProfileId,
        RelationshipType relationshipType,
        bool createMessage,
        bool skipBlockedGuard = false)
    {
        var context = await ValidateAsync(sourceCustomerId, targetProfileId, relationshipType, skipBlockedGuard);
        if (context.Result != null)
            return context.Result;

        if (_settings.ExecutionMode == WorkflowExecutionMode.Shadow)
        {
            await LogShadowAsync(relationshipType, context, "create");
            return Success(context, relationshipType, false, "Applied");
        }

        if (_settings.ExecutionMode != WorkflowExecutionMode.Live)
            return Failure(sourceCustomerId, targetProfileId, relationshipType, "WorkflowDisabled");

        var alreadyApplied = await IsAlreadyAppliedAsync(context, relationshipType);
        if (!alreadyApplied)
        {
            await UpsertRelationshipAsync(context, relationshipType, DateTime.UtcNow);
            if (createMessage && _settings.EnableRelationshipNotifications)
                await CreatePrivateMessageAsync(context, relationshipType);
        }

        return Success(context,
            relationshipType,
            alreadyApplied,
            alreadyApplied ? "AlreadyApplied" : "Applied");
    }

    private async Task UpsertRelationshipAsync(RelationshipContext context,
        RelationshipType relationshipType,
        DateTime occurredOnUtc)
    {
        if (relationshipType is RelationshipType.ViewedByMe or RelationshipType.ViewedMe)
        {
            var view = await _profileViewRepository.Table.FirstOrDefaultAsync(item =>
                item.ViewerCustomerId == context.SourceCustomer.Id &&
                item.ViewedProfileId == context.TargetProfile.Id);
            if (view == null)
            {
                await _profileViewRepository.InsertAsync(new JobSupportProfileView
                {
                    ViewerCustomerId = context.SourceCustomer.Id,
                    ViewedCustomerId = context.TargetCustomer.Id,
                    ViewerProfileId = context.SourceProfile.Id,
                    ViewedProfileId = context.TargetProfile.Id,
                    FirstViewedOnUtc = occurredOnUtc,
                    LastViewedOnUtc = occurredOnUtc,
                    ViewCount = 1
                }, false);
            }
            else
            {
                view.LastViewedOnUtc = occurredOnUtc;
                view.ViewCount++;
                await _profileViewRepository.UpdateAsync(view, false);
            }

            return;
        }

        var reverse = relationshipType is RelationshipType.AcceptedByMe or RelationshipType.AcceptedMe or
            RelationshipType.DeclinedByMe or RelationshipType.DeclinedMe;
        var sourceCustomerId = reverse ? context.TargetCustomer.Id : context.SourceCustomer.Id;
        var targetCustomerId = reverse ? context.SourceCustomer.Id : context.TargetCustomer.Id;
        var sourceProfile = reverse ? context.TargetProfile : context.SourceProfile;
        var targetProfile = reverse ? context.SourceProfile : context.TargetProfile;
        var typeId = GetRelationshipTypeId(relationshipType);
        var status = GetRelationshipStatus(relationshipType);

        var entity = await _relationshipRepository.Table.FirstOrDefaultAsync(item =>
            item.SourceCustomerId == sourceCustomerId &&
            item.TargetCustomerId == targetCustomerId &&
            item.RelationshipTypeId == typeId);
        if (entity == null)
        {
            await _relationshipRepository.InsertAsync(new JobSupportRelationship
            {
                SourceCustomerId = sourceCustomerId,
                TargetCustomerId = targetCustomerId,
                SourceProfileId = sourceProfile.Id,
                TargetProfileId = targetProfile.Id,
                RelationshipTypeId = typeId,
                StatusId = (int)status,
                CreatedOnUtc = occurredOnUtc,
                UpdatedOnUtc = occurredOnUtc,
                RespondedOnUtc = status is RelationshipStatus.Accepted or RelationshipStatus.Declined
                    ? occurredOnUtc
                    : null
            }, false);
            return;
        }

        entity.StatusId = (int)status;
        entity.UpdatedOnUtc = occurredOnUtc;
        if (status is RelationshipStatus.Accepted or RelationshipStatus.Declined)
            entity.RespondedOnUtc = occurredOnUtc;
        await _relationshipRepository.UpdateAsync(entity, false);
    }

    private async Task<bool> IsAlreadyAppliedAsync(RelationshipContext context, RelationshipType relationshipType)
    {
        if (relationshipType is RelationshipType.ViewedByMe or RelationshipType.ViewedMe)
            return false;

        var reverse = relationshipType is RelationshipType.AcceptedByMe or RelationshipType.AcceptedMe or
            RelationshipType.DeclinedByMe or RelationshipType.DeclinedMe;
        var sourceCustomerId = reverse ? context.TargetCustomer.Id : context.SourceCustomer.Id;
        var targetCustomerId = reverse ? context.SourceCustomer.Id : context.TargetCustomer.Id;
        return await _relationshipRepository.Table.AnyAsync(item =>
            item.SourceCustomerId == sourceCustomerId &&
            item.TargetCustomerId == targetCustomerId &&
            item.RelationshipTypeId == GetRelationshipTypeId(relationshipType) &&
            item.StatusId == (int)GetRelationshipStatus(relationshipType));
    }

    private async Task<RelationshipContext> ValidateAsync(int sourceCustomerId,
        int targetProfileId,
        RelationshipType relationshipType,
        bool skipBlockedGuard = false)
    {
        if (!_settings.Enabled || _settings.ExecutionMode == WorkflowExecutionMode.Disabled)
            return Invalid(sourceCustomerId, targetProfileId, relationshipType, "WorkflowDisabled");

        var sourceCustomer = await _customerService.GetCustomerByIdAsync(sourceCustomerId);
        if (sourceCustomer == null || sourceCustomer.Deleted)
            return Invalid(sourceCustomerId, targetProfileId, relationshipType, "SourceCustomerNotFound");

        var sourceProfile = await _profileRepository.Table.FirstOrDefaultAsync(profile =>
            profile.CustomerId == sourceCustomerId);
        var targetProfile = await _profileRepository.Table.FirstOrDefaultAsync(profile =>
            profile.Id == targetProfileId || profile.LegacyProductId == targetProfileId);
        if (sourceProfile == null || targetProfile == null || !targetProfile.IsPublished)
            return Invalid(sourceCustomerId, targetProfileId, relationshipType, "ProfileNotFound");

        var targetCustomer = await _customerService.GetCustomerByIdAsync(targetProfile.CustomerId);
        if (targetCustomer == null || targetCustomer.Deleted)
            return Invalid(sourceCustomerId, targetProfileId, relationshipType, "ProfileCustomerNotFound");
        if (sourceCustomer.Id == targetCustomer.Id)
            return Invalid(sourceCustomerId, targetProfileId, relationshipType, "SelfRelationship");

        var context = new RelationshipContext
        {
            SourceCustomer = sourceCustomer,
            TargetCustomer = targetCustomer,
            SourceProfile = sourceProfile,
            TargetProfile = targetProfile,
            TargetIdentifier = targetProfileId
        };
        if (!skipBlockedGuard && await IsBlockedAsync(context))
            context.Result = Failure(sourceCustomerId, targetProfileId, relationshipType, "RelationshipBlocked");

        return context;
    }

    private Task<bool> IsBlockedAsync(RelationshipContext context) =>
        _relationshipRepository.Table.AnyAsync(item =>
            item.RelationshipTypeId == 3 &&
            item.StatusId == (int)RelationshipStatus.Blocked &&
            ((item.SourceCustomerId == context.SourceCustomer.Id &&
              item.TargetCustomerId == context.TargetCustomer.Id) ||
             (item.SourceCustomerId == context.TargetCustomer.Id &&
              item.TargetCustomerId == context.SourceCustomer.Id)));

    private async Task CreatePrivateMessageAsync(RelationshipContext context, RelationshipType relationshipType)
    {
        var privateMessage = new PrivateMessage
        {
            StoreId = (await _storeContext.GetCurrentStoreAsync()).Id,
            FromCustomerId = context.SourceCustomer.Id,
            ToCustomerId = context.TargetCustomer.Id,
            Subject = "Job Support relationship update",
            Text = "A Job Support relationship action was recorded.",
            IsRead = false,
            IsDeletedByAuthor = false,
            IsDeletedByRecipient = false,
            CreatedOnUtc = DateTime.UtcNow
        };
        await _forumService.InsertPrivateMessageAsync(privateMessage);
        await _genericAttributeService.SaveAttributeAsync(privateMessage,
            JobSupportDefaults.RelationshipTypeAttribute,
            relationshipType.ToString());
        await _genericAttributeService.SaveAttributeAsync(privateMessage,
            JobSupportDefaults.RelationshipProfileIdAttribute,
            context.TargetProfile.Id);
    }

    private Task LogShadowAsync(RelationshipType relationshipType,
        RelationshipContext context,
        string operation) =>
        _logger.InformationAsync(
            $"JobSupport shadow relationship outcome: {operation} {relationshipType}, source {context.SourceCustomer.Id}, profile {context.TargetIdentifier}.");

    private static int GetRelationshipTypeId(RelationshipType relationshipType) => relationshipType switch
    {
        RelationshipType.ShortlistedByMe or RelationshipType.ShortlistedMe => 1,
        RelationshipType.InterestSent or RelationshipType.InterestReceived or
            RelationshipType.AcceptedByMe or RelationshipType.AcceptedMe or
            RelationshipType.DeclinedByMe or RelationshipType.DeclinedMe => 2,
        RelationshipType.BlockedByMe or RelationshipType.BlockedMe => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(relationshipType))
    };

    private static RelationshipStatus GetRelationshipStatus(RelationshipType relationshipType) => relationshipType switch
    {
        RelationshipType.InterestSent or RelationshipType.InterestReceived => RelationshipStatus.Pending,
        RelationshipType.AcceptedByMe or RelationshipType.AcceptedMe => RelationshipStatus.Accepted,
        RelationshipType.DeclinedByMe or RelationshipType.DeclinedMe => RelationshipStatus.Declined,
        RelationshipType.BlockedByMe or RelationshipType.BlockedMe => RelationshipStatus.Blocked,
        _ => RelationshipStatus.Active
    };

    private static RelationshipContext Invalid(int sourceCustomerId,
        int targetProfileId,
        RelationshipType relationshipType,
        string errorCode) => new()
    {
        Result = Failure(sourceCustomerId, targetProfileId, relationshipType, errorCode)
    };

    private static RelationshipActionResult Success(RelationshipContext context,
        RelationshipType relationshipType,
        bool alreadyApplied,
        string messageSuffix) => new()
    {
        Succeeded = true,
        AlreadyApplied = alreadyApplied,
        RelationshipType = relationshipType,
        SourceCustomerId = context.SourceCustomer.Id,
        TargetCustomerId = context.TargetCustomer.Id,
        ProfileProductId = context.TargetIdentifier,
        UserMessageKey = $"Plugins.Misc.JobSupport.Relationship.{messageSuffix}"
    };

    private static RelationshipActionResult Failure(int sourceCustomerId,
        int profileProductId,
        RelationshipType relationshipType,
        string errorCode) => new()
    {
        RelationshipType = relationshipType,
        SourceCustomerId = sourceCustomerId,
        ProfileProductId = profileProductId,
        ErrorCode = errorCode,
        UserMessageKey = $"Plugins.Misc.JobSupport.Relationship.Errors.{errorCode}"
    };

    private sealed class RelationshipContext
    {
        public Customer SourceCustomer { get; init; }
        public Customer TargetCustomer { get; init; }
        public JobSupportProfile SourceProfile { get; init; }
        public JobSupportProfile TargetProfile { get; init; }
        public int TargetIdentifier { get; init; }
        public RelationshipActionResult Result { get; set; }
    }
}
