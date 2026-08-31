using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Forums;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.JobSupport.Contracts;
using Nop.Plugin.Misc.JobSupport.Data;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Forums;
using Nop.Services.Logging;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.JobSupport.Services;

public partial class JobSupportRelationshipService : IJobSupportRelationshipService
{
    private readonly ICustomerService _customerService;
    private readonly IForumService _forumService;
    private readonly IGenericAttributeService _genericAttributeService;
    private readonly ILogger _logger;
    private readonly IProductService _productService;
    private readonly IShoppingCartService _shoppingCartService;
    private readonly IStoreContext _storeContext;
    private readonly JobSupportSettings _settings;

    public JobSupportRelationshipService(ICustomerService customerService,
        IForumService forumService,
        IGenericAttributeService genericAttributeService,
        ILogger logger,
        IProductService productService,
        IShoppingCartService shoppingCartService,
        IStoreContext storeContext,
        JobSupportSettings settings)
    {
        _customerService = customerService;
        _forumService = forumService;
        _genericAttributeService = genericAttributeService;
        _logger = logger;
        _productService = productService;
        _shoppingCartService = shoppingCartService;
        _storeContext = storeContext;
        _settings = settings;
    }

    public Task<RelationshipActionResult> ShortlistProfileAsync(int sourceCustomerId, int targetProfileId)
    {
        return ApplyRelationshipAsync(sourceCustomerId,
            targetProfileId,
            RelationshipType.ShortlistedByMe,
            RelationshipType.ShortlistedMe,
            createMessage: false);
    }

    public async Task<RelationshipActionResult> RemoveShortlistAsync(int sourceCustomerId, int targetProfileId)
    {
        var context = await ValidateAsync(sourceCustomerId, targetProfileId, RelationshipType.ShortlistedByMe);
        if (context.Result != null)
            return context.Result;

        var store = await _storeContext.GetCurrentStoreAsync();
        var sourceRows = await GetRowsAsync(context.SourceCustomer,
            context.TargetProfile,
            RelationshipType.ShortlistedByMe,
            store.Id);
        if (!sourceRows.Any())
            return Success(context, RelationshipType.ShortlistedByMe, alreadyApplied: true, "Removed");

        if (_settings.ExecutionMode == WorkflowExecutionMode.Shadow)
        {
            await LogShadowAsync(RelationshipType.ShortlistedByMe, context, "remove");
            return Success(context, RelationshipType.ShortlistedByMe, alreadyApplied: false, "Removed");
        }

        if (_settings.ExecutionMode != WorkflowExecutionMode.Live)
            return Failure(sourceCustomerId, targetProfileId, RelationshipType.ShortlistedByMe, "WorkflowDisabled");

        foreach (var row in sourceRows)
            await _shoppingCartService.DeleteShoppingCartItemAsync(row);

        var sourceProfile = await GetProfileByCustomerAsync(context.SourceCustomer.Id);
        if (sourceProfile != null)
        {
            var mirrorRows = await GetRowsAsync(context.TargetCustomer,
                sourceProfile,
                RelationshipType.ShortlistedMe,
                store.Id);
            foreach (var row in mirrorRows)
                await _shoppingCartService.DeleteShoppingCartItemAsync(row);
        }

        return Success(context, RelationshipType.ShortlistedByMe, alreadyApplied: false, "Removed");
    }

    public Task<RelationshipActionResult> SendInterestAsync(int sourceCustomerId, int targetProfileId)
    {
        return ApplyRelationshipAsync(sourceCustomerId,
            targetProfileId,
            RelationshipType.InterestSent,
            RelationshipType.InterestReceived,
            createMessage: true);
    }

    public async Task<RelationshipActionResult> AcceptInterestAsync(int sourceCustomerId,
        int relationshipSourceCustomerId)
    {
        return await ApplyCustomerRelationshipAsync(sourceCustomerId,
            relationshipSourceCustomerId,
            RelationshipType.AcceptedByMe,
            RelationshipType.AcceptedMe);
    }

    public async Task<RelationshipActionResult> DeclineInterestAsync(int sourceCustomerId,
        int relationshipSourceCustomerId)
    {
        return await ApplyCustomerRelationshipAsync(sourceCustomerId,
            relationshipSourceCustomerId,
            RelationshipType.DeclinedByMe,
            RelationshipType.DeclinedMe);
    }

    public Task<RelationshipActionResult> BlockProfileAsync(int sourceCustomerId, int targetProfileId)
    {
        return ApplyRelationshipAsync(sourceCustomerId,
            targetProfileId,
            RelationshipType.BlockedByMe,
            RelationshipType.BlockedMe,
            createMessage: true,
            skipBlockedGuard: true);
    }

