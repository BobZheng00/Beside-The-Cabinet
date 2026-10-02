extends RefCounted
## Loads a .vrm file while the app is running, the way the editor importer does (addons/vrm/import_vrm.gd and
## plugin.gd), so a character can come from a folder on disk instead of being built into the app.

const Vrm0 = preload("res://addons/vrm/vrm_extension.gd")
const Vrm1 = [
	preload("res://addons/vrm/1.0/VRMC_vrm.gd"),
	preload("res://addons/vrm/1.0/VRMC_node_constraint.gd"),
	preload("res://addons/vrm/1.0/VRMC_springBone.gd"),
	preload("res://addons/vrm/1.0/VRMC_materials_hdr_emissiveMultiplier.gd"),
	preload("res://addons/vrm/1.0/VRMC_materials_mtoon.gd"),
]
const IMPORT_USE_NAMED_SKIN_BINDS := 16


static func load_vrm(path: String) -> Node:
	var extensions: Array[GLTFDocumentExtension] = []
	for script in Vrm1:
		extensions.append(script.new())
	for extension in extensions:
		GLTFDocument.register_gltf_document_extension(extension)
	var vrm0: GLTFDocumentExtension = Vrm0.new()
	GLTFDocument.register_gltf_document_extension(vrm0, true)
	extensions.append(vrm0)

	var document := GLTFDocument.new()
	var state := GLTFState.new()
	state.set_additional_data(&"vrm/head_hiding_method", 0)
	state.set_additional_data(&"vrm/first_person_layers", 2)
	state.set_additional_data(&"vrm/third_person_layers", 4)
	state.handle_binary_image = GLTFState.HANDLE_BINARY_EMBED_AS_UNCOMPRESSED
	var scene: Node = null
	if document.append_from_file(path, state, IMPORT_USE_NAMED_SKIN_BINDS) == OK:
		scene = document.generate_scene(state)

	for extension in extensions:
		GLTFDocument.unregister_gltf_document_extension(extension)
	return scene
