using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using BaseX;
using FrooxEngine;

namespace NeosBakery.Core
{
    //TODO: More documentation.
    static class Defs
    {
        public enum ViewType
        {
            Realtime,
            Baked
        }
        public enum ChangesType
        {
            Discard,
            Keep
        }
        public enum TextureType
        {
            Baked,
            Albedo,
            Emissive,
            Normal,
            Height,
            Occlusion,
            Metallic,
            Specular
        }

        /// <summary>A definition that defines two viewing rotations for an object.</summary>
        public struct RotationDefinition
        {
            public readonly floatQ OriginalRotation;
            public readonly floatQ NewRotation;

            public RotationDefinition(floatQ original, floatQ newRotation)
            {
                OriginalRotation = original;
                NewRotation = newRotation;
            }
        }

        /// <summary>A definition that defines a mesh and its materials.</summary>
        public struct RendererDefinition
        {
            public readonly int Mesh;
            public readonly int[] Materials;

            public RendererDefinition(int mesh, int[] materials)
            {
                Mesh = mesh;
                Materials = materials;
            }
        }

        /// <summary>A definition that defines the properties of a material.</summary>
        public struct MaterialDefinition
        {
            public readonly float[] TextureScale;
            public readonly float[] TextureOffset;
            public readonly float[] AlbedoColor;
            public readonly float[] EmissiveColor;

            public readonly float Metallic;
            public readonly float Smoothness;
            public readonly float[] SpecularColor;

            public readonly int[] Textures;

            public MaterialDefinition(PBS_Material material, int[] textures)
            {
                TextureScale = new float[2] { material.TextureScale.Value.x, material.TextureScale.Value.y };
                TextureOffset = new float[2] { material.TextureOffset.Value.x, material.TextureOffset.Value.y };
                AlbedoColor = new float[4] { material.AlbedoColor.Value.r, material.AlbedoColor.Value.g, material.AlbedoColor.Value.b, material.AlbedoColor.Value.a };
                EmissiveColor = new float[4] { material.EmissiveColor.Value.r, material.EmissiveColor.Value.g, material.EmissiveColor.Value.b, material.EmissiveColor.Value.a };

                Metallic = 0f;
                Smoothness = 0f;
                SpecularColor = new float[4] { 0f, 0f, 0f, 0.25f };
                if (material is PBS_Metallic metallic)
                {
                    Metallic = metallic.Metallic.Value;
                    Smoothness = metallic.Smoothness.Value;
                }
                if (material is PBS_Specular specular)
                {
                    Smoothness = specular.Smoothness;
                    SpecularColor = new float[4] { specular.SpecularColor.Value.r, specular.SpecularColor.Value.g, specular.SpecularColor.Value.b, specular.SpecularColor.Value.a };
                }

                Textures = textures;
            }

            public override int GetHashCode()
            {
                StringBuilder stringBuilder = new StringBuilder();

                stringBuilder.Append(TextureScale[0]);
                stringBuilder.Append(TextureScale[1]);
                stringBuilder.Append(TextureOffset[0]);
                stringBuilder.Append(TextureOffset[1]);

                stringBuilder.Append(AlbedoColor[0]);
                stringBuilder.Append(AlbedoColor[1]);
                stringBuilder.Append(AlbedoColor[2]);
                stringBuilder.Append(AlbedoColor[3]);

                stringBuilder.Append(EmissiveColor[0]);
                stringBuilder.Append(EmissiveColor[1]);
                stringBuilder.Append(EmissiveColor[2]);
                stringBuilder.Append(EmissiveColor[3]);

                stringBuilder.Append(Metallic);
                stringBuilder.Append(Smoothness);

                foreach (int texture in Textures)
                {
                    stringBuilder.Append(texture);
                }
                foreach (int color in SpecularColor)
                {
                    stringBuilder.Append(color);
                }

                return stringBuilder.ToString().GetHashCode();
            }
        }

        /// <summary>A definition that defines a transform for an object.</summary>
        public struct TransformDefinition
        {
            public readonly float[] Position;
            public readonly float[] Rotation;
            public readonly float[] Scale;

            public TransformDefinition(Slot slot)
            {
                float3 position = slot.GlobalPosition;
                floatQ rotation = slot.GlobalRotation;
                float3 scale = slot.GlobalScale;

                Position = new float[3] { position.x, position.y, position.z };
                Rotation = new float[4] { rotation.x, rotation.y, rotation.z, rotation.w };
                Scale = new float[3] { scale.x, scale.y, scale.z };
            }
        }

