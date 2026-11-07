param([string]$Package = (Join-Path $PSScriptRoot 'dist/DreamDustAutomation'), [switch]$ReportOnly)
$ErrorActionPreference = 'Stop'
$managed = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path 'Probably Stolen_Data/Managed'
$Package = (Resolve-Path -LiteralPath $Package).Path
[xml]$manifest = Get-Content -LiteralPath (Join-Path $Package 'manifest.xml') -Raw
$assemblies = @($manifest.Manifest.Assemblies.Assembly | ForEach-Object { Join-Path $Package ([string]$_) })
$declared = @($manifest.Manifest.Capabilities.Capability | Where-Object { $_ } | ForEach-Object { [string]$_ })
# Execute the installed game's scanner outside Unity. This does not approve,
# enable or modify any mod, game DLL or trust record.
if (-not ('NativeManifestScan' -as [type])) { Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Reflection;
public static class NativeManifestScan {
    public static object Run(string managed, string[] paths) {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
            string path = Path.Combine(managed, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        var game = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));
        var method = game.GetType("ModScanner").GetMethod("Scan");
        var args = method.GetParameters().Select(p => p.DefaultValue).ToArray();
        args[0] = paths;
        // Unity normally populates the loader's game-assembly allowlist during
        // startup. Supply precisely those installed assemblies in this host.
        args[1] = Directory.GetFiles(managed, "*.dll").Select(Path.GetFileNameWithoutExtension).ToArray();
        if (args.Length > 2) args[2] = Path.GetDirectoryName(paths[0]);
        return method.Invoke(null, args);
    }
}
'@
}
$result = [NativeManifestScan]::Run($managed, [string[]]$assemblies)
$missing = @($result.Capabilities | Where-Object { $_ -notin $declared })
$unlisted = @(Get-ChildItem -LiteralPath $Package -Filter '*.dll' -Recurse | Where-Object { $_.FullName -notin $assemblies } | Select-Object -ExpandProperty FullName)
[ordered]@{
    version = [string]$manifest.Manifest.ModVersion
    methods = $result.MethodsScanned
    declared = $declared
    detected = @($result.Capabilities)
    missing = $missing
    unlisted = $unlisted
    refused = $result.Refused
    error = $result.Error
    findings = $result.FormatFindings(100)
    dllHashes = @($assemblies | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash)
} | ConvertTo-Json -Depth 6
if (!$ReportOnly -and ($result.Refused -or $missing.Count -or $unlisted.Count)) {
    throw 'Native mod-scan failed: fix the package before installation or publication.'
}
