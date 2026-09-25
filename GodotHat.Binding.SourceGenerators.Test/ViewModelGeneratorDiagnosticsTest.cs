using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace GodotHat.Binding.SourceGenerators.Test;

public class ViewModelGeneratorDiagnosticsTest
{
    private static (ImmutableArray<Diagnostic> Diagnostics, Compilation Output) Generate(string source)
    {
        GeneratorHarness.Run(GeneratorHarness.Compile(source), out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        return (diagnostics, output);
    }

    private static Diagnostic Single(string source, string id)
    {
        var (diagnostics, output) = Generate(source);
        GeneratorHarness.ErrorsIn(output).Where(d => !d.Id.StartsWith("GHB")).Should().BeEmpty();
        return diagnostics.Should().ContainSingle(d => d.Id == id).Subject;
    }

    private static string LineOf(Diagnostic diagnostic, string source) =>
        source.Split('\n')[diagnostic.Location.GetLineSpan().StartLinePosition.Line].Trim();

    [Fact]
    public void GHB0001ReactiveMemberWithPublicSetter()
    {
        const string source = """
            using GodotHat.Binding;
            using R3;
            namespace Game;
            [ViewModel]
            public sealed class Vm
            {
                public ReactiveProperty<int> Settable { get; set; } = new();
                public ReactiveProperty<int> InitOnly { get; init; } = new();
                public ReactiveProperty<int> PrivateSetter { get; private set; } = new();
                public string Plain { get; set; } = "";
            }
            """;
        Diagnostic diagnostic = Single(source, "GHB0001");

        diagnostic.Severity.Should().Be(DiagnosticSeverity.Warning);
        diagnostic.GetMessage().Should().Be("'Vm.Settable' is a reactive member with a public setter; replacing it doesn't update existing bindings, so make it get-only");
        LineOf(diagnostic, source).Should().StartWith("public ReactiveProperty<int> Settable");
    }

    [Fact]
    public void GHB0002MemberWithoutVariantConversion()
    {
        const string source = """
            using System;
            using GodotHat.Binding;
            using R3;
            namespace Game;
            [ViewModel]
            public sealed class Vm
            {
                public ReactiveProperty<DateTime> When { get; } = new();
                public ReactiveProperty<int?> Optional { get; } = new();
                public ReactiveProperty<string> Fine { get; } = new("");
                public Subject<Unit> Pinged { get; } = new();
                public ReactiveCommand<DateTime> Schedule { get; } = new();
            }
            """;
        var (diagnostics, _) = Generate(source);

        diagnostics.Where(d => d.Id == "GHB0002").Select(d => d.GetMessage()).Should().Equal(
            "'Vm.When' has values of type DateTime, which can't be passed to Godot directly; bind it with a converter such as to_string or format",
            "'Vm.Optional' has values of type int?, which can't be passed to Godot directly; bind it with a converter such as to_string or format");
        diagnostics.Should().OnlyContain(d => d.Severity == DiagnosticSeverity.Info);
    }

    [Fact]
    public void GHB0003MembersDifferingOnlyByCase()
    {
        const string source = """
            using GodotHat.Binding;
            using R3;
            namespace Game;
            [ViewModel]
            public sealed class Vm
            {
                public ReactiveProperty<int> Score { get; } = new();
                public ReactiveProperty<int> score { get; } = new();
            }
            """;
        Diagnostic diagnostic = Single(source, "GHB0003");

        diagnostic.GetMessage().Should().Be("'Vm' has bindable members 'Score' and 'score' whose names differ only by case, which is easy to confuse in binding paths");
        LineOf(diagnostic, source).Should().Be("public sealed class Vm");
    }

    [Fact]
    public void GHB0004InaccessibleViewModel()
    {
        const string source = """
            using GodotHat.Binding;
            using R3;
            namespace Game;
            public sealed class Outer
            {
                [ViewModel]
                private sealed class Hidden
                {
                    public ReactiveProperty<int> Value { get; } = new();
                }
            }
            """;
        Diagnostic diagnostic = Single(source, "GHB0004");

        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.GetMessage().Should().Be("View model 'Hidden' must be accessible from its assembly, not private or protected, for its accessor to be generated");
        LineOf(diagnostic, source).Should().Be("private sealed class Hidden");
    }

    [Fact]
    public void GHB0005GenericViewModel()
    {
        const string source = """
            using GodotHat.Binding;
            using R3;
            namespace Game;
            [ViewModel]
            public class ListViewModel<T>
            {
                public ReactiveProperty<T?> Selected { get; } = new();
            }
            """;
        var (diagnostics, output) = Generate(source);

        Diagnostic diagnostic = diagnostics.Should().ContainSingle().Subject;
        diagnostic.Id.Should().Be("GHB0005");
        diagnostic.GetMessage().Should().Be("View model 'ListViewModel' is generic, so no accessor is generated; mark a non-generic subclass [ViewModel] instead");
        output.SyntaxTrees.Should().ContainSingle("nothing is generated");
    }

    [Fact]
    public void GHB0006ViewList()
    {
        const string source = """
            using GodotHat.Binding;
            using ObservableCollections;
            namespace Game;
            [ViewModel]
            public sealed class Vm
            {
                public ObservableList<int> Numbers { get; } = new();
                public ISynchronizedViewList<int> NumberList { get; } = null!;
            }
            """;
        Diagnostic diagnostic = Single(source, "GHB0006");

        diagnostic.GetMessage().Should().Be("'Vm.NumberList' is a view list, which can't be bound as items; expose the collection or its ISynchronizedView instead");
        LineOf(diagnostic, source).Should().StartWith("public ISynchronizedViewList<int> NumberList");
    }

    [Fact]
    public void ValidViewModelHasNoDiagnostics()
    {
        const string source = """
            using GodotHat.Binding;
            using R3;
            namespace Game;
            [ViewModel]
            public sealed class Vm
            {
                public ReactiveProperty<int> Value { get; } = new();
                public ReactiveCommand Go { get; } = new();
            }
            """;
        var (diagnostics, output) = Generate(source);

        diagnostics.Should().BeEmpty();
        GeneratorHarness.ErrorsIn(output).Should().BeEmpty();
    }
}
