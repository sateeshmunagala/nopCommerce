using Moq;
using Nop.Core.Caching;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Plugin.Misc.AIInterview.Domain;
using Nop.Plugin.Misc.AIInterview.Infrastructure;
using Nop.Plugin.Misc.AIInterview.Services;
using Microsoft.Extensions.Logging;
using Nop.Services.Catalog;
using Nop.Services.Customers;
using Nop.Services.Orders;
using NUnit.Framework;

namespace Nop.Plugin.Misc.AIInterview.Tests;

[TestFixture]
public class CreditPurchaseTests
{
    private const string DefaultMappingsJson = "{\"AI-CREDIT-1\":1,\"AI-CREDIT-10\":10,\"AI-CREDIT-20\":20}";

    private Mock<IRepository<CreditPurchaseGrant>> _grantRepository;
    private Mock<IOrderService> _orderService;
    private Mock<IProductService> _productService;
    private Mock<ICustomerService> _customerService;
    private Mock<ICreditService> _creditService;
    private Mock<ILogger<CreditPurchaseService>> _logger;
    private AIInterviewSettings _settings;
    private CreditPurchaseService _service;

    [SetUp]
    public void SetUp()
    {
        _grantRepository = new Mock<IRepository<CreditPurchaseGrant>>();
        _orderService = new Mock<IOrderService>();
        _productService = new Mock<IProductService>();
        _customerService = new Mock<ICustomerService>();
        _creditService = new Mock<ICreditService>();
        _logger = new Mock<ILogger<CreditPurchaseService>>();
        _settings = new AIInterviewSettings { CreditProductSkuMappingsJson = DefaultMappingsJson };

        var grants = new List<CreditPurchaseGrant>();
        _grantRepository.Setup(x => x.GetAllAsync(
                It.IsAny<Func<IQueryable<CreditPurchaseGrant>, IQueryable<CreditPurchaseGrant>>>(),
                It.IsAny<Func<ICacheKeyService, CacheKey>>(),
                true))
            .ReturnsAsync((Func<IQueryable<CreditPurchaseGrant>, IQueryable<CreditPurchaseGrant>> func, Func<ICacheKeyService, CacheKey> _, bool __) =>
                func == null ? grants.ToList() : func(grants.AsQueryable()).ToList());
        _grantRepository.Setup(x => x.InsertAsync(It.IsAny<CreditPurchaseGrant>(), true))
            .Callback<CreditPurchaseGrant, bool>((grant, _) =>
            {
                grant.Id = grants.Count + 1;
                grants.Add(grant);
            })
            .Returns(Task.CompletedTask);

        _customerService.Setup(x => x.IsRegisteredAsync(It.IsAny<Customer>(), true)).ReturnsAsync(true);

        _service = new CreditPurchaseService(
            _grantRepository.Object,
            _orderService.Object,
            _productService.Object,
            _customerService.Object,
            _creditService.Object,
            _settings,
            _logger.Object);
    }

