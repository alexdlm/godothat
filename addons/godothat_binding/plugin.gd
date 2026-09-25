@tool
extends EditorPlugin
## A thin shell for the C# editor tooling in editor/, so the plugin stays enabled when the C# project hasn't been built
## yet (Godot disables C# plugin scripts it can't load).

const EDITOR_DIR := "res://addons/godothat_binding/editor/"

var _inspector: EditorInspectorPlugin
var _dock: Control
var _dock_container: Node


func _enter_tree() -> void:
	var inspector_script: Script = load(EDITOR_DIR + "BindingInspectorPlugin.cs")
	var dock_script: Script = load(EDITOR_DIR + "BindingsDock.cs")
	if inspector_script == null or dock_script == null \
			or not inspector_script.can_instantiate() or not dock_script.can_instantiate():
		push_warning("GodotHat Binding: build the C# project, then disable and re-enable this plugin.")
		return

	_inspector = inspector_script.new()
	add_inspector_plugin(_inspector)

	_dock = dock_script.new()
	_dock.name = "Bindings"
	if ClassDB.class_exists("EditorDock"):
		_dock_container = ClassDB.instantiate("EditorDock")
		_dock_container.set("title", "Bindings")
		_dock_container.set("default_slot", 8) # the bottom panel
		_dock_container.add_child(_dock)
		call("add_dock", _dock_container)
	else:
		add_control_to_bottom_panel(_dock, "Bindings")

	scene_changed.connect(_on_scene_changed)


func _exit_tree() -> void:
	if scene_changed.is_connected(_on_scene_changed):
		scene_changed.disconnect(_on_scene_changed)
	if _inspector:
		remove_inspector_plugin(_inspector)
		_inspector = null
	if _dock_container:
		call("remove_dock", _dock_container)
		_dock_container.queue_free()
	elif _dock:
		remove_control_from_bottom_panel(_dock)
		_dock.queue_free()
	_dock = null
	_dock_container = null


func _on_scene_changed(_scene_root: Node) -> void:
	if _dock:
		_dock.call("Refresh")
