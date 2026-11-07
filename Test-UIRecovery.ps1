$ErrorActionPreference = 'Stop'
$managed = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path 'Probably Stolen_Data/Managed'
$testRoot = Join-Path $PSScriptRoot 'TestResults/ui-recovery'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
[void][Reflection.Assembly]::LoadFrom((Join-Path $managed 'DseCecil.dll'))
$resolver = [DseCecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($managed)
$reader = [DseCecil.ReaderParameters]::new()
$reader.AssemblyResolver = $resolver
$fixture = [DseCecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'Assembly-CSharp.dll'), $reader)
try {
    $locType = $fixture.MainModule.Types | Where-Object FullName -eq 'LocHelper'
    $lookup = [DseCecil.FieldDefinition]::new('HeadlessUiText', ([DseCecil.FieldAttributes]::Public -bor [DseCecil.FieldAttributes]::Static), $fixture.MainModule.TypeSystem.String)
    $locType.Fields.Add($lookup)
    # Only test localization: preserve native RestoreTabs, panel lookup and traversal.
    foreach ($method in $locType.Methods | Where-Object { $_.HasBody -and $_.ReturnType.FullName -eq 'System.String' -and $_.Name.StartsWith('GetLocalized') }) {
        $method.Body.Instructions.Clear()
        $method.Body.ExceptionHandlers.Clear()
        $method.Body.Variables.Clear()
        $il = $method.Body.GetILProcessor()
        if ($method.Name -eq 'GetLocalizedOrDefault') { $il.Append($il.Create([DseCecil.Cil.OpCodes]::Ldsfld, $lookup)) }
        else { $il.Append($il.Create([DseCecil.Cil.OpCodes]::Ldstr, '[headless localization]')) }
        $il.Append($il.Create([DseCecil.Cil.OpCodes]::Ret))
    }
    $fixture.Write((Join-Path $testRoot 'Assembly-CSharp.dll'))
} finally { $fixture.Dispose() }
$trees = [Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($file in @('src/UI/MachineUiNames.cs','src/UI/MachineRegion.cs','src/UI/MachineLayoutPass.cs','tests/UiRecoveryTests.cs')) {
    $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $PSScriptRoot $file))))
}
$refs = [Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($file in Get-ChildItem $managed -Filter '*.dll') { $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($file.FullName)) }
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('DreamDustUiRecoveryTests', $trees, $refs, $options)
$stream = [IO.File]::Create((Join-Path $testRoot 'DreamDustUiRecoveryTests.dll'))
try { $result = $compilation.Emit($stream) } finally { $stream.Dispose() }
if (!$result.Success) { $result.Diagnostics | ForEach-Object ToString; throw 'UI recovery compilation failed' }
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
public static class UiRecoveryHost {
    public static string Run(string managed, string tests) {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
            string name = new AssemblyName(args.Name).Name;
            string path = Path.Combine(name == "Assembly-CSharp" ? tests : managed, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        var assembly = Assembly.LoadFrom(Path.Combine(tests, "DreamDustUiRecoveryTests.dll"));
        return (string)assembly.GetType("UiRecoveryTests").GetMethod("Run").Invoke(null, null);
    }
}
'@
[UiRecoveryHost]::Run($managed, $testRoot)
