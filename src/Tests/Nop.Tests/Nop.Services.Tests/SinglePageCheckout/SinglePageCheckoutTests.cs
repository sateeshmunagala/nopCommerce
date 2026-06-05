using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.SinglePageCheckout;
using Nop.Plugin.Misc.SinglePageCheckout.Factories;
using Nop.Web.Models.ShoppingCart;

namespace Nop.Tests.Nop.Services.Tests.SinglePageCheckout
{
    [TestFixture]
    public class SinglePageCheckoutTests : ServiceTest
    {
        [Test]
        public void ModelTuner_Applies_Tuning_Rules()
        {
            // Arrange
            var settings = new SinglePageCheckoutSettings
            {
                ShowDiscountBox = false,
                ShowGiftCardBox = false,
                ShowCheckoutAttributes = false,
                ShowOrderReviewData = false
            };
            var tuner = new SinglePageCheckoutModelTuner(settings);

            var model = new ShoppingCartModel
            {
                DiscountBox = new ShoppingCartModel.DiscountBoxModel { Display = true },
                GiftCardBox = new ShoppingCartModel.GiftCardBoxModel { Display = true },
                OrderReviewData = new ShoppingCartModel.OrderReviewDataModel { Display = true }
            };
            model.CheckoutAttributes.Add(new ShoppingCartModel.CheckoutAttributeModel());

            // Act
            tuner.TuneShoppingCartModel(model);

            // Assert
            model.HideCheckoutButton.Should().BeTrue();
            model.DiscountBox.Display.Should().BeFalse();
            model.GiftCardBox.Display.Should().BeFalse();
            model.CheckoutAttributes.Should().BeEmpty();
            model.OrderReviewData.Display.Should().BeFalse();
        }

        [Test]
        public void ModelTuner_Retains_Display_When_Settings_Enabled()
        {
            // Arrange
            var settings = new SinglePageCheckoutSettings
            {
                ShowDiscountBox = true,
                ShowGiftCardBox = true,
                ShowCheckoutAttributes = true,
                ShowOrderReviewData = true
            };
            var tuner = new SinglePageCheckoutModelTuner(settings);

            var model = new ShoppingCartModel
            {
                DiscountBox = new ShoppingCartModel.DiscountBoxModel { Display = true },
                GiftCardBox = new ShoppingCartModel.GiftCardBoxModel { Display = true },
                OrderReviewData = new ShoppingCartModel.OrderReviewDataModel { Display = true }
            };
            model.CheckoutAttributes.Add(new ShoppingCartModel.CheckoutAttributeModel());

            // Act
            tuner.TuneShoppingCartModel(model);

            // Assert
            model.HideCheckoutButton.Should().BeTrue();
            model.DiscountBox.Display.Should().BeTrue();
            model.GiftCardBox.Display.Should().BeTrue();
            model.CheckoutAttributes.Should().NotBeEmpty();
            model.OrderReviewData.Display.Should().BeTrue();
        }
    }
}