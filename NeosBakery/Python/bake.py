import json
import bpy
import os
import shutil
from math import pi

NeosBakeryPath = __file__.replace("bake.py", "")
NeosBakeryOutputPath = NeosBakeryPath + "Output\\"

MeshesPath = NeosBakeryPath + "Assets\\Meshes\\";
MaterialsPath = NeosBakeryPath + "Assets\\Materials\\";
TexturesPath = NeosBakeryPath + "Assets\\Textures\\";

bakeJob = json.load(open(NeosBakeryPath + "BakeJob.json"))

_scene = bpy.context.scene

_scene.tool_settings.transform_pivot_point = 'ACTIVE_ELEMENT'
_scene.render.image_settings.file_format = "PNG"
_scene.render.image_settings.color_mode = "RGBA"
_scene.world.use_nodes = True
worldOutputNode = _scene.world.node_tree.nodes["World Output"]
backgroundNode = _scene.world.node_tree.nodes.new(type="ShaderNodeBackground")
backgroundNode.inputs["Color"].default_value[0] = bakeJob["Skybox"]["PrimaryColor"][0]
backgroundNode.inputs["Color"].default_value[1] = bakeJob["Skybox"]["PrimaryColor"][1]
backgroundNode.inputs["Color"].default_value[2] = bakeJob["Skybox"]["PrimaryColor"][2]
skyTextureNode = _scene.world.node_tree.nodes.new(type="ShaderNodeTexImage")
if bakeJob["Skybox"]["Texture"] != -1:
    texturePath = TexturesPath + str(bakeJob["Skybox"]["Texture"]) + ".png"
    skyTextureNode.image = bpy.data.images.load(filepath=texturePath, check_existing=True).copy()
    _scene.world.node_tree.links.new(skyTextureNode.outputs["Color"], backgroundNode.inputs["Color"])
_scene.world.node_tree.links.new(backgroundNode.outputs["Background"], worldOutputNode.inputs["Surface"])

if bakeJob["BakeType"] == 0:
    _scene.render.bake.use_pass_direct = True
    _scene.render.bake.use_pass_indirect = True
if bakeJob["BakeType"] == 1:
    _scene.render.bake.use_pass_direct = True
    _scene.render.bake.use_pass_indirect = False
if bakeJob["BakeType"] == 2:
    _scene.render.bake.use_pass_direct = False
    _scene.render.bake.use_pass_indirect = True

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete()
_scene.render.engine = "CYCLES"

if bakeJob["BakeMethod"] == 1:
    bpy.context.scene.use_nodes = True
    compositorTree = bpy.context.scene.node_tree
    for node in compositorTree.nodes:
        compositorTree.nodes.remove(node)
    compositorNode = compositorTree.nodes.new(type="CompositorNodeComposite")
    outputNode = compositorTree.nodes.new(type="CompositorNodeOutputFile")
    tiledTextureNode = compositorTree.nodes.new(type="CompositorNodeTexture")
    compositorTree.links.new(tiledTextureNode.outputs[1], compositorNode.inputs[0])
    compositorTree.links.new(tiledTextureNode.outputs[1], outputNode.inputs[0])
    bpy.ops.texture.new()
    tiledTexture = bpy.data.textures["Texture"]
    tiledTextureNode.texture = tiledTexture

