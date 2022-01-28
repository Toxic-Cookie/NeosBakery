using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;

using BaseX;
using FrooxEngine;
using Newtonsoft.Json;

using static NeosBakery.Core.Paths;
using static NeosBakery.Core.Defs;

namespace NeosBakery.Core
{
    static class LightBaker
    {
        /// <summary>Checks if the bake job is in the process of baking.</summary>
        public static bool IsBusy { get; private set; }
        /// <summary>Checks if the bake job is awaiting finalization via discarding, keeping or rebaking.</summary>
        public static bool IsFinalized { get; private set; } = true;

        /// <summary>This event gets raised when the bake job begins.</summary>
        public static event Action OnBakeStarted;
        static void RaiseOnBakeStarted()
        {
            OnBakeStarted?.Invoke();
        }
        /// <summary>This event gets raised when the bake job is completed.</summary>
        public static event Action OnBakeCompleted;
        static void RaiseOnBakeCompleted()
        {
            OnBakeCompleted?.Invoke();
        }
        /// <summary>This event gets raised when the bake is finalized.</summary>
        public static event Action OnBakeFinalized;
        static void RaiseOnBakeFinalized()
        {
            OnBakeFinalized?.Invoke();
        }
        /// <summary>This event gets raised when the bake job has information to output.</summary>
        public static event Action<string> OnBakeInfo;
        static void RaiseOnBakeInfo(string info)
        {
            OnBakeInfo?.Invoke(info);
        }
        /// <summary>This event gets raised when the bake job is cancelled via the cancellation token.</summary>
        public static event Action OnBakeCancelled;
        static void RaiseOnBakeCancelled()
        {
            OnBakeCancelled?.Invoke();
        }

        /// <summary>The list of temporary assetMultiplexers used for previewing and finalizing changes.</summary>
        static readonly List<AssetMultiplexer<Mesh>> meshMultiplexers = new List<AssetMultiplexer<Mesh>>();
        /// <summary>The list of temporary assetMultiplexers used for previewing and finalizing changes.</summary>
        static readonly List<AssetMultiplexer<Material>> materialMultiplexers = new List<AssetMultiplexer<Material>>();

        /// <summary>The list of temporary renderers used for previewing and finalizing changes.</summary>
        static readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
        /// <summary>The list of temporary renderer roations used for previewing and finalizing changes.</summary>
        static readonly Dictionary<MeshRenderer, RotationDefinition> rendererRotations = new Dictionary<MeshRenderer, RotationDefinition>();