        /// <summary>A definition that defines an object to be used in baking.</summary>
        public struct BakeObjectDefinition
        {
            public readonly TransformDefinition Transform;
            public readonly RendererDefinition Renderer;
            public readonly ulong REFID;

            public BakeObjectDefinition(TransformDefinition transform, RendererDefinition mesh, RefID rendererRefID)
            {
                Transform = transform;
                Renderer = mesh;
                REFID = rendererRefID.Position;
            }
        }

        /// <summary>A definition that defines the properties of a light source.</summary>
        public struct LightDefinition
        {
            public readonly TransformDefinition Transform;
            //Point, Directional(Sun), Spot
            public readonly int LightType;
            public readonly float[] Color;
            public readonly float Watts;
            public readonly float SpotAngle;
            public readonly bool CastShadow;

            public LightDefinition(Light light)
            {
                LightType = (int)light.LightType.Value;
                Color = new float[4] { light.Color.Value.r, light.Color.Value.g, light.Color.Value.b, light.Color.Value.a };

                float rgb = Math.Max(Math.Max(light.Color.Value.r, light.Color.Value.g), light.Color.Value.b);
                //Convert from Intensity and Range to Watts. Might need more tweaking.
                switch (light.LightType.Value)
                {
                    case FrooxEngine.LightType.Point:
                        Watts = light.Intensity.Value * light.Range.Value * rgb * 10f;
                        break;
                    case FrooxEngine.LightType.Directional:
                        Watts = light.Intensity.Value;
                        break;
                    case FrooxEngine.LightType.Spot:
                        Watts = light.Intensity.Value * light.Range.Value * rgb * 10f;
                        break;
                    default:
                        Watts = light.Intensity.Value * light.Range.Value * rgb * 10f;
                        break;
                }

                SpotAngle = light.SpotAngle.Value;

                if (light.ShadowType.Value != ShadowType.None)
                {
                    CastShadow = true;
                }
                else
                {
                    CastShadow = false;
                }

                Transform = new TransformDefinition(light.Slot);
            }
        }

        /// <summary>A definition that defines the properties of a skybox.</summary>
        public struct SkyboxDefinition
        {
            public readonly float[] PrimaryColor;
            public readonly float[] SecondaryColor;
            public readonly int Texture;

            public SkyboxDefinition(Skybox skybox, int texture)
            {
                color primaryColor = color.Black;
                color secondaryColor = color.Black;

                if (skybox != null)
                {
                    IAssetProvider<Material> skyboxMaterial = skybox.Material.Target;

                    if (skyboxMaterial is GradientSkyMaterial gradientMaterial)
                    {
                        primaryColor = gradientMaterial.BaseColor.Value;
                        secondaryColor = gradientMaterial.BaseColor.Value;
                    }
                    if (skyboxMaterial is ProceduralSkyMaterial proceduralMaterial)
                    {
                        primaryColor = proceduralMaterial.SkyTint.Value;
                        secondaryColor = proceduralMaterial.GroundColor.Value;
                    }
                    if (skyboxMaterial is Projection360Material projectionMaterial)
                    {
                        primaryColor = projectionMaterial.Tint.Value;
                    }
                }

                PrimaryColor = new float[4] { primaryColor.r, primaryColor.g, primaryColor.b, primaryColor.a };
                SecondaryColor = new float[4] { secondaryColor.r, secondaryColor.g, secondaryColor.b, secondaryColor.a };
                Texture = texture;
            }
        }

        /// <summary>A definition that defines a bake job to be processed in blender.</summary>
        public struct BakeJob
        {
            public readonly BakeObjectDefinition[] BakeObjects;
            public readonly LightDefinition[] BakeLights;
            public readonly SkyboxDefinition Skybox;
            public readonly int DefaultResolution;
            public readonly bool Upscale;
            public readonly int BakeType;
            public readonly int BakeMethod;

            public BakeJob(BakeObjectDefinition[] bakeObjects, LightDefinition[] lights, SkyboxDefinition skybox, bool upscale = true, int defaultResolution = 1024, BakeType bakeType = Defs.BakeType.DirectAndIndirect, BakeMethod bakeMethod = Defs.BakeMethod.SeparateAlbedo)
            {
                BakeObjects = bakeObjects;
                BakeLights = lights;
                Skybox = skybox;
                DefaultResolution = defaultResolution;
                Upscale = upscale;
                BakeType = (int)bakeType;
                BakeMethod = (int)bakeMethod;
            }
        }
        public enum BakeType
        {
            DirectAndIndirect,
            Direct,
            Indirect
        }
        public enum BakeMethod
        {
            SeparateAlbedo,
            BurnAlbedo
        }
    }
}
