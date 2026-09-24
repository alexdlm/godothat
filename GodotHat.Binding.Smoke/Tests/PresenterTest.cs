namespace GodotHat.Binding.Smoke.Tests;

using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AwesomeAssertions;
using GdUnit4;
using Godot;
using GodotHat.Binding.Smoke.ViewModels;
using static Mount;

[TestSuite]
[RequireGodotRuntime]
public class PresenterTest
{
    private static PackedScene Scene(Node root)
    {
        var scene = new PackedScene();
        foreach (Node child in root.GetChildren())
        {
            child.Owner = root;
        }

        scene.Pack(root);
        root.Free();
        return scene;
    }

    // A page rooted at a BindingRoot, which receives the view model as its Context
    private static PackedScene BoundPage() =>
        Scene(new BindingRoot { Name = "Page" }.With(new Label { Name = "Title" }.Bind("text", Def("Title"))));

    // A page with no binding root of its own, bound with the view model as its context
    private static PackedScene PlainPage() => Scene(new Label { Name = "Page" }.Bind("text", Def("Title")));

    [TestCase]
    public async Task SwapsPagesAndDisposesOwnedViewModels()
    {
        using var errors = new ErrorLog();
        using var shell = new ShellViewModel();
        var presenter = new ContentPresenter { OwnsContent = true, Template = PlainPage() }.Bind(BindingRootBase.ContextKey, Def("Page"));
        presenter.Views[typeof(HomePageViewModel).FullName!] = BoundPage();
        var root = await AddToTree(new BindingRoot { Context = shell }.With(presenter));
        presenter.Content.Should().BeNull();

        var home = new HomePageViewModel();
        shell.Page.Value = home;
        var homePage = presenter.Content.Should().BeOfType<BindingRoot>().Subject;
        homePage.GetNode<Label>("Title").Text.Should().Be("Home");

        var about = new AboutPageViewModel();
        shell.Page.Value = about;
        home.IsDisposed.Should().BeTrue("the presenter owns its content");
        GodotObject.IsInstanceValid(homePage).Should().BeTrue("the old page is freed at the end of the frame");
        presenter.Content.Should().BeOfType<Label>().Which.Text.Should().Be("About");
        presenter.GetChildCount().Should().Be(1);

        shell.Page.Value = null;
        about.IsDisposed.Should().BeTrue();
        presenter.Content.Should().BeNull();
        errors.Errors.Should().BeEmpty();
        await Unmount(root);
    }

    [TestCase]
    public async Task UnownedContentIsNotDisposedAndUnbindingClearsIt()
    {
        using var shell = new ShellViewModel();
        using var home = new HomePageViewModel();
        shell.Page.Value = home;
        var presenter = new ContentPresenter { Template = PlainPage() }.Bind(BindingRootBase.ContextKey, Def("Page"));
        var root = await AddToTree(new BindingRoot { Context = shell }.With(presenter));
        presenter.Content.Should().NotBeNull();

        shell.Page.Value = new AboutPageViewModel();
        home.IsDisposed.Should().BeFalse();

        MainTree.Root.RemoveChild(root);
        presenter.Content.Should().BeNull();
        root.Free();
        await Frames();
    }

    [TestCase]
    public async Task PresenterWithoutContextBindingReports()
    {
        using var errors = new ErrorLog();
        using var shell = new ShellViewModel();
        var root = await AddToTree(new BindingRoot { Context = shell }.With(new ContentPresenter { Template = PlainPage() }));

        errors.Errors.Should().ContainSingle().Which.Message.Should().Contain("needs an '@context' binding");
        await Unmount(root);
    }

    [TestCase]
    public async Task ViewModelsGetServicesFromJabWithoutJabOwningThem()
    {
        IViewModelActivator previous = GodotBinding.Activator;
        using var services = new SmokeServices();
        GodotBinding.Activator = new ServiceProviderViewModelActivator(services);
        try
        {
            WeakReference viewModel = await MountAndUnmountGreeting();
            await Frames(2);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            viewModel.IsAlive.Should().BeFalse("the Jab provider, still alive, holds no reference to the view model");
        }
        finally
        {
            GodotBinding.Activator = previous;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> MountAndUnmountGreeting()
    {
        var label = new Label().Bind("text", Def("Greeting"));
        var root = await AddToTree(new BindingRoot { ViewModelType = typeof(GreetingViewModel).FullName! }.With(label));

        label.Text.Should().Be("Hello, Godot");
        var vm = root.ViewModel.Should().BeOfType<GreetingViewModel>().Subject;

        await Unmount(root);
        vm.IsDisposed.Should().BeTrue("the binding root created it, so it disposes it");
        return new WeakReference(vm);
    }
}