        /// <summary>Bakes lighting in a given scene of models and lights.</summary>
        /// <returns>True if the bake was successful.</returns>
        public static async Task<bool> Bake(Slot meshRoot, Slot lightRoot, string blenderPath = null, int defaultResolution = 1024, bool upscale = true, BakeType bakeType = BakeType.DirectAndIndirect, BakeMethod bakeMethod = BakeMethod.SeparateAlbedo, CancellationToken token = default)
        {
            if (IsBusy)
            {
                RaiseOnBakeInfo("Already busy baking! Cannot start bake job!");
                return false;
            }
            if (!IsFinalized)
            {
                RaiseOnBakeInfo("Previous bake job not finalized! Cannot start bake job!");
                return false;
            }
            if (meshRoot == null)
            {
                RaiseOnBakeInfo("Mesh root is null! Cannot start bake job!");
                return false;
            }
            if (lightRoot == null)
            {
                RaiseOnBakeInfo("Light root is null! Cannot start bake job!");
                return false;
            }
            if (blenderPath == null)
            {
                blenderPath = BlenderPath;
            }
            if (!File.Exists(blenderPath))
            {
                RaiseOnBakeInfo("Couldn't find blender at specified path! Cannot start bake job!");
                return false;
            }

            IsBusy = true;
            IsFinalized = false;

            RaiseOnBakeInfo("Beginning bake job!");
            RaiseOnBakeStarted();

            RaiseOnBakeInfo("Ensuring paths exist...");
            EnsureAllPathsExist();
            RegeneratePath(OutputPath);

            meshMultiplexers.Clear();
            materialMultiplexers.Clear();

            renderers.Clear();
            rendererRotations.Clear();

            RaiseOnBakeInfo("Gathering renderers...");
            renderers.AddRange(meshRoot.GetComponentsInChildren<MeshRenderer>());
            if (renderers.Count == 0)
            {
                IsBusy = false;
                IsFinalized = true;
                RaiseOnBakeInfo("Nothing to bake! Bake job cancelled.");
                return false;
            }
            Dictionary<ulong, MeshRenderer> ID_Renderer = new Dictionary<ulong, MeshRenderer>();
            List<BakeObjectDefinition> bakeObjectDefinitions = new List<BakeObjectDefinition>();
            foreach (MeshRenderer renderer in renderers)
            {
                RaiseOnBakeInfo("Evaluating renderer on slot: " + renderer.Slot.NameField.Value);
                if (token.IsCancellationRequested)
                {
                    PreCancelBake();
                    return false;
                }

                RaiseOnBakeInfo("Evaluating mesh on slot: " + renderer.Slot.NameField.Value);
                int meshUriHash = await CacheMesh(renderer);
                if (meshUriHash == -1)
                {
                    continue;
                }

                List<int> materials = new List<int>();
                for (int m = 0; m < renderer.Materials.Count; m++)
                {
                    RaiseOnBakeInfo("Evaluating material: " + (m + 1).ToString() + " of " + renderer.Materials.Count.ToString());
                    if (token.IsCancellationRequested)
                    {
                        PreCancelBake();
                        return false;
                    }

                    if (!(renderer.Materials[m] is PBS_Material material))
                    {
                        continue;
                    }

                    int[] cachedTextures = await CacheTextures(material);
                    int cachedMaterial = CacheMaterial(material, cachedTextures);
                    materials.Add(cachedMaterial);
                }

                bakeObjectDefinitions.Add(new BakeObjectDefinition(new TransformDefinition(renderer.Slot), new RendererDefinition(meshUriHash, materials.ToArray()), renderer.ReferenceID));
                ID_Renderer.Add(renderer.ReferenceID.Position, renderer);
            }
            RaiseOnBakeInfo("Gathered (" + renderers.Count.ToString() + ") total renderers!");

            RaiseOnBakeInfo("Gathering lights...");
            List<Light> lights = lightRoot.GetComponentsInChildren<Light>();
            LightDefinition[] lightDefinitions = new LightDefinition[lights.Count];
            for (int i = 0; i < lightDefinitions.Length; i++)
            {
                RaiseOnBakeInfo("Evaluating light on slot: " + lights[i].Slot.NameField.Value);
                lightDefinitions[i] = new LightDefinition(lights[i]);
            }
            RaiseOnBakeInfo("Gathered (" + lights.Count.ToString() + ") total lights!");

            RaiseOnBakeInfo("Evaluating Skybox...");
            int cachedSkybox;
            if (!(Engine.Current.WorldManager.FocusedWorld.KeyOwner(Skybox.ACTIVE_SKYBOX_KEY) is Skybox skybox))
            {
                skybox = Engine.Current.WorldManager.FocusedWorld.RootSlot.GetComponentInChildren<Skybox>();
                cachedSkybox = await CacheSkybox(skybox);
            }
            else
            {
                cachedSkybox = await CacheSkybox(skybox);
            }
            RaiseOnBakeInfo("Generating bake job...");
            BakeJob bakeJob = new BakeJob(bakeObjectDefinitions.ToArray(), lightDefinitions, new SkyboxDefinition(skybox, cachedSkybox), upscale, defaultResolution, bakeType, bakeMethod);
            File.WriteAllText(BakeJobPath, JsonConvert.SerializeObject(bakeJob, Formatting.Indented));
            RaiseOnBakeInfo("Generated bake job!");

            RaiseOnBakeInfo("Baking...");
            Process Blender = Process.Start(blenderPath, "-con -P " + '"' + BakePyPath + '"');
            CancellationTokenRegistration unregisterKill = token.Register(Blender.Kill);
            CancellationTokenRegistration unregisterCancel = token.Register(PreCancelBake);
            await Blender.WaitForExitAsync(token);
            unregisterKill.Dispose();
            unregisterCancel.Dispose();
            RaiseOnBakeInfo("Bake finished! Beginning automatic import and assignment...");

            await ImportAndAssignAssets(ID_Renderer, bakeMethod, token);
            if (token.IsCancellationRequested)
            {
                IsBusy = false;
                RaiseOnBakeInfo("Bake job cancelled.");
                FinalizeChanges(ChangesType.Discard);
                RaiseOnBakeCancelled();
                return false;
            }

            IsBusy = false;
            RaiseOnBakeInfo("Bake job complete!");
            RaiseOnBakeCompleted();
            return true;
        }
        /// <summary>Gets called before the bake is fully cancelled.</summary>
        static void PreCancelBake()
        {
            IsBusy = false;
            IsFinalized = true;
            RaiseOnBakeInfo("Bake job cancelled.");
            RaiseOnBakeCancelled();
        }

