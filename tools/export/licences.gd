# Writes Godot's licence and its third-party notices, as this Godot build reports them, to the file named after "--".
# Run by release.py: godot_console --headless --script licences.gd -- <file>
extends SceneTree


func _init() -> void:
	var out := FileAccess.open(OS.get_cmdline_user_args()[0], FileAccess.WRITE)
	out.store_string(Engine.get_license_text().strip_edges() + "\n\n")
	for component in Engine.get_copyright_info():
		out.store_string("Component: %s\n" % component["name"])
		for part in component["parts"]:
			out.store_string("  Files: %s\n" % ", ".join(part["files"]))
			for line in part["copyright"]:
				out.store_string("  Copyright: %s\n" % line)
			out.store_string("  License: %s\n" % part["license"])
		out.store_string("\n")
	var licences := Engine.get_license_info()
	for id in licences:
		out.store_string("License: %s\n\n%s\n\n" % [id, licences[id].strip_edges()])
	out.close()
	quit()
