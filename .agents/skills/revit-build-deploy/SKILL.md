---
name: revit-build-deploy
description: Configuration and procedures for Release and Debug builds. Use when configuring build profiles or deploying add-in builds.
---

# Revit Build & Deploy Playbook (`revit-build-deploy`)

Use this playbook to configure and execute Debug and Release builds across multiple target Revit versions.

## 1. Compilation Targets & Output Directory

All project `.csproj` compilations output to the common directory:
`output/Synthetic/`

## 2. Debug Build Profile

The Debug profile is optimized for local developer debugging and testing.

### Manifest Assembly Pathing
The build automatically executes the custom MSBuild task `ReplaceAddinPath` to update the `<Assembly>` tag in the local `.addin` manifest file to point to the active build output DLL (e.g., `output/Synthetic/Synthetic202X.dll`), enabling Revit to load the DLL directly from the workspace.

### Local Code Signing
To bypass Revit's publisher warnings, MSBuild automatically executes `signtool.exe` to sign the Debug DLL with a local self-signed certificate named `"Synthetic"`.

### Debug Manifest Deployment
To load the debug build in Revit:
* Copy the modified `.addin` manifest file from `output/Synthetic/` to the Revit Addins folder.
* *Note: A task is underway to automate copying the debug `.addin` to `%APPDATA%\Autodesk\Revit\Addins\<Year>\` to avoid ProgramData write permission issues.*

## 3. Release Build Profile

The Release profile compiles optimized assemblies for production distribution.

### Manifest Assembly Pathing
The build updates the `<Assembly>` tag in the `.addin` manifest to point to the production installation path:
`C:\ProgramData\Autodesk\Revit\Addins\Synthetic\Synthetic202X.dll`

### Code Signing
Release builds do **not** require any code signing.

### Production Deployment
To deploy release builds to the production UNC server:
1. Compile the solution in `Release` configuration:
   ```powershell
   & "C:\Program-Files-Path-to-MSBuild\MSBuild.exe" src/Synthetic.sln /t:Build /p:Configuration=Release /p:Platform="Any CPU"
   ```
2. Run the deployment batch file `Deploy to INC Server.bat` (based on `build/Deploy-To-INC-Server.template.bat`) to mirror the build output, manifests, and settings configurations to the server folder using `robocopy`.
