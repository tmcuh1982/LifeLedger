using System.Text.Json;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Exposes and validates strongly typed, versioned characteristic profiles for assets.</summary>
public interface IAssetProfileCatalog
{
    /// <summary>Returns every profile definition available to the local client.</summary>
    Task<IReadOnlyList<AssetProfileDefinition>> ListAsync(CancellationToken cancellationToken = default);
    /// <summary>Returns every custom historical version required for a complete private backup.</summary>
    Task<IReadOnlyList<AssetProfileDefinition>> ExportCustomHistoryAsync(CancellationToken cancellationToken = default);
    /// <summary>Validates profile values against the exact requested definition version.</summary>
    Task<IReadOnlyDictionary<string, string[]>> ValidateAsync(string? definitionKey, int? definitionVersion, IReadOnlyDictionary<string, JsonElement>? values, CancellationToken cancellationToken = default);
}

/// <summary>Creates and versions installation-local asset profile definitions.</summary>
public interface ICustomAssetProfileService
{
    /// <summary>Creates version one of a user-defined profile.</summary>
    Task<AssetProfileDefinition> AddAsync(AssetProfileDefinitionRequest request, CancellationToken cancellationToken = default);
    /// <summary>Creates the next immutable version of an existing user-defined profile.</summary>
    Task<AssetProfileDefinition> UpdateAsync(string key, AssetProfileDefinitionRequest request, CancellationToken cancellationToken = default);
    /// <summary>Deletes every version of an unused user-defined profile.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Signals that an asset profile cannot be removed while one or more assets use it.</summary>
public sealed class AssetProfileDefinitionInUseException(string key) : InvalidOperationException($"The asset profile '{key}' is still used by one or more assets.");

/// <summary>Combines built-in profiles with locally persisted custom profile versions.</summary>
public sealed class AssetProfileCatalog(LifeLedgerDbContext db) : IAssetProfileCatalog, ICustomAssetProfileService
{
    /// <summary>Application-settings key containing every historical custom profile definition.</summary>
    public const string SettingKey = "asset-profile-definitions";
    /// <summary>Locales supported by the current multilingual client.</summary>
    private static readonly string[] SupportedLocales = ["en", "fr", "pl", "de", "nl"];
    /// <summary>Immutable definitions shared by all local requests.</summary>
    private static readonly IReadOnlyList<AssetProfileDefinition> BuiltInDefinitions =
    [
        new("home", 1, L("Home", "Logement", "Nieruchomość", "Immobilie", "Woning"),
        [
            F("address", "Address", "Adresse", "Adres", "Adresse", "Adres", AssetProfileFieldType.Text, true),
            S("propertyType", "Property type", "Type de logement", "Typ nieruchomości", "Immobilienart", "Woningtype",
                O("house", "House", "Maison", "Dom", "Haus", "Huis"), O("apartment", "Apartment", "Appartement", "Mieszkanie", "Wohnung", "Appartement"), O("land", "Land", "Terrain", "Działka", "Grundstück", "Grond"), O("other", "Other", "Autre", "Inne", "Andere", "Andere")),
            F("livingArea", "Living area (m²)", "Surface habitable (m²)", "Powierzchnia mieszkalna (m²)", "Wohnfläche (m²)", "Woonoppervlakte (m²)", AssetProfileFieldType.Area),
            F("landArea", "Land area (m²)", "Surface du terrain (m²)", "Powierzchnia działki (m²)", "Grundstücksfläche (m²)", "Perceeloppervlakte (m²)", AssetProfileFieldType.Area),
            F("floorCount", "Number of floors", "Nombre d’étages", "Liczba pięter", "Anzahl Etagen", "Aantal verdiepingen", AssetProfileFieldType.Number),
            F("hasPool", "Swimming pool", "Piscine", "Basen", "Pool", "Zwembad", AssetProfileFieldType.Boolean),
            F("hasSolarPanels", "Solar panels", "Panneaux solaires", "Panele słoneczne", "Solarmodule", "Zonnepanelen", AssetProfileFieldType.Boolean),
            F("constructionYear", "Construction year", "Année de construction", "Rok budowy", "Baujahr", "Bouwjaar", AssetProfileFieldType.Number),
            S("energyRating", "Energy rating", "Classe énergétique (DPE)", "Klasa energetyczna", "Energieklasse", "Energielabel", "A", "B", "C", "D", "E", "F", "G"),
            F("soilCondition", "Ground condition (1–5)", "État du sol (1–5)", "Stan gruntu (1–5)", "Bodenzustand (1–5)", "Staat van de grond (1–5)", AssetProfileFieldType.Condition),
            F("kitchenCondition", "Kitchen condition (1–5)", "État de la cuisine (1–5)", "Stan kuchni (1–5)", "Küchenzustand (1–5)", "Staat van de keuken (1–5)", AssetProfileFieldType.Condition)
        ]),
        new("vehicle", 1, L("Vehicle", "Véhicule", "Pojazd", "Fahrzeug", "Voertuig"),
        [
            F("brand", "Brand", "Marque", "Marka", "Marke", "Merk", AssetProfileFieldType.Text, true),
            F("model", "Model", "Modèle", "Model", "Modell", "Model", AssetProfileFieldType.Text, true),
            F("constructionYear", "Model year", "Année du modèle", "Rok modelowy", "Modelljahr", "Modeljaar", AssetProfileFieldType.Number),
            F("mileage", "Mileage (km)", "Kilométrage (km)", "Przebieg (km)", "Kilometerstand (km)", "Kilometerstand (km)", AssetProfileFieldType.Distance),
            S("fuelType", "Powertrain", "Motorisation", "Napęd", "Antrieb", "Aandrijving",
                O("petrol", "Petrol", "Essence", "Benzyna", "Benzin", "Benzine"), O("diesel", "Diesel", "Diesel", "Diesel", "Diesel", "Diesel"), O("hybrid", "Hybrid", "Hybride", "Hybryda", "Hybrid", "Hybride"), O("electric", "Electric", "Électrique", "Elektryczny", "Elektrisch", "Elektrisch"), O("other", "Other", "Autre", "Inny", "Andere", "Andere")),
            F("registration", "Registration number", "Immatriculation", "Numer rejestracyjny", "Kennzeichen", "Kenteken", AssetProfileFieldType.Text),
            F("condition", "Condition (1–5)", "État (1–5)", "Stan (1–5)", "Zustand (1–5)", "Staat (1–5)", AssetProfileFieldType.Condition)
        ]),
        new("watch", 1, L("Watch", "Montre", "Zegarek", "Uhr", "Horloge"),
        [
            F("brand", "Brand", "Marque", "Marka", "Marke", "Merk", AssetProfileFieldType.Text, true),
            F("model", "Model", "Modèle", "Model", "Modell", "Model", AssetProfileFieldType.Text, true),
            F("reference", "Reference", "Référence", "Numer referencyjny", "Referenz", "Referentie", AssetProfileFieldType.Text),
            F("constructionYear", "Year", "Année", "Rok", "Jahr", "Jaar", AssetProfileFieldType.Number),
            F("condition", "Condition (1–5)", "État (1–5)", "Stan (1–5)", "Zustand (1–5)", "Staat (1–5)", AssetProfileFieldType.Condition),
            F("boxAndPapers", "Box and papers", "Boîte et papiers", "Pudełko i dokumenty", "Box und Papiere", "Doos en papieren", AssetProfileFieldType.Boolean)
        ])
    ];

