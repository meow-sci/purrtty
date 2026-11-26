using KSA.Kfs.Vm.Tables;
using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Tables;

namespace purrTTY.Kfs.Tests;

/// <summary>
/// The catalogue merged into the game's real tables by the game's own loader
/// (<see cref="TableAugmenter"/>), compared with tables built from the untouched stock streams.
/// </summary>
[TestFixture]
public sealed class AugmentedTablesTests : GameFixture
{
    [Test]
    public void Tables_Augmented_LoadAndValidate()
    {
        GameTables stock = KfsTables.Stock();
        GameTables aug = TableAugmenter.Build(TableAugmenter.OpenStock);

        Assert.That(aug.Origin, Is.EqualTo(TableAugmenter.OriginFor(XCatalog.Catalog)));
        Assert.That(aug.ServiceTableVersion, Is.EqualTo(stock.ServiceTableVersion), "service table version unchanged");
        Assert.That(aug.MessageCatalogueVersion, Is.EqualTo(stock.MessageCatalogueVersion), "message catalogue version unchanged");
        Assert.That(aug.EnumsData.Version, Is.EqualTo(stock.EnumsData.Version));
        Assert.That(aug.Services, Has.Count.EqualTo(stock.Services.Count + XCatalog.Services.Count));

        // Stock rows unchanged.
        foreach (ushort id in stock.Services.Keys)
        {
            Assert.That(aug.Services.ContainsKey(id), Is.True, $"stock id {id} kept");
            Assert.That(Describe(aug.Services[id]), Is.EqualTo(Describe(stock.Services[id])), $"stock id {id} unchanged");
        }
        Assert.That(aug.ServicesData.Services.Take(stock.ServicesData.Services.Count).Select(r => r.Id),
            Is.EqualTo(stock.ServicesData.Services.Select(r => r.Id)), "stock rows first, in their order");

        // Every catalogue row loaded as defined.
        foreach (var s in XCatalog.Services)
        {
            Assert.That(aug.Services.TryGetValue(s.Id, out var info), Is.True, $"{s.Name} loaded");
            Assert.That(info!.Name, Is.EqualTo(s.Name));
            Assert.That(info.Kind, Is.EqualTo(s.Kind), s.Name);
            Assert.That(info.ArgTypes, Is.EqualTo(s.ArgTypes), s.Name);
            Assert.That(info.ArgCells, Is.EqualTo(s.ArgTypes.Count), s.Name);
            Assert.That(info.ResultCells, Is.EqualTo(s.HasResult ? 1 : 0), s.Name);
            Assert.That(info.CostClass, Is.EqualTo(s.CostClass), s.Name);
            Assert.That(info.RequiresStock, Is.Null, s.Name);
            Assert.That(info.PerValue, Is.False, s.Name);
            Assert.That(info.Domains, Is.EqualTo(s.ArgDomains), s.Name);
            Assert.That(info.ArgumentNames, Is.EqualTo(s.Params.Select(p => p.Name)), s.Name);
            Assert.That(info.ArgumentOf, Is.EqualTo(s.CellOwners), $"{s.Name}: kc names group onto the prototype's cells");
            Assert.That(info.Summary, Is.EqualTo(s.Summary), s.Name);
        }

        // Enumerations: stock ones unchanged, ours added with fillers at their positions.
        foreach (var (key, values) in stock.EnumsData.Enums)
        {
            Assert.That(aug.EnumsData.Enums[key], Is.EqualTo(values), $"stock enum {key} unchanged");
        }
        Assert.That(aug.EnumsData.Enums, Has.Count.EqualTo(stock.EnumsData.Enums.Count + CatalogEmitter.EnumEntries(XCatalog.Catalog).Count));
        foreach (var (key, values) in CatalogEmitter.EnumEntries(XCatalog.Catalog))
        {
            Assert.That(aug.EnumsData.Enums[key], Is.EqualTo(values), key);
        }

        // Every enum domain resolves to the intended range.
        foreach (var s in XCatalog.Services)
        {
            foreach (var p in s.Params.Where(p => p.Domain.StartsWith("enum:", StringComparison.Ordinal)))
            {
                string resolved = DescribeEnumDomain(aug, p.Domain);
                string key = p.Domain["enum:".Length..].Split('[')[0];
                Assert.That(resolved, Is.EqualTo($"{key} {p.Selects!.Min()}..{p.Selects!.Max()}"), $"{s.Name} {p.Name}");
            }
        }
    }

