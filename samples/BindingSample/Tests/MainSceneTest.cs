namespace BindingSample.Tests;

using System.Threading.Tasks;
using BindingSample.ViewModels;
using GdUnit4;
using Godot;
using GodotHat.Binding.Testing;
using static GdUnit4.Assertions;

// Mounts the whole game with fake services, to test navigating between its pages.
[TestSuite]
[RequireGodotRuntime]
public class MainSceneTest
{
    [TestCase]
    public async Task NavigatesBetweenPages()
    {
        using var services = new TestServices();
        await using BindingHarness ui = await BindingHarness.Mount("res://Main.tscn", services: services);
        BoundPresenter pages = ui.Presenter("Page");

        SettingsViewModel settings = pages.AssertShowing<SettingsViewModel>();
        pages.AssertScene("res://Settings.tscn").Bound<Label>("Summary").AssertText("Ada: Normal, volume 70%");
        ui.Bound<Button>("ShowSettings").AssertDisabled();

        await ui.Bound<Button>("ShowAbout").Press();
        pages.AssertShowing<AboutViewModel>();
        pages.Bound<Label>("Text").AssertText("GodotHat.Binding sample: pages are view models, shown by a ContentPresenter.");
        AssertThat(settings.IsDisposed).IsTrue();

        await ui.Bound<Button>("ShowSettings").Press();
        AssertThat(pages.AssertShowing<SettingsViewModel>()).IsNotSame(settings);
        ui.AssertNoBindingErrors();
    }
}
