namespace GodotHat.Binding.Testing;

/// <summary>A failed check in a binding test, which test frameworks report as a failure with its message.</summary>
public sealed class BindingAssertionException(string message) : Exception(message);
