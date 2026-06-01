---
name: build-drcom
description: 'Compile, publish, and package the Dr.COM .NET Avalonia client project. Use when you need to build the project, publish NativeAOT/Framework-dependent, or create an Inno Setup installer executable.'
---

# Build Dr.COM Client

## When to Use
- Compile or publish the project.
- You need to generate a self-contained NativeAOT or framework-dependent version.
- You need to generate the `.exe` installer via Inno Setup.

## Build Procedures

### 1. Standard / Framework-Dependent Publish
This mode outputs a lightweight, framework-dependent build to the `publish/` folder. It requires the target machine to have the .NET 8 Runtime installed.

```powershell
dotnet publish CampusNetworkLogin/CampusNetworkLogin.csproj -c Release -o publish -r win-x64
```

### 2. Native AOT Publish (Single File)
This mode compiles the project natively without requiring the .NET runtime to be installed on the target machine. Output goes to the `publish_aot/` folder.

```powershell
dotnet publish CampusNetworkLogin/CampusNetworkLogin.csproj -c Release -p:PublishAot=true -p:PublishTrimmed=true -o publish_aot -r win-x64
```

### 3. Generate Installers (Inno Setup)
After running the publish commands above, you can build the installers using the Inno Setup compiler (`iscc`).

**For the Native AOT Build:** *(Default `setup.iss` maps to AOT)*
```powershell
iscc setup.iss
```

**For the Standard Framework-Dependent Build:** *(Mapped in `setup_aot.iss` but with framework files)*
```powershell
iscc setup_aot.iss
```
