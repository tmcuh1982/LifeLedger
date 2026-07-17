using System.Text.Json;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Manages the installation-wide list of user-defined asset categories.</summary>
public interface IAssetCategoryService
{
    /// <summary>Lists custom categories with their current asset usage.</summary>
    Task<IReadOnlyList<AssetCategoryResponse>> ListAsync(CancellationToken cancellationToken = default);
    /// <summary>Adds a custom category.</summary>
    Task<AssetCategoryResponse> AddAsync(string name, CancellationToken cancellationToken = default);
    /// <summary>Renames a custom category and every asset already assigned to it.</summary>
    Task<AssetCategoryResponse> RenameAsync(string currentName, string newName, CancellationToken cancellationToken = default);
    /// <summary>Deletes an unused custom category.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>Signals that removing a category would leave assigned assets without their chosen classification.</summary>
public sealed class AssetCategoryInUseException(string category) : InvalidOperationException($"The category '{category}' is still used by one or more assets.");

/// <summary>Stores the editable category catalogue in application settings and keeps asset values in sync.</summary>
public sealed class AssetCategoryService(LifeLedgerDbContext db) : IAssetCategoryService
{
    /// <summary>Application-settings key containing the JSON category-name list.</summary>
    public const string SettingKey = "asset-categories";

    /// <inheritdoc />
    public async Task<IReadOnlyList<AssetCategoryResponse>> ListAsync(CancellationToken cancellationToken = default)
    {
        var names = await LoadNamesAsync(cancellationToken);
        var assignedNames = await db.Assets.AsNoTracking()
            .Where(asset => asset.CustomCategory != null)
            .Select(asset => asset.CustomCategory!)
            .ToListAsync(cancellationToken);

        // Asset values are included so a restored backup can reconstruct its catalogue automatically.
        names.UnionWith(assignedNames);
        return names
            .Select(name => new AssetCategoryResponse(name, assignedNames.Count(assigned => string.Equals(assigned, name, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<AssetCategoryResponse> AddAsync(string name, CancellationToken cancellationToken = default)
    {
        name = ValidateName(name);
        var names = await LoadNamesAsync(cancellationToken);
        if (!names.Add(name)) throw new InvalidOperationException($"The category '{name}' already exists.");
        await SaveNamesAsync(names, cancellationToken);
        return new AssetCategoryResponse(name, 0);
    }

    /// <inheritdoc />
    public async Task<AssetCategoryResponse> RenameAsync(string currentName, string newName, CancellationToken cancellationToken = default)
    {
        currentName = ValidateName(currentName);
        newName = ValidateName(newName);
        var names = await LoadNamesAsync(cancellationToken);
        var storedName = names.FirstOrDefault(name => string.Equals(name, currentName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"The category '{currentName}' does not exist.");
        if (!string.Equals(storedName, newName, StringComparison.OrdinalIgnoreCase) && names.Contains(newName))
            throw new InvalidOperationException($"The category '{newName}' already exists.");

        var assignedAssets = (await db.Assets.Where(asset => asset.CustomCategory != null).ToListAsync(cancellationToken))
            .Where(asset => string.Equals(asset.CustomCategory, storedName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var asset in assignedAssets) asset.CustomCategory = newName;
        names.Remove(storedName);
        names.Add(newName);
        await SaveNamesAsync(names, cancellationToken);
        return new AssetCategoryResponse(newName, assignedAssets.Length);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        name = ValidateName(name);
        var names = await LoadNamesAsync(cancellationToken);
        var storedName = names.FirstOrDefault(category => string.Equals(category, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"The category '{name}' does not exist.");
        var isUsed = (await db.Assets.AsNoTracking().Where(asset => asset.CustomCategory != null).Select(asset => asset.CustomCategory!).ToListAsync(cancellationToken))
            .Any(category => string.Equals(category, storedName, StringComparison.OrdinalIgnoreCase));
        if (isUsed) throw new AssetCategoryInUseException(storedName);
        names.Remove(storedName);
        await SaveNamesAsync(names, cancellationToken);
    }

    /// <summary>Loads the persisted list while tolerating an absent setting on older databases.</summary>
    private async Task<HashSet<string>> LoadNamesAsync(CancellationToken cancellationToken)
    {
        var value = await db.ApplicationSettings.AsNoTracking().Where(setting => setting.Key == SettingKey).Select(setting => setting.Value).FirstOrDefaultAsync(cancellationToken);
        try
        {
            return new HashSet<string>(value is null ? [] : JsonSerializer.Deserialize<string[]>(value) ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The stored asset-category catalogue is malformed.");
        }
    }

    /// <summary>Writes the sorted list and commits category and assigned-asset changes atomically.</summary>
    private async Task SaveNamesAsync(HashSet<string> names, CancellationToken cancellationToken)
    {
        var setting = await db.ApplicationSettings.FindAsync([SettingKey], cancellationToken);
        var value = JsonSerializer.Serialize(names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        if (setting is null) db.ApplicationSettings.Add(new ApplicationSetting { Key = SettingKey, Value = value });
        else { setting.Value = value; setting.UpdatedAt = DateTimeOffset.UtcNow; }
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Normalises a category name and enforces a compact user-facing label.</summary>
    private static string ValidateName(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A category name is required.", nameof(name));
        if (name.Length > 80) throw new ArgumentException("A category name cannot exceed 80 characters.", nameof(name));
        return name;
    }
}
