
-- use onjobsupport47

-- use  [qaonjobsupport47]


-- SELECT * FROM [dbo].[LocaleStringResource] WHERE ResourceValue like '%options%'
-- SELECT * FROM [dbo].[LocaleStringResource] WHERE ResourceName like '%ContactUs.YourEnquiryHasBeenSent'

---------------  Local String update queries    ------------------------------------------

UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Recently viewed profiles' WHERE [ResourceName] = 'products.recentlyviewedproducts';
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Shortlisted' WHERE [ResourceName]='pagetitle.wishlist';
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='The profile has been added to your <a href="{0}">shortlist</a>'  WHERE [ResourceName]='products.producthasbeenaddedtothewishlist.link';
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Shortlisted'  WHERE [ResourceName]='wishlist';
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='There are no shortlisted profiles' WHERE [ResourceName]='wishlist.cartisempty';

UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='My profile reviews' WHERE [ResourceName]='account.customerproductreviews';
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Profile specifications' WHERE [ResourceName]='products.specs';
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='New Profiles' WHERE [ResourceName]='pagetitle.newproducts';
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='New profiles' WHERE [ResourceName]='products.newproducts';
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Search  Any Technology' WHERE [ResourceName]='search.searchbox.text.label';

UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Search  Any Technology' WHERE [ResourceName]='search.searchbox.tooltip';
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='No profiles were found that matched your criteria. Please adjust your filter criteria to see more profiles. </br> (OR) </br> Send an email using Contact us form, our team will try best to get the relavent profiles.' WHERE resourcename = 'catalog.products.noresult';
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Profile review for' WHERE [ResourceName]='account.customerproductreviews.productreviewfor'
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='You will see the profile review after approving by administrator.' WHERE [ResourceName]='reviews.seeafterapproving'
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Profile review for' WHERE [ResourceName]='account.customerproductreviews.productreviewfor'

UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Subscription(s)' WHERE [ResourceName]='shoppingcart.product(s)'
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='By creating an account on our website, you will be able to communicate those who needs/provide proxy support in an efficent manner with seamless experiance, and also you can switch from one proxy support to another with out worrying too much.' WHERE [ResourceName]='account.login.newcustomertext'
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Featured Profiles' WHERE [ResourceName]='homepage.products'
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Technical Details' WHERE [ResourceName]='account.options'

UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Mobile Number' WHERE [ResourceName]='account.fields.phone'
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Mobile number is not valid' WHERE [ResourceName]='account.fields.phone.notvalid'
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Mobile number is required' WHERE [ResourceName]='account.fields.phone.required'
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Profile Photo' WHERE [ResourceName]='account.avatar'
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Remove Photo' WHERE [ResourceName]='account.avatar.removeavatar'

UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Photo must be in GIF or JPEG format with the maximum size of 1 MB' WHERE [ResourceName]='account.avatar.uploadrules'
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Profile reviews for' WHERE [ResourceName]='reviews.productreviewsfor'
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Profile tags' WHERE [ResourceName]='products.tags'
UPDATE [dbo].[LocaleStringResource] SET [ResourceValue]='Sign In Here' WHERE [ResourceName]='account.login.returningcustomer'
Update [dbo].[LocaleStringResource] SET [ResourceValue]='Similar Profiles' WHERE [ResourceName]='products.relatedproducts'

Update [dbo].[LocaleStringResource] SET [ResourceValue]='You are already subscribed for this profile back in available notification' WHERE [ResourceName]='backinstocksubscriptions.alreadysubscribed'
Update [dbo].[LocaleStringResource] SET [ResourceValue]='Receive an email when profile is available' WHERE [ResourceName]='backinstocksubscriptions.popuptitle'
Update [dbo].[LocaleStringResource] SET [ResourceValue]='You will receive a onetime e-mail when this profile is available' WHERE [ResourceName]='backinstocksubscriptions.tooltip'
Update [dbo].[LocaleStringResource] SET [ResourceValue]='You will receive an e-mail when a particular profile is back to available.' WHERE [ResourceName]='account.backinstocksubscriptions.description'
Update [dbo].[LocaleStringResource] SET [ResourceValue]='Partner Consultancies' WHERE [ResourceName]='manufacturers'
--Update [dbo].[LocaleStringResource] SET ResourceValue='Your Shopping Cart is empty!' WHERE [ResourceName]='shoppingcart.cartisempty';


