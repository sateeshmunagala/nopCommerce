--USE [onjobsupport]
GO
/****** Object:  StoredProcedure [dbo].[ProductShortList]    Script Date: 11-08-2022 11.56.37 PM ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

------------ *************** --------------------------------------------
-- @ProductIds -- no need to pass this parameter . can delete it
-- 
-- 
-- 
-- 

------------ *************** --------------------------------------------

-- I short Listed profiles
-- exec [ProductShortList] NULL,2,1,0,0,1

-- product id : 133 for customer id:61
-- The below should give who short listed me (product id)
-- exec [ProductShortList] 14,3,0,0,0,100

ALTER PROCEDURE [dbo].[ProductShortList]
(
	@ProductIds			nvarchar(MAX) = null ,--a list of product IDs (comma-separated list). e.g. 1,2,3 -- 
	@ShoppingCartTypeId	int = 0, -- shortlist = 2, ShortListedMe =3, InterestSent = 4, InterestReceived = 5,
	@CustomerId			int = 0,
	@OrderBy			int = 0, --0 - position, 5 - Name: A to Z, 6 - Name: Z to A, 10 - Price: Low to High, 11 - Price: High to Low, 15 - creation date
	@PageIndex			int = 0, 
	@PageSize			int = 2147483644,
	@TotalRecords		int = null OUTPUT
)
AS
BEGIN	
	
	DECLARE
		@sql nvarchar(max),
		@sql_orderby nvarchar(max)

	SET NOCOUNT ON
	
	--filter by Product IDs
	SET @ProductIds = isnull(@ProductIds, '')	
	CREATE TABLE #FilteredProductIds
	(
		ProductId int not null
	)
	INSERT INTO #FilteredProductIds (ProductId)
	SELECT CAST(data as int) FROM [nop_splitstring_to_table](@ProductIds, ',')	

	DECLARE @ProductIdsCount int	
	SET @ProductIdsCount = (SELECT COUNT(1) FROM #FilteredProductIds)

	CREATE TABLE #Products 
	(
		--[Id] int IDENTITY (1, 1) NOT NULL,
		[ProductId] int NOT NULL
	)

	SET @sql = '
	SELECT p.Id
	FROM
		Product p with (NOLOCK)'

	-- filter products by customerid
	IF @CustomerId > 0
	BEGIN
		SET @sql = @sql + '
		INNER JOIN ShoppingCartItem sci with (NOLOCK)
				ON p.Id = sci.ProductId
		WHERE CustomerId='+  CAST(@CustomerId AS nvarchar(max))
	END

	-- filter products by shopping cart typeid
	IF @ShoppingCartTypeId > 0
	BEGIN
		SET @sql = @sql + '
		AND ShoppingCartTypeId='+ CAST(@ShoppingCartTypeId AS nvarchar(max))
	END
	
	-- get published products
	SET @sql = @sql + '
		AND p.Deleted = 0'

   -- I short Listed profiles
   -- exec [ProductShortList] NULL, 2, 1 ,0,0,100

	--filter by Product ids
	IF @ProductIdsCount > 0
	BEGIN
		SET @sql = @sql + '
		AND p.Id IN ('
		SET @sql = @sql + + CAST(@ProductIds AS nvarchar(max))
		SET @sql = @sql + ')'
	END
	
	
    SET @sql = '
    INSERT INTO #Products ([ProductId])' + @sql

	 PRINT (@sql)
	EXEC sp_executesql @sql
	
-- SELECT '#Products',* FROM #Products;

	DROP TABLE #FilteredProductIds

	CREATE TABLE #ProductSpecs 
	(
		[ProductId] int NOT NULL,
		[PrimaryTechnology] varchar(1000) NULL,
		[SecondaryTechnology] varchar(1000) NULL,
		[CurrentAvalibility] varchar(1000) NULL,
        [ProfileType] varchar(100) NULL,
		[MotherTongue] varchar(100) NULL,
		[WorkExperience] varchar(100) NULL
	)

	SELECT 
		p.ProductId as Id,
		sa.Name,
		Technology = STRING_AGG(sao.Name,' , ')
	INTO #ProductSpecsTemp
		FROM [dbo].[Product_SpecificationAttribute_Mapping] PS
		JOIN #Products p on p.ProductId=PS.ProductId
		--JOIN [Product] p on p.Id=PS.ProductId
		JOIN [SpecificationAttributeOption] sao on sao.id=ps.SpecificationAttributeOptionId
		JOIN [SpecificationAttribute] sa on sa.id=sao.SpecificationAttributeId
		GROUP BY p.ProductId,sa.name

--	SELECT '##ProductSpecsTemp',* FROM #ProductSpecsTemp

	-- I short Listed profiles
	-- exec [ProductShortList] NULL,2,1,0,0,10

 INSERT INTO #ProductSpecs ([ProductId],[PrimaryTechnology],[SecondaryTechnology],[CurrentAvalibility],[ProfileType],[MotherTongue],[WorkExperience])
		SELECT 
		p.Id as ProductId,
		(SELECT STRING_AGG(sao.Name, ',') AS 'Primary Technology'
                FROM [Product_SpecificationAttribute_Mapping] PS
				JOIN [SpecificationAttributeOption] sao on sao.id=ps.SpecificationAttributeOptionId
				JOIN [SpecificationAttribute] sa on sa.id=sao.SpecificationAttributeId
				WHERE sa.Id=7 and ps.ProductId=p.Id) AS 'PrimaryTechnology',
		(SELECT STRING_AGG(sao.Name, ',') AS 'Secondary Technology'
                FROM [Product_SpecificationAttribute_Mapping] PS
				JOIN [SpecificationAttributeOption] sao on sao.id=ps.SpecificationAttributeOptionId
				JOIN [SpecificationAttribute] sa on sa.id=sao.SpecificationAttributeId
				WHERE sa.Id=8 and ps.ProductId=p.Id) AS 'SecondaryTechnology',
		(SELECT STRING_AGG(sao.Name, ',') AS 'Current Avalibility'
                FROM [Product_SpecificationAttribute_Mapping] PS
				JOIN [SpecificationAttributeOption] sao on sao.id=ps.SpecificationAttributeOptionId
				JOIN [SpecificationAttribute] sa on sa.id=sao.SpecificationAttributeId
				WHERE sa.Id=2 and ps.ProductId=p.Id) AS 'CurrentAvalibility',
		(SELECT STRING_AGG(sao.Name, ',') AS 'ProfileType'
                FROM [Product_SpecificationAttribute_Mapping] PS
				JOIN [SpecificationAttributeOption] sao on sao.id=ps.SpecificationAttributeOptionId
				JOIN [SpecificationAttribute] sa on sa.id=sao.SpecificationAttributeId
				WHERE sa.Id=1 and ps.ProductId=p.Id) AS 'ProfileType',
		(SELECT STRING_AGG(sao.Name, ',') AS 'Mother Tongue'
                FROM [Product_SpecificationAttribute_Mapping] PS
				JOIN [SpecificationAttributeOption] sao on sao.id=ps.SpecificationAttributeOptionId
				JOIN [SpecificationAttribute] sa on sa.id=sao.SpecificationAttributeId
				WHERE sa.Id=4 and ps.ProductId=p.Id) AS 'MotherTongue',
		(SELECT STRING_AGG(sao.Name, ',') AS 'Relavent Experiance'
                FROM [Product_SpecificationAttribute_Mapping] PS
				JOIN [SpecificationAttributeOption] sao on sao.id=ps.SpecificationAttributeOptionId
				JOIN [SpecificationAttribute] sa on sa.id=sao.SpecificationAttributeId
				WHERE sa.Id=3 and ps.ProductId=p.Id) AS 'WorkExperience'
		FROM [Product] p
		order by P.Id asc

	-- SELECT '#ProductSpecs',* FROM #ProductSpecs

	SELECT 
		 C.FirstName,
		 C.LastName,
		 c.Phone,
		 c.Gender,
		 c.Company,
		 c.CountryId,
		 c.StateProvinceId,
		 c.LanguageId,
		 c.TimeZoneId,
		 (SELECT [value] from [GenericAttribute]
			WHERE KeyGroup='Customer' AND [Key]='AvatarPictureId' AND EntityId=c.Id) As 'AvatarPictureId',
		 c.CustomerProfileTypeId,
		 c.City,
		 C.LastLoginDateUtc,
		 C.LastActivityDateUtc,
		 P.* 
	INTO #CustomerTemp
		FROM #Products [pi]
		INNER JOIN Product p on p.Id = [pi].[ProductId]
		LEFT JOIN Customer C on P.Id = [C].VendorId 
   
   --  SELECT '#CustomerTemp',* FROM #CustomerTemp
	 -- exec [ProductShortList_v1] null,2,61,0,0,100


     -- create CustomerOrderTemp table
	SELECT  
		Distinct(C.VendorId) AS ProductId,
		CAST(CASE WHEN OI.ProductId=1 AND O.PaidDateUtc >= GETUTCDATE()-90  THEN 1	 -- 1 Month Subscription
				  WHEN OI.ProductId=2 AND O.PaidDateUtc >= GETUTCDATE()-180 THEN 1	 -- 6 Month Subscription
				  WHEN OI.ProductId=3 AND O.PaidDateUtc >= GETUTCDATE()-365 THEN 1	 -- 1 Year subscription
				  ELSE 0 END AS BIT) AS PremiumCustomer
	INTO #CustomerOrderTemp
		FROM [Order] O 
		INNER JOIN [OrderItem] OI ON OI.OrderId=O.Id -- AND O.OrderStatusId=30 
		INNER JOIN [Product] P ON P.Id=OI.ProductId
		INNER JOIN [Customer] C ON C.Id=O.CustomerId
		WHERE O.OrderStatusId=30 -- Order Paid



	SELECT 
		p.*,
		ps.*,
		C.Name AS Country,
		sp.Name AS StateProvince,
		la.Name AS Language,
		url.Slug AS Slug,
		CAST(CASE WHEN sci.Id IS NULL THEN 0 ELSE 1 END AS BIT) AS ProfileShortListed, 
		CAST(CASE WHEN sc.Id IS NULL THEN 0 ELSE 1 END AS BIT) AS InterestSent,
		CAST(CASE WHEN co.PremiumCustomer=1 THEN 1 ELSE 0 END AS BIT) AS PremiumCustomer
		--(Select VendorId FROM Product WHERE Id=p.Id) AS VendorId
	  INTO #FinalProductsTemp
	FROM
		#CustomerTemp P
	LEFT JOIN #ProductSpecs ps on p.Id=ps.ProductId
	LEFT JOIN [Country] C on C.Id=p.CountryId
	LEFT JOIN [StateProvince] sp on sp.Id=p.StateProvinceId
	LEFT JOIN [Language] la on la.Id=p.LanguageId
	LEFT JOIN [UrlRecord] url on url.EntityId=p.Id AND url.EntityName='Product'
	LEFT JOIN [ShoppingCartItem] sci on sci.ProductId=p.Id
									AND sci.ShoppingCartTypeId=2 -- Is Profile ShortListed?
								    AND sci.CustomerId =@CustomerId
	LEFT JOIN [ShoppingCartItem] sc on sc.ProductId=p.Id  -- Is Interest Sent?
									AND sc.ShoppingCartTypeId=4
								    AND sc.CustomerId =@CustomerId
    LEFT JOIN #CustomerOrderTemp co	on co.ProductId	= ps.ProductId

   -- WHERE CAST(P.CustomerProfileTypeId as INT)= @WarehouseId;
   -- WHERE sc.CustomerId=@CustomerId

   -- exec [ProductShortList] NULL,2,1,0,0,1

	-- I short Listed profiles
	-- exec [ProductShortList_v2] null,2,61,0,0,100

	-- SELECT * FROM #FinalProductsTemp

	--return products
	SELECT
		p.*
	FROM
		#FinalProductsTemp p with (NOLOCK)

	DROP TABLE #ProductSpecs
	DROP TABLE #ProductSpecsTemp
	DROP TABLE #FinalProductsTemp
END


