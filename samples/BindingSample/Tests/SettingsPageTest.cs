namespace BindingSample.Tests;

using System;
using System.Threading.Tasks;
using BindingSample.ViewModels;
using GdUnit4;
using Godot;
using GodotHat.Binding.Testing;
using static GdUnit4.Assertions;

// Mounts the settings page with a view model the test creates, then uses it as a player would.
[TestSuite]
[RequireGodotRuntime]
public class SettingsPageTest
{
    private const string Scene = "res://Settings.tscn";

    [TestCase]
    public async Task ShowsTheViewModel()
    {
        using var vm = new SettingsViewModel(new FakeSettingsStore(), TimeProvider.System);
        await using BindingHarness ui = await BindingHarness.Mount(Scene, vm);

        ui.Bound<Label>("Summary").AssertText("Ada: Normal, volume 70%");
        ui.Bound<Label>("Volume").AssertText("70%");
        ui.Bound<OptionButton>("Difficulty").AssertSelected("Normal");
        ui.Bound<Button>("Save").AssertDisabled();
        ui.Items("History").AssertCount(0);
        ui.AssertNoBindingErrors();
    }

    [TestCase]
    public async Task EditsUpdateTheViewModel()
    {
        using var vm = new SettingsViewModel(new FakeSettingsStore(), TimeProvider.System);
        await using BindingHarness ui = await BindingHarness.Mount(Scene, vm);

        await ui.Bound<LineEdit>("PlayerName").Type("Grace");
        await ui.Bound<OptionButton>("Difficulty").Select("Brutal");
        await ui.Bound<HSlider>("Volume").SetValue(30);
        await ui.Bound<CheckBox>("Fullscreen").SetPressed();

        AssertThat(vm.PlayerName.Value).IsEqual("Grace");
        AssertThat(vm.Fullscreen.Value).IsTrue();
        ui.Bound<Label>("Summary").AssertText("Grace: Brutal, volume 30%");
        ui.Bound<Button>("Save").AssertEnabled();

        await ui.Bound<Button>("ResetName").Press();
        ui.Bound<LineEdit>("PlayerName").AssertText("Ada");
        ui.AssertNoBindingErrors();
    }

    [TestCase]
    public async Task SavingShowsStatusAndHistory()
    {
        var store = new FakeSettingsStore();
        using var vm = new SettingsViewModel(store, TimeProvider.System);
        await using BindingHarness ui = await BindingHarness.Mount(Scene, vm);
        await ui.Bound<LineEdit>("PlayerName").Type("Grace");

        await ui.Bound<Button>("Save").Press();

        // Saving finishes on another thread, and its updates arrive on the main thread
        await ui.WaitUntil(() => ui.Bound<Label>("Status").Control.Text == "Saved 1 time(s)");
        AssertThat(store.Saved).ContainsExactly("Grace: Normal, volume 70%");
        ui.Bound<Label>("Saves").AssertText("Saves: 1");
        ui.Bound<Button>("Save").AssertDisabled();
        ui.Items("History").AssertCount(1).Row(0).Bound<Label>("Title").AssertText("#1 Grace: Normal, volume 70%");
        ui.AssertNoBindingErrors();
    }

    [TestCase]
    public async Task StartsFromTheSampleData()
    {
        // The same data the editor previews the page with
        await using BindingHarness ui = await BindingHarness.Mount(Scene, sample: "res://Samples/settings_sample.tres");

        ui.Bound<LineEdit>("PlayerName").AssertText("Sam");
        ui.Bound<OptionButton>("Difficulty").AssertSelected("Brutal");
        ui.Items("History").AssertCount(2).Row(0).Bound<Label>("Title").AssertText("#2 Sam: save 2");
        ui.Bound<Button>("Save").AssertDisabled();
        ui.AssertNoBindingErrors();
    }
}