    /// <inheritdoc />
    public async Task<IReadOnlyList<AssetProfileDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        var custom = await LoadHistoryAsync(cancellationToken);
        // Only the latest custom version is selectable; historical versions remain available to validation.
        return BuiltInDefinitions.Concat(custom.GroupBy(definition => definition.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.MaxBy(definition => definition.Version)!))
            .OrderBy(definition => definition.IsCustom)
            .ThenBy(definition => definition.Labels.GetValueOrDefault("en"), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AssetProfileDefinition>> ExportCustomHistoryAsync(CancellationToken cancellationToken = default) => await LoadHistoryAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string[]>> ValidateAsync(string? definitionKey, int? definitionVersion, IReadOnlyDictionary<string, JsonElement>? values, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(definitionKey))
        {
            if (values is { Count: > 0 }) errors["profileValues"] = ["Choose a characteristic profile before entering profile values."];
            return errors;
        }

        var definition = BuiltInDefinitions.SingleOrDefault(candidate => candidate.Key.Equals(definitionKey, StringComparison.OrdinalIgnoreCase) && candidate.Version == definitionVersion)
            ?? (await LoadHistoryAsync(cancellationToken)).SingleOrDefault(candidate => candidate.Key.Equals(definitionKey, StringComparison.OrdinalIgnoreCase) && candidate.Version == definitionVersion);
        if (definition is null)
        {
            errors["profileDefinitionKey"] = ["The selected characteristic profile or version does not exist."];
            return errors;
        }

        values ??= new Dictionary<string, JsonElement>();
        foreach (var key in values.Keys.Where(key => definition.Fields.All(field => !field.Key.Equals(key, StringComparison.OrdinalIgnoreCase))))
            errors[$"profileValues.{key}"] = ["This field is not part of the selected profile."];

        foreach (var field in definition.Fields)
        {
            if (!values.TryGetValue(field.Key, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
            {
                if (field.Required) errors[$"profileValues.{field.Key}"] = ["This field is required."];
                continue;
            }
            if (!HasExpectedType(field, value)) errors[$"profileValues.{field.Key}"] = ["The value does not match the field type."];
        }
        return errors;
    }

    /// <inheritdoc />
    public async Task<AssetProfileDefinition> AddAsync(AssetProfileDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var history = await LoadHistoryAsync(cancellationToken);
        var definition = NormaliseDefinition($"custom-{Guid.NewGuid():N}", 1, request);
        history.Add(definition);
        await SaveHistoryAsync(history, cancellationToken);
        return definition;
    }

    /// <inheritdoc />
    public async Task<AssetProfileDefinition> UpdateAsync(string key, AssetProfileDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        key = key.Trim();
        if (BuiltInDefinitions.Any(definition => definition.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Built-in profiles cannot be modified.");
        var history = await LoadHistoryAsync(cancellationToken);
        var latest = history.Where(definition => definition.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).MaxBy(definition => definition.Version)
            ?? throw new KeyNotFoundException("The custom asset profile does not exist.");
        var definition = NormaliseDefinition(latest.Key, latest.Version + 1, request);
        history.Add(definition);
        await SaveHistoryAsync(history, cancellationToken);
        return definition;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        key = key.Trim();
        var history = await LoadHistoryAsync(cancellationToken);
        if (!history.Any(definition => definition.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            throw new KeyNotFoundException("The custom asset profile does not exist.");
        if (await db.AssetCharacteristicProfiles.AsNoTracking().AnyAsync(profile => profile.DefinitionKey == key, cancellationToken))
            throw new AssetProfileDefinitionInUseException(key);
        history.RemoveAll(definition => definition.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        await SaveHistoryAsync(history, cancellationToken);
    }

    /// <summary>Loads every immutable custom profile version and rejects malformed local settings.</summary>
    private async Task<List<AssetProfileDefinition>> LoadHistoryAsync(CancellationToken cancellationToken)
    {
        var json = await db.ApplicationSettings.AsNoTracking().Where(setting => setting.Key == SettingKey).Select(setting => setting.Value).FirstOrDefaultAsync(cancellationToken);
        try { return json is null ? [] : JsonSerializer.Deserialize<List<AssetProfileDefinition>>(json) ?? []; }
        catch (JsonException) { throw new InvalidOperationException("The stored asset-profile catalogue is malformed."); }
    }

    /// <summary>Writes all historical versions so older asset data remains interpretable.</summary>
    private async Task SaveHistoryAsync(List<AssetProfileDefinition> history, CancellationToken cancellationToken)
    {
        var setting = await db.ApplicationSettings.FindAsync([SettingKey], cancellationToken);
        var json = JsonSerializer.Serialize(history.OrderBy(definition => definition.Key).ThenBy(definition => definition.Version));
        if (setting is null) db.ApplicationSettings.Add(new ApplicationSetting { Key = SettingKey, Value = json });
        else { setting.Value = json; setting.UpdatedAt = DateTimeOffset.UtcNow; }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Normalises labels and field definitions before a new immutable profile version is stored.</summary>
    private static AssetProfileDefinition NormaliseDefinition(string key, int version, AssetProfileDefinitionRequest request)
    {
        if (request.Fields is null || request.Fields.Count == 0) throw new ArgumentException("Add at least one field.", nameof(request));
        if (request.Fields.Count > 40) throw new ArgumentException("A profile cannot contain more than 40 fields.", nameof(request));
        var fields = request.Fields.Select((field, index) => NormaliseField(field, index)).ToArray();
        if (fields.Select(field => field.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Length)
            throw new ArgumentException("Every field needs a unique key.", nameof(request));
        return new AssetProfileDefinition(key, version, NormaliseLabels(request.Labels, 80, "A profile name is required."), fields, true);
    }

    /// <summary>Validates one field and keeps its stable key suitable for JSON values.</summary>
    private static AssetProfileFieldDefinition NormaliseField(AssetProfileFieldDefinition field, int index)
    {
        var key = string.IsNullOrWhiteSpace(field.Key) ? $"field{index + 1}" : field.Key.Trim();
        if (!char.IsAsciiLetterLower(key[0]) || key.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("Field keys must start with a lowercase letter and contain only letters or digits.");
        var options = field.Type == AssetProfileFieldType.Select
            ? NormaliseOptions(field.Options)
            : null;
        return new AssetProfileFieldDefinition(key, NormaliseLabels(field.Labels, 100, "Every field needs a name."), field.Type, field.Required, options);
    }

    /// <summary>Validates and normalises the closed option list of a select field.</summary>
    private static IReadOnlyList<AssetProfileOptionDefinition> NormaliseOptions(IReadOnlyList<AssetProfileOptionDefinition>? options)
    {
        if (options is null || options.Count == 0) throw new ArgumentException("A list field needs at least one choice.");
        if (options.Count > 30) throw new ArgumentException("A list field cannot contain more than 30 choices.");
        var normalised = options.Select((option, index) => new AssetProfileOptionDefinition(
            string.IsNullOrWhiteSpace(option.Value) ? $"option{index + 1}" : option.Value.Trim(),
            NormaliseLabels(option.Labels, 80, "Every choice needs a name."))).ToArray();
        if (normalised.Select(option => option.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalised.Length)
            throw new ArgumentException("Every choice must be unique.");
        return normalised;
    }

    /// <summary>Cleans translations and fills missing locales with a readable fallback label.</summary>
    private static IReadOnlyDictionary<string, string> NormaliseLabels(IReadOnlyDictionary<string, string>? labels, int maxLength, string requiredMessage)
    {
        var cleaned = (labels ?? new Dictionary<string, string>())
            .Where(entry => SupportedLocales.Contains(entry.Key, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry.Value))
            .ToDictionary(entry => entry.Key.ToLowerInvariant(), entry => entry.Value.Trim(), StringComparer.OrdinalIgnoreCase);
        var fallback = cleaned.GetValueOrDefault("en") ?? cleaned.Values.FirstOrDefault() ?? throw new ArgumentException(requiredMessage);
        if (cleaned.Values.Any(label => label.Length > maxLength)) throw new ArgumentException($"Labels cannot exceed {maxLength} characters.");
        return SupportedLocales.ToDictionary(locale => locale, locale => cleaned.GetValueOrDefault(locale) ?? fallback);
    }

    /// <summary>Checks JSON primitive and select-option constraints for a profile field.</summary>
    private static bool HasExpectedType(AssetProfileFieldDefinition field, JsonElement value) => field.Type switch
    {
        AssetProfileFieldType.Text or AssetProfileFieldType.Date => value.ValueKind == JsonValueKind.String,
        AssetProfileFieldType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        AssetProfileFieldType.Select => value.ValueKind == JsonValueKind.String && field.Options?.Any(option => option.Value == value.GetString()) == true,
        AssetProfileFieldType.Condition => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var condition) && condition is >= 1 and <= 5,
        _ => value.ValueKind == JsonValueKind.Number
    };

    /// <summary>Creates a translated field definition.</summary>
    private static AssetProfileFieldDefinition F(string key, string en, string fr, string pl, string de, string nl, AssetProfileFieldType type, bool required = false) => new(key, L(en, fr, pl, de, nl), type, required);

    /// <summary>Creates a translated select field from explicit translated options.</summary>
    private static AssetProfileFieldDefinition S(string key, string en, string fr, string pl, string de, string nl, params AssetProfileOptionDefinition[] options) => new(key, L(en, fr, pl, de, nl), AssetProfileFieldType.Select, false, options);

    /// <summary>Creates a select field whose stored values are already suitable labels.</summary>
    private static AssetProfileFieldDefinition S(string key, string en, string fr, string pl, string de, string nl, params string[] options) => new(key, L(en, fr, pl, de, nl), AssetProfileFieldType.Select, false, options.Select(value => new AssetProfileOptionDefinition(value, L(value, value, value, value, value))).ToArray());

    /// <summary>Creates one translated select option.</summary>
    private static AssetProfileOptionDefinition O(string value, string en, string fr, string pl, string de, string nl) => new(value, L(en, fr, pl, de, nl));

    /// <summary>Builds the complete language map used by the multilingual client.</summary>
    private static IReadOnlyDictionary<string, string> L(string en, string fr, string pl, string de, string nl) => new Dictionary<string, string> { ["en"] = en, ["fr"] = fr, ["pl"] = pl, ["de"] = de, ["nl"] = nl };
}
