# NeosBakery
A light baking solution for NeosVR.

# Prequisites
1. You must first install [NeosModLoader](https://github.com/zkxs/NeosModLoader).
2. You must also install [Blender 3.0](https://www.blender.org/download/) you may install it anywhere (You might be able to get away with alternate versions but I can verify 3.0 works.)
3. Done!

# Installing
1. Download the latest release of [NeosBakery](https://github.com/Toxic-Cookie/NeosBakery/releases).
2. Extract the contents of the latest zip release (NeosBakery x.x.x.zip) into your nml_mods folder. (Both _NeosBakery and NeosBakery.dll need to be in your nml_mods folder)
3. Done!

# Usage
1. Equip a DevToolTip and open a create menu.
2. Select the Light Baker Wizard option.

# Known Issues
1. Oddly specific models don't play nice with Assimp and will crash Neos instantly regardless of how they're imported/exported.
2. Meshes without proper UVs will not bake correctly. For some reason, some procedural meshes do not export with correct UVs.
3. Baked textures will come out at a lower resolution if the albedo is tiled too much and no upscaling is applied.
4. Upscaling is only reasonable up to 4096 depending on the item being baked.
5. Blender doesn't like to close itself sometimes. This can be worked around by focusing on or away from blender's window. (Only manually close Blender if its window is not grey)
6. Blender doesn't do a great job at creating lightmap UVs and baking with them. (Strange artifacts arise) Burn Albedo is the better option of the two methods.
7. Currently only PBS_Metallic and PBS_Specular are supported for baking.

# Planned Features
1. Possibly the addition of procedural textures being supported.
2. Possibly using Unity as a method of light baking.

# Rules
1. You must adequately credit me if you use this software in your project.
2. Adequate credit requires you to at least have my name and the link to this project listed in a credits section of your project.

# Contributing
Feedback and pull requests are welcome!
