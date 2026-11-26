using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>The extension's status as the shared runtime installed it, and its live counters.</summary>
[TestFixture]
public sealed class StatusTests : RuntimeFixture
{
    [Test]
    public void Runtime_Installed_Through_The_Real_Path()
    {
        KfsExtensionStatus status = Runtime.Extension;
        Assert.That(status.IsActive, Is.True, status.Describe());
        Assert.That(KfsExtension.IsActive, Is.True);
        Assert.That(status.Tables, Is.EqualTo(KfsTablesPublication.Published), status.Describe());
        Assert.That(status.RequiredPatches, Is.EqualTo(KfsExtension.Patches.Where(p => p.Required).Select(p => p.Name)));
        Assert.That(status.ServiceCount, Is.EqualTo(XCatalog.Services.Count));
        Assert.That(IsPublishedInstance(Runtime), Is.True, "Runtime.Tables is what KfsIntegration.Tables returns");
    }

    [Test]
    public void Status_Counters_Reflect_Activity()
    {
        KfsExtensionCounters before = Runtime.Extension.Counters;
        using SimVehicle v = Vehicle(lights: 2);
        v.LoadKc(Kc.Ticked("""
                if (k == 1u) {
                    x_light_set_on(0u, 1u);
                    kfs_logf("n %u", x_light_count());
                }
            """));
        v.Run(0.5, 0.1);
        Assert.That(v.Parts.Light(0).On, Is.True, v.Dump);

        KfsExtensionStatus status = Runtime.Extension;
        KfsExtensionCounters c = status.Counters;
        Assert.That(c.Bindings, Is.GreaterThanOrEqualTo(1), status.Describe());
        Assert.That(c.Accepted, Is.GreaterThan(0), status.Describe());
        Assert.That(c.Answered, Is.GreaterThan(0), status.Describe());
        Assert.That(c.Applied, Is.GreaterThan(0), status.Describe());
        Assert.That(before.Bindings, Is.LessThan(c.Bindings), "the vehicle's binding is counted while it lives");

        string text = status.Describe();
        Assert.That(text.Split('\n'), Has.Length.GreaterThan(3), text);
        Assert.That(text, Does.StartWith(status.ToString() + "\n"));
        Assert.That(text, Does.Contain($"requests {c.Accepted} accepted"));
    }

    private static bool IsPublishedInstance(KfsRuntime runtime) =>
        ReferenceEquals(KSA.Kfs.KfsIntegration.Tables, runtime.Tables) && ReferenceEquals(KfsIntegrationTables.Read(), runtime.Tables);
}
