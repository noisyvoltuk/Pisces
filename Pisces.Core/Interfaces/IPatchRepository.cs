using Pisces.Core.Models;

namespace Pisces.Core.Interfaces;

/// <summary>
/// Persistence for patches. Implementation: JsonPatchRepository.
/// </summary>
public interface IPatchRepository
{
    Task<IReadOnlyList<Patch>> GetAllAsync(CancellationToken ct = default);
    Task<Patch?> GetByIdAsync(string id, CancellationToken ct = default);
    Task SaveAsync(Patch patch, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}

/// <summary>
/// Read/write access to the module map.
/// Implementation: JsonModuleMapRepository.
/// </summary>
public interface IModuleMap
{
    Task<IReadOnlyList<Module>> GetAllModulesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Module>> GetByTypeAsync(ModuleType type, CancellationToken ct = default);
    Task<Module?> GetByIdAsync(string id, CancellationToken ct = default);
    Task SaveModuleAsync(Module module, CancellationToken ct = default);
    Task DeleteModuleAsync(string id, CancellationToken ct = default);
}
