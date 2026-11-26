namespace purrTTY.Kfs.Catalog;

/// <summary>
/// A set of service and enumeration definitions. <see cref="XCatalog.Catalog"/> is the real one;
/// the emitter and the self-check take any instance, so a test can check a deliberately broken
/// copy (<c>XCatalog.Catalog with { Services = … }</c>).
/// </summary>
/// <param name="Services">The services, in id order.</param>
/// <param name="Enums">The enumerations and header-only constant groups, in header order.</param>
public sealed record ServiceCatalog(IReadOnlyList<ServiceDef> Services, IReadOnlyList<EnumDef> Enums)
{
    /// <summary>The service with <paramref name="id"/>, or null.</summary>
    public ServiceDef? ById(ushort id) => Services.FirstOrDefault(s => s.Id == id);

    /// <summary>The service named <paramref name="name"/>, or null.</summary>
    public ServiceDef? ByName(string name) => Services.FirstOrDefault(s => s.Name == name);

    /// <summary>The enumeration with <c>enums.json</c> key <paramref name="key"/>, or null.</summary>
    public EnumDef? EnumByKey(string key) => Enums.FirstOrDefault(e => e.Key == key);

    /// <summary>A copy in which the service with <paramref name="id"/> is replaced by <paramref name="change"/>'s result.</summary>
    public ServiceCatalog WithService(ushort id, Func<ServiceDef, ServiceDef> change) =>
        this with { Services = Services.Select(s => s.Id == id ? change(s) : s).ToArray() };
}
