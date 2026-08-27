using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pisces.Core.Interfaces;
using Pisces.Core.Models;
using Pisces.Infrastructure.Configuration;

namespace Pisces.Infrastructure.Repositories;

/// <summary>
/// Loads and saves the module map from <c>{DataDirectory}/module_map.json</c>.
/// The file is a JSON array of <see cref="Module"/> objects.
/// </summary>
public sealed class JsonModuleMapRepository : IModuleMap
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly ILogger<JsonModuleMapRepository> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<Module>? _cache;

    public JsonModuleMapRepository(IOptions<PiscesConfig> config, ILogger<JsonModuleMapRepository> logger)
    {
        _logger = logger;
        var dir = config.Value.DataDirectory;
        if (string.IsNullOrWhiteSpace(dir))
            dir = "data";
        if (!Path.IsPathRooted(dir))
            dir = Path.Combine(AppContext.BaseDirectory, dir);
        _filePath = Path.Combine(dir, "module_map.json");
    }

    public async Task<IReadOnlyList<Module>> GetAllModulesAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await LoadAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<Module>> GetByTypeAsync(ModuleType type, CancellationToken ct = default)
    {
        var all = await GetAllModulesAsync(ct);
        return all.Where(m => m.Type == type).ToList();
    }

    public async Task<Module?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var all = await GetAllModulesAsync(ct);
        return all.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SaveModuleAsync(Module module, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var modules = new List<Module>(await LoadAsync(ct));
            var index = modules.FindIndex(m => string.Equals(m.Id, module.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                modules[index] = module;
            else
                modules.Add(module);
            await PersistAsync(modules, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteModuleAsync(string id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var modules = new List<Module>(await LoadAsync(ct));
            if (modules.RemoveAll(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) > 0)
                await PersistAsync(modules, ct);
        }
        finally { _lock.Release(); }
    }

    private async Task<List<Module>> LoadAsync(CancellationToken ct)
    {
        if (_cache is not null)
            return _cache;

        if (!File.Exists(_filePath))
        {
            _logger.LogWarning("Module map not found at {Path} — starting with an empty map", _filePath);
            _cache = [];
            return _cache;
        }

        await using var stream = File.OpenRead(_filePath);
        var modules = await JsonSerializer.DeserializeAsync<List<Module>>(stream, JsonOptions, ct);
        _cache = modules ?? [];
        _logger.LogInformation("Loaded {Count} module(s) from {Path}", _cache.Count, _filePath);
        return _cache;
    }

    private async Task PersistAsync(List<Module> modules, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, modules, JsonOptions, ct);
        _cache = modules;
    }
}
