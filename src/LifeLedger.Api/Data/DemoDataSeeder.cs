using LifeLedger.Api.Domain;
using LifeLedger.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Data;

/// <summary>Creates and restores a deterministic, local-only showcase dataset covering LifeLedger's main financial cases.</summary>
public static class DemoDataSeeder
{
    /// <summary>Stable identifier of the demo profile, used by regression tests and screenshot automation.</summary>
    public static readonly Guid ProfileId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    /// <summary>Stable identifier of the reference demo scenario.</summary>
    public static readonly Guid BaselineScenarioId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    /// <summary>Version of the deterministic fixture; increment when the intended demo contract changes.</summary>
    public const int DatasetVersion = 1;

    /// <summary>Local marker written after a user chooses to remove the automatically seeded data permanently.</summary>
    private const string DisableMarkerFileName = "demo-data-disabled.local";
    /// <summary>Fixed projection start keeps charts, ages and screenshot baselines identical across restorations.</summary>
    private static readonly DateOnly ReferenceDate = new(2026, 1, 1);
    /// <summary>Fixed UTC timestamp keeps history ordering deterministic.</summary>
    private static readonly DateTimeOffset ReferenceTimestamp = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Adds the showcase only to a new, empty installation unless automatic demo data was disabled.</summary>
    public static async Task SeedAsync(LifeLedgerDbContext db, string dataDirectory, CancellationToken cancellationToken = default)
    {
        if (File.Exists(Path.Combine(dataDirectory, DisableMarkerFileName))) return;
        if (await db.Profiles.AnyAsync(cancellationToken)) return;

        await AddDatasetAsync(db, cancellationToken);
    }

    /// <summary>Replaces every user-owned financial record with the canonical demo dataset inside one transaction.</summary>
    public static async Task RestoreAsync(LifeLedgerDbContext db, string dataDirectory, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await DeleteFinancialDataAsync(db, cancellationToken);
        db.ChangeTracker.Clear();
        await AddDatasetAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Restoring the demo explicitly re-enables first-start seeding if the user later empties the database manually.
        var markerPath = Path.Combine(dataDirectory, DisableMarkerFileName);
        if (File.Exists(markerPath)) File.Delete(markerPath);
    }

