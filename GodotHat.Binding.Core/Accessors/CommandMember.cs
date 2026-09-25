using System.Windows.Input;

namespace GodotHat.Binding;

/// <summary>A command member taking <typeparamref name="TParam"/>, eg an R3 <c>ReactiveCommand&lt;TParam&gt;</c>.</summary>
public abstract class CommandMember<TParam> : MemberAccessor
{
    /// <summary>Creates the member.</summary>
    protected CommandMember(string name) : base(name, MemberKind.Command, typeof(TParam))
    {
    }

    /// <summary>The command on <paramref name="owner"/>, for its availability.</summary>
    public abstract ICommand GetCommand(object owner);

    /// <summary>Executes the command on <paramref name="owner"/>, without checking availability.</summary>
    public abstract void Execute(object owner, TParam parameter);

    /// <inheritdoc />
    public override TResult Accept<TResult>(IMemberVisitor<TResult> visitor) => visitor.VisitCommand(this);
}

/// <summary>
/// A command member accessed through delegates. Availability comes from <see cref="ICommand.CanExecute"/> and
/// <see cref="ICommand.CanExecuteChanged"/>; execution uses the typed delegate to avoid boxing the parameter.
/// </summary>
/// <example>
/// <code>
/// new CommandMember&lt;QueueViewModel, Unit&gt;("Clear", static vm => vm.Clear, static (vm, p) => vm.Clear.Execute(p))
/// </code>
/// </example>
public sealed class CommandMember<TOwner, TParam> : CommandMember<TParam>
    where TOwner : class
{
    private readonly Func<TOwner, ICommand> command;
    private readonly Action<TOwner, TParam> execute;

    /// <summary>Creates the member.</summary>
    public CommandMember(string name, Func<TOwner, ICommand> command, Action<TOwner, TParam> execute) : base(name)
    {
        this.command = command;
        this.execute = execute;
    }

    /// <inheritdoc />
    public override ICommand GetCommand(object owner) => this.command((TOwner)owner);

    /// <inheritdoc />
    public override void Execute(object owner, TParam parameter) => this.execute((TOwner)owner, parameter);
}
