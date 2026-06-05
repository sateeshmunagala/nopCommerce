1. **Data model and service contract updates:**
   - Create `src/Plugins/Nop.Plugin.Misc.AIInterview/Data/SessionProductLinkMigration.cs` to add `ProductId` to `InterviewSession` table if not already doing so. It's much cleaner than creating dummy `JobApplication`s.
   - Update `InterviewSession.cs` domain entity to include `public int ProductId { get; set; }`.
   - Update `IInterviewSessionService` and `Services.cs`:
     - Modify `GetHighestScoreByCustomerIdAsync` to `GetHighestScoreByCustomerIdAndProductIdAsync(int customerId, int productId)`.
     - Modify `GetLatestCompletedSessionByCustomerIdAsync` to `GetLatestCompletedSessionByCustomerIdAndProductIdAsync`.
     - Update `GetSessionsByCustomerIdAsync` to optionally filter by `productId` or add `GetSessionsByCustomerIdAndProductIdAsync`.

2. **Controller logic updates:**
   - **`MockAiInterviewController.cs` (StartPost)**:
     - Accept `productId` as a parameter.
     - Validate sponsor credits / fallback to wallet logic.
       - Valid sponsor token + invite -> use sponsor path.
       - Invalid/exhausted sponsor token -> fallback to customer wallet.
       - No credits in both -> return visible error and pricing link.
     - Save `productId` in `InterviewSession`.
   - **`AIInterviewController.cs` (Apply)**:
     - Update to receive `productId` alongside `jobTitle`.
     - Update eligibility check to use `GetHighestScoreByCustomerIdAndProductIdAsync(customer.Id, productId)`.
     - Filter `applications` by `ProductId` instead of `JobTitle`.
     - Map `JobApplication.ProductId = productId`.
   - **`AIInterviewController.cs` (MyApplications)**:
     - Ensure each application row only looks at sessions tied to its `ProductId`.
     - Use `sessions.Where(s => s.ProductId == a.ProductId)` (or matching `JobApplicationId`).

3. **Views and widget rendering:**
   - Restore the missing widget view `AIInterviewProductDetails/Default.cshtml` in the correct location (`src/Plugins/Nop.Plugin.Misc.AIInterview/Views/Shared/Components/AIInterviewProductDetails/Default.cshtml`).
   - Fix the Start Interview and Apply buttons to pass `productId`.
   - Add sponsor-token, no-credit messaging, and pricing link to the widget view.

4. **Notifications and resource cleanup:**
   - Replace hardcoded validation text in `ApplyModelValidator.cs` with resource keys.
   - Update notification logic in `MockAiInterviewController.Stop` and `Services.cs` (`SendInterviewCompletionNotificationAsync` etc.) to use the correctly linked `ProductId` / `jobTitle` from `InterviewSession.ProductId` to resolve the Product and Vendor.
   - Update `Report.cshtml` and `Report` action to show per-question scores.
   - Update runtime UX (`Runtime.cshtml`): The requirements state "either real voice capture plus transcript display is implemented, or the requirement is formally revised". We will explicitly document a scoped exception as requested ("explicitly document a scoped exception") via an alert on the page or by just noting the limitation in text in `Runtime.cshtml`.

5. **Tests and regression pass:**
   - Update `ApplyFlowTests.cs`, `CandidateFlowTests.cs`, and `EmployerTests.cs` to cover `ProductId` linkage, sponsor-credit fallback, Same-job score gating, `My Applications` session mapping, and widget view rendering.