def exportTiledTexture(inputMap, outputDirectory, x_tiling, y_tiling, x_offset, y_offset, upscale):
    tiledTexture.image = inputMap
    outputNode.base_path = outputDirectory
    #tiledTexture.repeat_x = round(x_tiling)
    #tiledTexture.repeat_y = round(y_tiling)
    tiledTextureNode.inputs[0].default_value[0] = x_offset
    tiledTextureNode.inputs[0].default_value[1] = y_offset
    #I cannot find a solution to make the texture scale from the bottom left instead of the center. Whole number tiling it is.
    #https://blender.stackexchange.com/questions/250664/how-to-properly-scale-a-texture-from-the-bottom-left-in-the-compositor
    tiledTextureNode.inputs[1].default_value[0] = round(x_tiling)
    tiledTextureNode.inputs[1].default_value[1] = round(y_tiling)
    if upscale == True:
        x_resolution = round(clamp(inputMap.size[0] * x_tiling, 4, 4096))
        y_resolution = round(clamp(inputMap.size[1] * y_tiling, 4, 4096))
    else:
        x_resolution = inputMap.size[0]
        y_resolution = inputMap.size[1]
    bpy.context.scene.render.resolution_x = x_resolution
    bpy.context.scene.render.resolution_y = y_resolution
    bpy.ops.render.render(write_still=True)
    filename = os.listdir(outputDirectory)[0] #Get first file and name
    filepath = outputDirectory + filename #Combine 0\\Normal\\ + Image0001.png
    targetpath = os.path.dirname(outputDirectory) + os.path.basename(outputDirectory) + ".png" #Combine 0\\ + Normal + .png
    shutil.move(filepath, targetpath)
    os.rmdir(outputDirectory)
    return

def clamp(Val, Min, Max):
    return Min if Val < Min else Max if Val > Max else Val

for Light in bakeJob["BakeLights"]:
    lightType = "POINT"
    if Light["LightType"] == 0:
        lightType = "POINT"
    if Light["LightType"] == 1:
        lightType = "SUN"
    if Light["LightType"] == 2:
        lightType = "SPOT"

    bpy.ops.object.light_add(type=lightType)
    light_ob = bpy.context.object
    light = light_ob.data
    light.energy = Light["Watts"]
    light.color = (Light["Color"][0], Light["Color"][1], Light["Color"][2])
    light.use_shadow = Light["CastShadow"]
    if Light["LightType"] == 2:
        light.spot_size = Light["SpotAngle"]

    light_ob.location.x = Light["Transform"]["Position"][0]
    light_ob.location.z = Light["Transform"]["Position"][1]
    light_ob.location.y = Light["Transform"]["Position"][2]

    light_ob.rotation_quaternion.w = Light["Transform"]["Rotation"][3]
    light_ob.rotation_quaternion.x = Light["Transform"]["Rotation"][0]
    light_ob.rotation_quaternion.z = -Light["Transform"]["Rotation"][1]
    light_ob.rotation_quaternion.y = Light["Transform"]["Rotation"][2]

    light_ob.scale.x = Light["Transform"]["Scale"][0]
    light_ob.scale.z = Light["Transform"]["Scale"][1]
    light_ob.scale.y = Light["Transform"]["Scale"][2]

objectsToBake = []
bakeNodes = []
bakedTextures_MaterialIndex = []
bakedTextures_RendererIndex = []

