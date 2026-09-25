using AwesomeAssertions;
using GodotHat.Binding.Core.Test.Support;

namespace GodotHat.Binding.Core.Test;

public class ConverterTest
{
    private readonly ErrorCollector errors = new();
    private readonly BindingEngine engine;

    public ConverterTest()
    {
        this.engine = new BindingEngine(BindingDispatcher.Immediate, this.errors);
    }

    private FakeTarget Bind(object vm, string path, string converter, string? arg = null, BindingMode mode = BindingMode.OneWay)
    {
        var target = new FakeTarget();
        this.engine.BindProperty(
            DataContext.Constant(vm),
            new BindingSpec { Path = path, Converter = converter, ConverterArgument = arg, Mode = mode },
            target);
        return target;
    }

    [Fact]
    public void NotNegatesBothWays()
    {
        var vm = new PersonViewModel(age: 20);
        FakeTarget target = this.Bind(vm, "IsAdult", BuiltInConverters.NotId);
        target.Value.Should().Be(false);

        vm.Age.Value = 1;
        target.Value.Should().Be(true);
    }

    [Fact]
    public void InvertIsNot()
    {
        FakeTarget target = this.Bind(new PersonViewModel(age: 20), "IsAdult", BuiltInConverters.InvertId);
        target.Value.Should().Be(false);
    }

    [Fact]
    public void NotRejectsNonBool()
    {
        FakeTarget target = this.Bind(new PersonViewModel(), "Age", BuiltInConverters.NotId);
        target.Value.Should().Be("<fallback>");
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Contain("needs a bool");
    }

    [Fact]
    public void NotNullAndIsNullTreatMissingAsNull()
    {
        var vm = new PersonViewModel();
        FakeTarget notNull = this.Bind(vm, "Friend", BuiltInConverters.NotNullId);
        FakeTarget isNull = this.Bind(vm, "Friend", BuiltInConverters.IsNullId);
        FakeTarget nestedNotNull = this.Bind(vm, "Friend.Friend", BuiltInConverters.NotNullId);

        notNull.Value.Should().Be(false);
        isNull.Value.Should().Be(true);
        nestedNotNull.Value.Should().Be(false, "a missing value counts as null");

        vm.Friend.Value = new PersonViewModel();
        notNull.Value.Should().Be(true);
        isNull.Value.Should().Be(false);
    }

    [Fact]
    public void ToStringFormatsValues()
    {
        var vm = new PersonViewModel(age: 7);
        this.Bind(vm, "Age", BuiltInConverters.ToStringId).Value.Should().Be("7");
        this.Bind(vm, "Mood", BuiltInConverters.ToStringId).Value.Should().Be("Happy");
    }

    [Fact]
    public void FormatUsesArgument()
    {
        var vm = new PersonViewModel("Ada", 36);
        this.Bind(vm, "Age", BuiltInConverters.FormatId, "Age: {0:D3}").Value.Should().Be("Age: 036");
        this.Bind(vm, "Name", BuiltInConverters.FormatId).Value.Should().Be("Ada");
    }

    [Fact]
    public void FormatReportsBadFormatString()
    {
        this.Bind(new PersonViewModel(), "Age", BuiltInConverters.FormatId, "{oops");
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Contain("not a valid format string");
    }

    [Fact]
    public void EqualsComparesWithParsedArgument()
    {
        var vm = new PersonViewModel();
        FakeTarget sad = this.Bind(vm, "Mood", BuiltInConverters.EqualsId, "Sad", BindingMode.TwoWay);
        FakeTarget grumpy = this.Bind(vm, "Mood", BuiltInConverters.EqualsId, "Grumpy", BindingMode.TwoWay);
        sad.Value.Should().Be(false);

        vm.Mood.Value = Mood.Sad;
        sad.Value.Should().Be(true);

        grumpy.UserSets(true);
        vm.Mood.Value.Should().Be(Mood.Grumpy);
        sad.Value.Should().Be(false);

        grumpy.UserSets(false);
        vm.Mood.Value.Should().Be(Mood.Grumpy, "unchecking doesn't choose another value");
    }

    [Fact]
    public void EqualsWithNumbers()
    {
        this.Bind(new PersonViewModel(age: 3), "Age", BuiltInConverters.EqualsId, "3").Value.Should().Be(true);
    }

    [Fact]
    public void EqualsReportsUnparseableArgument()
    {
        this.Bind(new PersonViewModel(), "Age", BuiltInConverters.EqualsId, "three");
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Contain("is not a Int32");
    }

    [Fact]
    public void EnumNameConvertsBothWays()
    {
        var vm = new PersonViewModel();
        FakeTarget target = this.Bind(vm, "Mood", BuiltInConverters.EnumNameId, mode: BindingMode.TwoWay);
        target.Value.Should().Be("Happy");

        target.UserSets("Grumpy");
        vm.Mood.Value.Should().Be(Mood.Grumpy);

        target.UserSets("Unknown");
        vm.Mood.Value.Should().Be(Mood.Grumpy);
    }

    [Fact]
    public void EnumNameRejectsNonEnum()
    {
        this.Bind(new PersonViewModel(), "Age", BuiltInConverters.EnumNameId);
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Contain("needs an enum");
    }

    [Fact]
    public void UnknownConverterReports()
    {
        FakeTarget target = this.Bind(new PersonViewModel(), "Age", "nope");
        target.Value.Should().Be("<fallback>");
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Be("Unknown converter 'nope'.");
    }

    [Fact]
    public void OneWayConverterOnTwoWayBindingReports()
    {
        this.Bind(new PersonViewModel(), "Age", BuiltInConverters.FormatId, mode: BindingMode.TwoWay);
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Contain("can't convert back");
    }

    [Fact]
    public void CustomConverters()
    {
        var registry = BindingConverterRegistry.CreateWithBuiltIns();
        registry.Register(
            "double",
            BindingConverter.Create<int, int>(
                v => v * 2,
                (int doubled, out int value) =>
                {
                    value = doubled / 2;
                    return true;
                }));
        registry.Register("suffix", BindingConverter.Create<string, string>((v, arg) => v + arg));
        var engine = new BindingEngine(BindingDispatcher.Immediate, this.errors, registry);
        var vm = new PersonViewModel("Ada", 4);

        var doubled = new FakeTarget();
        engine.BindProperty(DataContext.Constant(vm), new BindingSpec { Path = "Age", Converter = "double", Mode = BindingMode.TwoWay }, doubled);
        var suffixed = new FakeTarget();
        engine.BindProperty(DataContext.Constant(vm), new BindingSpec { Path = "Name", Converter = "suffix", ConverterArgument = "!" }, suffixed);
        var wrongType = new FakeTarget();
        engine.BindProperty(DataContext.Constant(vm), new BindingSpec { Path = "Name", Converter = "double" }, wrongType);

        doubled.Value.Should().Be(8);
        doubled.UserSets(20);
        vm.Age.Value.Should().Be(10);
        suffixed.Value.Should().Be("Ada!");
        this.errors.Errors.Should().ContainSingle().Which.Message.Should().Be("Converter takes Int32, not String.");
    }
}