        /// <summary>Caches a mesh.</summary>
        /// <returns>The hash of the mesh or -1 if the mesh is null, not static or a non-supported procedural.</returns>
        static async Task<int> CacheMesh(MeshRenderer renderer)
        {
            if (renderer.Mesh.Target == null)
            {
                return -1;
            }
            if (!(renderer.Mesh.Target is StaticMesh) && !(renderer.Mesh.Target is ProceduralMesh))
            {
                return -1;
            }
            int meshUriHash;
            if (renderer.Mesh.Target is StaticMesh mesh)
            {
                meshUriHash = mesh.URL.Value.AbsoluteUri.GetHashCode();
            }
            else
            {
                int proceduralHash = ProceduralMeshDefinitions.GetMeshHashCode((ProceduralMesh)renderer.Mesh.Target);
                if (proceduralHash == -1)
                {
                    return -1;
                }
                meshUriHash = proceduralHash;
            }
            if (!File.Exists(MeshesPath + meshUriHash.ToString() + ".gltf"))
            {
                ModelExportable modelExportable = renderer.Slot.GetComponentOrAttach<ModelExportable>();
                modelExportable.Root.Target = renderer.Slot;
                modelExportable.OnlyComponents.Add(renderer);
                await modelExportable.Export(MeshesPath, meshUriHash.ToString(), 6);
                modelExportable.Destroy();
            }

            return meshUriHash;
        }
        /// <summary>Caches textures on a given material.</summary>
        /// <returns>The hashes of all possible textures. A hash of -1 means the texture is null or procedural.</returns>
        static async Task<int[]> CacheTextures(PBS_Material material)
        {
            int[] textures = new int[7] { -1, -1, -1, -1, -1, -1, -1 };
            if (material == null)
            {
                return textures;
            }
            for (int t = 0; t < 7; t++)
            {
                RaiseOnBakeInfo("Evaluating texture: " + t.ToString());
                try
                {
                    StaticTexture2D texture = null;
                    switch (t)
                    {
                        case 0:
                            texture = (StaticTexture2D)material.AlbedoTexture.Target;
                            break;
                        case 1:
                            texture = (StaticTexture2D)material.EmissiveMap.Target;
                            break;
                        case 2:
                            texture = (StaticTexture2D)material.NormalMap.Target;
                            break;
                        case 3:
                            texture = (StaticTexture2D)material.HeightMap.Target;
                            break;
                        case 4:
                            texture = (StaticTexture2D)material.OcclusionMap.Target;
                            break;
                        case 5:
                            PBS_Metallic metallic = (PBS_Metallic)material;
                            texture = (StaticTexture2D)metallic.MetallicMap.Target;
                            break;
                        case 6:
                            PBS_Specular specular = (PBS_Specular)material;
                            texture = (StaticTexture2D)specular.SpecularMap.Target;
                            break;
                    }

                    Uri textureUri = texture.URL.Value;
                    int textureUriHash = textureUri.AbsoluteUri.GetHashCode();
                    textures[t] = textureUriHash;
                    if (!File.Exists(TexturesPath + textureUriHash.ToString() + ".png"))
                    {
                        Slot isolatedTextureSlot = texture.Slot.AddSlot(textureUriHash.ToString());

                        StaticTexture2D isolatedTexture = isolatedTextureSlot.GetComponentOrAttach<StaticTexture2D>();
                        isolatedTexture.URL.Value = textureUri;

                        TextureExportable textureExportable = isolatedTextureSlot.GetComponentOrAttach<TextureExportable>();
                        textureExportable.Texture.Target = isolatedTexture;

                        await textureExportable.Export(TexturesPath, textureUriHash.ToString(), 0);
                        textureExportable.Destroy();
                    }
                }
                catch
                {
                    textures[t] = -1;
                }
            }

            return textures;
        }
        /// <summary>Caches the material.</summary>
        /// <returns>The hash of the material.</returns>
        static int CacheMaterial(PBS_Material material, int[] textures)
        {
            MaterialDefinition materialDefinition = new MaterialDefinition(material, textures);
            int materialUriHash = materialDefinition.GetHashCode();
            if (!File.Exists(MaterialsPath + materialUriHash.ToString() + ".json"))
            {
                File.WriteAllText(MaterialsPath + materialUriHash.ToString() + ".json", JsonConvert.SerializeObject(materialDefinition, Formatting.Indented));
            }

            return materialUriHash;
        }
        /// <summary>Caches the texture of the skybox if one is available.</summary>
        /// <returns>The hash of the skybox texture or -1 if it is null.</returns>
        static async Task<int> CacheSkybox(Skybox skybox)
        {
            if (skybox.Material.Target is Projection360Material projectionMaterial)
            {
                try
                {
                    StaticTexture2D texture = (StaticTexture2D)projectionMaterial.Texture.Target;
                    Uri textureUri = texture.URL.Value;
                    int textureUriHash = textureUri.AbsoluteUri.GetHashCode();
                    if (!File.Exists(TexturesPath + textureUriHash.ToString() + ".png"))
                    {
                        Slot isolatedTextureSlot = texture.Slot.AddSlot(textureUriHash.ToString());

                        StaticTexture2D isolatedTexture = isolatedTextureSlot.GetComponentOrAttach<StaticTexture2D>();
                        isolatedTexture.URL.Value = textureUri;

                        TextureExportable textureExportable = isolatedTextureSlot.GetComponentOrAttach<TextureExportable>();
                        textureExportable.Texture.Target = isolatedTexture;

                        await textureExportable.Export(TexturesPath, textureUriHash.ToString(), 0);
                        textureExportable.Destroy();
                    }
                    return textureUriHash;
                }
                catch
                {
                    return -1;
                }
            }

            return -1;
        }