Update [dbo].[LocaleStringResource] SET [ResourceValue]='Your enquiry has been successfully sent to support team. Team will reach out to you in next 24 hours.' WHERE [ResourceName]='contactus.yourenquiryhasbeensent'
UPDATE LocaleStringResource 
SET 
ResourceValue='<p><strong>Welcome to on job support!</strong><br />Register with us for future convenience:</p><p style="text-align: left;">1.Resgistration is mandatory as we need to show relavent profiles to provide support and take support</p><p style="text-align: left;">2. You can directly contact with people who can provide support thus eliminating middle man</p><p style="text-align: left;">3. Please visit <a title="This Link" href="https://onjobsupport.in" target="_blank" rel="noopener">This Link</a> for further information</p>'
Where ResourceName='Account.Login.NewCustomerText'

IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='Orders.UpgradeSubscription.Message')
   BEGIN
		INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
		VALUES('Orders.UpgradeSubscription.Message','Please upgrade to Subscription to View Mobile Number ,Send the messages.',1)
   END


Update [dbo].[LocaleStringResource] SET [ResourceValue]='Show profiles in category {0}' WHERE [ResourceName]='media.category.imagelinktitleformat'
-- Show products in category {0}

IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='rewardpoints.fields.date')
   BEGIN
		INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
		VALUES('rewardpoints.fields.date','Date',1)
   END


Update [dbo].[LocaleStringResource] SET [ResourceValue]='Be the first to review this profile' WHERE [ResourceName]='reviews.overview.first'
Update [dbo].[LocaleStringResource] SET [ResourceValue]='An error occured. Please try again later.' WHERE [ResourceName]='shoppingcart.maximumquantity'
--The maximum quantity allowed for purchase is {0}.

Update LocaleStringResource SET ResourceValue='Your registration completed. HR Manager will contact you shortly for further processing of your profile.' WHERE ResourceName='account.register.result.standard'
Update LocaleStringResource SET ResourceValue='Your registration completed. HR Manager will contact you for activation of your account and for further processing of your profile..' WHERE ResourceName='account.register.result.adminapproval'
Update LocaleStringResource SET ResourceValue='Search In profile descriptions' WHERE ResourceName='search.searchindescriptions'


IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='Catalog.Products.GuestCustomerResult')
   BEGIN
		INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
		VALUES('Catalog.Products.GuestCustomerResult','Please login/register to see the profiles. This helps us to show the relavent profiles.',1)
   END

Update LocaleStringResource SET ResourceValue='Please <a href="https://onjobsupport.in/login">Login / Register</a> to see the profiles. This helps us to show the relavent profiles.' WHERE ResourceName='Catalog.Products.GuestCustomerResult'

Update [LocaleStringResource] SET ResourceValue='Added a profile to wishlist (''{0}'')' WHERE ResourceName='activitylog.publicstore.addtowishlist'
--Added a product to wishlist ('{0}')

Update [LocaleStringResource] SET ResourceValue='Popular Technologies' WHERE ResourceName='categories';
Update [LocaleStringResource] SET ResourceValue='Public store. Viewed a profile details page (''{0}'')' WHERE ResourceName='activitylog.publicstore.viewproduct';

update LocaleStringResource SET ResourceValue='Your subscription has been successfully processed!' Where ResourceName='checkout.yourorderhasbeensuccessfullyprocessed'

update LocaleStringResource SET ResourceValue='Skills!' Where ResourceName='search.category'

IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='admin.contentmanagement.messagetemplates.description.newcustomer.notifytargetcustomers')
   BEGIN
		INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
		VALUES('admin.contentmanagement.messagetemplates.description.newcustomer.notifytargetcustomers','This message template is used to notify give support users when a new take support customer is registered.',1)
   END


UPDATE [dbo].[LocaleStringResource] 
SET [ResourceValue]='<div class="panel panel-danger"><div class="panel-heading"><h3 class="panel-title">No Results!</h3></div><div class="panel-body">No profiles were found that matched your criteria. Please adjust your filter criteria to see more profiles. </br> (OR) </br> Send an email using Contact us form, our team will try best to get the relavent profiles.</div></div>' 
WHERE resourcename = 'catalog.products.noresult';

--Update LocaleStringResource 
--SET ResourceValue='<div class="panel panel-danger"><div class="panel-heading"><h3 class="panel-title">Login/Register!</h3></div><div class="panel-body">Please <a href="https://onjobsupport.in/login">Login/Register</a> to see the profiles</br>This helps us to show the relavent profiles</div></div>'  
--WHERE ResourceName='Catalog.Products.GuestCustomerResult'

