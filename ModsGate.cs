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
using System.Text;
using AutoHackGame;

namespace ModsGate;

using C = Constants;

[BepInPlugin("org.kidev.ltd2.modsgate", "Mods Gate", "1.0.0")]
public class ModsGate : BaseUnityPlugin
{
    private static readonly HttpClient Client = new HttpClient();
    private static ManualLogSource _logger;
    private UIPatcher _patcher;
    private List<Mod> _mods;

    private Mod GetMod(string modName)
    {
        return _mods.FirstOrDefault(m => m.Name == modName);
    }

    public void Awake()
    {
        _logger = Logger;
        _logger.LogInfo("Mods gate loaded!");
        
        _patcher = new UIPatcher(Assembly.GetExecutingAssembly(), Path.Combine(Paths.GameRootPath, "Legion TD 2_Data", "uiresources", "AeonGT"));

        _ = CheckForUpdatesAsync();
    }

    public void OnDestroy()
    {
        _patcher.CleanupPatchedFiles();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            string jsonContent = await FetchFileContentAsync(C.JsonURL);
            ModsConfig modsConfig = JsonConvert.DeserializeObject<ModsConfig>(jsonContent);

            await _patcher.DownloadAndApplyPatchesAsync(modsConfig.Core.UIPatches);
            
            List<PluginInfo> installedPlugins = GetInstalledPlugins();
            
            _logger.LogInfo("Before...");
            _logger.LogInfo(modsConfig);

            modsConfig.Mods = modsConfig.Mods.Prepend(
                new Mod(modsConfig.Core.Name,
                    modsConfig.Core.GUID,
                    modsConfig.Core.Author,
                    modsConfig.Core.IconUrl,
                    modsConfig.Core.Url,
                    modsConfig.Core.Version,
                    modsConfig.Core.GameVersion,
                    modsConfig.Core.Description
                )
            ).ToList();

            foreach (var mod in modsConfig.Mods)
            {
                var installedPlugin = installedPlugins.FirstOrDefault(p => p.Metadata.Name == mod.Name);
                if (installedPlugin == null) continue;
                Version installedVersion = new Version(installedPlugin.Metadata.Version.ToString());
                Version jsonVersion = new Version(mod.Version);

                mod.ReplaceVersionInUrls();
                
                HudApi.TriggerHudEvent(C.UpdatedModsDataEvent, mod.GUID, mod.ToString());

                if (jsonVersion <= installedVersion) continue;
                _logger.LogInfo($"Update available for {mod.Name}: {installedVersion} -> {jsonVersion}");
                await UpdateModAsync(mod, installedPlugin);
            }
            
            _mods = modsConfig.Mods;

            _logger.LogInfo("After...");
            _logger.LogInfo(modsConfig);
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
    
    public async Task InstallMod(string modName)
    {
        var mod = GetMod(modName);

        if (mod == null)
        {
            throw new Exception($"Mod {modName} not found");
        }

        using var client = new HttpClient();
        var response = await client.GetAsync(mod.Url["*"]);
        response.EnsureSuccessStatusCode();

        var dllBytes = await response.Content.ReadAsByteArrayAsync();

        await File.WriteAllBytesAsync(mod.GetDllFilePath(), dllBytes);
        _logger.LogInfo($"Mod {modName} installed successfully.");
    }

    public void UninstallMod(string modName)
    {
        var mod = GetMod(modName);

        if (mod == null)
        {
            throw new Exception($"Mod {modName} not found");
        }

        var dllPath = mod.GetDllFilePath();

        if (File.Exists(dllPath))
        {
            File.Delete(dllPath);
            _logger.LogInfo($"Mod {modName} uninstalled successfully.");
        }
        else
        {
            _logger.LogInfo($"Mod {modName} not found in plugins folder.");
        }
    }

    public void DeactivateMod(string modName)
    {
        var mod = GetMod(modName);

        if (mod == null)
        {
            throw new Exception($"Mod {modName} not found");
        }

        var dllPath = mod.GetDllFilePath();
        var dllDeactivatedPath = $"{dllPath}.deactivated";

        if (File.Exists(dllPath) && !File.Exists(dllDeactivatedPath))
        {
            File.Move(dllPath, dllDeactivatedPath);
            _logger.LogInfo($"Mod {modName} deactivated successfully.");
        }
        else
        {
            _logger.LogInfo($"Mod {modName} is already deactivated or not found.");
        }
    }

    public void ReactivateMod(string modName)
    {
        var mod = GetMod(modName);

        if (mod == null)
        {
            throw new Exception($"Mod {modName} not found");
        }

        var dllPath = mod.GetDllFilePath();
        var dllDeactivatedPath = $"{dllPath}.deactivated";

        if (File.Exists(dllDeactivatedPath) && !File.Exists(dllPath))
        {
            File.Move(dllDeactivatedPath, dllPath);
            _logger.LogInfo($"Mod {modName} reactivated successfully.");
        }
        else
        {
            _logger.LogInfo($"Mod {modName} is not deactivated or the activated file already exists.");
        }
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
            await using (var fs = new FileStream(zipPath, FileMode.CreateNew))
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

public class Mod(
    string name,
    string guid,
    string author,
    string iconUrl,
    Dictionary<string, string> url,
    string version,
    string gameVersion,
    string description)
{
    [JsonProperty("name")] public string Name { get; set; } = name;
    [JsonProperty("guid")] public string GUID { get; set; } = guid;
    [JsonProperty("author")] public string Author { get; set; } = author;
    [JsonProperty("icon_url")] public string IconUrl { get; set; } = iconUrl;
    [JsonProperty("url")] public Dictionary<string, string> Url { get; set; } = url;
    [JsonProperty("version")] public string Version { get; set; } = version;
    [JsonProperty("game_version")] public string GameVersion { get; set; } = gameVersion;
    [JsonProperty("description")] public string Description { get; set; } = description;

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

    public string GetDllFilePath()
    {
        return Path.Combine(Paths.PluginPath, $"{Name}.dll");
    }
    
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Mod: {Name}");
        sb.AppendLine($"  GUID: {GUID}");
        sb.AppendLine($"  Author: {Author}");
        sb.AppendLine($"  Icon URL: {IconUrl}");
        sb.AppendLine($"  URLs:");
        foreach (var kvp in Url)
        {
            sb.AppendLine($"    {kvp.Key}: {kvp.Value}");
        }
        sb.AppendLine($"  Version: {Version}");
        sb.AppendLine($"  Game Version: {GameVersion}");
        sb.AppendLine($"  Description: {Description}");
        return sb.ToString();
    }
}

public class Core
{
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("guid")] public string GUID { get; set; }   
    [JsonProperty("author")] public string Author { get; set; }
    [JsonProperty("icon_url")] public string IconUrl { get; set; }
    [JsonProperty("url")] public Dictionary<string, string> Url { get; set; }
    [JsonProperty("version")] public string Version { get; set; }
    [JsonProperty("game_version")] public string GameVersion { get; set; }
    [JsonProperty("description")] public string Description { get; set; }
    [JsonProperty("ui_patches")] public string UIPatches { get; set; }
    [JsonProperty("dependencies")] public List<Dictionary<string, string>> Dependencies { get; set; }
    [JsonProperty("dependencies_versions")] public List<DependencyVersion> DependencyVersions { get; set; }
    [JsonProperty("installers")] public Dictionary<string, string> Installers { get; set; }
    [JsonProperty("signatures")] public string Signatures { get; set; }
    
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Core: {Name}");
        sb.AppendLine($"  GUID: {GUID}");
        sb.AppendLine($"  Author: {Author}");
        sb.AppendLine($"  Icon URL: {IconUrl}");
        sb.AppendLine($"  URLs:");
        foreach (var kvp in Url)
        {
            sb.AppendLine($"    {kvp.Key}: {kvp.Value}");
        }
        sb.AppendLine($"  Version: {Version}");
        sb.AppendLine($"  Game Version: {GameVersion}");
        sb.AppendLine($"  Description: {Description}");
        sb.AppendLine($"  UI Patches: {UIPatches}");
        sb.AppendLine("  Dependencies:");
        foreach (var dep in Dependencies)
        {
            sb.AppendLine($"    {string.Join(", ", dep.Select(kvp => $"{kvp.Key}: {kvp.Value}"))}");
        }
        sb.AppendLine("  Dependency Versions:");
        foreach (var depVer in DependencyVersions)
        {
            sb.AppendLine($"    {depVer}");
        }
        sb.AppendLine("  Installers:");
        foreach (var kvp in Installers)
        {
            sb.AppendLine($"    {kvp.Key}: {kvp.Value}");
        }
        sb.AppendLine($"  Signatures: {Signatures}");
        return sb.ToString();
    }
}

public class DependencyVersion
{
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("version")] public string Version { get; set; }
    
    public override string ToString()
    {
        return $"DependencyVersion: {Name} (Version: {Version})";
    }
}

public class ModsConfig
{
    [JsonProperty("core")] public Core Core { get; set; }
    [JsonProperty("mods")] public List<Mod> Mods { get; set; }
    
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("### CONFIG.JSON ###:");
        sb.AppendLine("Core:");
        sb.AppendLine(Core.ToString());
        sb.AppendLine("Mods:");
        foreach (var mod in Mods)
        {
            sb.AppendLine(mod.ToString());
        }
        return sb.ToString();
    }
}

internal static class Constants
{
    internal const string JsonURL = "https://raw.githubusercontent.com/LegionTD2-Modding/.github/main/mods/config.json";
    internal const string UpdatedModsDataEvent = "UpdatedModsData";
}