    public Task<RelationshipActionResult> RecordProfileViewAsync(int sourceCustomerId, int targetProfileId)
    {
        return ApplyRelationshipAsync(sourceCustomerId,
            targetProfileId,
            RelationshipType.ViewedByMe,
            RelationshipType.ViewedMe,
            createMessage: false);
    }

    private async Task<RelationshipActionResult> ApplyCustomerRelationshipAsync(int sourceCustomerId,
        int relationshipSourceCustomerId,
        RelationshipType sourceType,
        RelationshipType mirrorType)
    {
        var targetProfile = await GetProfileByCustomerAsync(relationshipSourceCustomerId);
        if (targetProfile == null)
            return Failure(sourceCustomerId, 0, sourceType, "ProfileNotFound");

        return await ApplyRelationshipAsync(sourceCustomerId,
            targetProfile.Id,
            sourceType,
            mirrorType,
            createMessage: true);
    }

    private async Task<RelationshipActionResult> ApplyRelationshipAsync(int sourceCustomerId,
        int targetProfileId,
        RelationshipType sourceType,
        RelationshipType mirrorType,
        bool createMessage,
        bool skipBlockedGuard = false)
    {
        var context = await ValidateAsync(sourceCustomerId, targetProfileId, sourceType, skipBlockedGuard);
        if (context.Result != null)
            return context.Result;

        var store = await _storeContext.GetCurrentStoreAsync();
        if ((await GetRowsAsync(context.SourceCustomer, context.TargetProfile, sourceType, store.Id)).Any())
            return Success(context, sourceType, alreadyApplied: true, "AlreadyApplied");

        var sourceProfile = await GetProfileByCustomerAsync(context.SourceCustomer.Id);
        if (sourceProfile == null)
            return Failure(sourceCustomerId, targetProfileId, sourceType, "SourceProfileNotFound");

        if (_settings.ExecutionMode == WorkflowExecutionMode.Shadow)
        {
            await LogShadowAsync(sourceType, context, "create");
            return Success(context, sourceType, alreadyApplied: false, "Applied");
        }

        if (_settings.ExecutionMode != WorkflowExecutionMode.Live)
            return Failure(sourceCustomerId, targetProfileId, sourceType, "WorkflowDisabled");

        var warnings = await AddCompatibilityRowAsync(context.SourceCustomer, context.TargetProfile, sourceType, store.Id);
        if (warnings.Any())
            return Failure(sourceCustomerId, targetProfileId, sourceType, "CompatibilityWriteRejected");

        if (!(await GetRowsAsync(context.TargetCustomer, sourceProfile, mirrorType, store.Id)).Any())
        {
            warnings = await AddCompatibilityRowAsync(context.TargetCustomer, sourceProfile, mirrorType, store.Id);
            if (warnings.Any())
                return Failure(sourceCustomerId, targetProfileId, sourceType, "CompatibilityMirrorWriteRejected");
        }

        if (createMessage && _settings.EnableRelationshipNotifications)
            await CreatePrivateMessageAsync(context, sourceType, store.Id);

        return Success(context, sourceType, alreadyApplied: false, "Applied");
    }

    private async Task<RelationshipContext> ValidateAsync(int sourceCustomerId,
        int targetProfileId,
        RelationshipType relationshipType,
        bool skipBlockedGuard = false)
    {
        if (!_settings.Enabled || _settings.ExecutionMode == WorkflowExecutionMode.Disabled)
            return new RelationshipContext
            {
                Result = Failure(sourceCustomerId, targetProfileId, relationshipType, "WorkflowDisabled")
            };

        var sourceCustomer = await _customerService.GetCustomerByIdAsync(sourceCustomerId);
        if (sourceCustomer == null || sourceCustomer.Deleted)
        {
            return new RelationshipContext
            {
                Result = Failure(sourceCustomerId, targetProfileId, relationshipType, "SourceCustomerNotFound")
            };
        }

        var targetProfile = await _productService.GetProductByIdAsync(targetProfileId);
        if (targetProfile == null || targetProfile.Deleted || targetProfile.VendorId <= 0)
        {
            return new RelationshipContext
            {
                Result = Failure(sourceCustomerId, targetProfileId, relationshipType, "ProfileNotFound")
            };
        }

        var targetCustomer = await _customerService.GetCustomerByIdAsync(targetProfile.VendorId);
        if (targetCustomer == null || targetCustomer.Deleted)
        {
            return new RelationshipContext
            {
                Result = Failure(sourceCustomerId, targetProfileId, relationshipType, "ProfileCustomerNotFound")
            };
        }

        if (sourceCustomer.Id == targetCustomer.Id)
        {
            return new RelationshipContext
            {
                Result = Failure(sourceCustomerId, targetProfileId, relationshipType, "SelfRelationship")
            };
        }

        var context = new RelationshipContext
        {
            SourceCustomer = sourceCustomer,
            TargetCustomer = targetCustomer,
            TargetProfile = targetProfile
        };
        if (!skipBlockedGuard && await IsBlockedAsync(context))
            context.Result = Failure(sourceCustomerId, targetProfileId, relationshipType, "RelationshipBlocked");

        return context;
    }

