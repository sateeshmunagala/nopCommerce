CREATE OR ALTER PROCEDURE [dbo].[JobSupport_ProfileRelationships]
(
  @CustomerId int,
  @Direction int,
  @RelationshipType int = null,
  @RelationshipStatus int = null,
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
  ),
  DirectedRelationships AS
  (
    SELECT
      relationship.Id AS RelationshipId,
      relationship.RelationshipTypeId,
      relationship.StatusId,
      relationship.CreatedOnUtc AS RelationshipCreatedOnUtc,
      relationship.UpdatedOnUtc,
      CASE
        WHEN @Direction = 1 THEN relationship.TargetProfileId
        WHEN @Direction = 2 THEN relationship.SourceProfileId
      END AS ProfileId
    FROM [dbo].[JobSupportRelationship] relationship
    WHERE @Direction IN (1, 2)
      AND
      (
        (@Direction = 1 AND relationship.SourceCustomerId = @CustomerId)
        OR (@Direction = 2 AND relationship.TargetCustomerId = @CustomerId)
      )
      AND
      (
        relationship.SourceCustomerId = @CustomerId
        OR relationship.TargetCustomerId = @CustomerId
      )
      AND (@RelationshipType IS NULL OR relationship.RelationshipTypeId = @RelationshipType)
      AND (@RelationshipStatus IS NULL OR relationship.StatusId = @RelationshipStatus)
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
    directed.RelationshipId,
    directed.RelationshipTypeId,
    directed.StatusId,
    directed.RelationshipCreatedOnUtc,
    directed.UpdatedOnUtc
  INTO #FilteredRelationships
  FROM DirectedRelationships directed
  INNER JOIN [dbo].[JobSupportProfile] profile
    ON profile.Id = directed.ProfileId
    AND profile.IsPublished = 1
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
  ) premium;

  SELECT @TotalRecords = COUNT(*)
  FROM #FilteredRelationships;

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
    CreatedOnUtc,
    RelationshipId,
    RelationshipTypeId,
    StatusId,
    RelationshipCreatedOnUtc,
    UpdatedOnUtc
  FROM #FilteredRelationships
  ORDER BY
    CASE WHEN @OrderBy = 5 THEN DisplayName END ASC,
    CASE WHEN @OrderBy = 6 THEN DisplayName END DESC,
    CASE WHEN @OrderBy = 10 THEN RelationshipCreatedOnUtc END ASC,
    CASE WHEN @OrderBy IN (11, 15) THEN RelationshipCreatedOnUtc END DESC,
    CASE WHEN @OrderBy NOT IN (5, 6, 10, 11, 15) THEN UpdatedOnUtc END DESC,
    ProfileId ASC
  OFFSET @PageIndex * @PageSize ROWS
  FETCH NEXT @PageSize ROWS ONLY;

  DROP TABLE #FilteredRelationships;
END;
