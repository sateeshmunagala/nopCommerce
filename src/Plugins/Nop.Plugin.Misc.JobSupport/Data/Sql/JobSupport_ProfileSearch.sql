CREATE OR ALTER PROCEDURE [dbo].[JobSupport_ProfileSearch]
(
  @CustomerId int,
  @StoreId int,
  @ProfileType int = null,
  @PrimarySkillIds nvarchar(max) = null,
  @SecondarySkillIds nvarchar(max) = null,
  @Availability nvarchar(400) = null,
  @Keywords nvarchar(400) = null,
  @ExcludeOwnProfile bit = 1,
  @OrderBy int = 0,
  @PageIndex int = 0,
  @PageSize int = 12,
  @TotalRecords int = null OUTPUT
)
AS
BEGIN
  SET NOCOUNT ON;

  SET @PageIndex = CASE WHEN @PageIndex < 0 THEN 0 ELSE @PageIndex END;
  SET @PageSize = CASE WHEN @PageSize < 1 THEN 12 ELSE @PageSize END;

  ;WITH PrimarySkills AS
  (
    SELECT
      skill.ProfileId,
      STRING_AGG(CONVERT(nvarchar(max), skill.Name), N',')
        WITHIN GROUP (ORDER BY skill.DisplayOrder, skill.Name) AS PrimaryTechnology
    FROM [dbo].[JobSupportProfileSkill] skill
    WHERE skill.SkillType = 1
    GROUP BY skill.ProfileId
  ),
  SecondarySkills AS
  (
    SELECT
      skill.ProfileId,
      STRING_AGG(CONVERT(nvarchar(max), skill.Name), N',')
        WITHIN GROUP (ORDER BY skill.DisplayOrder, skill.Name) AS SecondaryTechnology
    FROM [dbo].[JobSupportProfileSkill] skill
    WHERE skill.SkillType = 2
    GROUP BY skill.ProfileId
  )
  SELECT
    profile.Id AS ProfileId,
    profile.CustomerId,
    profile.LegacyProductId,
    profile.DisplayName,
    profile.ProfileType,
    profile.ShortDescription,
    profile.CurrentAvailability,
    profile.MotherTongue,
    profile.RelevantExperience,
    profile.AvatarPictureId,
    profile.CountryId,
    profile.StateProvinceId,
    profile.City,
    primarySkills.PrimaryTechnology,
    secondarySkills.SecondaryTechnology,
    profile.Slug,
    CONVERT(datetime2, NULL) AS LastLoginDateUtc,
    CONVERT(datetime2, NULL) AS LastActivityDateUtc,
    CONVERT(bit, CASE WHEN relationshipFlags.ProfileShortlisted = 1 THEN 1 ELSE 0 END) AS Requested,
    CONVERT(bit, CASE WHEN relationshipFlags.Connected = 1 THEN 1 ELSE 0 END) AS Connected,
    relationshipFlags.InterestStatus,
    CONVERT(bit, CASE WHEN premium.SubscriptionId IS NULL THEN 0 ELSE 1 END) AS PremiumCustomer,
    profile.CreatedOnUtc,
    profile.UpdatedOnUtc AS SortUpdatedOnUtc
  INTO #FilteredProfiles
  FROM [dbo].[JobSupportProfile] profile
  LEFT JOIN PrimarySkills primarySkills
    ON primarySkills.ProfileId = profile.Id
  LEFT JOIN SecondarySkills secondarySkills
    ON secondarySkills.ProfileId = profile.Id
  OUTER APPLY
  (
    SELECT
      MAX(CASE
        WHEN relationship.RelationshipTypeId = 1
          AND relationship.StatusId = 1
          AND relationship.SourceCustomerId = @CustomerId
        THEN 1 ELSE 0 END) AS ProfileShortlisted,
      MAX(CASE
        WHEN relationship.RelationshipTypeId = 2
          AND relationship.StatusId = 4
        THEN 1 ELSE 0 END) AS Connected,
      MAX(CASE
        WHEN relationship.RelationshipTypeId = 2
          AND relationship.SourceCustomerId = @CustomerId
        THEN relationship.StatusId END) AS InterestStatus
    FROM [dbo].[JobSupportRelationship] relationship
    WHERE
      (relationship.SourceCustomerId = @CustomerId
        AND relationship.TargetCustomerId = profile.CustomerId)
      OR
      (relationship.TargetCustomerId = @CustomerId
        AND relationship.SourceCustomerId = profile.CustomerId)
  ) relationshipFlags
  OUTER APPLY
  (
    SELECT TOP (1)
      subscription.Id AS SubscriptionId
    FROM [dbo].[JobSupportSubscription] subscription
    WHERE subscription.CustomerId = profile.CustomerId
      AND subscription.Status = 1
      AND subscription.StartOnUtc <= SYSUTCDATETIME()
      AND subscription.EndOnUtc > SYSUTCDATETIME()
    ORDER BY subscription.EndOnUtc DESC, subscription.Id DESC
  ) premium
  WHERE profile.IsPublished = 1
    AND (@ProfileType IS NULL OR profile.ProfileType = @ProfileType)
    AND (@ExcludeOwnProfile = 0 OR profile.CustomerId <> @CustomerId)
    AND (@Availability IS NULL OR profile.CurrentAvailability = @Availability)
    AND
    (
      @PrimarySkillIds IS NULL
      OR EXISTS
      (
        SELECT 1
        FROM [dbo].[JobSupportProfileSkill] primaryFilter
        INNER JOIN STRING_SPLIT(@PrimarySkillIds, N',') requestedPrimary
          ON primaryFilter.LegacySpecificationAttributeOptionId = TRY_CONVERT(int, requestedPrimary.[value])
        WHERE primaryFilter.ProfileId = profile.Id
          AND primaryFilter.SkillType = 1
      )
    )
    AND
    (
      @SecondarySkillIds IS NULL
      OR EXISTS
      (
        SELECT 1
        FROM [dbo].[JobSupportProfileSkill] secondaryFilter
        INNER JOIN STRING_SPLIT(@SecondarySkillIds, N',') requestedSecondary
          ON secondaryFilter.LegacySpecificationAttributeOptionId = TRY_CONVERT(int, requestedSecondary.[value])
        WHERE secondaryFilter.ProfileId = profile.Id
          AND secondaryFilter.SkillType = 2
      )
    )
    AND
    (
      @Keywords IS NULL
      OR profile.DisplayName LIKE N'%' + @Keywords + N'%'
      OR profile.ShortDescription LIKE N'%' + @Keywords + N'%'
      OR primarySkills.PrimaryTechnology LIKE N'%' + @Keywords + N'%'
      OR secondarySkills.SecondaryTechnology LIKE N'%' + @Keywords + N'%'
    );

  SELECT @TotalRecords = COUNT(*)
  FROM #FilteredProfiles;

  SELECT
    ProfileId,
    CustomerId,
    LegacyProductId,
    DisplayName,
    ProfileType,
    ShortDescription,
    CurrentAvailability,
    MotherTongue,
    RelevantExperience,
    AvatarPictureId,
    CountryId,
    StateProvinceId,
    City,
    PrimaryTechnology,
    SecondaryTechnology,
    Slug,
    LastLoginDateUtc,
    LastActivityDateUtc,
    Requested,
    Connected,
    InterestStatus,
    PremiumCustomer,
    CreatedOnUtc
  FROM #FilteredProfiles
  ORDER BY
    CASE WHEN @OrderBy = 5 THEN DisplayName END ASC,
    CASE WHEN @OrderBy = 6 THEN DisplayName END DESC,
    CASE WHEN @OrderBy = 10 THEN CreatedOnUtc END ASC,
    CASE WHEN @OrderBy IN (11, 15) THEN CreatedOnUtc END DESC,
    CASE WHEN @OrderBy NOT IN (5, 6, 10, 11, 15) THEN SortUpdatedOnUtc END DESC,
    ProfileId ASC
  OFFSET @PageIndex * @PageSize ROWS
  FETCH NEXT @PageSize ROWS ONLY;

  DROP TABLE #FilteredProfiles;
END;