    [Test]
    public void Catalog_SelfCheck_Passes_Against_Stock_Tables()
    {
        GameTables stock = KfsTables.Stock();
        var problems = CatalogSelfCheck.Run(XCatalog.Catalog, stock.Services.Keys.ToArray(), stock.EnumsData.Enums.Keys.ToArray());
        Assert.That(problems, Is.Empty, () => string.Join("\n", problems));

        // And the check does see the stock table: a stock id is reported.
        var clash = CatalogSelfCheck.Run(XCatalog.Catalog.WithService(Svc.PartsInfo, s => s with { Id = 1 }),
            stock.Services.Keys.ToArray(), stock.EnumsData.Enums.Keys.ToArray());
        Assert.That(clash.Select(p => p.Rule), Does.Contain(CatalogRule.IdInStockTable));
    }

    [Test]
    public void Getter_Selector_Ranges_Are_Disjoint()
    {
        GameTables aug = TableAugmenter.Build(TableAugmenter.OpenStock);

        var getters = XCatalog.Services.Where(s => s.Name.EndsWith("_geti", StringComparison.Ordinal)).ToList();
        Assert.That(getters, Has.Count.EqualTo(3));
        foreach (var geti in getters)
        {
            string getfName = geti.Name[..^1] + "f";
            var getf = XCatalog.Catalog.ByName(getfName);
            Assert.That(getf, Is.Not.Null, getfName);

            var (iKey, iFirst, iLast) = EnumRange(aug, aug.Services[geti.Id].Domains![1]);
            var (fKey, fFirst, fLast) = EnumRange(aug, aug.Services[getf!.Id].Domains![1]);
            Assert.That(fKey, Is.EqualTo(iKey), $"{geti.Name}/{getfName} select from one enumeration");
            Assert.That(iLast < fFirst || fLast < iFirst, Is.True, $"{geti.Name} [{iFirst}..{iLast}] and {getfName} [{fFirst}..{fLast}] overlap");

            List<string> list = aug.EnumsData.Enums[iKey];
            var admitted = Enumerable.Range(iFirst, iLast - iFirst + 1).Concat(Enumerable.Range(fFirst, fLast - fFirst + 1)).ToList();
            Assert.That(admitted.Select(i => list[i]).Where(EnumDef.IsFiller), Is.Empty, $"{iKey}: no filler admitted");

            // Together they admit every real value of the enumeration, integers to geti, floats to getf.
            var real = Enumerable.Range(0, list.Count).Where(i => !EnumDef.IsFiller(list[i])).ToList();
            Assert.That(admitted.Order(), Is.EqualTo(real), $"{iKey}: every property readable by exactly one getter");
            Assert.That(iLast, Is.LessThan(PropertyRanges.FirstFloat));
            Assert.That(fFirst, Is.GreaterThanOrEqualTo(PropertyRanges.FirstFloat));
        }
    }

    [Test]
    public void Augmentation_Refuses_An_Id_Already_In_The_Stock_File()
    {
        var clash = XCatalog.Catalog.WithService(Svc.PartsInfo, s => s with { Id = 1 });
        var e = Assert.Throws<InvalidOperationException>(() => TableAugmenter.Build(TableAugmenter.OpenStock, clash));
        Assert.That(e!.Message, Does.Contain("already has id 1"));
    }

    [Test]
    public void Loader_Rejection_Propagates()
    {
        // A domain naming no enumeration: the game's loader rejects the whole table.
        var broken = XCatalog.Catalog.WithService(Svc.LightGeti,
            s => s with { Params = [s.Params[0], s.Params[1] with { Domain = "enum:x_nope" }] });
        var e = Assert.Throws<GameTablesException>(() => TableAugmenter.Build(TableAugmenter.OpenStock, broken));
        Assert.That(e!.Message, Does.Contain("x_light_geti").And.Contain("enum:x_nope"));
    }

    // ------------------------------------------------------------------ helpers (game types allowed in signatures)

    private static string Describe(ServiceInfo s) =>
        string.Join(" | ", s.Id, s.Name, s.Kind, string.Join(",", s.ArgTypes), s.ArgCells, s.ResultCells, s.CostClass, s.PerValue,
            s.RequiresStock ?? "null", string.Join(",", s.Domains ?? []), string.Join(",", s.ArgumentOf ?? []),
            string.Join(",", s.ArgumentNames ?? []), s.Summary);

    private static (string Key, int First, int Last) EnumRange(GameTables tables, string domain)
    {
        var range = tables.EnumDomain(domain);
        Assert.That(range, Is.Not.Null, domain);
        return (range!.Value.Name, range.Value.First, range.Value.Last);
    }

    private static string DescribeEnumDomain(GameTables tables, string domain)
    {
        var (key, first, last) = EnumRange(tables, domain);
        return $"{key} {first}..{last}";
    }
}
