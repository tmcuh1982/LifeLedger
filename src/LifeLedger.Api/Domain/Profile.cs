namespace LifeLedger.Api.Domain;

public sealed class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = "My financial life";
    public DateOnly BirthDate { get; set; }
    public string HomeCountryCode { get; set; } = "PL";
    public string BaseCurrency { get; set; } = "EUR";
    public int ExpectedLifespan { get; set; } = 92;
    public int? PartnerBirthYear { get; set; }
    public int ChildrenCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<CareerPeriod> Careers { get; set; } = [];
}

public sealed class CareerPeriod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public Profile? Profile { get; set; }
    public string CountryCode { get; set; } = "PL";
    public DateOnly StartedOn { get; set; }
    public DateOnly? EndedOn { get; set; }
    public decimal AnnualInsurableIncome { get; set; }
    public decimal EstimatedMonthlyPublicPension { get; set; }
    public int PensionAge { get; set; } = 65;
    public string Notes { get; set; } = string.Empty;
}
