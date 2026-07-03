1. **Add missing Razor views**
   - Create `src/Plugins/Nop.Plugin.Payments.Razorpay/Views/_ViewImports.cshtml`
   - Create `src/Plugins/Nop.Plugin.Payments.Razorpay/Views/Configure.cshtml`
   - Create `src/Plugins/Nop.Plugin.Payments.Razorpay/Views/PaymentInfo.cshtml`

2. **Implement the hosted popup checkout UI in `PaymentInfo.cshtml`**
   - Load `checkout.js`.
   - Render checkout button.
   - Add hidden fields: `RazorpayOrderId`, `RazorpayPaymentId`, `RazorpaySignature`.
   - On button click, call `CreateOrder` endpoint, then open Razorpay popup.
   - On success, verify payment by calling `VerifyPayment` endpoint.
   - Do not allow proceeding without verification.

3. **Add `VerifyPayment` endpoint in `RazorpayPublicController.cs`**
   - Accept `razorpay_order_id`, `razorpay_payment_id`, `razorpay_signature`.
   - Verify HMAC SHA-256.
   - Return JSON success or error.

4. **Harden server-side payment processing**
   - Update `ProcessPaymentAsync` to fetch payment from Razorpay API.
   - Set `CaptureTransactionId`.

5. **Fix additional fee calculation**
   - Inject `IOrderTotalCalculationService` into `RazorpayPaymentMethod`.
   - Update `GetAdditionalHandlingFeeAsync`.

6. **Improve admin configuration behavior**
   - `Configure.cshtml` uses nopCommerce tag helpers.
   - Hide/do not echo `KeySecret`.
   - Clear test/live instructions.
   - Model validation for keys.

7. **Add route constants/provider**
   - Add routes in `RazorpayDefaults` or public controller for easy URL generation.

8. **Clean unused imports and warnings**
   - Check and remove unused usings.

9. **Pre-commit tasks**
   - Check pre-commit instructions, run `dotnet build`.