    [Test]
    public async Task Paid_Order_With_No_Credit_Products_Grants_No_Credits()
    {
        var order = new Order { Id = 1001, CustomerId = 10 };
        _customerService.Setup(x => x.GetCustomerByIdAsync(10)).ReturnsAsync(new Customer { Id = 10 });
        _orderService.Setup(x => x.GetOrderItemsAsync(1001, null, null, 0))
            .ReturnsAsync(new List<OrderItem>
            {
                new() { Id = 11, OrderId = 1001, ProductId = 100, Quantity = 1 }
            });
        _orderService.Setup(x => x.GetProductByOrderItemIdAsync(11))
            .ReturnsAsync(new Product { Id = 100, Sku = "REGULAR-1" });

        await _service.GrantCreditsForPaidOrderAsync(order);

        _creditService.Verify(x => x.AddCreditAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        _grantRepository.Verify(x => x.InsertAsync(It.IsAny<CreditPurchaseGrant>(), true), Times.Never);
    }

    [Test]
    public async Task Paid_Order_With_AICredit1_Quantity1_Grants_One_Credit()
    {
        var order = new Order { Id = 1002, CustomerId = 20 };
        _customerService.Setup(x => x.GetCustomerByIdAsync(20)).ReturnsAsync(new Customer { Id = 20 });
        _orderService.Setup(x => x.GetOrderItemsAsync(1002, null, null, 0))
            .ReturnsAsync(new List<OrderItem>
            {
                new() { Id = 12, OrderId = 1002, ProductId = 101, Quantity = 1 }
            });
        _orderService.Setup(x => x.GetProductByOrderItemIdAsync(12))
            .ReturnsAsync(new Product { Id = 101, Sku = "AI-CREDIT-1" });

        await _service.GrantCreditsForPaidOrderAsync(order);

        _creditService.Verify(x => x.AddCreditAsync(20, 1, "Purchased credit pack: order #1002, SKU AI-CREDIT-1, credits 1"), Times.Once);
        _grantRepository.Verify(x => x.InsertAsync(It.Is<CreditPurchaseGrant>(grant =>
            grant.OrderId == 1002 &&
            grant.OrderItemId == 12 &&
            grant.CustomerId == 20 &&
            grant.Sku == "AI-CREDIT-1" &&
            grant.CreditsGranted == 1), true), Times.Once);
    }

    [Test]
    public async Task Paid_Order_With_AICredit10_Quantity2_Grants_Twenty_Credits()
    {
        var order = new Order { Id = 1003, CustomerId = 30 };
        _customerService.Setup(x => x.GetCustomerByIdAsync(30)).ReturnsAsync(new Customer { Id = 30 });
        _orderService.Setup(x => x.GetOrderItemsAsync(1003, null, null, 0))
            .ReturnsAsync(new List<OrderItem>
            {
                new() { Id = 13, OrderId = 1003, ProductId = 102, Quantity = 2 }
            });
        _orderService.Setup(x => x.GetProductByOrderItemIdAsync(13))
            .ReturnsAsync(new Product { Id = 102, Sku = "AI-CREDIT-10" });

        await _service.GrantCreditsForPaidOrderAsync(order);

        _creditService.Verify(x => x.AddCreditAsync(30, 20, "Purchased credit pack: order #1003, SKU AI-CREDIT-10, credits 20"), Times.Once);
    }

    [Test]
    public async Task Paid_Order_With_AICredit20_Quantity1_Grants_Twenty_Credits()
    {
        var order = new Order { Id = 1004, CustomerId = 40 };
        _customerService.Setup(x => x.GetCustomerByIdAsync(40)).ReturnsAsync(new Customer { Id = 40 });
        _orderService.Setup(x => x.GetOrderItemsAsync(1004, null, null, 0))
            .ReturnsAsync(new List<OrderItem>
            {
                new() { Id = 14, OrderId = 1004, ProductId = 103, Quantity = 1 }
            });
        _orderService.Setup(x => x.GetProductByOrderItemIdAsync(14))
            .ReturnsAsync(new Product { Id = 103, Sku = "AI-CREDIT-20" });

        await _service.GrantCreditsForPaidOrderAsync(order);

        _creditService.Verify(x => x.AddCreditAsync(40, 20, "Purchased credit pack: order #1004, SKU AI-CREDIT-20, credits 20"), Times.Once);
    }

    [Test]
    public async Task Mixed_Cart_Grants_Only_Configured_Items()
    {
        var order = new Order { Id = 1005, CustomerId = 50 };
        _customerService.Setup(x => x.GetCustomerByIdAsync(50)).ReturnsAsync(new Customer { Id = 50 });
        _orderService.Setup(x => x.GetOrderItemsAsync(1005, null, null, 0))
            .ReturnsAsync(new List<OrderItem>
            {
                new() { Id = 15, OrderId = 1005, ProductId = 104, Quantity = 1 },
                new() { Id = 16, OrderId = 1005, ProductId = 105, Quantity = 2 }
            });
        _orderService.Setup(x => x.GetProductByOrderItemIdAsync(15))
            .ReturnsAsync(new Product { Id = 104, Sku = "AI-CREDIT-1" });
        _orderService.Setup(x => x.GetProductByOrderItemIdAsync(16))
            .ReturnsAsync(new Product { Id = 105, Sku = "UNKNOWN-SKU" });

        await _service.GrantCreditsForPaidOrderAsync(order);

        _creditService.Verify(x => x.AddCreditAsync(50, 1, It.Is<string>(remarks => remarks.Contains("AI-CREDIT-1"))), Times.Once);
        _creditService.Verify(x => x.AddCreditAsync(50, It.Is<decimal>(amount => amount != 1), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task Reprocessing_Same_OrderPaidEvent_Does_Not_Duplicate_Credits()
    {
        var order = new Order { Id = 1006, CustomerId = 60 };
        _customerService.Setup(x => x.GetCustomerByIdAsync(60)).ReturnsAsync(new Customer { Id = 60 });
        _orderService.Setup(x => x.GetOrderItemsAsync(1006, null, null, 0))
            .ReturnsAsync(new List<OrderItem>
            {
                new() { Id = 17, OrderId = 1006, ProductId = 106, Quantity = 1 }
            });
        _orderService.Setup(x => x.GetProductByOrderItemIdAsync(17))
            .ReturnsAsync(new Product { Id = 106, Sku = "AI-CREDIT-1" });

        await _service.GrantCreditsForPaidOrderAsync(order);
        await _service.GrantCreditsForPaidOrderAsync(order);

        _creditService.Verify(x => x.AddCreditAsync(60, 1, It.IsAny<string>()), Times.Once);
        _grantRepository.Verify(x => x.InsertAsync(It.IsAny<CreditPurchaseGrant>(), true), Times.Once);
    }

    [Test]
    public async Task Existing_Grant_Record_Skips_Purchase_And_Wallet_Update()
    {
        var order = new Order { Id = 1009, CustomerId = 90 };
        _customerService.Setup(x => x.GetCustomerByIdAsync(90)).ReturnsAsync(new Customer { Id = 90 });
        _orderService.Setup(x => x.GetOrderItemsAsync(1009, null, null, 0))
            .ReturnsAsync(new List<OrderItem>
            {
                new() { Id = 19, OrderId = 1009, ProductId = 109, Quantity = 1 }
            });
        _orderService.Setup(x => x.GetProductByOrderItemIdAsync(19))
            .ReturnsAsync(new Product { Id = 109, Sku = "AI-CREDIT-1" });

        // Simulate a row already created by a previous successful run.
        var existingGrant = new CreditPurchaseGrant { Id = 1, OrderId = 1009, OrderItemId = 19, CustomerId = 90 };
        _grantRepository.Setup(x => x.GetAllAsync(
                It.IsAny<Func<IQueryable<CreditPurchaseGrant>, IQueryable<CreditPurchaseGrant>>>(),
                It.IsAny<Func<ICacheKeyService, CacheKey>>(),
                true))
            .ReturnsAsync((Func<IQueryable<CreditPurchaseGrant>, IQueryable<CreditPurchaseGrant>> func, Func<ICacheKeyService, CacheKey> _, bool __) =>
            {
                var grants = new List<CreditPurchaseGrant> { existingGrant };
                return func == null ? grants : func(grants.AsQueryable()).ToList();
            });

        await _service.GrantCreditsForPaidOrderAsync(order);

        _creditService.Verify(x => x.AddCreditAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        _grantRepository.Verify(x => x.InsertAsync(It.IsAny<CreditPurchaseGrant>(), true), Times.Never);
    }

    [Test]
    public async Task Guest_Order_Is_Skipped()
    {
        var order = new Order { Id = 1007, CustomerId = 70 };
        _customerService.Setup(x => x.GetCustomerByIdAsync(70)).ReturnsAsync((Customer)null);

        await _service.GrantCreditsForPaidOrderAsync(order);

        _creditService.Verify(x => x.AddCreditAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>()), Times.Never);
        _grantRepository.Verify(x => x.InsertAsync(It.IsAny<CreditPurchaseGrant>(), true), Times.Never);
    }

    [Test]
    public async Task OrderPaidEventConsumer_Forwards_Order_To_Service()
    {
        var creditPurchaseService = new Mock<ICreditPurchaseService>();
        var consumer = new CreditPurchaseEventConsumer(creditPurchaseService.Object);
        var order = new Order { Id = 1008, CustomerId = 80 };

        await consumer.HandleEventAsync(new OrderPaidEvent(order));

        creditPurchaseService.Verify(x => x.GrantCreditsForPaidOrderAsync(order), Times.Once);
    }
}
