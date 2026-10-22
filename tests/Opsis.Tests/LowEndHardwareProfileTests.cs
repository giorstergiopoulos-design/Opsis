using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

// Προσομοίωση προφίλ υλικού (v2.0.0): τι ταξινομεί το Opsis σε laptops διαφορετικών κατηγοριών.
// Καθαρή συνάρτηση, δεν αγγίζει ρυθμίσεις/registry/WMI.
public sealed class LowEndHardwareProfileTests
{
    [Theory]
    [InlineData(8192, 2, false)]  // Celeron/Pentium Silver 2c, 8GB, iGPU (laptop ~300€)
    [InlineData(8192, 4, false)]  // Ryzen 3 / i3 4 λογικοί, 8GB, iGPU (laptop ~400€)
    [InlineData(4096, 2, false)]  // 4GB εισαγωγικό
    [InlineData(4096, 4, false)]
    public void BudgetLaptops_AreLow(long ramMb, int cores, bool dgpu) =>
        Assert.Equal(HardwareTier.Low, HardwareProfileService.Classify(ramMb, cores, dgpu));

    [Fact]
    public void Budget8GB_WithDiscreteGpu_IsBalanced_NotHigh() =>
        Assert.Equal(HardwareTier.Balanced, HardwareProfileService.Classify(8192, 4, true));

    [Fact]
    public void Mainstream_16GB_6Cores_IsBalanced() =>
        Assert.Equal(HardwareTier.Balanced, HardwareProfileService.Classify(16384, 6, false));

    [Fact]
    public void Gaming_32GB_12Cores_dGPU_IsHigh() =>
        Assert.Equal(HardwareTier.High, HardwareProfileService.Classify(32768, 16, true));

    [Fact]
    public void MoreResources_NeverLowerTheTier()
    {
        HardwareTier prev = HardwareTier.Low;
        foreach (var (ram, cores, gpu) in new[] { (4096L, 2, false), (8192L, 2, false), (8192L, 6, false), (16384L, 6, false), (16384L, 12, true) })
        {
            var t = HardwareProfileService.Classify(ram, cores, gpu);
            Assert.True(t >= prev, $"{ram}MB/{cores}c/{gpu} -> {t} < {prev}");
            prev = t;
        }
    }

    [Fact]
    public void Low_Tier_RecommendsLowPower_WhenOnAC()
    {
        // Η σύσταση μόνο για Low επηρεάζεται από το tier σε ρεύμα· δεν ελέγχεται εδώ η μπαταρία.
        Assert.Equal(HardwareTier.Low, HardwareProfileService.Classify(8192, 2, false));
    }
}
