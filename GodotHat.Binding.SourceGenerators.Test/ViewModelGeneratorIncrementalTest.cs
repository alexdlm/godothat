using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GodotHat.Binding.SourceGenerators.Test;

public class ViewModelGeneratorIncrementalTest
{
    private const string ViewModel = """
        using GodotHat.Binding;
        using R3;
        namespace Game;
        [ViewModel]
        public sealed class Vm
        {
            public ReactiveProperty<int> Value { get; } = new();
        }
        """;

    private static IEnumerable<IncrementalStepRunReason> Reasons(GeneratorDriver driver)
    {
        GeneratorRunResult result = driver.GetRunResult().Results.Single();
        return result.TrackedSteps[ViewModelGenerator.TrackingName]
            .SelectMany(step => step.Outputs)
            .Concat(result.TrackedOutputSteps.SelectMany(kv => kv.Value).SelectMany(step => step.Outputs))
            .Select(output => output.Reason);
    }

    [Fact]
    public void UnrelatedEditsAreCached()
    {
        CSharpCompilation compilation = GeneratorHarness.Compile(ViewModel);
        GeneratorDriver driver = GeneratorHarness.Run(compilation, out _, out _);

        CSharpCompilation edited = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace Game; public class Unrelated {}"));
        driver = driver.RunGenerators(edited);

        Reasons(driver).Should().NotBeEmpty().And.OnlyContain(r => r == IncrementalStepRunReason.Cached || r == IncrementalStepRunReason.Unchanged);
    }

    [Fact]
    public void EditsToTheViewModelRegenerate()
    {
        CSharpCompilation compilation = GeneratorHarness.Compile(ViewModel);
        GeneratorDriver driver = GeneratorHarness.Run(compilation, out _, out _);

        SyntaxTree old = compilation.SyntaxTrees.Single();
        SyntaxTree changed = old.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From(
            ViewModel.Replace("ReactiveProperty<int> Value", "ReactiveProperty<long> Value"),
            System.Text.Encoding.UTF8));
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(old, changed));

        Reasons(driver).Should().Contain(IncrementalStepRunReason.Modified);
    }
}