Update LocaleStringResource 
SET ResourceValue='<div class="panel panel-danger"><div class="panel-heading"><h3 class="panel-title">Login/Register!</h3></div><div class="panel-body">Please <a href="https://onjobsupport.in/login" class="btn btn-default">Login/Register</a> to see the profiles </br>This helps us to show the relavent profiles</div></div>'  
WHERE ResourceName='Catalog.Products.GuestCustomerResult'

update LocaleStringResource SET ResourceValue='Profiles tagged with ''{0}''' Where ResourceName='pagetitle.productsbytag'
update LocaleStringResource SET ResourceValue='Profiles tagged with ''{0}''' Where ResourceName='products.tags.productstaggedwith'

IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='account.register.errors.phonealreadyexists')
   BEGIN
		INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
		VALUES('account.register.errors.phonealreadyexists','The specified phone already exists.',1)
   END


IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='admin.configuration.emailaccounts.redirecturl.info')
BEGIN
	INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
	VALUES('admin.configuration.emailaccounts.redirecturl.info','<![CDATA[<b>{0}</b> - enter this "Authorized redirect URI" when creating your credentials in the Google Cloud console. You will be redirected to this path after authenticating with Google.]]>',1)
END

IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='Admin.Configuration.EmailAccounts.Fields.EmailAuthenticationMethod')
BEGIN
	INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
	VALUES('Admin.Configuration.EmailAccounts.Fields.EmailAuthenticationMethod','Authentication method',1)
END

IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='admin.configuration.emailaccounts.fields.clientid')
BEGIN
	INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
	VALUES('admin.configuration.emailaccounts.fields.clientid','Client ID',1)
END

IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='admin.configuration.emailaccounts.fields.clientsecret')
BEGIN
	INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
	VALUES('admin.configuration.emailaccounts.fields.clientsecret','Client Secret',1)
END

IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='admin.configuration.emailaccounts.fields.tenantid')
BEGIN
	INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
	VALUES('admin.configuration.emailaccounts.fields.tenantid','Tenant identifier',1)
END

IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='admin.configuration.emailaccounts.fields.clientsecret.change')
BEGIN
	INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
	VALUES('admin.configuration.emailaccounts.fields.clientsecret.change','Change',1)
END

IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='Admin.Configuration.EmailAccounts.Fields.ClientSecret.ClientSecretChanged')
BEGIN
	INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
	VALUES('Admin.Configuration.EmailAccounts.Fields.ClientSecret.ClientSecretChanged','The client Secret has been changed successfully.',1)
END

update LocaleStringResource SET ResourceValue='The specified mobile number already exists.' Where ResourceName='account.register.errors.phonealreadyexists'

-- 4.7.5 insert/upgrade scrips

IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='Admin.Configuration.EmailAccounts.Fields.TenantId.Required')
BEGIN
	INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
	VALUES('Admin.Configuration.EmailAccounts.Fields.TenantId.Required','The tenant identifier is required',1)
END

IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='Admin.Configuration.EmailAccounts.Fields.ClientId.Required')
BEGIN
	INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
	VALUES('Admin.Configuration.EmailAccounts.Fields.ClientId.Required','The client identifier is required',1)
END

IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='Admin.Configuration.EmailAccounts.Fields.ClientSecret.Required')
BEGIN
	INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
	VALUES('Admin.Configuration.EmailAccounts.Fields.ClientSecret.Required','The client Secret is required',1)
END

IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='Admin.Configuration.EmailAccounts.AuthorizationRequest.Text')
BEGIN
	INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
	VALUES('Admin.Configuration.EmailAccounts.AuthorizationRequest.Text','Authorization request',1)
END

IF NOT EXISTS (SELECT * FROM [LocaleStringResource] WHERE [ResourceName]='Admin.Configuration.EmailAccounts.AuthorizationRequest.Info')
BEGIN
	INSERT INTO [dbo].[LocaleStringResource]([ResourceName],[ResourceValue],[LanguageId]) 
	VALUES('Admin.Configuration.EmailAccounts.AuthorizationRequest.Info','Your application must have that consent before it can execute a Google API request that requires user authorization. Click the {0} button below and follow the steps to perform API requests.',1)
END


