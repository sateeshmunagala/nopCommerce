/*
JobSupport plugin-owned schema for Microsoft SQL Server.

Purpose:
- Creates the normalized tables needed by Nop.Plugin.Misc.JobSupport.
- Can be run against another nopCommerce SQL Server database.
- Is safe to rerun after a successful execution.
- Does not copy data, alter core tables, create procedures, or drop legacy objects.

Execution:
1. Back up the target database.
2. Select the target nopCommerce database in the SQL client.
3. Execute this entire script.
4. Use the phase 5 migration/backfill process to copy legacy data.

Important:
- The target database must already contain the standard nopCommerce tables checked below.
- Existing JobSupport tables are not modified by this bootstrap script. Use versioned plugin
  migrations for schema upgrades after initial creation.
- Standard customer data such as email, phone, first/last name, gender, company, login dates,
  and activity dates remains in nopCommerce and is resolved through CustomerId. It is not
  duplicated here.
- Legacy product, shopping-cart, reward-point, generic-attribute, and private-message data is
  retained for migration evidence and rollback.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'[dbo].[Customer]', N'U') IS NULL
    THROW 51000, 'The target database is missing the nopCommerce Customer table.', 1;

IF OBJECT_ID(N'[dbo].[Product]', N'U') IS NULL
    THROW 51001, 'The target database is missing the nopCommerce Product table.', 1;

IF OBJECT_ID(N'[dbo].[Picture]', N'U') IS NULL
    THROW 51002, 'The target database is missing the nopCommerce Picture table.', 1;

IF OBJECT_ID(N'[dbo].[Country]', N'U') IS NULL
    THROW 51003, 'The target database is missing the nopCommerce Country table.', 1;

IF OBJECT_ID(N'[dbo].[StateProvince]', N'U') IS NULL
    THROW 51004, 'The target database is missing the nopCommerce StateProvince table.', 1;

IF OBJECT_ID(N'[dbo].[Order]', N'U') IS NULL
    THROW 51005, 'The target database is missing the nopCommerce Order table.', 1;

IF OBJECT_ID(N'[dbo].[OrderItem]', N'U') IS NULL
    THROW 51006, 'The target database is missing the nopCommerce OrderItem table.', 1;

IF OBJECT_ID(N'[dbo].[PrivateMessage]', N'U') IS NULL

BEGIN TRY
BEGIN TRANSACTION;

IF OBJECT_ID(N'[dbo].[JobSupport_Profile]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobSupport_Profile]
    (
        [Id] int IDENTITY(1,1) NOT NULL,
        [CustomerId] int NOT NULL,
        [LegacyProductId] int NULL,
        [ProfileType] int NOT NULL,
        [DisplayName] nvarchar(400) NOT NULL,
        [Slug] nvarchar(400) NULL,
        [ShortDescription] nvarchar(max) NULL,
        [FullDescription] nvarchar(max) NULL,
        [CurrentAvailability] nvarchar(200) NULL,
        [AvailabilityDays] nvarchar(400) NULL,
        [AvailabilityTimings] nvarchar(400) NULL,
        [HoursPerWeek] nvarchar(100) NULL,
        [MotherTongue] nvarchar(200) NULL,
        [RelevantExperience] nvarchar(1000) NULL,
        [AvatarPictureId] int NULL,
        [CountryId] int NULL,
        [StateProvinceId] int NULL,
        [City] nvarchar(100) NULL,
        [IsPublished] bit NOT NULL CONSTRAINT [DF_JobSupport_Profile_IsPublished] DEFAULT (0),
        [CreatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_Profile_CreatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        [UpdatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_Profile_UpdatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        [MigrationSource] nvarchar(100) NULL,
        [LegacySourceId] int NULL,
        CONSTRAINT [PK_JobSupport_Profile] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_JobSupport_Profile_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
        CONSTRAINT [FK_JobSupport_Profile_LegacyProduct] FOREIGN KEY ([LegacyProductId]) REFERENCES [dbo].[Product] ([Id]),
        CONSTRAINT [FK_JobSupport_Profile_AvatarPicture] FOREIGN KEY ([AvatarPictureId]) REFERENCES [dbo].[Picture] ([Id]),
        CONSTRAINT [FK_JobSupport_Profile_Country] FOREIGN KEY ([CountryId]) REFERENCES [dbo].[Country] ([Id]),
        CONSTRAINT [FK_JobSupport_Profile_StateProvince] FOREIGN KEY ([StateProvinceId]) REFERENCES [dbo].[StateProvince] ([Id]),
        CONSTRAINT [CK_JobSupport_Profile_ProfileType] CHECK ([ProfileType] >= 0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_Profile]') AND [name] = N'UX_JobSupport_Profile_CustomerId')
    CREATE UNIQUE INDEX [UX_JobSupport_Profile_CustomerId] ON [dbo].[JobSupport_Profile] ([CustomerId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_Profile]') AND [name] = N'UX_JobSupport_Profile_LegacyProductId')
    CREATE UNIQUE INDEX [UX_JobSupport_Profile_LegacyProductId] ON [dbo].[JobSupport_Profile] ([LegacyProductId]) WHERE [LegacyProductId] IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_Profile]') AND [name] = N'UX_JobSupport_Profile_Slug')
    CREATE UNIQUE INDEX [UX_JobSupport_Profile_Slug] ON [dbo].[JobSupport_Profile] ([Slug]) WHERE [Slug] IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_Profile]') AND [name] = N'IX_JobSupport_Profile_ProfileType_IsPublished')
    CREATE INDEX [IX_JobSupport_Profile_ProfileType_IsPublished] ON [dbo].[JobSupport_Profile] ([ProfileType], [IsPublished]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_Profile]') AND [name] = N'IX_JobSupport_Profile_UpdatedOnUtc')
    CREATE INDEX [IX_JobSupport_Profile_UpdatedOnUtc] ON [dbo].[JobSupport_Profile] ([UpdatedOnUtc]);

IF OBJECT_ID(N'[dbo].[JobSupport_ProfileSkill]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobSupport_ProfileSkill]
    (
        [Id] int IDENTITY(1,1) NOT NULL,
        [ProfileId] int NOT NULL,
        [SkillType] int NOT NULL,
        [Name] nvarchar(400) NOT NULL,
        [LegacySpecificationAttributeId] int NULL,
        [LegacySpecificationAttributeOptionId] int NULL,
        [DisplayOrder] int NOT NULL CONSTRAINT [DF_JobSupport_ProfileSkill_DisplayOrder] DEFAULT (0),
        [CreatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_ProfileSkill_CreatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        [UpdatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_ProfileSkill_UpdatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_JobSupport_ProfileSkill] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_JobSupport_ProfileSkill_Profile] FOREIGN KEY ([ProfileId]) REFERENCES [dbo].[JobSupport_Profile] ([Id]),
        CONSTRAINT [CK_JobSupport_ProfileSkill_SkillType] CHECK ([SkillType] >= 0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_ProfileSkill]') AND [name] = N'UX_JobSupport_ProfileSkill_Profile_Type_Name')
    CREATE UNIQUE INDEX [UX_JobSupport_ProfileSkill_Profile_Type_Name] ON [dbo].[JobSupport_ProfileSkill] ([ProfileId], [SkillType], [Name]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_ProfileSkill]') AND [name] = N'IX_JobSupport_ProfileSkill_LegacyOptionId')
    CREATE INDEX [IX_JobSupport_ProfileSkill_LegacyOptionId] ON [dbo].[JobSupport_ProfileSkill] ([LegacySpecificationAttributeOptionId]) WHERE [LegacySpecificationAttributeOptionId] IS NOT NULL;

IF OBJECT_ID(N'[dbo].[JobSupport_Relationship]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobSupport_Relationship]
    (
        [Id] int IDENTITY(1,1) NOT NULL,
        [SourceCustomerId] int NOT NULL,
        [TargetCustomerId] int NOT NULL,
        [SourceProfileId] int NOT NULL,
        [TargetProfileId] int NOT NULL,
        [RelationshipType] int NOT NULL,
        [Status] int NOT NULL,
        [CreatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_Relationship_CreatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        [UpdatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_Relationship_UpdatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        [RespondedOnUtc] datetime2(6) NULL,
        [LegacyShoppingCartItemId] int NULL,
        [MetadataJson] nvarchar(max) NULL,
        CONSTRAINT [PK_JobSupport_Relationship] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_JobSupport_Relationship_SourceCustomer] FOREIGN KEY ([SourceCustomerId]) REFERENCES [dbo].[Customer] ([Id]),
        CONSTRAINT [FK_JobSupport_Relationship_TargetCustomer] FOREIGN KEY ([TargetCustomerId]) REFERENCES [dbo].[Customer] ([Id]),
        CONSTRAINT [FK_JobSupport_Relationship_SourceProfile] FOREIGN KEY ([SourceProfileId]) REFERENCES [dbo].[JobSupport_Profile] ([Id]),
        CONSTRAINT [FK_JobSupport_Relationship_TargetProfile] FOREIGN KEY ([TargetProfileId]) REFERENCES [dbo].[JobSupport_Profile] ([Id]),
        CONSTRAINT [CK_JobSupport_Relationship_DifferentCustomers] CHECK ([SourceCustomerId] <> [TargetCustomerId]),
        CONSTRAINT [CK_JobSupport_Relationship_DifferentProfiles] CHECK ([SourceProfileId] <> [TargetProfileId])
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_Relationship]') AND [name] = N'UX_JobSupport_Relationship_LogicalKey')
    CREATE UNIQUE INDEX [UX_JobSupport_Relationship_LogicalKey] ON [dbo].[JobSupport_Relationship] ([SourceCustomerId], [TargetCustomerId], [RelationshipType]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_Relationship]') AND [name] = N'UX_JobSupport_Relationship_LegacyShoppingCartItemId')
    CREATE UNIQUE INDEX [UX_JobSupport_Relationship_LegacyShoppingCartItemId] ON [dbo].[JobSupport_Relationship] ([LegacyShoppingCartItemId]) WHERE [LegacyShoppingCartItemId] IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_Relationship]') AND [name] = N'IX_JobSupport_Relationship_Source_Type_Status')
    CREATE INDEX [IX_JobSupport_Relationship_Source_Type_Status] ON [dbo].[JobSupport_Relationship] ([SourceCustomerId], [RelationshipType], [Status]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_Relationship]') AND [name] = N'IX_JobSupport_Relationship_Target_Type_Status')
    CREATE INDEX [IX_JobSupport_Relationship_Target_Type_Status] ON [dbo].[JobSupport_Relationship] ([TargetCustomerId], [RelationshipType], [Status]);

IF OBJECT_ID(N'[dbo].[JobSupport_ProfileView]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobSupport_ProfileView]
    (
        [Id] int IDENTITY(1,1) NOT NULL,
        [ViewerCustomerId] int NOT NULL,
        [ViewedCustomerId] int NOT NULL,
        [ViewerProfileId] int NOT NULL,
        [ViewedProfileId] int NOT NULL,
        [FirstViewedOnUtc] datetime2(6) NOT NULL,
        [LastViewedOnUtc] datetime2(6) NOT NULL,
        [ViewCount] int NOT NULL CONSTRAINT [DF_JobSupport_ProfileView_ViewCount] DEFAULT (1),
        [ContactRevealed] bit NOT NULL CONSTRAINT [DF_JobSupport_ProfileView_ContactRevealed] DEFAULT (0),
        [ContactRevealedOnUtc] datetime2(6) NULL,
        [CreatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_ProfileView_CreatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        [UpdatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_ProfileView_UpdatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_JobSupport_ProfileView] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_JobSupport_ProfileView_ViewerCustomer] FOREIGN KEY ([ViewerCustomerId]) REFERENCES [dbo].[Customer] ([Id]),
        CONSTRAINT [FK_JobSupport_ProfileView_ViewedCustomer] FOREIGN KEY ([ViewedCustomerId]) REFERENCES [dbo].[Customer] ([Id]),
        CONSTRAINT [FK_JobSupport_ProfileView_ViewerProfile] FOREIGN KEY ([ViewerProfileId]) REFERENCES [dbo].[JobSupport_Profile] ([Id]),
        CONSTRAINT [FK_JobSupport_ProfileView_ViewedProfile] FOREIGN KEY ([ViewedProfileId]) REFERENCES [dbo].[JobSupport_Profile] ([Id]),
        CONSTRAINT [CK_JobSupport_ProfileView_DifferentCustomers] CHECK ([ViewerCustomerId] <> [ViewedCustomerId]),
        CONSTRAINT [CK_JobSupport_ProfileView_ViewCount] CHECK ([ViewCount] > 0),
        CONSTRAINT [CK_JobSupport_ProfileView_ContactDate] CHECK ([ContactRevealed] = 1 OR [ContactRevealedOnUtc] IS NULL)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_ProfileView]') AND [name] = N'UX_JobSupport_ProfileView_Viewer_Viewed')
    CREATE UNIQUE INDEX [UX_JobSupport_ProfileView_Viewer_Viewed] ON [dbo].[JobSupport_ProfileView] ([ViewerProfileId], [ViewedProfileId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_ProfileView]') AND [name] = N'IX_JobSupport_ProfileView_ViewedCustomer_Profile')
    CREATE INDEX [IX_JobSupport_ProfileView_ViewedCustomer_Profile] ON [dbo].[JobSupport_ProfileView] ([ViewedCustomerId], [ViewedProfileId]);

IF OBJECT_ID(N'[dbo].[JobSupport_Subscription]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobSupport_Subscription]
    (
        [Id] int IDENTITY(1,1) NOT NULL,
        [CustomerId] int NOT NULL,
        [OrderId] int NOT NULL,
        [OrderItemId] int NULL,
        [SubscriptionProductId] int NOT NULL,
        [Status] int NOT NULL,
        [StartOnUtc] datetime2(6) NOT NULL,
        [EndOnUtc] datetime2(6) NOT NULL,
        [AllottedCredits] int NOT NULL,
        [CarriedForwardCredits] int NOT NULL CONSTRAINT [DF_JobSupport_Subscription_CarriedForwardCredits] DEFAULT (0),
        [UsedCredits] int NOT NULL CONSTRAINT [DF_JobSupport_Subscription_UsedCredits] DEFAULT (0),
        [CreatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_Subscription_CreatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        [UpdatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_Subscription_UpdatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        [LegacyRewardPointsHistoryId] int NULL,
        [MigrationSource] nvarchar(100) NULL,
        CONSTRAINT [PK_JobSupport_Subscription] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_JobSupport_Subscription_Customer] FOREIGN KEY ([CustomerId]) REFERENCES [dbo].[Customer] ([Id]),
        CONSTRAINT [FK_JobSupport_Subscription_Order] FOREIGN KEY ([OrderId]) REFERENCES [dbo].[Order] ([Id]),
        CONSTRAINT [FK_JobSupport_Subscription_OrderItem] FOREIGN KEY ([OrderItemId]) REFERENCES [dbo].[OrderItem] ([Id]),
        CONSTRAINT [FK_JobSupport_Subscription_Product] FOREIGN KEY ([SubscriptionProductId]) REFERENCES [dbo].[Product] ([Id]),
        CONSTRAINT [CK_JobSupport_Subscription_Dates] CHECK ([EndOnUtc] >= [StartOnUtc]),
        CONSTRAINT [CK_JobSupport_Subscription_Credits] CHECK ([AllottedCredits] >= 0 AND [CarriedForwardCredits] >= 0 AND [UsedCredits] >= 0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_Subscription]') AND [name] = N'UX_JobSupport_Subscription_OrderItemId')
    CREATE UNIQUE INDEX [UX_JobSupport_Subscription_OrderItemId] ON [dbo].[JobSupport_Subscription] ([OrderItemId]) WHERE [OrderItemId] IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_Subscription]') AND [name] = N'UX_JobSupport_Subscription_OrderWithoutItem')
    CREATE UNIQUE INDEX [UX_JobSupport_Subscription_OrderWithoutItem] ON [dbo].[JobSupport_Subscription] ([OrderId]) WHERE [OrderItemId] IS NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_Subscription]') AND [name] = N'IX_JobSupport_Subscription_Customer_Status_EndOnUtc')
    CREATE INDEX [IX_JobSupport_Subscription_Customer_Status_EndOnUtc] ON [dbo].[JobSupport_Subscription] ([CustomerId], [Status], [EndOnUtc]);

IF OBJECT_ID(N'[dbo].[JobSupport_ContactReveal]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobSupport_ContactReveal]
    (
        [Id] int IDENTITY(1,1) NOT NULL,
        [SubscriptionId] int NOT NULL,
        [ViewerCustomerId] int NOT NULL,
        [TargetCustomerId] int NOT NULL,
        [TargetProfileId] int NOT NULL,
        [CreditCost] int NOT NULL CONSTRAINT [DF_JobSupport_ContactReveal_CreditCost] DEFAULT (1),
        [RevealedOnUtc] datetime2(6) NOT NULL,
        [CreatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_ContactReveal_CreatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_JobSupport_ContactReveal] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_JobSupport_ContactReveal_Subscription] FOREIGN KEY ([SubscriptionId]) REFERENCES [dbo].[JobSupport_Subscription] ([Id]),
        CONSTRAINT [FK_JobSupport_ContactReveal_ViewerCustomer] FOREIGN KEY ([ViewerCustomerId]) REFERENCES [dbo].[Customer] ([Id]),
        CONSTRAINT [FK_JobSupport_ContactReveal_TargetCustomer] FOREIGN KEY ([TargetCustomerId]) REFERENCES [dbo].[Customer] ([Id]),
        CONSTRAINT [FK_JobSupport_ContactReveal_TargetProfile] FOREIGN KEY ([TargetProfileId]) REFERENCES [dbo].[JobSupport_Profile] ([Id]),
        CONSTRAINT [CK_JobSupport_ContactReveal_DifferentCustomers] CHECK ([ViewerCustomerId] <> [TargetCustomerId]),
        CONSTRAINT [CK_JobSupport_ContactReveal_CreditCost] CHECK ([CreditCost] > 0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_ContactReveal]') AND [name] = N'UX_JobSupport_ContactReveal_Viewer_TargetProfile')
    CREATE UNIQUE INDEX [UX_JobSupport_ContactReveal_Viewer_TargetProfile] ON [dbo].[JobSupport_ContactReveal] ([ViewerCustomerId], [TargetProfileId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_ContactReveal]') AND [name] = N'IX_JobSupport_ContactReveal_SubscriptionId')
    CREATE INDEX [IX_JobSupport_ContactReveal_SubscriptionId] ON [dbo].[JobSupport_ContactReveal] ([SubscriptionId]);

IF OBJECT_ID(N'[dbo].[JobSupport_ProfileAttributeDefinition]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobSupport_ProfileAttributeDefinition]
    (
        [Id] int IDENTITY(1,1) NOT NULL,
        [SystemName] nvarchar(400) NOT NULL,
        [Name] nvarchar(400) NOT NULL,
        [HelpText] nvarchar(max) NULL,
        [ControlType] int NOT NULL,
        [IsRequired] bit NOT NULL CONSTRAINT [DF_JobSupport_ProfileAttributeDefinition_IsRequired] DEFAULT (0),
        [ShowOnOnboarding] bit NOT NULL CONSTRAINT [DF_JobSupport_ProfileAttributeDefinition_ShowOnOnboarding] DEFAULT (0),
        [ShowOnProfileEdit] bit NOT NULL CONSTRAINT [DF_JobSupport_ProfileAttributeDefinition_ShowOnProfileEdit] DEFAULT (0),
        [ShowOnPublicProfile] bit NOT NULL CONSTRAINT [DF_JobSupport_ProfileAttributeDefinition_ShowOnPublicProfile] DEFAULT (0),
        [DisplayOrder] int NOT NULL CONSTRAINT [DF_JobSupport_ProfileAttributeDefinition_DisplayOrder] DEFAULT (0),
        [IsActive] bit NOT NULL CONSTRAINT [DF_JobSupport_ProfileAttributeDefinition_IsActive] DEFAULT (1),
        [CreatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_ProfileAttributeDefinition_CreatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        [UpdatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_ProfileAttributeDefinition_UpdatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_JobSupport_ProfileAttributeDefinition] PRIMARY KEY CLUSTERED ([Id])
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_ProfileAttributeDefinition]') AND [name] = N'UX_JobSupport_ProfileAttributeDefinition_SystemName')
    CREATE UNIQUE INDEX [UX_JobSupport_ProfileAttributeDefinition_SystemName] ON [dbo].[JobSupport_ProfileAttributeDefinition] ([SystemName]);

IF OBJECT_ID(N'[dbo].[JobSupport_ProfileAttributeOption]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobSupport_ProfileAttributeOption]
    (
        [Id] int IDENTITY(1,1) NOT NULL,
        [AttributeDefinitionId] int NOT NULL,
        [Name] nvarchar(400) NOT NULL,
        [DisplayOrder] int NOT NULL CONSTRAINT [DF_JobSupport_ProfileAttributeOption_DisplayOrder] DEFAULT (0),
        [IsActive] bit NOT NULL CONSTRAINT [DF_JobSupport_ProfileAttributeOption_IsActive] DEFAULT (1),
        [LegacyOptionId] int NULL,
        [CreatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_ProfileAttributeOption_CreatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        [UpdatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_ProfileAttributeOption_UpdatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_JobSupport_ProfileAttributeOption] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_JobSupport_ProfileAttributeOption_Definition] FOREIGN KEY ([AttributeDefinitionId]) REFERENCES [dbo].[JobSupport_ProfileAttributeDefinition] ([Id])
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_ProfileAttributeOption]') AND [name] = N'UX_JobSupport_ProfileAttributeOption_Definition_Name')
    CREATE UNIQUE INDEX [UX_JobSupport_ProfileAttributeOption_Definition_Name] ON [dbo].[JobSupport_ProfileAttributeOption] ([AttributeDefinitionId], [Name]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_ProfileAttributeOption]') AND [name] = N'UX_JobSupport_ProfileAttributeOption_LegacyOptionId')
    CREATE UNIQUE INDEX [UX_JobSupport_ProfileAttributeOption_LegacyOptionId] ON [dbo].[JobSupport_ProfileAttributeOption] ([LegacyOptionId]) WHERE [LegacyOptionId] IS NOT NULL;

IF OBJECT_ID(N'[dbo].[JobSupport_ProfileAttributeValue]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobSupport_ProfileAttributeValue]
    (
        [Id] int IDENTITY(1,1) NOT NULL,
        [ProfileId] int NOT NULL,
        [AttributeDefinitionId] int NOT NULL,
        [AttributeOptionId] int NULL,
        [TextValue] nvarchar(max) NULL,
        [DisplayOrder] int NOT NULL CONSTRAINT [DF_JobSupport_ProfileAttributeValue_DisplayOrder] DEFAULT (0),
        [CreatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_ProfileAttributeValue_CreatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        [UpdatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_ProfileAttributeValue_UpdatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_JobSupport_ProfileAttributeValue] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_JobSupport_ProfileAttributeValue_Profile] FOREIGN KEY ([ProfileId]) REFERENCES [dbo].[JobSupport_Profile] ([Id]),
        CONSTRAINT [FK_JobSupport_ProfileAttributeValue_Definition] FOREIGN KEY ([AttributeDefinitionId]) REFERENCES [dbo].[JobSupport_ProfileAttributeDefinition] ([Id]),
        CONSTRAINT [FK_JobSupport_ProfileAttributeValue_Option] FOREIGN KEY ([AttributeOptionId]) REFERENCES [dbo].[JobSupport_ProfileAttributeOption] ([Id]),
        CONSTRAINT [CK_JobSupport_ProfileAttributeValue_Value] CHECK ([AttributeOptionId] IS NOT NULL OR NULLIF(LTRIM(RTRIM([TextValue])), N'') IS NOT NULL)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_ProfileAttributeValue]') AND [name] = N'UX_JobSupport_ProfileAttributeValue_Option')
    CREATE UNIQUE INDEX [UX_JobSupport_ProfileAttributeValue_Option] ON [dbo].[JobSupport_ProfileAttributeValue] ([ProfileId], [AttributeDefinitionId], [AttributeOptionId]) WHERE [AttributeOptionId] IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_ProfileAttributeValue]') AND [name] = N'UX_JobSupport_ProfileAttributeValue_Text')
    CREATE UNIQUE INDEX [UX_JobSupport_ProfileAttributeValue_Text] ON [dbo].[JobSupport_ProfileAttributeValue] ([ProfileId], [AttributeDefinitionId]) WHERE [AttributeOptionId] IS NULL;

IF OBJECT_ID(N'[dbo].[JobSupport_MessageMetadata]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobSupport_MessageMetadata]
    (
        [Id] int IDENTITY(1,1) NOT NULL,
        [PrivateMessageId] int NOT NULL,
        [RelationshipId] int NULL,
        [SenderSubject] nvarchar(400) NULL,
        [SenderBodyText] nvarchar(max) NULL,
        [RecipientBodyText] nvarchar(max) NULL,
        [IsSystemGenerated] bit NOT NULL CONSTRAINT [DF_JobSupport_MessageMetadata_IsSystemGenerated] DEFAULT (0),
        [ParentMessageId] int NULL,
        [CreatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_MessageMetadata_CreatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_JobSupport_MessageMetadata] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_JobSupport_MessageMetadata_PrivateMessage] FOREIGN KEY ([PrivateMessageId]) REFERENCES [dbo].[PrivateMessage] ([Id]),
        CONSTRAINT [FK_JobSupport_MessageMetadata_Relationship] FOREIGN KEY ([RelationshipId]) REFERENCES [dbo].[JobSupport_Relationship] ([Id]),
        CONSTRAINT [FK_JobSupport_MessageMetadata_ParentMessage] FOREIGN KEY ([ParentMessageId]) REFERENCES [dbo].[PrivateMessage] ([Id]),
        CONSTRAINT [CK_JobSupport_MessageMetadata_Parent] CHECK ([ParentMessageId] IS NULL OR [ParentMessageId] <> [PrivateMessageId])
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_MessageMetadata]') AND [name] = N'UX_JobSupport_MessageMetadata_PrivateMessageId')
    CREATE UNIQUE INDEX [UX_JobSupport_MessageMetadata_PrivateMessageId] ON [dbo].[JobSupport_MessageMetadata] ([PrivateMessageId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_MessageMetadata]') AND [name] = N'IX_JobSupport_MessageMetadata_RelationshipId')
    CREATE INDEX [IX_JobSupport_MessageMetadata_RelationshipId] ON [dbo].[JobSupport_MessageMetadata] ([RelationshipId]) WHERE [RelationshipId] IS NOT NULL;

IF OBJECT_ID(N'[dbo].[JobSupport_MigrationCheckpoint]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[JobSupport_MigrationCheckpoint]
    (
        [Id] int IDENTITY(1,1) NOT NULL,
        [MigrationName] nvarchar(400) NOT NULL,
        [LastProcessedId] int NULL,
        [RowsProcessed] bigint NOT NULL CONSTRAINT [DF_JobSupport_MigrationCheckpoint_RowsProcessed] DEFAULT (0),
        [RowsSkipped] bigint NOT NULL CONSTRAINT [DF_JobSupport_MigrationCheckpoint_RowsSkipped] DEFAULT (0),
        [RowsFailed] bigint NOT NULL CONSTRAINT [DF_JobSupport_MigrationCheckpoint_RowsFailed] DEFAULT (0),
        [StartedOnUtc] datetime2(6) NOT NULL,
        [CompletedOnUtc] datetime2(6) NULL,
        [Status] int NOT NULL,
        [ErrorSummary] nvarchar(2000) NULL,
        [UpdatedOnUtc] datetime2(6) NOT NULL CONSTRAINT [DF_JobSupport_MigrationCheckpoint_UpdatedOnUtc] DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_JobSupport_MigrationCheckpoint] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [CK_JobSupport_MigrationCheckpoint_Counts] CHECK ([RowsProcessed] >= 0 AND [RowsSkipped] >= 0 AND [RowsFailed] >= 0)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[JobSupport_MigrationCheckpoint]') AND [name] = N'UX_JobSupport_MigrationCheckpoint_MigrationName')
    CREATE UNIQUE INDEX [UX_JobSupport_MigrationCheckpoint_MigrationName] ON [dbo].[JobSupport_MigrationCheckpoint] ([MigrationName]);

COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;

SELECT
    [TableName] = [name],
    [Created] = CAST(1 AS bit)
FROM sys.tables
WHERE [name] LIKE N'JobSupport[_]%'
ORDER BY [name];