for bakeObject in bakeJob["BakeObjects"]:
    meshPath = MeshesPath + str(bakeObject["Renderer"]["Mesh"]) + ".gltf"
    bpy.ops.import_scene.gltf(filepath=meshPath, import_pack_images=False)
    meshObj = bpy.context.selected_objects[0]
    bpy.ops.object.select_all(action="DESELECT")
    meshObj.select_set(True)
    objectsToBake.append(meshObj)
        
    if bakeJob["BakeMethod"] == 0:
        bpy.ops.object.editmode_toggle()
        size = clamp(bakeJob["DefaultResolution"], 64, 4096)
        bpy.ops.uv.lightmap_pack(PREF_CONTEXT="ALL_FACES", PREF_PACK_IN_ONE=True, PREF_NEW_UVLAYER=True, PREF_IMG_PX_SIZE=size, PREF_BOX_DIV=48, PREF_MARGIN_DIV=0.02)
        bpy.ops.object.editmode_toggle()

    meshObj.location.x = bakeObject["Transform"]["Position"][0]
    meshObj.location.z = bakeObject["Transform"]["Position"][1]
    meshObj.location.y = bakeObject["Transform"]["Position"][2]

    meshObj.rotation_mode = "QUATERNION"
    meshObj.rotation_quaternion.w = bakeObject["Transform"]["Rotation"][3]
    meshObj.rotation_quaternion.x = bakeObject["Transform"]["Rotation"][0]
    meshObj.rotation_quaternion.z = -bakeObject["Transform"]["Rotation"][1]
    meshObj.rotation_quaternion.y = bakeObject["Transform"]["Rotation"][2]

    meshObj.scale.x = bakeObject["Transform"]["Scale"][0]
    meshObj.scale.z = bakeObject["Transform"]["Scale"][1]
    meshObj.scale.y = bakeObject["Transform"]["Scale"][2]

    materialIndex = 0
    for materialID in bakeObject["Renderer"]["Materials"]:
        materialPath = MaterialsPath + str(materialID) + ".json"
        _material = json.load(open(materialPath))
        meshObj.active_material_index = materialIndex
        currentMaterial = meshObj.active_material
        currentMaterial.use_nodes = True
        nodetree = currentMaterial.node_tree
        nodes = currentMaterial.node_tree.nodes
        inputs = nodes["Principled BSDF"].inputs
        inputs["Base Color"].default_value[0] = _material["AlbedoColor"][0]
        inputs["Base Color"].default_value[1] = _material["AlbedoColor"][1]
        inputs["Base Color"].default_value[2] = _material["AlbedoColor"][2]
        inputs["Metallic"].default_value = _material["Metallic"]
        inputs["Roughness"].default_value = 1 - _material["Smoothness"]
        inputs["Emission"].default_value[0] = _material["EmissiveColor"][0]
        inputs["Emission"].default_value[1] = _material["EmissiveColor"][1]
        inputs["Emission"].default_value[2] = _material["EmissiveColor"][2]

        bakeNode = nodes.new(type="ShaderNodeTexImage")
        bakeNode.label = "BakeTexture"
        textureName = str(bakeObject["REFID"]) + "_" + str(materialIndex) + "_Albedo"
        if _material["Textures"][0] == -1:
            if bakeJob["Upscale"] == True:
                x_res = round(clamp(bakeJob["DefaultResolution"] * _material["TextureScale"][0], 64, 4096))
                y_res = round(clamp(bakeJob["DefaultResolution"] * _material["TextureScale"][1], 64, 4096))
                bakeNode.image = bpy.data.images.new(name=textureName, width=x_res, height=y_res)
            else:
                bakeNode.image = bpy.data.images.new(name=textureName, width=bakeJob["DefaultResolution"], height=bakeJob["DefaultResolution"])
        else:
            if bakeJob["Upscale"] == True:
                texturePath = TexturesPath + str(_material["Textures"][0]) + ".png"
                bakeNodeImage = bpy.data.images.load(filepath=texturePath, check_existing=True).copy()
                x_res = round(clamp(bakeNodeImage.size[0] * _material["TextureScale"][0], 64, 4096))
                y_res = round(clamp(bakeNodeImage.size[1] * _material["TextureScale"][1], 64, 4096))
                bakeNode.image = bpy.data.images.new(name=textureName, width=x_res, height=y_res)
            else:
                texturePath = TexturesPath + str(_material["Textures"][0]) + ".png"
                bakeNode.image = bpy.data.images.load(filepath=texturePath, check_existing=True).copy()
        bakeNodes.append(bakeNode)
        bakedTextures_MaterialIndex.append(materialIndex)
        bakedTextures_RendererIndex.append(bakeObject["REFID"])

        textureCoordNode = nodes.new(type="ShaderNodeTexCoord")
        mappingNode = nodes.new(type="ShaderNodeMapping")
        mappingNode.inputs["Scale"].default_value[0] = round(_material["TextureScale"][0])
        mappingNode.inputs["Scale"].default_value[1] = round(_material["TextureScale"][1])
        mappingNode.inputs["Location"].default_value[0] = _material["TextureOffset"][0]
        mappingNode.inputs["Location"].default_value[1] = _material["TextureOffset"][1]
        nodetree.links.new(textureCoordNode.outputs["UV"], mappingNode.inputs["Vector"])
        if bakeJob["BakeMethod"] == 1:
            if _material["Textures"][0] != -1:
                albedoNode = nodes.new(type="ShaderNodeTexImage")
                albedoNode.label = "AlbedoTexture"
                texturePath = TexturesPath + str(_material["Textures"][0]) + ".png"
                albedoNode.image = bpy.data.images.load(filepath=texturePath, check_existing=True)
                mixRGBNode = nodes.new(type="ShaderNodeMixRGB")
                mixRGBNode.blend_type = "MULTIPLY"
                mixRGBNode.inputs[0].default_value = 1.0
                mixRGBNode.inputs[2].default_value[0] = _material["AlbedoColor"][0]
                mixRGBNode.inputs[2].default_value[1] = _material["AlbedoColor"][1]
                mixRGBNode.inputs[2].default_value[2] = _material["AlbedoColor"][2]
                nodetree.links.new(mappingNode.outputs["Vector"], albedoNode.inputs["Vector"])
                nodetree.links.new(albedoNode.outputs["Color"], mixRGBNode.inputs[1])
                nodetree.links.new(mixRGBNode.outputs["Color"], inputs["Base Color"])
            if _material["Textures"][1] != -1:
                emissiveNode = nodes.new(type="ShaderNodeTexImage")
                emissiveNode.label = "EmissiveTexture"
                texturePath = TexturesPath + str(_material["Textures"][1]) + ".png"
                emissiveNode.image = bpy.data.images.load(filepath=texturePath, check_existing=True)
                mixRGBNode = nodes.new(type="ShaderNodeMixRGB")
                mixRGBNode.blend_type = "MULTIPLY"
                mixRGBNode.inputs[0].default_value = 1.0
                mixRGBNode.inputs[2].default_value[0] = _material["EmissiveColor"][0]
                mixRGBNode.inputs[2].default_value[1] = _material["EmissiveColor"][1]
                mixRGBNode.inputs[2].default_value[2] = _material["EmissiveColor"][2]
                nodetree.links.new(mappingNode.outputs["Vector"], emissiveNode.inputs["Vector"])
                nodetree.links.new(emissiveNode.outputs["Color"], mixRGBNode.inputs[1])
                nodetree.links.new(mixRGBNode.outputs["Color"], inputs["Emission"])
                outDir = NeosBakeryOutputPath + str(bakeObject["REFID"]) + "\\Materials\\" + str(materialIndex) + "\\Emissive\\"
                exportTiledTexture(emissiveNode.image, outDir, _material["TextureScale"][0], _material["TextureScale"][1], _material["TextureOffset"][0], _material["TextureOffset"][1], bakeJob["Upscale"])
            if _material["Textures"][2] != -1:
                normalNode = nodes.new(type="ShaderNodeTexImage")
                normalNode.label = "NormalTexture"
                texturePath = TexturesPath + str(_material["Textures"][2]) + ".png"
                normalNode.image = bpy.data.images.load(filepath=texturePath, check_existing=True)
                nodetree.links.new(mappingNode.outputs["Vector"], normalNode.inputs["Vector"])
                normalmapNode = nodes.new(type="ShaderNodeNormalMap")
                nodetree.links.new(normalNode.outputs["Color"], normalmapNode.inputs["Color"])
                nodetree.links.new(normalmapNode.outputs["Normal"], inputs["Normal"])
                outDir = NeosBakeryOutputPath + str(bakeObject["REFID"]) + "\\Materials\\" + str(materialIndex) + "\\Normal\\"
                exportTiledTexture(normalNode.image, outDir, _material["TextureScale"][0], _material["TextureScale"][1], _material["TextureOffset"][0], _material["TextureOffset"][1], bakeJob["Upscale"])
            if _material["Textures"][5] != -1:
                metallicNode = nodes.new(type="ShaderNodeTexImage")
                metallicNode.label = "MetallicTexture"
                texturePath = TexturesPath + str(_material["Textures"][5]) + ".png"
                metallicNode.image = bpy.data.images.load(filepath=texturePath, check_existing=True)
                nodetree.links.new(mappingNode.outputs["Vector"], metallicNode.inputs["Vector"])
                splitRGBnode = nodes.new(type="ShaderNodeSeparateRGB")
                invertNode = nodes.new(type="ShaderNodeInvert")
                nodetree.links.new(metallicNode.outputs["Color"], splitRGBnode.inputs["Image"])
                nodetree.links.new(splitRGBnode.outputs["R"], inputs["Metallic"])
                nodetree.links.new(metallicNode.outputs["Alpha"], invertNode.inputs["Color"])
                nodetree.links.new(invertNode.outputs["Color"], inputs["Roughness"])
                outDir = NeosBakeryOutputPath + str(bakeObject["REFID"]) + "\\Materials\\" + str(materialIndex) + "\\Metallic\\"
                exportTiledTexture(metallicNode.image, outDir, _material["TextureScale"][0], _material["TextureScale"][1], _material["TextureOffset"][0], _material["TextureOffset"][1], bakeJob["Upscale"])
            if _material["Textures"][6] != -1:
                specularNode = nodes.new(type="ShaderNodeTexImage")
                specularNode.label = "SpecularTexture"
                texturePath = TexturesPath + str(_material["Textures"][6]) + ".png"
                specularNode.image = bpy.data.images.load(filepath=texturePath, check_existing=True)
                nodetree.links.new(mappingNode.outputs["Vector"], specularNode.inputs["Vector"])
                nodetree.links.new(specularNode.outputs["Color"], inputs["Specular"])
                outDir = NeosBakeryOutputPath + str(bakeObject["REFID"]) + "\\Materials\\" + str(materialIndex) + "\\Specular\\"
                exportTiledTexture(specularNode.image, outDir, _material["TextureScale"][0], _material["TextureScale"][1], _material["TextureOffset"][0], _material["TextureOffset"][1], bakeJob["Upscale"])
            if _material["Textures"][3] != -1:
                texturePath = TexturesPath + str(_material["Textures"][3]) + ".png"
                heightImage = bpy.data.images.load(filepath=texturePath, check_existing=True)
                outDir = NeosBakeryOutputPath + str(bakeObject["REFID"]) + "\\Materials\\" + str(materialIndex) + "\\Height\\"
                exportTiledTexture(heightImage, outDir, _material["TextureScale"][0], _material["TextureScale"][1], _material["TextureOffset"][0], _material["TextureOffset"][1], bakeJob["Upscale"])
            if _material["Textures"][4] != -1:
                texturePath = TexturesPath + str(_material["Textures"][4]) + ".png"
                occlusionImage = bpy.data.images.load(filepath=texturePath, check_existing=True)
                outDir = NeosBakeryOutputPath + str(bakeObject["REFID"]) + "\\Materials\\" + str(materialIndex) + "\\Occlusion\\"
                exportTiledTexture(occlusionImage, outDir, _material["TextureScale"][0], _material["TextureScale"][1], _material["TextureOffset"][0], _material["TextureOffset"][1], bakeJob["Upscale"])

        for node in nodes:
            node.select = False
        bakeNode.select = True
        materialIndex = materialIndex + 1

