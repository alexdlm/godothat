namespace BindingSample.ViewModels;

// Where settings are saved. The game provides the real one; tests and sample data provide fakes.
public interface ISettingsStore
{
    Task SaveAsync(string settings);
}
