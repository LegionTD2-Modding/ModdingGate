/*
    Mods Gate
    Copyright (C) 2024  Alexandre 'kidev' Poumaroux

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

// ReSharper disable SuggestVarOrType_BuiltInTypes
// ReSharper disable SuggestVarOrType_SimpleTypes
// ReSharper disable SuggestVarOrType_Elsewhere

using System.Reflection;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
using System.IO.Compression;

namespace ModsGate
{
    using C = Constants;
    using Dependency = Dictionary<string, string>;

    [BepInPlugin("org.kidev.ltd2.modsgate", "Mods Gate", "1.0.0")]
    public class ModsGate : BaseUnityPlugin
    {
        private readonly Assembly _assembly = Assembly.GetExecutingAssembly();
        private readonly Harmony _harmony = new("org.kidev.ltd2.modsgate");
        
        private static readonly HttpClient Client = new HttpClient();
        private static ManualLogSource _logger;
        private string _gatewayFileAbs;
        private string _gatewayFileModdedAbs;
        private string _htmlUrl;
        private int _htmlInjectionLine;
        
        public void Awake()
        {
            _logger = Logger;
            _logger.LogInfo("Mods gate loaded!");
            
            _gatewayFileAbs =
                Path.Combine(Paths.GameRootPath, "Legion TD 2_Data", "uiresources", "AeonGT", C.GatewayFileName);
            _gatewayFileModdedAbs =
                Path.Combine(Paths.GameRootPath, "Legion TD 2_Data", "uiresources", "AeonGT", C.GatewayFileNameModded);
            
            try {
                RemoveTempFiles();
                _harmony.PatchAll(_assembly);
            }
            catch (Exception e) {
                Logger.LogError($"Error while patching: {e}");
                throw;
            }

            _ = CheckForUpdatesAsync();
        }
        
        public void OnDestroy() {
            RemoveTempFiles();
        }
        
        private void InjectIntoGateway()
        {
            var lines = File.ReadAllLines(_gatewayFileAbs);
            using (var client = new System.Net.WebClient())
            {
                string htmlContent = client.DownloadString(_htmlUrl);
                lines[_htmlInjectionLine] = htmlContent + Environment.NewLine + lines[_htmlInjectionLine];
            }

            File.WriteAllLines(_gatewayFileModdedAbs, lines);
        }

        private void RemoveTempFiles() {
            if (File.Exists(_gatewayFileModdedAbs)) {
                File.Delete(_gatewayFileModdedAbs);
            }
        }
        
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [SuppressMessage("ReSharper", "UnusedMember.Local")]
        [HarmonyPatch]
        internal static class PatchSendCreateView
        {
            private static Type _typeCoherentUIGTView;
            
            [HarmonyPrepare]
            private static void Prepare() {
                _typeCoherentUIGTView = AccessTools.TypeByName("CoherentUIGTView");
            }
            
            [HarmonyTargetMethod]
            private static MethodBase TargetMethod() {
                return AccessTools.Method(_typeCoherentUIGTView, "SendCreateView");
            }
            
            [HarmonyPrefix]
            private static bool SendCreateViewPre(ref string ___m_Page) {
                if (___m_Page.Equals(C.GatewayFile)) {
                    ___m_Page = C.GatewayFileModded;
                }
                return true;
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                string jsonContent = await FetchFileContentAsync(C.JsonURL);
                ModData modData = JsonConvert.DeserializeObject<ModData>(jsonContent);
                List<PluginInfo> installedPlugins = GetInstalledPlugins();
                _htmlUrl = modData.Core.InjectHtml;
                _htmlInjectionLine = modData.Core.InjectLine;
                
                InjectIntoGateway();

                _ = modData.Mods.Prepend(
                    new Mod(modData.Core.Name, 
                        modData.Core.Author, 
                        modData.Core.IconUrl,
                        modData.Core.Url,
                        modData.Core.Version,
                        modData.Core.GameVersion,
                        modData.Core.Description
                    )
                );

                foreach (var mod in modData.Mods)
                {
                    var installedPlugin = installedPlugins.FirstOrDefault(p => p.Metadata.Name == mod.Name);
                    if (installedPlugin == null) continue;
                    Version installedVersion = new Version(installedPlugin.Metadata.Version.ToString());
                    Version jsonVersion = new Version(mod.Version);

                    mod.ReplaceVersionInUrls();

                    if (jsonVersion <= installedVersion) continue;
                    _logger.LogInfo($"Update available for {mod.Name}: {installedVersion} -> {jsonVersion}");
                    await UpdateModAsync(mod, installedPlugin);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during update check: {ex.Message}");
            }
        }

        private static async Task<string> FetchFileContentAsync(string url)
        {
            HttpResponseMessage response = await Client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        private static List<PluginInfo> GetInstalledPlugins()
        {
            return BepInEx.Bootstrap.Chainloader.PluginInfos.Values.ToList();
        }

        private static async Task UpdateModAsync(Mod mod, PluginInfo installedPlugin)
        {
            try
            {
                string downloadUrl = mod.GetUrlForOS();
                string zipPath = Path.Combine(Paths.PluginPath, $"{mod.Name}_update.zip");
                string extractPath = Path.Combine(Paths.PluginPath, mod.Name);

                _logger.LogInfo($"Downloading {downloadUrl} to {extractPath}");
                using (var response = await Client.GetAsync(downloadUrl))
                using (var fs = new FileStream(zipPath, FileMode.CreateNew))
                {
                    await response.Content.CopyToAsync(fs);
                }

                string backupPath = Path.Combine(Paths.PluginPath,
                    $"{mod.Name}_v{installedPlugin.Metadata.Version}.outdated");
                Directory.Move(extractPath, backupPath);

                ZipFile.ExtractToDirectory(zipPath, extractPath);

                File.Delete(zipPath);

                _logger.LogInfo($"Successfully updated {mod.Name} to version {mod.Version}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating {mod.Name}: {ex.Message}");
            }
        }
    }

    [SuppressMessage("ReSharper", "CollectionNeverUpdated.Global")]
    public class ModData
    {
        public Core Core { get; set; }
        public List<Mod> Mods { get; set; }
    }

    public class Core
    {
        public string Name { get; set; }
        public string Author { get; set; }
        public string IconUrl { get; set; }
        public Dependency Url { get; set; }
        public string Version { get; set; }
        public string GameVersion { get; set; }
        public string Description { get; set; }
        public string InjectHtml { get; set; }
        public int InjectLine { get; set; }
        public List<Dependency> Dependencies { get; set; }
        public List<Dependency> DependenciesVersions { get; set; }
        public List<Dependency> Installers { get; set; }
        public string Signatures { get; set; }
    }

    public class Mod(
        string modName,
        string author,
        string iconUrl,
        Dependency url,
        string version,
        string gameVersion,
        string description)
    {
        public string Name { get; set; } = modName;
        public string Author { get; set; } = author;
        public string IconUrl { get; set; } = iconUrl;
        public Dependency Url { get; set; } = url;
        public string Version { get; set; } = version;
        public string GameVersion { get; set; } = gameVersion;
        public string Description { get; set; } = description;

        public string GetUrlForOS()
        {
            if (Url.TryGetValue("*", out var fromMap))
            {
                return fromMap;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Url.TryGetValue("win", out var valueFromMap))
            {
                return valueFromMap;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && Url.TryGetValue("linux", out var map1))
            {
                return map1;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && Url.TryGetValue("mac", out var fromMap1))
            {
                return fromMap1;
            }

            throw new Exception($"Unsupported OS: {RuntimeInformation.OSDescription}");
        }

        public void ReplaceVersionInUrls()
        {
            if (Url == null || Version == null)
                return;

            var keys = new List<string>(Url.Keys); // To avoid modifying the dictionary while iterating
            foreach (var key in keys)
            {
                // Replace all occurrences of '$' with the value of Version
                Url[key] = Url[key].Replace("$", Version);
            }
        }
    }
    
    internal static class Constants
    {
        internal const string GatewayFileName = "gateway.html";
        internal const string GatewayFileNameModded = "__gateway.html";
        internal const string GatewayFile = "coui://uiresources/AeonGT/gateway.html";
        internal const string GatewayFileModded = "coui://uiresources/AeonGT/__gateway.html";
        internal const string JsonURL = "https://raw.githubusercontent.com/LegionTD2-Modding/.github/main/mods/config.json";
    }
}