# Deterministic build

## Inputs

- .NET SDK 8+
- PowerShell
- reference files matching `references/reference-manifest.json`
- pinned NuGet packages in the project files

## Command

```powershell
.\build.ps1
```

Use a separate reference location with:

```powershell
.\build.ps1 -ReferenceRoot "D:\BannerlordRefs\1.3.15-TOR-1.16"
```

## Pipeline

1. Validate required reference files and SHA-256 values.
2. Validate load-bearing ECMA-335 API signatures when Python is present.
3. Restore pinned managed dependencies.
4. Run xUnit pure logic tests.
5. Compile `Voidstep.Core` and `Voidstep` in Release.
6. Copy only `Voidstep.dll` and `Voidstep.Core.dll` into the module template.
7. Reject TaleWorlds, TOR and MCM DLLs in the staged module.
8. Produce `dist/Voidstep-v1.0.0-Bannerlord-1.3.15.zip` with a `Modules/Voidstep` root.
9. Verify required ZIP entries and forbidden dependencies.
10. Print and record ZIP and DLL SHA-256 hashes.

Transport/extraction of proprietary references is intentionally separate from compilation. GitHub Actions expects a repository secret named `BANNERLORD_REFERENCES_ZIP_BASE64` containing a ZIP of the exact reference set.