bo = 0
for bakedObject in objectsToBake:
    bpy.ops.object.select_all(action="DESELECT")
    selectedObject = bpy.data.objects[bakedObject.name]
    selectedObject.select_set(True)
    bpy.ops.object.bake(use_automatic_name=True, type="COMBINED",  save_mode="EXTERNAL")
    if bakeJob["BakeMethod"] == 0:
        savepath = NeosBakeryOutputPath + str(bakedTextures_RendererIndex[bo]) + "\\Mesh"
        if not os.path.exists(savepath):
            os.makedirs(savepath)
        #For some reason, reimported meshes seem to be arbitrarily rotated. Guess I have to rotate them in-game instead.
        #selectedObject.rotation_mode = "XYZ"
        #selectedObject.rotation_euler[2] = selectedObject.rotation_euler[2] + pi/2
        #selectedObject.rotation_mode = "QUATERNION"
        #bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        bpy.ops.export_scene.gltf(filepath=savepath + "\\Mesh.glb", check_existing=False, export_selected=True)
    bo = bo + 1

bn = 0
for bakedNode in bakeNodes:
    savepath = NeosBakeryOutputPath + str(bakedTextures_RendererIndex[bn]) + "\\Materials\\" + str(bakedTextures_MaterialIndex[bn])
    if not os.path.exists(savepath):
        os.makedirs(savepath)
    bakedNode.image.save_render(filepath=savepath + "\\Albedo.png")
    bn = bn + 1

bpy.ops.wm.quit_blender()