        /// <summary>Imports and assigns all output meshes and materials.</summary>
        /// <returns>True if the operation is successful. Otherwise, returns false.</returns>
        static async Task<bool> ImportAndAssignAssets(Dictionary<ulong, MeshRenderer> ID_Renderer, BakeMethod bakeMethod, CancellationToken token = default)
        {
            foreach (string _renderer in Directory.GetDirectories(OutputPath))
            {
                if (token.IsCancellationRequested)
                {
                    return false;
                }

                string _rendererID = new DirectoryInfo(_renderer).Name;
                MeshRenderer renderer;
                try
                {
                    renderer = ID_Renderer[ulong.Parse(_rendererID)];
                }
                catch
                {
                    Directory.Delete(_renderer, true);
                    continue;
                }

                Slot BakeOutputRoot = renderer.Slot.AddSlot("Bake Output");
                Slot MaterialOutputRoot = BakeOutputRoot.AddSlot("Materials");

                if (bakeMethod == BakeMethod.SeparateAlbedo)
                {
                    Slot MeshOutputRoot = BakeOutputRoot.AddSlot("Mesh");
                    AssetMultiplexer<Mesh> meshMultiplexer = MeshOutputRoot.AttachComponent<AssetMultiplexer<Mesh>>();
                    meshMultiplexers.Add(meshMultiplexer);
                    meshMultiplexer.Assets.Add(renderer.Mesh.Target);
                    StaticMesh importedMesh = await ImportAndAssignMesh(_rendererID, MeshOutputRoot, renderer);
                    meshMultiplexer.Assets.Add(importedMesh);
                    meshMultiplexer.Index.Value = 1;
                    meshMultiplexer.Target.Target = renderer.Mesh;
                }

                for (int m = 0; m < renderer.Materials.Count; m++)
                {
                    if (token.IsCancellationRequested)
                    {
                        return false;
                    }

                    if (!(renderer.Materials[m] is PBS_Material pbs))
                    {
                        continue;
                    }
                    Slot MaterialOutputSlot = MaterialOutputRoot.AddSlot(m.ToString());
                    AssetMultiplexer<Material> materialMultiplexer = MaterialOutputSlot.AttachComponent<AssetMultiplexer<Material>>();
                    materialMultiplexers.Add(materialMultiplexer);
                    materialMultiplexer.Assets.Add(pbs);
                    materialMultiplexer.Target.Target = renderer.Materials.GetElement(m);
                    if (pbs is PBS_Metallic)
                    {
                        if (bakeMethod == BakeMethod.SeparateAlbedo)
                        {
                            PBS_MultiUV_Metallic bakedMaterial = MaterialOutputSlot.AttachComponent<PBS_MultiUV_Metallic>();
                            materialMultiplexer.Assets.Add(bakedMaterial);
                            CopyMaterialProperties(pbs, bakedMaterial);
                            bakedMaterial.AlbedoColor.Value = new color(1f, 1f);
                            bakedMaterial.SecondaryEmissiveColor.Value = new color(0.5f, 1f);
                            await ImportAndAssignTexture(_rendererID, m, MaterialOutputSlot, bakedMaterial, TextureType.Baked);
                        }
                        else
                        {
                            PBS_Metallic bakedMaterial = MaterialOutputSlot.AttachComponent<PBS_Metallic>();
                            materialMultiplexer.Assets.Add(bakedMaterial);
                            CopyMaterialProperties(pbs, bakedMaterial);
                            bakedMaterial.AlbedoColor.Value = new color(1f, 1f);
                            bakedMaterial.EmissiveColor.Value = new color(0.5f, 1f);
                            await ImportAndAssignTexture(_rendererID, m, MaterialOutputSlot, bakedMaterial, TextureType.Baked);
                            if (pbs.TextureScale.Value != new float2(1f, 1f) || pbs.TextureOffset.Value != new float2(1f, 1f))
                            {
                                await ImportAndAssignTexture(_rendererID, m, MaterialOutputSlot, bakedMaterial, TextureType.Normal);
                                await ImportAndAssignTexture(_rendererID, m, MaterialOutputSlot, bakedMaterial, TextureType.Height);
                                await ImportAndAssignTexture(_rendererID, m, MaterialOutputSlot, bakedMaterial, TextureType.Occlusion);
                                await ImportAndAssignTexture(_rendererID, m, MaterialOutputSlot, bakedMaterial, TextureType.Metallic);
                                await ImportAndAssignTexture(_rendererID, m, MaterialOutputSlot, bakedMaterial, TextureType.Specular);
                            }
                        }
                    }
                    if (pbs is PBS_Specular)
                    {
                        if (bakeMethod == BakeMethod.SeparateAlbedo)
                        {
                            PBS_MultiUV_Specular bakedMaterial = MaterialOutputSlot.AttachComponent<PBS_MultiUV_Specular>();
                            materialMultiplexer.Assets.Add(bakedMaterial);
                            CopyMaterialProperties(pbs, bakedMaterial);
                            bakedMaterial.AlbedoColor.Value = new color(1f, 1f);
                            bakedMaterial.SecondaryEmissiveColor.Value = new color(0.5f, 1f);
                            await ImportAndAssignTexture(_rendererID, m, MaterialOutputSlot, bakedMaterial, TextureType.Baked);
                        }
                        else
                        {
                            PBS_Specular bakedMaterial = MaterialOutputSlot.AttachComponent<PBS_Specular>();
                            materialMultiplexer.Assets.Add(bakedMaterial);
                            CopyMaterialProperties(pbs, bakedMaterial);
                            bakedMaterial.AlbedoColor.Value = new color(1f, 1f);
                            bakedMaterial.EmissiveColor.Value = new color(0.5f, 1f);
                            await ImportAndAssignTexture(_rendererID, m, MaterialOutputSlot, bakedMaterial, TextureType.Baked);
                            if (pbs.TextureScale.Value != new float2(1f, 1f) || pbs.TextureOffset.Value != new float2(1f, 1f))
                            {
                                await ImportAndAssignTexture(_rendererID, m, MaterialOutputSlot, bakedMaterial, TextureType.Normal);
                                await ImportAndAssignTexture(_rendererID, m, MaterialOutputSlot, bakedMaterial, TextureType.Height);
                                await ImportAndAssignTexture(_rendererID, m, MaterialOutputSlot, bakedMaterial, TextureType.Occlusion);
                                await ImportAndAssignTexture(_rendererID, m, MaterialOutputSlot, bakedMaterial, TextureType.Metallic);
                                await ImportAndAssignTexture(_rendererID, m, MaterialOutputSlot, bakedMaterial, TextureType.Specular);
                            }
                        }
                    }
                    materialMultiplexer.Index.Value = 1;
                }
            }

            return true;
        }
        /// <summary>Imports and assigns a specified mesh.</summary>
        /// <returns>The imported and assigned mesh.</returns>
        static async Task<StaticMesh> ImportAndAssignMesh(string rendererID, Slot bakeOutputSlot, MeshRenderer renderer)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(OutputPath);
            stringBuilder.Append(rendererID);
            stringBuilder.Append(@"\\Mesh\\Mesh.glb");
            string meshPath = stringBuilder.ToString();
            ModelImportSettings modelImportSettings = ModelImportSettings.PBS();
            RaiseOnBakeInfo("Importing mesh for renderer on slot: " + renderer.Slot.NameField.Value);
            await ModelImporter.ImportModelAsync(meshPath, bakeOutputSlot, modelImportSettings);
            StaticMesh importedMesh = bakeOutputSlot.AttachComponent<StaticMesh>();
            MeshRenderer importedMeshRenderer = bakeOutputSlot.GetComponentInChildren<MeshRenderer>();
            importedMesh.URL.Value = ((StaticMesh)importedMeshRenderer.Mesh.Target).URL;
            importedMeshRenderer.Mesh.Target.Destroy();
            importedMeshRenderer.Slot.Destroy();

