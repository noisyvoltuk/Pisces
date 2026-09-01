using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pisces.Core.Interfaces;
using Pisces.Core.Models;
using Pisces.Infrastructure.Configuration;

namespace Pisces.Infrastructure.Repositories;

/// <summary>
/// Stores each patch as its own file at <c>{DataDirectory}/patches/{id}.json</c>.
/// </summary>
public sealed class JsonPatchRepository : IPatchRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _dir;
    private readonly ILogger<JsonPatchRepository> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonPatchRepository(IOptions<PiscesConfig> config, ILogger<JsonPatchRepository> logger)
    {
        _logger = logger;
        var data = config.Value.DataDirectory;
        if (string.IsNullOrWhiteSpace(data))
            data = "data";
        if (!Path.IsPathRooted(data))
            data = Path.Combine(AppContext.BaseDirectory, data);
        _dir = Path.Combine(data, "patches");
    }

    public async Task<IReadOnlyList<Patch>> GetAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!Directory.Exists(_dir))
                return [];

            var patches = new List<Patch>();
            foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
            {
                try
                {
                    await using var stream = File.OpenRead(file);
                    var patch = await JsonSerializer.DeserializeAsync<Patch>(stream, JsonOptions, ct);
                    if (patch is not null)
                        patches.Add(patch);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping unreadable patch file {File}", file);
                }
            }
            return patches.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        finally { _lock.Release(); }
    }

    public async Task<Patch?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var path = PathFor(id);
        if (!File.Exists(path))
            return null;
        await _lock.WaitAsync(ct);
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<Patch>(stream, JsonOptions, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(Patch patch, CancellationToken ct = default)
    {
        patch.UpdatedAt = DateTimeOffset.UtcNow;
        await _lock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_dir);
            await using var stream = File.Create(PathFor(patch.Id));
            await JsonSerializer.SerializeAsync(stream, patch, JsonOptions, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var path = PathFor(id);
            if (File.Exists(path))
                File.Delete(path);
        }
        finally { _lock.Release(); }
    }

    private string PathFor(string id)
    {
        // ids are GUIDs, but guard against anything odd sneaking into a file path
        var safe = string.Concat(id.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        return Path.Combine(_dir, safe + ".json");
    }
}
