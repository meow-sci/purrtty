using NUnit.Framework;

namespace purrTTY.Kfs.Tests;

[TestFixture]
public sealed class KfsBuildInfoTests
{
    [Test]
    public void KfsBuildInfo_Owns_Its_Block()
    {
        Assert.That(KfsBuildInfo.ServiceIdFirst, Is.EqualTo(0x5000));
        Assert.That(KfsBuildInfo.ServiceIdLast, Is.EqualTo(0x50FF));

        Assert.That(KfsBuildInfo.Owns(0x5000), Is.True);
        Assert.That(KfsBuildInfo.Owns(0x5042), Is.True);
        Assert.That(KfsBuildInfo.Owns(0x50FF), Is.True);

        Assert.That(KfsBuildInfo.Owns(0x4FFF), Is.False);
        Assert.That(KfsBuildInfo.Owns(0x5100), Is.False);
        Assert.That(KfsBuildInfo.Owns(0), Is.False);
        Assert.That(KfsBuildInfo.Owns(ushort.MaxValue), Is.False);
    }
}