            floatQ euler = floatQ.Euler(renderer.Slot.Rotation_Field.Value.EulerAngles.x,
                renderer.Slot.Rotation_Field.Value.EulerAngles.y + 180f,
                renderer.Slot.Rotation_Field.Value.EulerAngles.z);
            rendererRotations.Add(renderer, new RotationDefinition(renderer.Slot.Rotation_Field.Value, euler));
            renderer.Slot.Rotation_Field.Value = euler;

            return importedMesh;
        }
        
        /// <summary>Imports and assigns a specified output texture.</summary>
        /// <returns>The imported and assigned texture.</returns>
        static async Task<StaticTexture2D> ImportAndAssignTexture(string rendererID, int materialIndex, Slot bakeOutputSlot, PBS_MultiUV_Material bakedMaterial, TextureType textureType)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(OutputPath);
            stringBuilder.Append(rendererID);
            stringBuilder.Append(@"\\Materials\\");
            stringBuilder.Append(materialIndex);
            switch (textureType)
            {
                case TextureType.Baked:
                    stringBuilder.Append(@"\\Albedo.png");
                    break;
                case TextureType.Albedo:
                    stringBuilder.Append(@"\\Albedo.png");
                    break;
                case TextureType.Emissive:
                    stringBuilder.Append(@"\\Emissive.png");
                    break;
                case TextureType.Normal:
                    stringBuilder.Append(@"\\Normal.png");
                    break;
                case TextureType.Occlusion:
                    stringBuilder.Append(@"\\Occlusion.png");
                    break;
                case TextureType.Metallic:
                    stringBuilder.Append(@"\\Metallic.png");
                    break;
                case TextureType.Specular:
                    stringBuilder.Append(@"\\Specular.png");
                    break;
                default:
                    return null;
            }
            string texturePath = stringBuilder.ToString();

