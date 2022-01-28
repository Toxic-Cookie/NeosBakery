using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using Newtonsoft.Json;

namespace NeosBakery.Core
{
    static class Paths
    {
        public static readonly string AssetsPath = AppDomain.CurrentDomain.BaseDirectory + @"\\nml_mods\\_NeosBakery\\Assets\\";
        public static readonly string OutputPath = AppDomain.CurrentDomain.BaseDirectory + @"\\nml_mods\\_NeosBakery\\Output\\";

        public static readonly string MeshesPath = AppDomain.CurrentDomain.BaseDirectory + @"\\nml_mods\\_NeosBakery\\Assets\\Meshes\\";
        public static readonly string MaterialsPath = AppDomain.CurrentDomain.BaseDirectory + @"\\nml_mods\\_NeosBakery\\Assets\\Materials\\";
        public static readonly string TexturesPath = AppDomain.CurrentDomain.BaseDirectory + @"\\nml_mods\\_NeosBakery\\Assets\\Textures\\";

        public static readonly string BakeJobPath = AppDomain.CurrentDomain.BaseDirectory + @"\\nml_mods\\_NeosBakery\\BakeJob.json";
        public static readonly string BakePyPath = AppDomain.CurrentDomain.BaseDirectory + @"\\nml_mods\\_NeosBakery\\bake.py";

        public static string BlenderPath { get; private set; } = @"C:\\Program Files\\Blender Foundation\\Blender 3.0\\blender.exe";
        public static void SetBlenderPath(string newPath)
        {
            BlenderPath = newPath;
            File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(new Config(newPath), Formatting.Indented));
        }
        public static readonly string BakeSettingsPath = AppDomain.CurrentDomain.BaseDirectory + @"\\nml_mods\\_NeosBakery\\BakeSettings.json";
        public static readonly string ConfigPath = AppDomain.CurrentDomain.BaseDirectory + @"\\nml_mods\\_NeosBakery\\Config.json";

        static Paths()
        {
            if (File.Exists(ConfigPath))
            {
                BlenderPath = JsonConvert.DeserializeObject<Config>(File.ReadAllText(ConfigPath)).BlenderPath;
            }
            else
            {
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(new Config(@"C:\\Program Files\\Blender Foundation\\Blender 3.0\\blender.exe"), Formatting.Indented));
            }
        }

        public static void EnsureAllPathsExist()
        {
            if (!Directory.Exists(AssetsPath))
            {
                Directory.CreateDirectory(AssetsPath);
            }
            if (!Directory.Exists(OutputPath))
            {
                Directory.CreateDirectory(OutputPath);
            }
            if (!Directory.Exists(MeshesPath))
            {
                Directory.CreateDirectory(MeshesPath);
            }
            if (!Directory.Exists(TexturesPath))
            {
                Directory.CreateDirectory(TexturesPath);
            }
            if (!Directory.Exists(MaterialsPath))
            {
                Directory.CreateDirectory(MaterialsPath);
            }
        }
        public static void RegeneratePath(string path)
        {
            Directory.Delete(path, true);
            Directory.CreateDirectory(path);
        }

        struct Config
        {
            public string BlenderPath;

            public Config(string blenderPath)
            {
                BlenderPath = blenderPath;
            }
        }
    }
}
