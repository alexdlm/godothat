using Microsoft.CodeAnalysis;

namespace GodotHat.Binding.SourceGenerators.Test;

public class ViewModelGeneratorSnapshotTest
{
    private const string AllMemberKinds = """
        using System.Collections.Generic;
        using GodotHat.Binding;
        using ObservableCollections;
        using R3;

        namespace Game.Ui;

        public enum Mood { Happy, Sad, @event }

        [ViewModel]
        public sealed class ChildViewModel
        {
            public ReactiveProperty<string> Label { get; } = new("");
        }

        [ViewModel]
        public sealed class ScreenViewModel
        {
            public ReactiveProperty<int> Count { get; } = new();
            public BindableReactiveProperty<float> Volume { get; } = new();
            public ReadOnlyReactiveProperty<bool> HasItems { get; } = null!;
            public IBindableReactiveProperty<string> Search { get; } = null!;
            public IReadOnlyBindableReactiveProperty<string> Status { get; } = null!;
            public SynchronizedReactiveProperty<long> Ticks { get; } = new();
            public Subject<string> Toasts { get; } = new();
            public Observable<Unit> Flashed { get; } = null!;
            public ReactiveCommand Clear { get; } = new();
            public ReactiveCommand<int> Remove { get; } = new();
            public ReactiveCommand<string, int> Parse { get; } = new(int.Parse);
            public ReactiveProperty<Mood> Mood { get; } = new();
            public ReactiveProperty<ChildViewModel?> Selected { get; } = new();
            public ChildViewModel Header { get; } = new();
            public ObservableList<ChildViewModel> Children { get; } = new();
            public IObservableCollection<Mood> Moods { get; } = new ObservableList<Mood>();
            public ObservableDictionary<string, int> Stock { get; } = new();
            public ISynchronizedView<ChildViewModel, string> Labels { get; } = null!;
            public string Title => "Screen";
            public int @class => 1;

            [NotBindable]
            public ReactiveProperty<int> Hidden { get; } = new();

            public List<int> NotBindableType { get; } = new();
            internal ReactiveProperty<int> NotPublic { get; } = new();
            public static ReactiveProperty<int> Static { get; } = new();
        }
        """;

    private static SettingsTask VerifyGenerated(string source)
    {
        GeneratorDriver driver = GeneratorHarness.Run(GeneratorHarness.Compile(source), out Compilation output, out _);
        GeneratorHarness.ErrorsIn(output).Should().BeEmpty("generated code must compile");
        return Verify(driver).UseDirectory("Snapshots");
    }

    [Fact]
    public Task EmitsAccessorsForEveryMemberKind() => VerifyGenerated(AllMemberKinds);

    [Fact]
    public Task InheritedMembersComeFirst()
    {
        const string source = """
            using GodotHat.Binding;
            using R3;

            namespace Game;

            public abstract class ScreenBase
            {
                public ReactiveProperty<string> Title { get; } = new("");
                public virtual ReadOnlyReactiveProperty<bool> Busy { get; } = null!;
            }

            [ViewModel]
            public class PageViewModel : ScreenBase
            {
                public ReactiveProperty<int> Page { get; } = new();
                public override ReadOnlyReactiveProperty<bool> Busy { get; } = null!;
            }

            [ViewModel]
            public sealed class DetailPageViewModel : PageViewModel
            {
                public ReactiveProperty<string> Detail { get; } = new("");
            }

            [ViewModel]
            public sealed class DerivesFromViewModelBase : ViewModelBase
            {
                public ReactiveProperty<int> Value { get; } = new();
            }
            """;
        return VerifyGenerated(source);
    }

    [Fact]
    public Task ConstructorsResolveServices()
    {
        const string source = """
            using System;
            using GodotHat.Binding;
            using R3;

            namespace Game;

            public interface IClock;
            public interface ILog;

            [ViewModel]
            public sealed class ClockViewModel
            {
                public ClockViewModel() : this(null!, null, null!) { }

                public ClockViewModel(IClock clock, ILog? log, IServiceProvider services, int retries = 3)
                {
                }

                public ReactiveProperty<int> Ticks { get; } = new();
            }
            """;
        return VerifyGenerated(source);
    }

    [Fact]
    public Task RecordViewModels()
    {
        const string source = """
            using GodotHat.Binding;

            namespace Game;

            [ViewModel]
            public sealed record Tag(string Label, int Uses);

            [ViewModel]
            public record class Entry
            {
                public required string Title { get; init; }
            }
            """;
        return VerifyGenerated(source);
    }

    [Fact]
    public Task NestedAndInternalViewModels()
    {
        const string source = """
            using GodotHat.Binding;
            using R3;

            namespace Game;

            public static partial class Screens
            {
                [ViewModel]
                internal sealed class Inventory
                {
                    public ReactiveProperty<int> Gold { get; } = new();

                    public Inventory(int gold) => Gold.Value = gold;
                }
            }

            [ViewModel]
            public abstract class AbstractViewModel
            {
                public ReactiveProperty<int> Value { get; } = new();
            }
            """;
        return VerifyGenerated(source);
    }
}