            if (File.Exists(texturePath))
            {
                Slot textureOutputSlot = bakeOutputSlot.AddSlot(textureType.ToString());
                RaiseOnBakeInfo("Importing: " + textureType.ToString());
                await ImageImporter.ImportImage(texturePath, textureOutputSlot);

                StaticTexture2D importedTexture = textureOutputSlot.GetComponent<StaticTexture2D>();
                switch (textureType)
                {
                    case TextureType.Baked:
                        bakedMaterial.SecondaryAlbedoTexture.Target = importedTexture;
                        bakedMaterial.SecondaryEmissiveMap.Target = importedTexture;
                        break;
                    case TextureType.Albedo:
                        bakedMaterial.AlbedoTexture.Target = importedTexture;
                        break;
                    case TextureType.Emissive:
                        bakedMaterial.EmissiveMap.Target = importedTexture;
                        break;
                    case TextureType.Normal:
                        bakedMaterial.NormalMap.Target = importedTexture;
                        break;
                    case TextureType.Occlusion:
                        bakedMaterial.OcclusionMap.Target = importedTexture;
                        break;
                    case TextureType.Metallic:
                        PBS_MultiUV_Metallic metallic = (PBS_MultiUV_Metallic)bakedMaterial;
                        metallic.MetallicMap.Target = importedTexture;
                        break;
                    case TextureType.Specular:
                        PBS_MultiUV_Specular specular = (PBS_MultiUV_Specular)bakedMaterial;
                        specular.SpecularMap.Target = importedTexture;
                        break;
                }

                Slot slot = importedTexture.Slot;

                slot.GetComponent<TextureExportable>().Destroy();
                slot.GetComponent<ItemTextureThumbnailSource>().Destroy();
                slot.GetComponent<SnapPlane>().Destroy();
                slot.GetComponent<ReferenceProxy>().Destroy();
                slot.GetComponent<AssetProxy<Texture2D>>().Destroy();
                slot.GetComponent<UnlitMaterial>().Destroy();
                slot.GetComponent<QuadMesh>().Destroy();
                slot.GetComponent<MeshRenderer>().Destroy();
                slot.GetComponent<BoxCollider>().Destroy();
                slot.GetComponent<Float2ToFloat3SwizzleDriver>().Destroy();

                return importedTexture;
            }

