﻿
using Nop.Core;
using Nop.Core.Domain.Affiliates;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Events;
using Nop.Services.Affiliates;
using Nop.Services.Attributes;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Events;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Seo;
using Nop.Web.Areas.Admin.Infrastructure.Mapper.Extensions;
using Nop.Web.Areas.Admin.Models.Catalog;
using Nop.Web.Framework.Events;
using Nop.Web.Framework.Models;
using Nop.Web.Models.Catalog;
using Nop.Web.Models.Common;
using Nop.Web.Models.Customer;
using Nop.Web.Models.Media;


namespace Nop.CustomExtensions.Services
{
    /// <summary>
    /// Represents event consumer
    /// </summary>
    public class EventConsumer : IConsumer<OrderPaidEvent>,
        IConsumer<CustomerRegisteredEvent>,
        IConsumer<CustomerActivatedEvent>,
        IConsumer<EntityInsertedEvent<Category>>,
        IConsumer<EntityUpdatedEvent<Category>>,
        IConsumer<EntityInsertedEvent<GenericAttribute>>,
        IConsumer<EntityUpdatedEvent<GenericAttribute>>,
        IConsumer<EntityDeletedEvent<GenericAttribute>>,
        IConsumer<EntityTokensAddedEvent<Customer, Token>>,
        IConsumer<MessageTokensAddedEvent<Token>>,
        IConsumer<ModelPreparedEvent<BaseNopModel>>
    {
        #region Fields

        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ICustomerService _customerService;
        private readonly Nop.Services.Logging.ILogger _logger;
        private readonly ICustomerActivityService _customerActivityService;
        private readonly ShoppingCartSettings _shoppingCartSettings;
        private readonly IOrderService _orderService;
        private readonly IStoreContext _storeContext;
        private readonly IAffiliateService _affiliateService;
        private readonly IAddressService _addressService;
        private readonly ILocalizationService _localizationService;

        protected readonly IAttributeParser<CustomerAttribute, CustomerAttributeValue> _customerAttributeParser;
        private readonly ISpecificationAttributeService _specificationAttributeService;
        private readonly ICategoryService _categoryService;
        private readonly IUrlRecordService _urlRecordService;
        private readonly IProductService _productService;
        private readonly CustomerSettings _customerSettings;
        private readonly IWorkContext _workContext;
        protected readonly IWebHelper _webHelper;
        private readonly IWorkflowMessageService _workflowMessageService;
        private readonly LocalizationSettings _localizationSettings;
        private readonly IRewardPointService _rewardPointService;

        #endregion

        #region Ctor

        public EventConsumer(IGenericAttributeService genericAttributeService,
             ICustomerService customerService,
             Nop.Services.Logging.ILogger logger,
             IStoreContext storeContext,
             ShoppingCartSettings shoppingCartSettings,
             IOrderService orderService,
             IAffiliateService affiliateService,
             IAddressService addressService,
             ILocalizationService localizationService,
             ICustomerActivityService customerActivityService,

             IAttributeParser<CustomerAttribute, CustomerAttributeValue> customerAttributeParser,
             ISpecificationAttributeService specificationAttributeService,
             ICategoryService categoryService,
             IUrlRecordService urlRecordService,
             IProductService productService,
             CustomerSettings customerSettings,
             IWorkContext workContext,
             IWebHelper webHelper,
             IWorkflowMessageService workflowMessageService,
             LocalizationSettings localizationSettings,
             IRewardPointService rewardPointService
            )
        {
            _genericAttributeService = genericAttributeService;
            _customerService = customerService;
            _logger = logger;
            _customerActivityService = customerActivityService;
            _shoppingCartSettings = shoppingCartSettings;
            _orderService = orderService;
            _storeContext = storeContext;
            _affiliateService = affiliateService;
            _addressService = addressService;
            _localizationService = localizationService;

            _customerAttributeParser = customerAttributeParser;
            _specificationAttributeService = specificationAttributeService;
            _categoryService = categoryService;
            _urlRecordService = urlRecordService;
            _productService = productService;
            _customerSettings = customerSettings;
            _workContext = workContext;
            _webHelper = webHelper;
            _workflowMessageService = workflowMessageService;
            _localizationSettings = localizationSettings;
            _rewardPointService = rewardPointService;
        }

        #endregion

        #region Events

        public async Task HandleEventAsync(OrderPaidEvent eventMessage)
        {
            await AddCustomerToPaidCustomerRole(eventMessage.Order.CustomerId);

            //await AddCustomerSubscriptionInfoToGenericAttributes(eventMessage.Order);
            await AddCustomerSubscriptionInfoToOrderAsync(eventMessage.Order);

            //new method to use reward (credit) point system for credits
            await AddCustomerSubscriptionInfoToRewardPointsAsync(eventMessage.Order);
        }

        public async Task HandleEventAsync(CustomerRegisteredEvent eventMessage)
        {
            var customer = eventMessage.Customer;

            //update customer customerprofiletypeid
            await UpdateCustomerCustomerProfileTypeIdAsync(customer);

            //add customer to givesupport/take support roles
            await AddCustomerToJobSupportRoleAsync(customer);

            //create product immediatly after customer registered
            await CreateProductAsync(customer, customer.CustomCustomerAttributesXML, customer.FirstName, customer.LastName, customer.Gender);

            //create customer as customer affliate so that he can refer his friends.
            await CreateCustomerAffliateAsync(customer);

        }

        public async Task HandleEventAsync(CustomerActivatedEvent eventMessage)
        {
            var customer = eventMessage.Customer;

            //publish the customer associated product
            await PublishCustomerAssociatedProductAsync(customer);

            //notify other customers who match registered customer's specification attributes
            await NotifyOtherCustomersWhenNewCustomerRegistersAsync(customer);
        }

        public async Task HandleEventAsync(EntityInsertedEvent<Customer> eventMessage)
        {
            await Task.FromResult(0);
        }

        public async Task HandleEventAsync(EntityInsertedEvent<GenericAttribute> eventMessage)
        {
            await CreateOrUpdateProductPictureMappingAsync(eventMessage.Entity);
        }

        public async Task HandleEventAsync(EntityUpdatedEvent<GenericAttribute> eventMessage)
        {
            await CreateOrUpdateProductPictureMappingAsync(eventMessage.Entity);
        }

        public async Task HandleEventAsync(EntityDeletedEvent<GenericAttribute> eventMessage)
        {
            await Task.FromResult(0);
        }

        public async Task HandleEventAsync(ModelPreparedEvent<BaseNopModel> eventMessage)
        {
            if (eventMessage.Model is CustomerNavigationModel model)
            {
                //insert private messages at index 1
                model.CustomerNavigationItems.Insert(1, new CustomerNavigationItemModel
                {
                    RouteName = "PrivateMessages",
                    Title = "Mails & Messages ",
                    Tab = (int)CustomerNavigationEnum.PrivateMessages,
                    ItemClass = "customer-PrivateMessages"
                });

                //insert ShortListed at index 2
                model.CustomerNavigationItems.Insert(2, new CustomerNavigationItemModel
                {
                    RouteName = "ShortListed",
                    Title = "Short Listed",
                    Tab = (int)CustomerNavigationEnum.ShortListed,
                    ItemClass = "customer-shortlisted"
                });

                //remove address item
                model.CustomerNavigationItems.RemoveAt(3);

                //add customer affiliations
                model.CustomerNavigationItems.Add(new CustomerNavigationItemModel
                {
                    RouteName = "CustomerAffiliations",
                    Title = "Affiliations",
                    Tab = (int)CustomerNavigationEnum.Affiliations,
                    ItemClass = "customer-affiliations"
                });

                //sort by name
                //model.CustomerNavigationItems = model.CustomerNavigationItems.OrderBy(x => x.Title).ToList();
            }

            if (eventMessage.Model is ProductDetailsModel productModel)
            {
                //remove last part after space which is surname
                //var strTrimmed = productModel.DefaultPictureModel.Title.Trim();
                //var finalString = strTrimmed.Substring(strTrimmed.LastIndexOf(" ", strTrimmed.Length));

                //productModel.DefaultPictureModel.Title = finalString;
                //productModel.DefaultPictureModel.AlternateText = finalString;

                if (productModel.PictureModels.Count == 0)
                {
                    if (productModel.Gender?.ToLower() == "F".ToLower())
                    {
                        //change picture to women image
                        productModel.DefaultPictureModel.ImageUrl = "https://localhost:54077/images/thumbs/default-women-image_615.png";

                    }
                }
            }

            if (eventMessage.Model is ProductEmailAFriendModel emailAFriendModel)
            {
                //customization
                var orders = await _orderService.SearchOrdersAsync(customerId: (await _workContext.GetCurrentCustomerAsync()).Id);

                //check order status code
                var isValid = orders.Where(a => a.OrderStatus == OrderStatus.OrderActive).SingleOrDefault();

                if (isValid == null)
                {
                    //Dispaly Upgrade View
                    //emailAFriendModel.Result = await _localizationService.GetResourceAsync("Orders.UpgradeSubscription.Message");
                    //ModelState.AddModelError("", await _localizationService.GetResourceAsync("Orders.UpgradeSubscription.Message"));
                    //return View("_UpgradeSubscription.cshtml", model);
                }
            }

            //this is for related products 
            if (eventMessage.Model is ProductOverviewModel productOverviewModel)
            {

            }

            //show shopping cart in pricing, onepagecheckout & cart page and hide in all other categories
            if (eventMessage.Model is HeaderLinksModel headerLinksModel)
            {
                var currentPageUrl = _webHelper.GetThisPageUrl(false);

                if (currentPageUrl.Contains("pricing", StringComparison.InvariantCultureIgnoreCase)
                    || currentPageUrl.Contains("cart", StringComparison.InvariantCultureIgnoreCase)
                    || currentPageUrl.Contains("onepagecheckout", StringComparison.InvariantCultureIgnoreCase))
                    headerLinksModel.ShoppingCartEnabled = true;
                else
                    headerLinksModel.ShoppingCartEnabled = false;

            }

            if (eventMessage.Model is SearchBoxModel searchBoxModel)
            {

            }

            //account activation model
            if (eventMessage.Model is AccountActivationModel accountActivationModel)
            {
                var customerProfileTypeId = (await _workContext.GetCurrentCustomerAsync()).Id;

                //wants to give support. Return url must be give support page
                if (customerProfileTypeId == 1)
                {
                    //accountActivationModel.ReturnUrl= string.Empty;
                }
                else if (customerProfileTypeId == 2)
                {
                    //wants to take support.  Return url must be take support page
                    //accountActivationModel.ReturnUrl = string.Empty;
                }

                accountActivationModel.CustomProperties = new Dictionary<string, string> { };
            }

            //top menu model
            if (eventMessage.Model is TopMenuModel topMenuModel)
            {
                var customerProfileTypeId = (await _workContext.GetCurrentCustomerAsync()).Id;

                //topMenuModel.Categories=

            }

            #region Admin functionality

            if (eventMessage.Model is Web.Areas.Admin.Models.Catalog.CategoryModel adminCategoryModel)
            {
                //set default values for the new model. i.e new category creation mode
                if (adminCategoryModel.Id == 0)
                {
                    adminCategoryModel.ShowOnHomepage = false;
                    adminCategoryModel.IncludeInTopMenu = false;
                    adminCategoryModel.AllowCustomersToSelectPageSize = false;
                    adminCategoryModel.PriceRangeFiltering = false;
                    adminCategoryModel.ManuallyPriceRange = false;
                }
            }

            #endregion
            //return Task.FromResult(0);
        }

        public async Task HandleEventAsync(EntityTokensAddedEvent<Customer, Token> eventMessage)
        {
            //add customer phone number
            eventMessage.Tokens.Add(new Token("Customer.Phone", eventMessage.Entity.Phone));

            var product = await _productService.GetProductByIdAsync(eventMessage.Entity.VendorId);
            var customer = eventMessage.Entity;

            //add product specification attributes
            eventMessage.Tokens.Add(new Token("Customer.ProfileType", await GetCustomerSpecificationAttributesAsync(customer, (int)ProductAndCustomerAttributeEnum.ProfileType)));
            eventMessage.Tokens.Add(new Token("Customer.PrimaryTechnology", await GetCustomerSpecificationAttributesAsync(customer, (int)ProductAndCustomerAttributeEnum.PrimaryTechnology)));
            eventMessage.Tokens.Add(new Token("Customer.MotherTongue", await GetCustomerSpecificationAttributesAsync(customer, (int)ProductAndCustomerAttributeEnum.MotherTongue)));
            eventMessage.Tokens.Add(new Token("Customer.Gender", await GetCustomerSpecificationAttributesAsync(customer, (int)ProductAndCustomerAttributeEnum.Gender)));
            eventMessage.Tokens.Add(new Token("Customer.ShortDescription", await GetCustomerSpecificationAttributesAsync(customer, (int)ProductAndCustomerAttributeEnum.ShortDescription)));
            eventMessage.Tokens.Add(new Token("Customer.FullDescription", await GetCustomerSpecificationAttributesAsync(customer, (int)ProductAndCustomerAttributeEnum.FullDescription)));
        }

        public Task HandleEventAsync(MessageTokensAddedEvent<Token> eventMessage)
        {
            if (eventMessage?.Message?.Name == MessageTemplateSystemNames.CUSTOMER_REGISTERED_STORE_OWNER_NOTIFICATION)
            {
                //var list = eventMessage.Tokens.Select(x => x.Key == "Customer.ProfileType").ToList();

                //if (list.Count != 0)
                //    _logger.Information("Customer Token ProfileType found");

            }

            return Task.CompletedTask;
        }

        public async Task HandleEventAsync(EntityInsertedEvent<Category> eventMessage)
        {
            var specificationAttributeId = (int)ProductAndCustomerAttributeEnum.PrimaryTechnology;
            var specificationAttributeOptions = await _specificationAttributeService.GetSpecificationAttributeOptionsByNameAsync(specificationAttributeId, eventMessage.Entity.Name);

            if (specificationAttributeOptions == null || specificationAttributeOptions.Count == 0)
            {
                //technology doesnt exist for this category. Lets create one.

                var spao = new SpecificationAttributeOption()
                {
                    Name = eventMessage.Entity.Name.Trim(),
                    SpecificationAttributeId = specificationAttributeId,
                    DisplayOrder = 0
                };

                await _specificationAttributeService.InsertSpecificationAttributeOptionAsync(spao);

                await _logger.InsertLogAsync(Core.Domain.Logging.LogLevel.Information, "Created technology from category created (EntityInsertedEvent) event");
            }
        }

        public async Task HandleEventAsync(EntityUpdatedEvent<Category> eventMessage)
        {
            await Task.CompletedTask;
        }

        #endregion

        #region Methods for supporting events

        public async Task AddCustomerToPaidCustomerRole(int customerId)
        {
            var customer = await _customerService.GetCustomerByIdAsync(customerId);
            var isCustomerInPaidCustomerRole = await _customerService.IsInCustomerRoleAsync(customer, NopCustomerDefaults.PaidCustomerRoleName, true);

            if (!isCustomerInPaidCustomerRole)
            {
                //add customer to paidcustomer role. CustomerRoleId= 9 - PaidCustomer
                await _customerService.AddCustomerRoleMappingAsync(new CustomerCustomerRoleMapping { CustomerId = customerId, CustomerRoleId = 9 });

                //customer activity
                await _customerActivityService.InsertActivityAsync(customer, "PublicStore.CustomerSubscriptionInfo", "Customer Has Been Added To PaidCustomer Role ", customer);

            }
            else
                await _customerActivityService.InsertActivityAsync(customer, "PublicStore.CustomerSubscriptionInfo", "Customer already having PaidCustomer Role.Paid again may be by mistake.", customer);

        }

        public async Task AddCustomerSubscriptionInfoToGenericAttributes(Order order)
        {
            var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);

            //get order product id
            var activeOrderItems = await _orderService.GetOrderItemsAsync(order.Id);
            var customerSubscribedProductId = activeOrderItems.FirstOrDefault().ProductId;

            var storeId = (await _storeContext.GetCurrentStoreAsync()).Id;
            var allottedCount = 00;

            if (customerSubscribedProductId == _shoppingCartSettings.FreeSubscriptionProductId)
            {
                allottedCount = _shoppingCartSettings.FreeSubscriptionAllottedCount;
            }
            else if (customerSubscribedProductId == _shoppingCartSettings.OneMonthSubscriptionProductId)
            {
                allottedCount = _shoppingCartSettings.OneMonthSubscriptionAllottedCount;
            }
            else if (customerSubscribedProductId == _shoppingCartSettings.ThreeMonthSubscriptionProductId)
            {
                allottedCount = _shoppingCartSettings.ThreeMonthSubscriptionAllottedCount;
            }
            else if (customerSubscribedProductId == _shoppingCartSettings.SixMonthSubscriptionProductId)
            {
                allottedCount = _shoppingCartSettings.SixMonthSubscriptionAllottedCount;
            }

            // get the subscription details from generic attribute table
            var subscriptionId = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.SubscriptionId, storeId);
            var subscriptionAllottedCount = await _genericAttributeService.GetAttributeAsync<int>(customer, NopCustomerDefaults.SubscriptionAllottedCount, storeId);
            var subscriptionDate = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.SubscriptionDate, storeId);
            var oldSubscriptionExpiryDate = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.SubscriptionExpiryDate, storeId);

            // carry forward previous credits
            allottedCount += subscriptionAllottedCount;

            var oldSubscriptionInfo = string.Format("Old Subscription Info - Customer Email:{0} ; SubscriptionId: {1} ; Credits: {2} ; SubscriptionDate: {3} ; SubscriptionExpiryDate: {4}",
                                        customer.Email,
                                        subscriptionId,
                                        subscriptionAllottedCount,
                                        order.CreatedOnUtc.ToString(),
                                        oldSubscriptionExpiryDate);

            //customer activity : Before updating the new subscription , save the old subscription details
            await _customerActivityService.InsertActivityAsync(customer, "PublicStore.CustomerSubscriptionInfo", oldSubscriptionInfo, customer);

            DateTime? subscriptionExpiryDate;

            //subscription expiry date
            if (customerSubscribedProductId == 1)
            {
                //free subscription
                subscriptionExpiryDate = order.CreatedOnUtc.AddMonths(1);
            }
            else if (customerSubscribedProductId == 2)
            {
                //1 Month Subscription
                subscriptionExpiryDate = order.CreatedOnUtc.AddMonths(1);

            }
            else if (customerSubscribedProductId == 3)
            {
                //3 months subscription
                subscriptionExpiryDate = order.CreatedOnUtc.AddMonths(3);
            }
            else
            {
                //default expiry date
                subscriptionExpiryDate = order.CreatedOnUtc.AddMonths(1);
            }

            //save SubscriptionId, credits , subscription date and expiry date
            await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SubscriptionId, customerSubscribedProductId, storeId);
            await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SubscriptionAllottedCount, allottedCount, storeId);
            await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SubscriptionDate, order.CreatedOnUtc, storeId);
            await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SubscriptionExpiryDate, subscriptionExpiryDate, storeId);


            var newSubscriptionInfo = string.Format("New Subscription Info - Customer Email:{0} ; SubscriptionId: {1} ; Credits: {2} ; SubscriptionDate: {3}; SubscriptionExpiryDate: {4}",
                                        customer.Email,
                                        customerSubscribedProductId,
                                        allottedCount,
                                        order.CreatedOnUtc.ToString(),
                                        subscriptionExpiryDate);

            //customer activity
            await _customerActivityService.InsertActivityAsync(customer, "PublicStore.CustomerSubscriptionInfo", newSubscriptionInfo, customer);

        }

        public async Task AddCustomerSubscriptionInfoToOrderAsync(Order order)
        {
            var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);

            //get order product id
            var activeOrderItems = await _orderService.GetOrderItemsAsync(order.Id);
            var customerSubscribedProductId = activeOrderItems.FirstOrDefault().ProductId;

            DateTime? subscriptionExpiryDate;
            int allottedCount;

            if (customerSubscribedProductId == _shoppingCartSettings.FreeSubscriptionProductId)
            {
                allottedCount = _shoppingCartSettings.FreeSubscriptionAllottedCount;
                //free subscription
                subscriptionExpiryDate = order.CreatedOnUtc.AddMonths(1);
            }
            else if (customerSubscribedProductId == _shoppingCartSettings.OneMonthSubscriptionProductId)
            {
                allottedCount = _shoppingCartSettings.OneMonthSubscriptionAllottedCount;
                //1 Month Subscription
                subscriptionExpiryDate = order.CreatedOnUtc.AddMonths(1);
            }
            else if (customerSubscribedProductId == _shoppingCartSettings.ThreeMonthSubscriptionProductId)
            {
                allottedCount = _shoppingCartSettings.ThreeMonthSubscriptionAllottedCount;
                //3 months subscription
                subscriptionExpiryDate = order.CreatedOnUtc.AddMonths(3);
            }
            else if (customerSubscribedProductId == _shoppingCartSettings.SixMonthSubscriptionProductId)
            {
                allottedCount = _shoppingCartSettings.SixMonthSubscriptionAllottedCount;
                //6 months subscription
                subscriptionExpiryDate = order.CreatedOnUtc.AddMonths(6);
            }
            else
            {
                allottedCount = 0;
                //default expiry date
                subscriptionExpiryDate = order.CreatedOnUtc.AddMonths(1);
            }

            order.CardType = customerSubscribedProductId.ToString(); // subscriptionId
            order.CardName = allottedCount.ToString(); // subscription AllottedCount
            order.CardNumber = order.PaidDateUtc?.ToString(); // subscription Date
            order.CardCvv2 = subscriptionExpiryDate?.ToString(); // subscription ExpiryDate

            //get customer previous paid order if any
            var previousOrder = (await _orderService.SearchOrdersAsync(customerId: order.CustomerId, psIds: new List<int> { (int)PaymentStatus.Paid })).Skip(1).FirstOrDefault();

            if (previousOrder != null)
            {
                order.CardExpirationMonth = previousOrder.CardExpirationMonth; // Previous un-used count

                int.TryParse(previousOrder.CardExpirationMonth, out var unUsedCount);

                //total allotted count now.
                order.CardExpirationYear = (allottedCount + unUsedCount).ToString();
            }

            //save current order subscription data
            await _orderService.UpdateOrderAsync(order);

        }

        public async Task AddCustomerSubscriptionInfoToRewardPointsAsync(Order order)
        {
            var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);
            var storeId = (await _storeContext.GetCurrentStoreAsync()).Id;

            //get order product id
            var activeOrderItems = await _orderService.GetOrderItemsAsync(order.Id);
            var customerSubscribedProductId = activeOrderItems.FirstOrDefault().ProductId;

            DateTime? subscriptionExpiryDate;
            int allottedCount;

            if (customerSubscribedProductId == _shoppingCartSettings.FreeSubscriptionProductId)
            {
                allottedCount = _shoppingCartSettings.FreeSubscriptionAllottedCount;
                //free subscription
                subscriptionExpiryDate = order.CreatedOnUtc.AddMonths(1);
            }
            else if (customerSubscribedProductId == _shoppingCartSettings.OneMonthSubscriptionProductId)
            {
                allottedCount = _shoppingCartSettings.OneMonthSubscriptionAllottedCount;
                //1 Month Subscription
                subscriptionExpiryDate = order.CreatedOnUtc.AddMonths(1);
            }
            else if (customerSubscribedProductId == _shoppingCartSettings.ThreeMonthSubscriptionProductId)
            {
                allottedCount = _shoppingCartSettings.ThreeMonthSubscriptionAllottedCount;
                //3 months subscription
                subscriptionExpiryDate = order.CreatedOnUtc.AddMonths(3);
            }
            else if (customerSubscribedProductId == _shoppingCartSettings.SixMonthSubscriptionProductId)
            {
                allottedCount = _shoppingCartSettings.SixMonthSubscriptionAllottedCount;
                //6 months subscription
                subscriptionExpiryDate = order.CreatedOnUtc.AddMonths(6);
            }
            else
            {
                allottedCount = 0;
                //default expiry date
                subscriptionExpiryDate = order.CreatedOnUtc.AddMonths(1);
            }

            var now = DateTime.UtcNow;
            var startDate = new DateTime(now.Year, now.Month, 1);
            //var expiryDate = startDate.AddMonths(1).AddDays(-1);
            var orderItems = await _orderService.GetOrderItemsAsync(order.Id);
            var subscriptionProductId = orderItems.FirstOrDefault().ProductId;

            var product = await _productService.GetProductByIdAsync(subscriptionProductId);
            var message = $"Customer Buy Credits with Plan: {product.Id} - {product.Name}";

            await _rewardPointService.AddRewardPointsHistoryEntryAsync(customer, allottedCount, storeId, message, order, endDate: subscriptionExpiryDate);

        }

        private async Task CreateProductAsync(Customer customer, string customerAttributesXml, string firstName, string lastName, string gender)
        {
            var customerProfileTypeId = GetCustomerProfileTypeId(customerAttributesXml);

            var shortDescriptionId = (int)ProductAndCustomerAttributeEnum.ShortDescription;
            var fullDescriptionId = (int)ProductAndCustomerAttributeEnum.FullDescription;

            var primaryTechnologyAttributeValues = _customerAttributeParser.ParseValues(customerAttributesXml, (int)ProductAndCustomerAttributeEnum.PrimaryTechnology).ToList();
            var secondaryTechnologyAttributeValues = _customerAttributeParser.ParseValues(customerAttributesXml, (int)ProductAndCustomerAttributeEnum.SecondaryTechnology).ToList();

            var totalAttributeValues = primaryTechnologyAttributeValues.Select(int.Parse).ToList();
            totalAttributeValues.AddRange(secondaryTechnologyAttributeValues.Select(x => int.Parse(x)).Distinct().ToList());

            var attributeValuesFromSpec = (await _specificationAttributeService.GetSpecificationAttributeOptionsByIdsAsync(totalAttributeValues.ToArray())).Select(x => x.Name).ToList();

            var categories = await _categoryService.GetAllCategoriesAsync(attributeValuesFromSpec);
            var missedCategories = attributeValuesFromSpec.Except(categories.Select(x => x.Name)).ToList();

            var categoryIds = new List<int>();

            categoryIds.Add(customerProfileTypeId); //give support or take support
            categoryIds.AddRange(categories.Select(x => x.Id).ToList()); //primary technologies & secondary technologies selected

            //create technical category if it doesnt exist in the selected primary technology list
            var newlyAddedCategoryIds = await CreateCategoryAsync(missedCategories);
            categoryIds.AddRange(newlyAddedCategoryIds);

            // create product model with customer data
            var productModel = new Nop.Web.Areas.Admin.Models.Catalog.ProductModel()
            {
                Name = firstName + " " + lastName,
                Published = false, //activate the product when customer account is activated
                ShortDescription = _customerAttributeParser.ParseValues(customerAttributesXml, shortDescriptionId).FirstOrDefault(),
                FullDescription = _customerAttributeParser.ParseValues(customerAttributesXml, fullDescriptionId).FirstOrDefault(),
                ShowOnHomepage = false,
                AllowCustomerReviews = true,
                IsShipEnabled = false,
                Price = 500,
                SelectedCategoryIds = categoryIds,
                OrderMinimumQuantity = 1,
                OrderMaximumQuantity = 1000,
                IsTaxExempt = true,
                //below are mandatory feilds otherwise the product will not be visible in front end store
                Sku = $"SKU_{firstName}_{lastName}",
                ProductTemplateId = 1, // simple product template
                ProductTypeId = (int)ProductType.SimpleProduct,
                VisibleIndividually = true,
                //set product vendor id to customer id. AKA VendorId means Customer Id
                VendorId = customer.Id
            };

            var product = productModel.ToEntity<Product>();
            product.CreatedOnUtc = DateTime.UtcNow;
            product.UpdatedOnUtc = DateTime.UtcNow;

            //product creation
            await _productService.InsertProductAsync(product);

            //product categories mappings (map this product to its cateogories)
            await SaveCategoryMappingsAsync(product, productModel);

            //set SEO settings. Otherwise the product wont be visible in front end
            productModel.SeName = await _urlRecordService.ValidateSeNameAsync(product, productModel.SeName, product.Name, true);
            await _urlRecordService.SaveSlugAsync(product, productModel.SeName, 0);

            //Update customer with Productid as VendorId. Here Vendor Id means Product Id
            customer.VendorId = product.Id;
            await _customerService.UpdateCustomerAsync(customer);

            //create product specification attribute mappings
            await CreateProductSpecificationAttributeMappingsAsync(product, customerAttributesXml, gender);

            //update customer availability in order to send notifications to other similar customers
            //await CreateOrUpdateCustomerCurrentAvailabilityAsync(customer, customerAttributesXml);

        }

        public int GetCustomerProfileTypeId(string customerAttributesXml)
        {
            var profileTypeId = (int)ProductAndCustomerAttributeEnum.ProfileType;
            var profileType = _customerAttributeParser.ParseValues(customerAttributesXml, profileTypeId).FirstOrDefault();

            var customerProfileTypeId = Convert.ToInt32(profileType);
            return customerProfileTypeId;
        }

        private async Task<List<int>> CreateCategoryAsync(List<string> categories)
        {
            var newlyAddedcategoryIds = new List<int>();
            foreach (var category in categories)
            {
                var newCategory = new Category
                {
                    Name = category,
                    CategoryTemplateId = 1,
                    IncludeInTopMenu = false,
                    ShowOnHomepage = false,
                    Published = true,
                    PriceRangeFiltering = false,
                    PageSize = 50 //default page size otherwise cateogory wont appear
                };
                await _categoryService.InsertCategoryAsync(newCategory);

                //search engine name
                var seName = await _urlRecordService.ValidateSeNameAsync(newCategory, category, category, true);
                await _urlRecordService.SaveSlugAsync(newCategory, seName, 0);
                newlyAddedcategoryIds.Add(newCategory.Id);
            }

            return newlyAddedcategoryIds;
        }

        protected virtual async Task SaveCategoryMappingsAsync(Product product, ProductModel model)
        {
            var existingProductCategories = await _categoryService.GetProductCategoriesByProductIdAsync(product.Id, true);

            //delete categories
            foreach (var existingProductCategory in existingProductCategories)
                if (!model.SelectedCategoryIds.Contains(existingProductCategory.CategoryId))
                    await _categoryService.DeleteProductCategoryAsync(existingProductCategory);

            //add categories
            foreach (var categoryId in model.SelectedCategoryIds)
            {
                if (_categoryService.FindProductCategory(existingProductCategories, product.Id, categoryId) == null)
                {
                    //find next display order
                    var displayOrder = 1;
                    var existingCategoryMapping = await _categoryService.GetProductCategoriesByCategoryIdAsync(categoryId, showHidden: true);
                    if (existingCategoryMapping.Any())
                        displayOrder = existingCategoryMapping.Max(x => x.DisplayOrder) + 1;

                    await _categoryService.InsertProductCategoryAsync(new ProductCategory
                    {
                        ProductId = product.Id,
                        CategoryId = categoryId,
                        DisplayOrder = displayOrder
                    });
                }
            }
        }

        protected virtual async Task CreateProductSpecificationAttributeMappingsAsync(Product product, string customerAttributesXml, string gender)
        {
            var spectAttributes = await _specificationAttributeService.GetSpecificationAttributesWithOptionsAsync();

            foreach (var attribute in spectAttributes)
            {
                //gender specification attribute mapping
                if (attribute.Id == _customerSettings.GenderSpecificationAttributeId)
                {
                    var psaGender = new ProductSpecificationAttribute
                    {
                        AllowFiltering = true,
                        ProductId = product.Id,
                        SpecificationAttributeOptionId = (gender == "M") ? _customerSettings.GenderMaleSpecificationAttributeOptionId : _customerSettings.GenderFeMaleSpecificationAttributeOptionId,
                        ShowOnProductPage = true
                    };
                    await _specificationAttributeService.InsertProductSpecificationAttributeAsync(psaGender);
                }

                //attribute.Id means primary tech,secondary tect etc.
                var attributeOptionIds = _customerAttributeParser.ParseValues(customerAttributesXml, attribute.Id).ToList();

                foreach (var attributeOptionId in attributeOptionIds)
                {
                    //create product to spec attribute mapping
                    var psa = new ProductSpecificationAttribute
                    {
                        //attribute id 1 means profile type. Do not Show Profile Type filter on product filters page
                        AllowFiltering = attribute.Id == 1 ? false : true,
                        ProductId = product.Id,
                        SpecificationAttributeOptionId = Convert.ToInt32(attributeOptionId),
                        ShowOnProductPage = true
                    };
                    await _specificationAttributeService.InsertProductSpecificationAttributeAsync(psa);
                }
            }
        }

        private async Task CreateOrUpdateCustomerCurrentAvailabilityAsync(Customer customer, string newCustomerAttributesXml)
        {
            var oldCustomAttributeXml = customer.CustomCustomerAttributesXML;

            if (!string.IsNullOrEmpty(oldCustomAttributeXml))
            {
                //existing customer
                var OldCustomerAvailability = _customerAttributeParser.ParseValues(oldCustomAttributeXml, (int)ProductAndCustomerAttributeEnum.CurrentAvalibility)
                                                                  .ToList().Select(int.Parse).FirstOrDefault();
                var newCustomerAvailability = _customerAttributeParser.ParseValues(newCustomerAttributesXml, (int)ProductAndCustomerAttributeEnum.CurrentAvalibility)
                                                                      .ToList().Select(int.Parse).FirstOrDefault();

                // SpecificationAttributeOption: 3 - Available ; 4 - UnAvailable
                if (OldCustomerAvailability == 4 && newCustomerAvailability == 3)
                {
                    //customer changed from UnAvailable to Available
                    await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.NotifiedAboutCustomerAvailabilityAttribute, false, (await _storeContext.GetCurrentStoreAsync()).Id);

                    await _customerActivityService.InsertActivityAsync(await _workContext.GetCurrentCustomerAsync(), "PublicStore.EditCustomerAvailabilityToTrue",
                    "Public Store. Customer changed from UnAvailable to Available", await _workContext.GetCurrentCustomerAsync());
                }
            }
            else
            {
                //new customer
                await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.NotifiedAboutCustomerAvailabilityAttribute, false, (await _storeContext.GetCurrentStoreAsync()).Id);

                await _customerActivityService.InsertActivityAsync(await _workContext.GetCurrentCustomerAsync(), "PublicStore.EditCustomerAvailabilityToTrue",
                        "Public Store. New Customer Registered", await _workContext.GetCurrentCustomerAsync());
            }

        }

        public async Task CreateOrUpdateProductPictureMappingAsync(GenericAttribute entity)
        {
            //check if customer deleted the profile picture
            if (entity.Key == NopCustomerDefaults.AvatarPictureIdAttribute && entity.KeyGroup == "Customer" && entity.Value == "0")
            {
                var customerId = entity.EntityId;
                var customer = await _customerService.GetCustomerByIdAsync(customerId);

                var pictures = await _productService.GetProductPicturesByProductIdAsync(customer.VendorId);

                //delete existing product to picture mappings
                foreach (var picture in pictures)
                    await _productService.DeleteProductPictureAsync(picture);
            }

            //check if customer updated/created the profile picture
            if (entity.Key == NopCustomerDefaults.AvatarPictureIdAttribute && entity.KeyGroup == "Customer" && Convert.ToUInt32(entity.Value) > 0)
            {
                var customerId = entity.EntityId;
                var pictureId = Convert.ToInt32(entity.Value);
                var customer = await _customerService.GetCustomerByIdAsync(customerId);

                var pictures = await _productService.GetProductPicturesByProductIdAsync(customer.VendorId);

                //delete existing product to picture mappings
                foreach (var picture in pictures)
                    await _productService.DeleteProductPictureAsync(picture);

                //create product to picture mappings
                await _productService.InsertProductPictureAsync(new ProductPicture
                {
                    ProductId = customer.VendorId,
                    PictureId = pictureId,
                    DisplayOrder = 1
                });
            }
        }

        public async Task AddCustomerToJobSupportRoleAsync(Customer customer)
        {
            if (customer.CustomerProfileTypeId == (int)CustomerProfileTypeEnum.GiveSupport)
            {
                var giveSupportRole = await _customerService.GetCustomerRoleBySystemNameAsync(NopCustomerDefaults.GiveSupportRoleName);
                if (giveSupportRole == null)
                    throw new NopException("'Give Support' role could not be loaded");

                await _customerService.AddCustomerRoleMappingAsync(new CustomerCustomerRoleMapping { CustomerId = customer.Id, CustomerRoleId = giveSupportRole.Id });
            }
            else if (customer.CustomerProfileTypeId == (int)CustomerProfileTypeEnum.TakeSupport)
            {
                var takeSupportRole = await _customerService.GetCustomerRoleBySystemNameAsync(NopCustomerDefaults.TakeSupportRoleName);
                if (takeSupportRole == null)
                    throw new NopException("'Take Support' role could not be loaded");

                await _customerService.AddCustomerRoleMappingAsync(new CustomerCustomerRoleMapping { CustomerId = customer.Id, CustomerRoleId = takeSupportRole.Id });
            }
        }

        public async Task CreateCustomerAffliateAsync(Customer customer)
        {
            var address = await _customerService.GetCustomerBillingAddressAsync(customer);

            var firstName = customer.FirstName;
            var lastName = customer.LastName;

            var affiliate = new Affiliate
            {
                Active = true,
                AdminComment = "Affiliate created for customer while registering..",
                FriendlyUrlName = "",
                AddressId = address.Id
            };

            //validate friendly URL name
            var freindlyName = string.Format("referral-{0}-{1}", firstName, lastName);
            var friendlyUrlName = await _affiliateService.ValidateFriendlyUrlNameAsync(affiliate, freindlyName);

            affiliate.FriendlyUrlName = friendlyUrlName;

            await _affiliateService.InsertAffiliateAsync(affiliate);

            //update logged-in customer with newly created affiliate id
            customer.VatNumberStatusId = affiliate.Id;
            await _customerService.UpdateCustomerAsync(customer);

            //activity log
            await _customerActivityService.InsertActivityAsync("AddNewAffiliate",
                string.Format(await _localizationService.GetResourceAsync("ActivityLog.AddNewAffiliate"), affiliate.Id), affiliate);
        }

        public async Task UpdateCustomerCustomerProfileTypeIdAsync(Customer customer)
        {
            customer.CustomerProfileTypeId = GetCustomerProfileTypeId(customer.CustomCustomerAttributesXML);
            await _customerService.UpdateCustomerAsync(customer);
        }

        public async Task NotifyOtherCustomersWhenNewCustomerRegistersAsync(Customer customer)
        {
            var customerProfileTypeId = GetCustomerProfileTypeId(customer.CustomCustomerAttributesXML);


            if (customerProfileTypeId == 2)
            {
                //get primary skills
                var primarySkillIds = _customerAttributeParser.ParseValues(customer.CustomCustomerAttributesXML,
                                                                        (int)ProductAndCustomerAttributeEnum.PrimaryTechnology).Select(int.Parse).ToList();

                var categoryIds = new List<int>() { 1 };

                var specOptions = await _specificationAttributeService.GetSpecificationAttributeOptionsByIdsAsync(primarySkillIds.ToArray());

                //get similar target customers
                var targetProducts = (await _productService.SearchProductsAsync(categoryIds: categoryIds, filteredSpecOptions: specOptions)).ToList();

                var targetCustomerIds = targetProducts.Where(x => x.VendorId > 0).Select(x => x.VendorId).ToArray();
                var targetCustomers = (await _customerService.GetCustomersByIdsAsync(targetCustomerIds)).Where(x => x.Active = true).ToList();

                var product = await _productService.GetProductByIdAsync(customer.VendorId);

                //notify the target customers that a customer is just registered (i.e looking for support)
                foreach (var customerToNotify in targetCustomers)
                {
                    await _workflowMessageService.SendCustomerAvilableNotificationToOtherCustomersAsync(product, customerToNotify, _localizationSettings.DefaultAdminLanguageId, specOptions);
                }

            }

            await Task.FromResult(0);
        }

        private async Task PublishCustomerAssociatedProductAsync(Customer customer)
        {
            //get product associted to customer
            var product = await _productService.GetProductByIdAsync(customer.VendorId);

            product.Published = true;
            product.UpdatedOnUtc = DateTime.UtcNow;

            //update product
            await _productService.UpdateProductAsync(product);
        }

        private async Task<string> GetCustomerSpecificationAttributesAsync(Customer customer, int specificationAttributeId)
        {
            var result = string.Empty;

            if (specificationAttributeId == (int)(int)ProductAndCustomerAttributeEnum.ShortDescription ||
                specificationAttributeId == (int)(int)ProductAndCustomerAttributeEnum.FullDescription)
            {
                var description = _customerAttributeParser.ParseValues(customer.CustomCustomerAttributesXML, specificationAttributeId);
                if (description.Any())
                    return description[0];
            }
            else
            {
                var specificationOptionIds = _customerAttributeParser.ParseValues(customer.CustomCustomerAttributesXML, specificationAttributeId).Select(int.Parse).ToList();
                var specOptions = await _specificationAttributeService.GetSpecificationAttributeOptionsByIdsAsync(specificationOptionIds.ToArray());

                result = string.Join(", ", specOptions.Select(x => x.Name).ToArray());
            }

            return result;
        }

        #endregion
    }

}