/*
    AutoHackGame
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

using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DiffMatchPatch;
using HarmonyLib;
using Patch = DiffMatchPatch.Patch;

namespace AutoHackGame;

using C = Constants;

public class UIPatcher
{
    private readonly Harmony _harmony = new("org.kidev.ltd2.uipatcher");
    
    private readonly string _baseDirectory;
    private readonly string _modsDirectory;
    private readonly diff_match_patch _dmp;
    private readonly List<string> _patchedFiles;

    public UIPatcher(Assembly assembly, string baseDirectory)
    {
        _baseDirectory = baseDirectory;
        _modsDirectory = Path.Combine(_baseDirectory, "mods");
        _dmp = new diff_match_patch();
        _patchedFiles = new List<string>();
        
        try
        {
            _harmony.PatchAll(assembly);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Can't patch using harmony: {e.Message}");
        }
    }

    public async Task DownloadAndApplyPatchesAsync(string patchZipUrl)
    {
        string patchZipPath = await DownloadFileAsync(patchZipUrl);
        ExtractZip(patchZipPath, _modsDirectory);

        ApplyPatches(_modsDirectory);
        ApplySpecialPatchToGateway();
    }

    private async Task<string> DownloadFileAsync(string url)
    {
        using var client = new HttpClient();
        byte[] fileBytes = await client.GetByteArrayAsync(url);
        string filePath = Path.Combine(_baseDirectory, "Patches.zip");
        await File.WriteAllBytesAsync(filePath, fileBytes);
        return filePath;
    }

    private void ExtractZip(string zipPath, string extractPath)
    {
        ZipFile.ExtractToDirectory(zipPath, extractPath, true);
        File.Delete(zipPath);
    }

    private void ApplyPatches(string directory)
    {
        foreach (string filePath in Directory.GetFiles(directory, "*.patch", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(_modsDirectory, filePath);
            string originalFilePath = Path.Combine(_baseDirectory, relativePath.Replace(".patch", ""));
            string patchedFilePath = Path.Combine(Path.GetDirectoryName(originalFilePath) ?? string.Empty, $"__{Path.GetFileName(originalFilePath)}");

            string patchContent = File.ReadAllText(filePath);
            string originalContent = File.Exists(originalFilePath) ? File.ReadAllText(originalFilePath) : "";

            List<Patch> patches = _dmp.patch_fromText(patchContent);
            Object[] patchResult = _dmp.patch_apply(patches, originalContent);
            string patchedContent = (string)patchResult[0];

            File.WriteAllText(patchedFilePath, patchedContent);

            if (Path.GetFileName(originalFilePath) != "gateway.html")
            {
                _patchedFiles.Add(relativePath.Replace(".patch", ""));
            }
        }
    }

    private void ApplySpecialPatchToGateway()
    {
        string gatewayPath = Path.Combine(_baseDirectory, "gateway.html");
        string patchedGatewayPath = Path.Combine(_baseDirectory, "__gateway.html");

        if (File.Exists(patchedGatewayPath))
        {
            string content = File.ReadAllText(patchedGatewayPath);

            foreach (string patchedFile in _patchedFiles)
            {
                string originalPath = patchedFile;
                string patchedPath = Path.Combine(Path.GetDirectoryName(patchedFile) ?? string.Empty, $"__{Path.GetFileName(patchedFile)}");

                content = Regex.Replace(content, Regex.Escape(originalPath), patchedPath);
            }

            File.WriteAllText(patchedGatewayPath, content);
        }
    }

    public void CleanupPatchedFiles()
    {
        foreach (string filePath in Directory.GetFiles(_baseDirectory, "__*", SearchOption.AllDirectories))
        {
            File.Delete(filePath);
        }
        _harmony.UnpatchSelf();
    }
    
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    [HarmonyPatch]
    internal static class PatchSendCreateView
    {
        private static Type _typeCoherentUIGTView;

        [HarmonyPrepare]
        private static void Prepare()
        {
            _typeCoherentUIGTView = AccessTools.TypeByName("CoherentUIGTView");
        }

        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(_typeCoherentUIGTView, "SendCreateView");
        }

        [HarmonyPrefix]
        private static bool SendCreateViewPre(ref string ___m_Page)
        {
            if (___m_Page.Equals(C.GatewayOldPath))
            {
                ___m_Page = C.GatewayNewPath;
            }

            return true;
        }
    }
}

internal static class Constants
{
    private const string GatewayFileName = "gateway.html";
    private const string PatchedFolder = "coui://uiresources/AeonGT/";
    internal const string GatewayOldPath = $"{PatchedFolder}/{GatewayFileName}";
    internal const string GatewayNewPath = $"{PatchedFolder}/__{GatewayFileName}";
    internal const string UpdatedModsDataEvent = "UpdatedModsData";
}