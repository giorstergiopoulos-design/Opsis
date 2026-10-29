using LibreHardwareMonitor.Hardware;
using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

public sealed class GpuAdapterOrderTests
{
    [Fact]
    public void DedicatedAdaptersComeBeforeIntegratedAndOthersAreIgnored()
    {
        var list = GpuMonitorService.Adapters(new (HardwareType, string)[]
        {
            (HardwareType.Cpu, "CPU"), (HardwareType.GpuIntel, "UHD 630"), (HardwareType.GpuNvidia, "RTX"), (HardwareType.GpuAmd, "Radeon"),
        });
        Assert.Equal(new[] { "RTX", "Radeon", "UHD 630" }, list.Select(x => x.Name));
    }

    [Fact]
    public void NoAdaptersGivesEmptyList() =>
        Assert.Empty(GpuMonitorService.Adapters(new (HardwareType, string)[] { (HardwareType.Memory, "RAM") }));

    [Fact]
    public void SingleIntegratedAdapterIsKept() =>
        Assert.Single(GpuMonitorService.Adapters(new (HardwareType, string)[] { (HardwareType.GpuIntel, "UHD 630") }));
}