            return null;
        }
        /// <summary>Imports and assigns a specified output texture.</summary>
        /// <returns>The imported and assigned texture.</returns>
        static async Task<StaticTexture2D> ImportAndAssignTexture(string rendererID, int materialIndex, Slot bakeOutputSlot, PBS_Material bakedMaterial, TextureType textureType)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(OutputPath);
            stringBuilder.Append(rendererID);
            stringBuilder.Append(@"\\Materials\\");
            stringBuilder.Append(materialIndex);
            switch (textureType)
            {
                case TextureType.Baked:
                    stringBuilder.Append(@"\\Albedo.png");
                    break;
                case TextureType.Albedo:
                    stringBuilder.Append(@"\\Albedo.png");
                    break;
                case TextureType.Emissive:
                    stringBuilder.Append(@"\\Emissive.png");
                    break;
                case TextureType.Normal:
                    stringBuilder.Append(@"\\Normal.png");
                    break;
                case TextureType.Height:
                    stringBuilder.Append(@"\\Height.png");
                    break;
                case TextureType.Occlusion:
                    stringBuilder.Append(@"\\Occlusion.png");
                    break;
                case TextureType.Metallic:
                    stringBuilder.Append(@"\\Metallic.png");
                    break;
                case TextureType.Specular:
                    stringBuilder.Append(@"\\Specular.png");
                    break;
                default:
                    return null;
            }
            string texturePath = stringBuilder.ToString();

            if (File.Exists(texturePath))
            {
                Slot textureOutputSlot = bakeOutputSlot.AddSlot(textureType.ToString());
                RaiseOnBakeInfo("Importing: " + textureType.ToString());
                await ImageImporter.ImportImage(texturePath, textureOutputSlot);

                StaticTexture2D importedTexture = textureOutputSlot.GetComponent<StaticTexture2D>();
                switch (textureType)
                {
                    case TextureType.Baked:
                        bakedMaterial.AlbedoTexture.Target = importedTexture;
                        bakedMaterial.EmissiveMap.Target = importedTexture;
                        break;
                    case TextureType.Albedo:
                        bakedMaterial.AlbedoTexture.Target = importedTexture;
                        break;
                    case TextureType.Emissive:
                        bakedMaterial.EmissiveMap.Target = importedTexture;
                        break;
                    case TextureType.Normal:
                        bakedMaterial.NormalMap.Target = importedTexture;
                        break;
                    case TextureType.Height:
                        bakedMaterial.HeightMap.Target = importedTexture;
                        break;
                    case TextureType.Occlusion:
                        bakedMaterial.OcclusionMap.Target = importedTexture;
                        break;
                    case TextureType.Metallic:
                        PBS_Metallic metallic = (PBS_Metallic)bakedMaterial;
                        metallic.MetallicMap.Target = importedTexture;
                        break;
                    case TextureType.Specular:
                        PBS_Specular specular = (PBS_Specular)bakedMaterial;
                        specular.SpecularMap.Target = importedTexture;
                        break;
                }

                Slot slot = importedTexture.Slot;

                slot.GetComponent<TextureExportable>().Destroy();
                slot.GetComponent<ItemTextureThumbnailSource>().Destroy();
                slot.GetComponent<SnapPlane>().Destroy();
                slot.GetComponent<ReferenceProxy>().Destroy();
                slot.GetComponent<AssetProxy<Texture2D>>().Destroy();
                slot.GetComponent<UnlitMaterial>().Destroy();
                slot.GetComponent<QuadMesh>().Destroy();
                slot.GetComponent<MeshRenderer>().Destroy();
                slot.GetComponent<BoxCollider>().Destroy();
                slot.GetComponent<Float2ToFloat3SwizzleDriver>().Destroy();

                return importedTexture;
            }

            return null;
        }

        /// <summary>Copies the material properties from one material to another.</summary>
        static void CopyMaterialProperties(PBS_Material inputMaterial, PBS_MultiUV_Material outputMaterial, int primaryUV = 1)
        {
            primaryUV = primaryUV.Clamp(0, 1);
            int secondaryUV = primaryUV == 1 ? 0 : 1;

            float2 inputTextureScale = inputMaterial.TextureScale.Value;
            float2 inputTextureOffset = inputMaterial.TextureOffset.Value;

            outputMaterial.AlbedoScale.Value = inputTextureScale;
            outputMaterial.EmissionMapScale.Value = inputTextureScale;
            outputMaterial.NormalMapScale.Value = inputTextureScale;
            outputMaterial.OcclusionMapScale.Value = inputTextureScale;

            outputMaterial.AlbedoOffset.Value = inputTextureOffset;
            outputMaterial.EmissionMapOffset.Value = inputTextureOffset;
            outputMaterial.NormalMapOffset.Value = inputTextureOffset;
            outputMaterial.OcclusionMapOffset.Value = inputTextureOffset;

            outputMaterial.AlbedoTexture.Target = inputMaterial.AlbedoTexture.Target;
            outputMaterial.EmissiveMap.Target = inputMaterial.EmissiveMap.Target;
            outputMaterial.NormalMap.Target = inputMaterial.NormalMap.Target;
            outputMaterial.OcclusionMap.Target = inputMaterial.OcclusionMap.Target;

            outputMaterial.AlbedoColor.Value = inputMaterial.AlbedoColor.Value;
            outputMaterial.EmissiveColor.Value = inputMaterial.EmissiveColor.Value;
            outputMaterial.NormalScale.Value = inputMaterial.NormalScale.Value;
            outputMaterial.AlphaClip.Value = inputMaterial.AlphaCutoff.Value;
            outputMaterial.OffsetFactor.Value = inputMaterial.OffsetFactor.Value;
            outputMaterial.OffsetUnits.Value = inputMaterial.OffsetUnits.Value;
            outputMaterial.RenderQueue.Value = inputMaterial.RenderQueue.Value;

            outputMaterial.AlbedoUV.Value = primaryUV;
            outputMaterial.EmissionMapUV.Value = primaryUV;
            outputMaterial.NormalMapUV.Value = primaryUV;
            outputMaterial.OcclusionMapUV.Value = primaryUV;

            outputMaterial.SecondaryAlbedoUV.Value = secondaryUV;
            outputMaterial.SecondaryEmissionMapUV.Value = secondaryUV;

            if (inputMaterial is PBS_Metallic metallic)
            {
                PBS_MultiUV_Metallic multiMaterial = (PBS_MultiUV_Metallic)outputMaterial;

                multiMaterial.MetallicMapScale.Value = inputTextureScale;
                multiMaterial.MetallicMapOffset.Value = inputTextureOffset;
                multiMaterial.MetallicMap.Target = metallic.MetallicMap.Target;

                multiMaterial.Metallic.Value = metallic.Metallic.Value;
                multiMaterial.Smoothness.Value = metallic.Smoothness.Value;
                multiMaterial.MetallicMapUV.Value = primaryUV;
            }
            if (inputMaterial is PBS_Specular specular)
            {
                PBS_MultiUV_Specular multiMaterial = (PBS_MultiUV_Specular)outputMaterial;

                multiMaterial.SpecularMapScale.Value = inputTextureScale;
                multiMaterial.SpecularMapOffset.Value = inputTextureOffset;
                multiMaterial.SpecularMap.Target = specular.SpecularMap.Target;

                multiMaterial.SpecularColor.Value = specular.SpecularColor.Value;
                multiMaterial.SpecularMapUV.Value = primaryUV;
            }
        }
        /// <summary>Copies the material properties from one material to another.</summary>
        static void CopyMaterialProperties(PBS_Material inputMaterial, PBS_Material outputMaterial)
        {
            outputMaterial.AlbedoTexture.Target = inputMaterial.AlbedoTexture.Target;
            outputMaterial.EmissiveMap.Target = inputMaterial.EmissiveMap.Target;
            outputMaterial.NormalMap.Target = inputMaterial.NormalMap.Target;
            outputMaterial.OcclusionMap.Target = inputMaterial.OcclusionMap.Target;

            outputMaterial.AlbedoColor.Value = inputMaterial.AlbedoColor.Value;
            outputMaterial.EmissiveColor.Value = inputMaterial.EmissiveColor.Value;
            outputMaterial.NormalScale.Value = inputMaterial.NormalScale.Value;
            outputMaterial.AlphaCutoff.Value = inputMaterial.AlphaCutoff.Value;
            outputMaterial.OffsetFactor.Value = inputMaterial.OffsetFactor.Value;
            outputMaterial.OffsetUnits.Value = inputMaterial.OffsetUnits.Value;
            outputMaterial.RenderQueue.Value = inputMaterial.RenderQueue.Value;

            if (inputMaterial is PBS_Metallic metallic)
            {
                PBS_Metallic multiMaterial = (PBS_Metallic)outputMaterial;
                multiMaterial.MetallicMap.Target = metallic.MetallicMap.Target;
                multiMaterial.Metallic.Value = metallic.Metallic.Value;
                multiMaterial.Smoothness.Value = metallic.Smoothness.Value;
            }
            if (inputMaterial is PBS_Specular specular)
            {
                PBS_Specular multiMaterial = (PBS_Specular)outputMaterial;
                multiMaterial.SpecularMap.Target = specular.SpecularMap.Target;
                multiMaterial.SpecularColor.Value = specular.SpecularColor.Value;
            }
        }

        /// <summary>Allows swapping between realtime and baked views.</summary>
        /// <returns>True if the operation is successful. Otherwise, returns false.</returns>
        public static bool ViewChanges(ViewType viewMode)
        {
            if (IsBusy)
            {
                RaiseOnBakeInfo("Bake job in progress! Cannot change view while baking!");
                return false;
            }
            if (IsFinalized)
            {
                RaiseOnBakeInfo("Bake job already finalized! Cannot change view when already finalized!");
                return false;
            }

            foreach (AssetMultiplexer<Mesh> assetMultiplexer in meshMultiplexers)
            {
                assetMultiplexer.Index.Value = viewMode == ViewType.Realtime ? 0 : 1;
            }
            foreach (AssetMultiplexer<Material> assetMultiplexer in materialMultiplexers)
            {
                assetMultiplexer.Index.Value = viewMode == ViewType.Realtime ? 0 : 1;
            }
            foreach (MeshRenderer renderer in renderers)
            {
                try
                {
                    renderer.Slot.Rotation_Field.Value = viewMode == ViewType.Realtime ? rendererRotations[renderer].OriginalRotation : rendererRotations[renderer].NewRotation;
                }
                catch
                {

                }
            }

            return true;
        }
        /// <summary>Makes the final specified changes and signals the reset of the baking process.</summary>
        /// <returns>True if the operation is successful. Otherwise, returns false.</returns>
        public static bool FinalizeChanges(ChangesType changesType)
        {
            if (IsBusy)
            {
                RaiseOnBakeInfo("Bake job in progress! Cannot finalize while baking!");
                return false;
            }
            if (IsFinalized)
            {
                RaiseOnBakeInfo("Bake job already finalized! Cannot finalize changes when already finalized!");
                return false;
            }

            foreach (AssetMultiplexer<Mesh> assetMultiplexer in meshMultiplexers)
            {
                IAssetProvider<Mesh> mesh = assetMultiplexer.Assets.GetElement(changesType == ChangesType.Discard ? 0 : 1).Target;
                AssetRef<Mesh> target = assetMultiplexer.Target.Target;
                assetMultiplexer.Target.Target = null;
                assetMultiplexer.Destroy();
                target.Target = mesh;
            }
            foreach (AssetMultiplexer<Material> assetMultiplexer in materialMultiplexers)
            {
                IAssetProvider<Material> material = assetMultiplexer.Assets.GetElement(changesType == ChangesType.Discard ? 0 : 1).Target;
                AssetRef<Material> target = assetMultiplexer.Target.Target;
                assetMultiplexer.Target.Target = null;
                assetMultiplexer.Destroy();
                target.Target = material;

                Slot destroySlot = assetMultiplexer.Slot.Parent.Parent;
                if (changesType == ChangesType.Discard)
                {
                    destroySlot.Destroy();
                }
            }
            foreach (MeshRenderer renderer in renderers)
            {
                try
                {
                    renderer.Slot.Rotation_Field.Value = changesType == ChangesType.Discard ? rendererRotations[renderer].OriginalRotation : rendererRotations[renderer].NewRotation;
                }
                catch
                {

                }
            }

            IsFinalized = true;
            RaiseOnBakeInfo("Bake job finalized!");
            RaiseOnBakeFinalized();
            return true;
        }

        /// <summary>Clears the cached assets in the Assets and Output folders.</summary>
        /// <returns>True if the operation is successful. Otherwise, returns false.</returns>
        public static bool ClearCache()
        {
            if (IsBusy)
            {
                RaiseOnBakeInfo("Bake job in progress! Cannot clear cache while baking!");
                return false;
            }
            if (!IsFinalized)
            {
                RaiseOnBakeInfo("Bake job not finalized! Cannot clear cache before bake job finalized!");
                return false;
            }

            RegeneratePath(MaterialsPath);
            RegeneratePath(MeshesPath);
            RegeneratePath(TexturesPath);
            RegeneratePath(OutputPath);
            RaiseOnBakeInfo("Cache successfully cleared!");

            return true;
        }
    }
}