    /// <summary>Persists the user's choice to keep a local installation empty after deleting all data.</summary>
    public static async Task DisableFutureSeedingAsync(string dataDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, DisableMarkerFileName),
            "Automatic demo data was disabled by the local user.",
            cancellationToken);
    }

    /// <summary>Deletes data owned by profiles while preserving exchange rates and the business-data version marker.</summary>
    private static async Task DeleteFinancialDataAsync(LifeLedgerDbContext db, CancellationToken cancellationToken)
    {
        // Children are removed first for providers or deployments using stricter foreign-key behaviour.
        await db.BankTransactions.ExecuteDeleteAsync(cancellationToken);
        await db.BankStatementImports.ExecuteDeleteAsync(cancellationToken);
        await db.BankAccounts.ExecuteDeleteAsync(cancellationToken);
        await db.AssetQuoteSnapshots.ExecuteDeleteAsync(cancellationToken);
        await db.AssetValuationSnapshots.ExecuteDeleteAsync(cancellationToken);
        await db.NetWorthSnapshots.ExecuteDeleteAsync(cancellationToken);
        await db.Scenarios.ExecuteDeleteAsync(cancellationToken);
        await db.Profiles.ExecuteDeleteAsync(cancellationToken);
        await db.ApplicationSettings
            .Where(setting => setting.Key == AssetCategoryService.SettingKey || setting.Key == AssetProfileCatalog.SettingKey || setting.Key.StartsWith("flex:"))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Builds and persists one complete showcase plus a smaller alternative scenario for comparison.</summary>
    private static async Task AddDatasetAsync(LifeLedgerDbContext db, CancellationToken cancellationToken)
    {
        var profile = BuildProfile();
        var baseline = BuildBaseline(profile);
        var alternative = BuildAlternative(profile);
        db.Scenarios.AddRange(baseline, alternative);
        db.AssetQuoteSnapshots.AddRange(BuildQuoteHistory(baseline));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Creates a multilingual, multi-country household with stable historical net-worth points.</summary>
    private static Profile BuildProfile() => new()
    {
        Id = ProfileId,
        DisplayName = "Famille Martin · Démo",
        BirthDate = new DateOnly(1986, 7, 17),
        Sex = ProfileSex.Neutral,
        HomeCountryCode = "BE",
        BaseCurrency = "EUR",
        ExpectedLifespan = 92,
        PartnerBirthYear = 1988,
        ChildrenCount = 2,
        CreatedAt = ReferenceTimestamp,
        Careers =
        [
            new CareerPeriod { Id = G(101), CountryCode = "FR", StartedOn = new DateOnly(2008, 9, 1), EndedOn = new DateOnly(2013, 12, 31), AnnualInsurableIncome = 38_000m, EstimatedMonthlyPublicPension = 280m, PensionAge = 64, Notes = "Premier emploi en France" },
            new CareerPeriod { Id = G(102), CountryCode = "PL", StartedOn = new DateOnly(2014, 1, 1), EndedOn = new DateOnly(2020, 6, 30), AnnualInsurableIncome = 180_000m, EstimatedMonthlyPublicPension = 1_900m, PensionAge = 65, Notes = "Carrière en Pologne, montants en PLN" },
            new CareerPeriod { Id = G(103), CountryCode = "BE", StartedOn = new DateOnly(2020, 7, 1), AnnualInsurableIncome = 84_000m, EstimatedMonthlyPublicPension = 720m, PensionAge = 67, Notes = "Carrière actuelle en Belgique" }
        ],
        NetWorthSnapshots =
        [
            new NetWorthSnapshot { Id = G(111), CapturedAt = ReferenceTimestamp.AddYears(-2), NetWorth = 328_000m, Currency = "EUR" },
            new NetWorthSnapshot { Id = G(112), CapturedAt = ReferenceTimestamp.AddYears(-1), NetWorth = 371_500m, Currency = "EUR" },
            new NetWorthSnapshot { Id = G(113), CapturedAt = ReferenceTimestamp, NetWorth = 419_800m, Currency = "EUR" }
        ]
    };

    /// <summary>Creates the screenshot reference scenario with every principal input category represented.</summary>
    private static FinancialScenario BuildBaseline(Profile profile)
    {
        var cash = Asset(G(301), "Épargne de sécurité", AssetKind.Cash, 30_000m, "EUR", 0m, 0m, true, null);
        var etf = Asset(G(302), "ETF Monde", AssetKind.Etf, 120_000m, "USD", 0.07m, 0.15m, true, "VT", 75_000m, 1_000m, "ETF Monde");
        var stock = Asset(G(303), "Actions polonaises", AssetKind.Stock, 80_000m, "PLN", 0.05m, 0.22m, true, "PKO.WA", 50_000m, 1_200m, "Actions Europe");
        var crypto = Asset(G(304), "Bitcoin", AssetKind.Crypto, 15_000m, "EUR", 0.08m, 0.65m, true, null, 9_000m);
        var home = Asset(G(305), "Maison familiale à Tournai", AssetKind.RealEstate, 320_000m, "EUR", 0.022m, 0.05m, false, null, 220_000m, customCategory: "Immobilier résidentiel");
        var rental = Asset(G(306), "Appartement locatif à Poznań", AssetKind.RealEstate, 900_000m, "PLN", 0.03m, 0.08m, false, null, 600_000m, customCategory: "Immobilier locatif");
        var car = Asset(G(307), "Voiture familiale", AssetKind.Other, 28_000m, "EUR", -0.10m, 0.10m, false, null, 42_000m, customCategory: "Véhicules");
        var business = Asset(G(308), "Participation PME", AssetKind.Business, 45_000m, "EUR", 0.04m, 0.25m, false, null, 30_000m);
        var watch = Asset(G(309), "Montre de collection", AssetKind.Collectible, 12_000m, "EUR", 0.03m, 0.12m, false, null, 8_500m);

        home.CharacteristicProfile = new AssetCharacteristicProfile { DefinitionKey = "home", DefinitionVersion = 1, ValuesJson = "{\"address\":\"12 rue de la Démo, Tournai\",\"propertyType\":\"house\",\"livingArea\":165,\"landArea\":680,\"floorCount\":2,\"hasPool\":false,\"hasSolarPanels\":true,\"constructionYear\":1998,\"energyRating\":\"B\",\"soilCondition\":4,\"kitchenCondition\":3}" };
        rental.CharacteristicProfile = new AssetCharacteristicProfile { DefinitionKey = "home", DefinitionVersion = 1, ValuesJson = "{\"address\":\"8 ul. Przykładowa, Poznań\",\"propertyType\":\"apartment\",\"livingArea\":62,\"floorCount\":1,\"hasPool\":false,\"hasSolarPanels\":false,\"constructionYear\":2016,\"energyRating\":\"C\",\"soilCondition\":4,\"kitchenCondition\":4}" };
        car.CharacteristicProfile = new AssetCharacteristicProfile { DefinitionKey = "vehicle", DefinitionVersion = 1, ValuesJson = "{\"brand\":\"Volvo\",\"model\":\"XC40\",\"constructionYear\":2022,\"mileage\":48000,\"fuelType\":\"hybrid\",\"registration\":\"DEMO-001\",\"condition\":4}" };
        watch.CharacteristicProfile = new AssetCharacteristicProfile { DefinitionKey = "watch", DefinitionVersion = 1, ValuesJson = "{\"brand\":\"Omega\",\"model\":\"Seamaster\",\"reference\":\"DEMO-300\",\"constructionYear\":2019,\"condition\":4,\"boxAndPapers\":true}" };

        var homeMortgage = new Liability { Id = G(401), Name = "Crédit maison", Kind = LiabilityKind.Mortgage, OutstandingBalance = 145_000m, InterestRate = 0.032m, MonthlyPayment = 890m, PaidOffOn = new DateOnly(2044, 12, 31), Currency = "EUR" };
        var rentalMortgage = new Liability { Id = G(402), Name = "Crédit appartement polonais", Kind = LiabilityKind.Mortgage, OutstandingBalance = 420_000m, InterestRate = 0.061m, MonthlyPayment = 3_200m, PaidOffOn = new DateOnly(2040, 6, 30), Currency = "PLN" };
        var carLoan = new Liability { Id = G(403), Name = "Financement voiture", Kind = LiabilityKind.Loan, OutstandingBalance = 12_000m, InterestRate = 0.049m, MonthlyPayment = 390m, PaidOffOn = new DateOnly(2028, 12, 31), Currency = "EUR" };
        home.LiabilityLinks.Add(new AssetLiabilityLink { Liability = homeMortgage, AllocationRate = 1m });
        rental.LiabilityLinks.Add(new AssetLiabilityLink { Liability = rentalMortgage, AllocationRate = 1m });
        car.LiabilityLinks.Add(new AssetLiabilityLink { Liability = carLoan, AllocationRate = 1m });

        var investment = new InvestmentPlan { Id = G(501), Name = "Investissement ETF mensuel", MonthlyContribution = 1_000m, ExpectedAnnualReturn = 0.065m, StartsOn = ReferenceDate, EndsOn = new DateOnly(2051, 12, 31) };
        var seasonalFreelance = new IncomeStream
        {
            Id = G(202), Name = "Conseil indépendant", Kind = IncomeKind.Freelance, AmountMode = IncomeAmountMode.Seasonal, AnnualAmount = 24_000m,
            AnnualGrowthRate = 0.01m, StartsOn = ReferenceDate, IsTaxable = true, TaxRate = 0.35m, TaxCountryCode = "BE", Currency = "USD",
            MonthlyAllocations = Enumerable.Range(1, 12).Select(month => new IncomeMonthlyAllocation { Month = month, Share = month is 7 or 8 ? 0.025m : 0.095m }).ToList()
        };

        var scenario = new FinancialScenario
        {
            Id = BaselineScenarioId,
            Profile = profile,
            Name = "Démonstration complète",
            Description = "Jeu fictif et reproductible : devises, fiscalité, patrimoine, crédits, inflation, banque et événements.",
            IsBaseline = true,
            StartsOn = ReferenceDate,
            UpdatedAt = ReferenceTimestamp,
            Assumptions = new SimulationAssumptions { Id = G(201), InflationRate = 0.025m, SalaryGrowthRate = 0.015m, SafeWithdrawalRate = 0.035m, RetirementAge = 65, MonteCarloRuns = 600, DefaultReturnVolatility = 0.12m },
            Assets = [cash, etf, stock, crypto, home, rental, car, business, watch],
            Liabilities = [homeMortgage, rentalMortgage, carLoan],
            Investments = [investment],
            Incomes =
            [
                new IncomeStream { Id = G(2010), Name = "Salaire en Belgique", Kind = IncomeKind.Salary, MonthlyAmount = 6_500m, AnnualGrowthRate = 0.015m, StartsOn = ReferenceDate, IsTaxable = true, TaxRate = 0.28m, TaxCountryCode = "BE", Currency = "EUR" },
                seasonalFreelance,
                new IncomeStream { Id = G(203), Name = "Location saisonnière Poznań", Kind = IncomeKind.Rental, AmountMode = IncomeAmountMode.Annual, AnnualAmount = 60_000m, AnnualGrowthRate = 0.02m, StartsOn = ReferenceDate, IsTaxable = true, TaxRate = 0.19m, TaxCountryCode = "PL", Currency = "PLN", LinkedAsset = rental },
                new IncomeStream { Id = G(204), Name = "Dividendes du portefeuille", Kind = IncomeKind.Dividends, MonthlyAmount = 300m, StartsOn = ReferenceDate, IsTaxable = true, TaxRate = 0.30m, TaxCountryCode = "US", Currency = "EUR", LinkedAsset = etf },
                new IncomeStream { Id = G(205), Name = "Droits d’auteur", Kind = IncomeKind.Royalties, MonthlyAmount = 180m, StartsOn = ReferenceDate, IsTaxable = true, TaxRate = 0.25m, TaxCountryCode = "BE", Currency = "EUR" }
            ],
            Expenses = BuildExpenses(home, rental),
            Events =
            [
                new ScenarioEvent { Id = G(701), Name = "Remplacement de la voiture", Kind = EventKind.VehiclePurchase, HappensOn = new DateOnly(2030, 3, 1), RecurrenceFrequency = RecurrenceFrequency.EveryFiveYears, RecurrenceEndsOn = new DateOnly(2050, 3, 1), OneOffCashImpact = -38_000m, Currency = "EUR", Notes = "Achat prévu tous les cinq ans" },
                new ScenarioEvent { Id = G(702), Name = "Perte d’emploi temporaire", Kind = EventKind.JobLoss, HappensOn = new DateOnly(2032, 4, 1), MonthlyCashImpact = -4_500m, DurationMonths = 6, Currency = "EUR" },
                new ScenarioEvent { Id = G(703), Name = "Héritage estimé", Kind = EventKind.Inheritance, HappensOn = new DateOnly(2038, 9, 1), OneOffCashImpact = 80_000m, Currency = "EUR" },
                new ScenarioEvent { Id = G(704), Name = "Augmentation de salaire", Kind = EventKind.SalaryIncrease, HappensOn = new DateOnly(2028, 1, 1), MonthlyCashImpact = 450m, DurationMonths = 0, Currency = "EUR" }
            ],
            AssetSales =
            [
                new PlannedAssetSale { Id = G(801), Name = "Vente de l’appartement locatif", Asset = rental, HappensOn = new DateOnly(2040, 6, 1), UseProjectedValue = true, SellingCosts = 18_000m, CapitalGainsTaxRate = 0.19m, CapitalGainsTaxCountryCode = "PL", RepayLinkedLiabilities = true, Destination = AssetSaleDestination.InvestmentPlan, DestinationInvestmentPlan = investment, Currency = "PLN", Notes = "Transformation de l’immobilier en portefeuille liquide" }
            ],
            AllocationStrategies =
            [
                new AllocationStrategy
                {
                    Id = G(901), Name = "Équilibre long terme 2026", Description = "Répartition fictive utilisée pour tester les écarts de portefeuille.", EffectiveFrom = ReferenceDate,
                    Targets =
                    [
                        new AllocationStrategyTarget { Id = G(911), Category = "ETF Monde", TargetPercentage = 45m, TolerancePercentage = 5m },
                        new AllocationStrategyTarget { Id = G(912), Category = "Actions Europe", TargetPercentage = 15m, TolerancePercentage = 4m },
                        new AllocationStrategyTarget { Id = G(913), Category = "Cash", TargetPercentage = 10m, TolerancePercentage = 3m },
                        new AllocationStrategyTarget { Id = G(914), Category = "RealEstate", TargetPercentage = 20m, TolerancePercentage = 5m },
                        new AllocationStrategyTarget { Id = G(915), Category = "Other", TargetPercentage = 10m, TolerancePercentage = 4m }
                    ]
                }
            ]
        };

        scenario.BankAccounts = BuildBankAccounts(cash, home, investment);
        return scenario;
    }

    /// <summary>Creates recurring, stepped and reserved costs so inflation and calendar rules stay regression-tested.</summary>
    private static List<Expense> BuildExpenses(Asset home, Asset rental) =>
    [
        new Expense { Id = G(601), Name = "Coût de la vie", Kind = ExpenseKind.Recurring, Frequency = RecurrenceFrequency.Monthly, MonthlyAmount = 2_300m, IndexedToInflation = true, StartsOn = ReferenceDate, EndsOn = new DateOnly(2076, 12, 31), Currency = "EUR", AmountChanges = [new ExpenseAmountChange { Id = G(611), EffectiveOn = new DateOnly(2031, 1, 1), Amount = 2_900m }, new ExpenseAmountChange { Id = G(612), EffectiveOn = new DateOnly(2036, 1, 1), Amount = 3_500m }] },
        new Expense { Id = G(602), Name = "Courses alimentaires", Kind = ExpenseKind.Recurring, Frequency = RecurrenceFrequency.Weekly, MonthlyAmount = 165m, IndexedToInflation = true, StartsOn = ReferenceDate, EndsOn = new DateOnly(2076, 12, 31), Currency = "EUR", ObservedBankCategory = "food" },
        new Expense { Id = G(603), Name = "Café", Kind = ExpenseKind.Recurring, Frequency = RecurrenceFrequency.Daily, MonthlyAmount = 3.50m, IndexedToInflation = true, StartsOn = ReferenceDate, EndsOn = new DateOnly(2076, 12, 31), Currency = "EUR" },
        new Expense { Id = G(604), Name = "Charges appartement", Kind = ExpenseKind.Recurring, Frequency = RecurrenceFrequency.Monthly, MonthlyAmount = 900m, IndexedToInflation = true, StartsOn = ReferenceDate, EndsOn = new DateOnly(2040, 6, 30), Currency = "PLN", LinkedAsset = rental },
        new Expense { Id = G(605), Name = "Assurances annuelles", Kind = ExpenseKind.Recurring, Frequency = RecurrenceFrequency.Yearly, MonthlyAmount = 1_400m, IndexedToInflation = true, StartsOn = new DateOnly(2026, 9, 1), EndsOn = new DateOnly(2076, 9, 1), Currency = "EUR" },
        new Expense { Id = G(606), Name = "Vacances d’été 2027", Kind = ExpenseKind.Exceptional, MonthlyAmount = 4_800m, SaveInAdvance = true, SavingsStartsOn = ReferenceDate, StartsOn = new DateOnly(2027, 7, 1), Currency = "EUR" },
        new Expense { Id = G(607), Name = "Rénovation de la toiture", Kind = ExpenseKind.Exceptional, MonthlyAmount = 35_000m, SaveInAdvance = false, StartsOn = new DateOnly(2029, 4, 1), Currency = "EUR", LinkedAsset = home }
    ];

    /// <summary>Creates locally observed EUR history with editable categories, exclusions and asset links.</summary>
    private static List<BankAccount> BuildBankAccounts(Asset cash, Asset home, InvestmentPlan investment)
    {
        var import = new BankStatementImport
        {
            Id = G(1002), SourceFileName = "fortis-demo-2026.pdf", SourceFingerprint = "demo-fortis-v1", ImporterKey = "bnp-paribas-fortis-pdf-v1",
            PeriodStartsOn = new DateOnly(2025, 10, 1), PeriodEndsOn = new DateOnly(2025, 12, 31), ImportedAt = ReferenceTimestamp,
            Transactions =
            [
                Transaction(1011, new DateOnly(2025, 10, 3), "Supermarché Démo", -142.80m, "food", BankTransactionClassification.Expense),
                Transaction(1012, new DateOnly(2025, 10, 8), "Station-service", -76.20m, "transport", BankTransactionClassification.Expense),
                Transaction(1013, new DateOnly(2025, 10, 15), "Virement portefeuille", -1_000m, "investment", BankTransactionClassification.Investment, investment: investment),
                Transaction(1014, new DateOnly(2025, 11, 4), "Supermarché Démo", -158.40m, "food", BankTransactionClassification.Expense),
                Transaction(1015, new DateOnly(2025, 11, 12), "Restaurant", -94m, "leisure", BankTransactionClassification.Expense),
                Transaction(1016, new DateOnly(2025, 11, 20), "Réparation toiture", -35_000m, "housing", BankTransactionClassification.AssetExpense, asset: home, excluded: true),
                Transaction(1017, new DateOnly(2025, 12, 2), "Supermarché Démo", -149.60m, "food", BankTransactionClassification.Expense),
                Transaction(1018, new DateOnly(2025, 12, 18), "Cadeaux", -320m, "other", BankTransactionClassification.Expense),
                Transaction(1019, new DateOnly(2025, 12, 28), "Virement entre comptes", -2_000m, "transfer", BankTransactionClassification.Transfer, excluded: true)
            ]
        };
        return
        [
            new BankAccount { Id = G(1001), BankKey = "bnp-paribas-fortis-pdf-v1", Name = "Compte courant Fortis · Démo", MaskedIdentifier = "•••• 4242", IdentifierHash = "demo-account-fortis-v1", Currency = "EUR", LinkedAsset = cash, Imports = [import] }
        ];
    }

    /// <summary>Creates a deliberately different scenario for comparison and scenario CRUD tests.</summary>
    private static FinancialScenario BuildAlternative(Profile profile) => new()
    {
        Id = Guid.Parse("20000000-0000-0000-0000-000000000002"),
        Profile = profile,
        ParentScenarioId = BaselineScenarioId,
        Name = "Retraite anticipée à 58 ans",
        Description = "Alternative fictive avec davantage d’épargne, moins de dépenses et un arrêt du salaire à 58 ans.",
        StartsOn = ReferenceDate,
        UpdatedAt = ReferenceTimestamp,
        Assumptions = new SimulationAssumptions { Id = G(1201), InflationRate = 0.025m, SalaryGrowthRate = 0.015m, SafeWithdrawalRate = 0.035m, RetirementAge = 58, MonteCarloRuns = 600, DefaultReturnVolatility = 0.12m },
        Incomes = [new IncomeStream { Id = G(1202), Name = "Salaire flexible", Kind = IncomeKind.Salary, MonthlyAmount = 6_000m, AnnualGrowthRate = 0.015m, StartsOn = ReferenceDate, IsTaxable = true, TaxRate = 0.28m, TaxCountryCode = "BE", Currency = "EUR" }],
        Assets = [Asset(G(1203), "Capital de départ", AssetKind.Etf, 210_000m, "EUR", 0.065m, 0.15m, true, null, 150_000m, customCategory: "ETF Monde"), Asset(G(1204), "Réserve retraite", AssetKind.Cash, 55_000m, "EUR", 0m, 0m, true, null)],
        Expenses = [new Expense { Id = G(1205), Name = "Budget simplifié", Kind = ExpenseKind.Recurring, Frequency = RecurrenceFrequency.Monthly, MonthlyAmount = 2_500m, IndexedToInflation = true, StartsOn = ReferenceDate, EndsOn = new DateOnly(2076, 12, 31), Currency = "EUR" }],
        Investments = [new InvestmentPlan { Id = G(1206), Name = "Effort retraite anticipée", MonthlyContribution = 1_800m, ExpectedAnnualReturn = 0.065m, StartsOn = ReferenceDate, EndsOn = new DateOnly(2044, 7, 1) }],
        Events = [new ScenarioEvent { Id = G(1207), Name = "Départ anticipé", Kind = EventKind.EarlyRetirement, HappensOn = new DateOnly(2044, 7, 17), Currency = "EUR", Notes = "Objectif de retraite à 58 ans" }]
    };

    /// <summary>Creates stable quote points for the market-history chart without making any external request.</summary>
    private static IEnumerable<AssetQuoteSnapshot> BuildQuoteHistory(FinancialScenario scenario)
    {
        var etf = scenario.Assets.Single(asset => asset.Id == G(302));
        var stock = scenario.Assets.Single(asset => asset.Id == G(303));
        return
        [
            Quote(1301, etf, new DateTimeOffset(2025, 10, 1, 12, 0, 0, TimeSpan.Zero), 108m, "USD"),
            Quote(1302, etf, new DateTimeOffset(2025, 11, 1, 12, 0, 0, TimeSpan.Zero), 114m, "USD"),
            Quote(1303, etf, ReferenceTimestamp, 120m, "USD"),
            Quote(1304, stock, new DateTimeOffset(2025, 10, 1, 12, 0, 0, TimeSpan.Zero), 62m, "PLN"),
            Quote(1305, stock, new DateTimeOffset(2025, 11, 1, 12, 0, 0, TimeSpan.Zero), 65m, "PLN"),
            Quote(1306, stock, ReferenceTimestamp, 66.67m, "PLN")
        ];
    }

    /// <summary>Creates one asset and the stable total-value history used by dossier charts.</summary>
    private static Asset Asset(Guid id, string name, AssetKind kind, decimal currentValue, string currency, decimal expectedReturn, decimal volatility, bool liquid, string? ticker, decimal purchasePrice = 0m, decimal quantity = 0m, string? customCategory = null)
    {
        var fixtureId = int.Parse(id.ToString("N")[^12..], System.Globalization.CultureInfo.InvariantCulture);
        var asset = new Asset
        {
            Id = id, Name = name, Kind = kind, CustomCategory = customCategory, CurrentValue = currentValue, PurchasePrice = purchasePrice,
            PurchasedOn = new DateOnly(2020, 1, 1), ValuedOn = ReferenceDate, ValuationSource = "Estimation de démonstration",
            ExpectedAnnualReturn = expectedReturn, Volatility = volatility, IsLiquid = liquid, Ticker = ticker, Quantity = quantity,
            IsIncludedInPortfolioAllocation = kind is AssetKind.Cash or AssetKind.Etf or AssetKind.Stock or AssetKind.Crypto,
            CapitalGainsTaxRate = kind is AssetKind.Etf or AssetKind.Stock ? 0.19m : 0m, CapitalGainsTaxCountryCode = kind is AssetKind.Etf or AssetKind.Stock ? "BE" : null, Currency = currency
        };
        asset.ValuationSnapshots =
        [
            new AssetValuationSnapshot { Id = G(fixtureId, 1), ValuedOn = ReferenceDate.AddYears(-2), Value = Math.Round(currentValue * 0.82m, 2), Currency = currency, Source = "Historique démo", RecordedAt = ReferenceTimestamp.AddYears(-2) },
            new AssetValuationSnapshot { Id = G(fixtureId, 2), ValuedOn = ReferenceDate.AddYears(-1), Value = Math.Round(currentValue * 0.91m, 2), Currency = currency, Source = "Historique démo", RecordedAt = ReferenceTimestamp.AddYears(-1) },
            new AssetValuationSnapshot { Id = G(fixtureId, 3), ValuedOn = ReferenceDate, Value = currentValue, Currency = currency, Source = "Estimation de démonstration", RecordedAt = ReferenceTimestamp }
        ];
        return asset;
    }

    /// <summary>Creates one deterministic bank transaction.</summary>
    private static BankTransaction Transaction(int id, DateOnly date, string description, decimal amount, string category, BankTransactionClassification classification, Asset? asset = null, InvestmentPlan? investment = null, bool excluded = false) => new()
    {
        Id = G(id), Fingerprint = $"demo-transaction-{id}", BookedOn = date, ValueOn = date, Description = description, Counterparty = "Démonstration", Amount = amount,
        Currency = "EUR", Classification = classification, Category = category, IsExcludedFromSpendingAnalysis = excluded, LinkedAsset = asset, LinkedInvestmentPlan = investment
    };

    /// <summary>Creates one deterministic market quote.</summary>
    private static AssetQuoteSnapshot Quote(int id, Asset asset, DateTimeOffset capturedAt, decimal price, string currency) => new()
    {
        Id = G(id), Asset = asset, CapturedAt = capturedAt, Price = price, Currency = currency, Source = "Cours fictif LifeLedger"
    };

    /// <summary>Builds a stable GUID from an integer fixture identifier.</summary>
    private static Guid G(int id) => Guid.Parse($"90000000-0000-0000-0000-{Math.Abs((long)id):D12}");

    /// <summary>Builds a stable GUID from two integers when several children share a parent-derived identifier.</summary>
    private static Guid G(int parent, int child) => G(unchecked(Math.Abs(parent % 1_000_000) * 10 + child));
}
