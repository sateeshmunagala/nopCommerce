﻿using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Events;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Events;
using Nop.Services.Logging;
using Nop.Services.Orders;
using System.Linq;
using System.Threading.Tasks;
using Nop.Services.Affiliates;
using Nop.Web.Areas.Admin.Models.Affiliates;
using Nop.Core.Domain.Affiliates;
using Nop.Services.Localization;
using Nop.Core.Domain.Catalog;
using Nop.Services.Catalog;
using System.Collections.Generic;
using System;
using Nop.Web.Areas.Admin.Infrastructure.Mapper.Extensions;
using Nop.Services.Seo;
using Nop.Web.Areas.Admin.Models.Catalog;
using Nop.Core.Domain.Common;

namespace Nop.CustomExtensions.Services
{
    /// <summary>
    /// Represents event consumer
    /// </summary>
    public class EventConsumer : IConsumer<OrderPaidEvent>,
        IConsumer<CustomerRegisteredEvent>,
        IConsumer<EntityInsertedEvent<GenericAttribute>>,
        IConsumer<EntityUpdatedEvent<GenericAttribute>>,
        IConsumer<EntityDeletedEvent<GenericAttribute>>
    {
        #region Fields

        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ICustomerService _customerService;
        private readonly ILogger _logger;
        private readonly ICustomerActivityService _customerActivityService;
        private readonly ShoppingCartSettings _shoppingCartSettings;
        private readonly IOrderService _orderService;
        private readonly IStoreContext _storeContext;
        private readonly IAffiliateService _affiliateService;
        private readonly IAddressService _addressService;
        private readonly ILocalizationService _localizationService;

        private readonly ICustomerAttributeParser _customerAttributeParser;
        private readonly ISpecificationAttributeService _specificationAttributeService;
        private readonly ICategoryService _categoryService;
        private readonly IUrlRecordService _urlRecordService;
        private readonly IProductService _productService;
        private readonly CustomerSettings _customerSettings;
        private readonly IWorkContext _workContext;
        #endregion

        #region Ctor

        public EventConsumer(IGenericAttributeService genericAttributeService,
             ICustomerService customerService,
             ILogger logger,
             IStoreContext storeContext,
             ShoppingCartSettings shoppingCartSettings,
             IOrderService orderService,
             IAffiliateService affiliateService,
             IAddressService addressService,
             ILocalizationService localizationService,
             ICustomerActivityService customerActivityService,

             ICustomerAttributeParser customerAttributeParser,
             ISpecificationAttributeService specificationAttributeService,
             ICategoryService categoryService,
             IUrlRecordService urlRecordService,
             IProductService productService,
             CustomerSettings customerSettings,
             IWorkContext workContext

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
        }

        #endregion

        #region Methods

        public async Task HandleEventAsync(OrderPaidEvent eventMessage)
        {
            await AddCustomerToPaidCustomerRole(eventMessage.Order.CustomerId);
            await AddCustomerGenericAttributes(eventMessage.Order);
        }

        public async Task HandleEventAsync(CustomerRegisteredEvent eventMessage)
        {
            var customer = eventMessage.Customer;

            //create product immediatly after customer registered
            await CreateProductAsync(customer, customer.CustomCustomerAttributesXML, customer.FirstName, customer.LastName, customer.Gender);

            var address = await _customerService.GetAddressesByCustomerIdAsync(customer.Id);

            var storeId = (await _storeContext.GetCurrentStoreAsync()).Id;
            var firstName = customer.FirstName;
            var lastName = customer.LastName;

            //affiliate.AddressId = address.Id;

            var affiliate = new Affiliate
            {
                Active = true,
                AdminComment = "Affiliate created for customer",
                FriendlyUrlName = ""
            };

            //validate friendly URL name
            var freindlyName = string.Format("referral-{0}-{1}", firstName, lastName);
            var friendlyUrlName = await _affiliateService.ValidateFriendlyUrlNameAsync(affiliate, freindlyName);

            affiliate.FriendlyUrlName = friendlyUrlName;
            //affiliate.AddressId = address.FirstOrDefault().Id;

            //await _affiliateService.InsertAffiliateAsync(affiliate);

            //activity log
            await _customerActivityService.InsertActivityAsync("AddNewAffiliate",
                string.Format(await _localizationService.GetResourceAsync("ActivityLog.AddNewAffiliate"), affiliate.Id), affiliate);

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

        public async Task AddCustomerToPaidCustomerRole(int customerId)
        {
            var customer = await _customerService.GetCustomerByIdAsync(customerId);
            var isCustomerInPaidCustomerRole = await _customerService.IsInCustomerRoleAsync(customer, NopCustomerDefaults.PaidCustomerRoleName, true);

            if (!isCustomerInPaidCustomerRole)
            {
                //add customer to paidcustomer role. CustomerRoleId= 9 - PaidCustomer
                await _customerService.AddCustomerRoleMappingAsync(new CustomerCustomerRoleMapping { CustomerId = customerId, CustomerRoleId = 9 });

                //customer activity
                await _customerActivityService.InsertActivityAsync(customer, "PublicStore.EditCustomerAvailabilityToTrue", "Customer Has Been Added To PaidCustomer Role ", customer);

            }
            else
                await _customerActivityService.InsertActivityAsync(customer, "PublicStore.EditCustomerAvailabilityToTrue", "Customer already having PaidCustomer Role.Paid again may be by mistake.", customer);

        }

        public async Task AddCustomerGenericAttributes(Order order)
        {
            var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);

            var activeOrderItems = await _orderService.GetOrderItemsAsync(order.Id);
            var customerSubscribedProductId = activeOrderItems.FirstOrDefault().ProductId;

            var storeId = (await _storeContext.GetCurrentStoreAsync()).Id;
            var allottedCount = 00;

            if (customerSubscribedProductId == _shoppingCartSettings.ThreeMonthSubscriptionProductId)
            {
                allottedCount = _shoppingCartSettings.ThreeMonthSubscriptionAllottedCount;
            }
            else if (customerSubscribedProductId == _shoppingCartSettings.SixMonthSubscriptionProductId)
            {
                allottedCount = _shoppingCartSettings.SixMonthSubscriptionAllottedCount;
            }
            else if (customerSubscribedProductId == _shoppingCartSettings.OneYearSubscriptionProductId)
            {
                allottedCount = _shoppingCartSettings.OneYearSubscriptionAllottedCount;
            }

            var SubscriptionId = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.SubscriptionId, storeId);
            var SubscriptionAllottedCount = await _genericAttributeService.GetAttributeAsync<int>(customer, NopCustomerDefaults.SubscriptionAllottedCount, storeId);
            var SubscriptionDate = await _genericAttributeService.GetAttributeAsync<string>(customer, NopCustomerDefaults.SubscriptionDate, storeId);

            // carry forward previous credits
            allottedCount += SubscriptionAllottedCount;

            var oldSubscriptionInfo = string.Format("Old Subscription Info - Customer Email:{0} ; SubscriptionId: {1} ; Credits: {2} ; SubscriptionDate: {3}",
                                        customer.Email,
                                        SubscriptionId,
                                        SubscriptionAllottedCount,
                                        SubscriptionDate);

            //customer activity : Before updating the new subscription , save the old subscription details
            await _customerActivityService.InsertActivityAsync(customer, "PublicStore.EditCustomerAvailabilityToTrue", oldSubscriptionInfo, customer);

            //save SubscriptionId, credits , subscription date 
            await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SubscriptionId, customerSubscribedProductId, storeId);
            await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SubscriptionAllottedCount, allottedCount, storeId);
            await _genericAttributeService.SaveAttributeAsync(customer, NopCustomerDefaults.SubscriptionDate, order.CreatedOnUtc, storeId);


            var newSubscriptionInfo = string.Format("New Subscription Info - Customer Email:{0} ; SubscriptionId: {1} ; Credits: {2} ; SubscriptionDate: {3}",
                                        customer.Email,
                                        customerSubscribedProductId,
                                        allottedCount,
                                        order.CreatedOnUtc.ToString());

            //customer activity
            await _customerActivityService.InsertActivityAsync(customer, "PublicStore.EditCustomerAvailabilityToTrue", newSubscriptionInfo, customer);

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
                Published = true,
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
                    PageSize = 20 //default page size otherwise cateogory wont appear 
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

        #endregion
    }
}