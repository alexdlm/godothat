namespace GodotHat.Binding.Smoke.Tests;

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AwesomeAssertions;
using GdUnit4;
using Godot;
using GodotHat.Binding.Smoke.ViewModels;
using static Mount;

// Budgets are loose, to catch regressions of an order of magnitude rather than measure precisely.
[TestSuite]
[RequireGodotRuntime]
public class PerformanceTest
{
    private const int Labels = 1000;
    private const int Updates = 100;

    [TestCase]
    public async Task UpdatesToThousandLabelsStayWithinBudget()
    {
        using var vm = new FormViewModel();
        var container = new VBoxContainer();
        for (int i = 0; i < Labels; i++)
        {
            container.AddChild(new Label()
                .Bind("text", Def("Count", converter: "to_string"))
                .Bind("visible", Def("Enabled")));
        }

        var root = await AddToTree(new BindingRoot { Context = vm }.With(container));

        // Typed setters mean a bool reaches every label without allocating
        for (int i = 0; i < 10; i++)
        {
            vm.Enabled.Value = !vm.Enabled.Value;
        }

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Updates; i++)
        {
            vm.Enabled.Value = !vm.Enabled.Value;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        var watch = Stopwatch.StartNew();
        for (int i = 0; i < Updates; i++)
        {
            vm.Count.Value = i;
        }

        watch.Stop();
        GD.Print($"{Labels * Updates} label text updates in {watch.ElapsedMilliseconds} ms; bool updates allocated {allocated} bytes");

        allocated.Should().Be(0);
        watch.ElapsedMilliseconds.Should().BeLessThan(5000);
        ((Label)container.GetChild(Labels - 1)).Text.Should().Be($"{Updates - 1}");
        await Unmount(root);
    }
}
