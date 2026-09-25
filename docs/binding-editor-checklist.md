# Binding editor manual checklist

The inspector UI is hard to automate. `GodotHat.Binding.Smoke/editor_smoke` covers the basics headlessly (link buttons,
linking and undo, the dock, previews); run through this list in the editor before each release, on the oldest
supported Godot (4.4) and the latest.

Open `samples/BindingSample` after `dotnet pack -o nupkgs && dotnet build samples/BindingSample`.

## Inspector

- [ ] Select a Label in `Settings.tscn`: the Bindings section is at the top of the inspector, listing its bindings.
- [ ] Hover a property such as `tooltip_text`: a link button appears; moving away hides it unless the property is bound.
- [ ] Layout properties (`anchor_*`, `offset_*`, `size_flags_*`) have no link button.
- [ ] Link `tooltip_text`: the row greys out the scene value and shows the binding; its editor opens below.
- [ ] Pick a path from the search menu: compatible members come first; incompatible ones are disabled; choosing
      `Saves` for a text property also selects the `format` or `to_string` converter.
- [ ] Change mode, converter and argument: each change updates the summary and is a separate undo step.
- [ ] Ctrl+Z / Ctrl+Shift+Z undo and redo linking, editing and unlinking.
- [ ] Unlink: the stock editor returns, with its revert arrow and keyframe button working as before.
- [ ] Bindings section → Add: `@context`, `@items` (with a Template picker on containers, item text and icon on
      ItemList/OptionButton/Tree), `@selected` on list controls, a command on each signal, an event method, a property.
- [ ] "All settings" opens the BindingDef resource, including its fallback.
- [ ] Saving and reopening the scene keeps every binding.

## Dock

- [ ] The Bindings panel lists every binding in the edited scene with its status; broken paths are red.
- [ ] Selecting a row selects its node.
- [ ] "Problems only" hides working bindings; Revalidate picks up renamed view model members after a build.
- [ ] Preview opens the scene with its bindings live, using the binding root's sample data, and closes cleanly.

## Robustness

- [ ] Rebuild the C# project while a Control is selected: the inspector rebuilds and still works; the dock still
      refreshes.
- [ ] On a fresh clone before building, enabling the plugin warns to build first rather than disabling it.
