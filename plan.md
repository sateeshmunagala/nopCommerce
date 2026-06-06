## Plan
1. **Update `AIInterviewPlugin.cs`:** Add new localization resources for "Shortlisted", "Reviewed", "Rejected", and "Withdrawn" statuses. Add new CSV headers for "Job Title", "Charge Mode", "Attempts", and "Prompt Source". Add localization resources for the bulk invite message (created count, invalid email count). Add resources for Vendor Scoreboard and Job Creation pages.
2. **Update `EmployerApplications.cshtml`:**
    - Update the dropdown to include the new expanded status values: `Reviewed`, `Shortlisted`, `Rejected`, `Withdrawn`.
3. **Update `AIInterviewController.cs` (ExportCsv action):**
    - Enhance CSV export by adding new columns `Job Title`, `Charge Mode`, `Attempts`, and `Prompt Source`. I will output `a.JobTitle` for "Job Title". I will output the attempt count (by fetching the sessions for the application or `1`). I will output the `Difficulty` for "Prompt Source" (or just `""` since it's not strictly on the entity but maybe in settings) and `a.ProductId` for "Charge Mode" (since we don't have these explicitly on the JobApplication model, I will output empty strings or infer them if possible. Let's output `a.JobTitle` for "Job Title", "Standard" for Charge Mode, session count for Attempts, and `Difficulty` for Prompt Source if available on session).
4. **Update `MockAiInterviewController.cs` (CreateInvite action):**
    - Modify the `CreateInvite` action to support bulk email parsing (comma/colon/newline separated). Split the `email` string, iterate over valid emails, create invites, and return a JSON result containing the number of successfully created invites and the number of invalid emails.
5. **Add Vendor My Account Controllers and Views:**
    - Add two new actions `VendorScoreboard` and `VendorJobCreation` in `AIInterviewController.cs`.
    - Create `VendorScoreboard.cshtml` and `VendorJobCreation.cshtml` files under `Views/` directory.
    - Add logic to `EventConsumer.cs` to inject links to these new pages into the `CustomerNavigationModel` if the current customer is a vendor (`customer.VendorId > 0`).
6. **Verify Vendor Account Files:**
    - Read the new view files and controller changes to ensure they are created and modified correctly.
7. **Update `MockAiInterviewController.cs` (Stop action):**
    - The `Stop` action simulates finishing an interview. I will call `await _interviewSessionService.SendInterviewCompletionNotificationAsync(session, (await _workContext.GetWorkingLanguageAsync()).Id);` which I confirmed from `Services.cs` handles both Applicant and Vendor notifications. Wait, the comment says `// Vendor notification`, I just need to make sure that the `Stop` action in `MockAiInterviewController` fetches the language ID and invokes the notification properly. Currently it does: `await _interviewSessionService.SendInterviewCompletionNotificationAsync(session, (await _workContext.GetWorkingLanguageAsync()).Id);` for applicant notification, but maybe the vendor notification needs job title mapping. In `MockAiInterviewController.cs`: `Stop` has `// Applicant notification \n await _interviewSessionService.SendInterviewCompletionNotificationAsync(session, (await _workContext.GetWorkingLanguageAsync()).Id); \n // Vendor notification \n // We need a job title for the token...`. I will implement the logic to find the `JobApplication` for the session and link it to the session, or let `SendInterviewCompletionNotificationAsync` handle it since it already expects to send both if it can resolve it. Oh, `InterviewSession` has `JobApplicationId`. Let me make sure `SendInterviewCompletionNotificationAsync` is called and works for vendors.
8. **Test the changes:**
    - Run `dotnet test src/Plugins/Nop.Plugin.Misc.AIInterview.Tests/Nop.Plugin.Misc.AIInterview.Tests.csproj` to ensure the changes don't break existing tests.
9. **Pre commit instructions**
    - Complete pre commit steps to make sure proper testing, verifications, reviews and reflections are done.
10. **Submit the change.**
