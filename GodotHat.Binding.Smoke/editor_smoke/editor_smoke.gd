@tool
extends EditorPlugin
## Drives the binding editor tooling in a real, headless editor, then quits with a non-zero exit code on failure:
## GODOTHAT_EDITOR_SMOKE=1 godot --headless -e --path GodotHat.Binding.Smoke

const EDITOR_DIR := "res://addons/godothat_binding/editor/"

var _failures := 0


func _ready() -> void:
	if OS.get_environment("GODOTHAT_EDITOR_SMOKE") != "1":
		return
	await _frames(5)
	await _run()
	print("EDITOR SMOKE " + ("PASSED" if _failures == 0 else "FAILED: %d" % _failures))
	get_tree().quit(1 if _failures > 0 else 0)


func _run() -> void:
	EditorInterface.open_scene_from_path("res://editor_smoke/Screen.tscn")
	await _frames(10)
	var root := EditorInterface.get_edited_scene_root()
	_check(root != null and root.name == "Screen", "the scene opens")
	if root == null:
		return

	var inspector_plugin: EditorInspectorPlugin = load(EDITOR_DIR + "BindingInspectorPlugin.cs").new()
	add_inspector_plugin(inspector_plugin)
	var title: Label = root.get_node("Title")
	_check(inspector_plugin._can_handle(title), "the inspector handles Controls in the edited scene")

	EditorInterface.edit_node(title)
	await _frames(5)
	var rows := _scripted(EditorInterface.get_inspector(), "BindableProperty.cs")
	var names := rows.map(func(row): return String(row.get_edited_property()))
	_check(names.has("text"), "text has a link button")
	_check(names.has("visible"), "visible has a link button")
	_check(not names.has("anchor_left"), "anchor_left doesn't")
	_check(_scripted(EditorInterface.get_inspector(), "BindingsSection.cs").size() == 1, "the Bindings section is shown")

	var text_row = rows[names.find("text")]
	var link: Button = text_row.find_children("*", "Button", true, false).filter(func(b): return b.toggle_mode)[0]
	link.button_pressed = true
	_check(title.has_meta("godothat_bindings") and title.get_meta("godothat_bindings").has(&"text"), "linking adds a binding")
	var undo := EditorInterface.get_editor_undo_redo()
	undo.get_history_undo_redo(undo.get_object_history_id(title)).undo()
	_check(not title.has_meta("godothat_bindings"), "undo removes it")

	var dock: Control = load(EDITOR_DIR + "BindingsDock.cs").new()
	add_control_to_bottom_panel(dock, "Bindings")
	await _frames(2)
	dock.call("Refresh")
	var labels := dock.find_children("*", "Label", true, false)
	_check(labels.any(func(l): return l.text == "1 bindings, 1 errors, 0 warnings"), "the dock reports the broken binding")

	dock.call("Preview")
	await _frames(5)
	var windows := EditorInterface.get_base_control().find_children("*", "Window", false, false) \
		.filter(func(w): return w.title.begins_with("Binding preview"))
	_check(windows.size() == 1, "preview opens a window")
	if windows.size() == 1:
		var preview_root = windows[0].find_children("Screen", "", true, false)[0]
		_check(preview_root.IsBound, "the preview binds")
		windows[0].queue_free()

	remove_control_from_bottom_panel(dock)
	dock.queue_free()
	remove_inspector_plugin(inspector_plugin)


func _scripted(root: Node, script_file: String) -> Array:
	return root.find_children("*", "", true, false).filter(
		func(node): return node.get_script() != null and node.get_script().resource_path.ends_with(script_file))


func _check(condition: bool, what: String) -> void:
	if condition:
		print("ok: " + what)
	else:
		_failures += 1
		push_error("FAIL: " + what)


func _frames(count: int) -> void:
	for i in count:
		await get_tree().process_frame