    private async Task<bool> IsBlockedAsync(RelationshipContext context)
    {
        var store = await _storeContext.GetCurrentStoreAsync();
        if ((await GetRowsAsync(context.SourceCustomer,
                context.TargetProfile,
                RelationshipType.BlockedByMe,
                store.Id)).Any())
            return true;

        var sourceProfile = await GetProfileByCustomerAsync(context.SourceCustomer.Id);
        return sourceProfile != null &&
               (await GetRowsAsync(context.TargetCustomer,
                   sourceProfile,
                   RelationshipType.BlockedByMe,
                   store.Id)).Any();
    }

    private async Task<Product> GetProfileByCustomerAsync(int customerId)
    {
        var customer = await _customerService.GetCustomerByIdAsync(customerId);
        if (customer?.VendorId > 0)
        {
            var linked = await _productService.GetProductByIdAsync(customer.VendorId);
            if (linked != null && !linked.Deleted && linked.VendorId == customerId)
                return linked;
        }

        return (await _productService.SearchProductsAsync(vendorId: customerId,
            showHidden: true,
            overridePublished: null)).FirstOrDefault();
    }

    private async Task<IList<ShoppingCartItem>> GetRowsAsync(Customer customer,
        Product profile,
        RelationshipType relationshipType,
        int storeId)
    {
        return await _shoppingCartService.GetShoppingCartAsync(customer,
            GetCompatibilityCartType(relationshipType),
            storeId,
            profile.Id);
    }

    private Task<IList<string>> AddCompatibilityRowAsync(Customer customer,
        Product profile,
        RelationshipType relationshipType,
        int storeId)
    {
        return _shoppingCartService.AddToCartAsync(customer,
            profile,
            GetCompatibilityCartType(relationshipType),
            storeId);
    }

    private static ShoppingCartType GetCompatibilityCartType(RelationshipType relationshipType)
    {
        if (!LegacyRelationshipMapper.TryMapToLegacyCartType(relationshipType, out var cartTypeId))
            throw new InvalidOperationException($"No compatibility mapping exists for {relationshipType}.");

        return (ShoppingCartType)cartTypeId;
    }

    private async Task CreatePrivateMessageAsync(RelationshipContext context,
        RelationshipType relationshipType,
        int storeId)
    {
        var privateMessage = new PrivateMessage
        {
            StoreId = storeId,
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
        string operation)
    {
        return _logger.InformationAsync(
            $"JobSupport shadow relationship outcome: {operation} {relationshipType}, source {context.SourceCustomer.Id}, profile {context.TargetProfile.Id}.");
    }

    private static RelationshipActionResult Success(RelationshipContext context,
        RelationshipType relationshipType,
        bool alreadyApplied,
        string messageSuffix)
    {
        return new RelationshipActionResult
        {
            Succeeded = true,
            AlreadyApplied = alreadyApplied,
            RelationshipType = relationshipType,
            SourceCustomerId = context.SourceCustomer.Id,
            TargetCustomerId = context.TargetCustomer.Id,
            ProfileProductId = context.TargetProfile.Id,
            UserMessageKey = $"Plugins.Misc.JobSupport.Relationship.{messageSuffix}"
        };
    }

    private static RelationshipActionResult Failure(int sourceCustomerId,
        int profileProductId,
        RelationshipType relationshipType,
        string errorCode)
    {
        return new RelationshipActionResult
        {
            RelationshipType = relationshipType,
            SourceCustomerId = sourceCustomerId,
            ProfileProductId = profileProductId,
            ErrorCode = errorCode,
            UserMessageKey = $"Plugins.Misc.JobSupport.Relationship.Errors.{errorCode}"
        };
    }

    private sealed class RelationshipContext
    {
        public Customer SourceCustomer { get; init; }
        public Customer TargetCustomer { get; init; }
        public Product TargetProfile { get; init; }
        public RelationshipActionResult Result { get; set; }
    }
}
