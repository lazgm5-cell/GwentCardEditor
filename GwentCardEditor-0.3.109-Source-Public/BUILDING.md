# Building Gwent Card Editor

This repository contains the source code for Gwent Card Editor 0.3.109. The regular application build does not need a GWENT installation and does not modify game files. Optional BepInEx plugins are built separately and do require a local GWENT installation with BepInEx IL2CPP.

## Requirements

- Windows 10 or 11, 64-bit
- .NET 10 SDK
- Internet access for the first NuGet restore
- CPython 3.12.14, 64-bit, only when building the optional card-art runtime
- A local GWENT installation with BepInEx IL2CPP, only when building the optional plugins

## Build and test the editor

Open PowerShell in the repository root and run:

```powershell
dotnet restore Tests\CardEditor.Tests.csproj
dotnet run --project Tests\CardEditor.Tests.csproj -c Release --no-restore
.\Build-Source.ps1
```

The script runs the test suite, publishes the self-contained Windows x64 editor and audio decoder, and stages the Python tool scripts. The application is written to `build-output\Editor\GwentCardEditor.exe`. If `ArtRuntime` has not been prepared, card-art import remains unavailable; other editor functions still build normally.

The main test suite contains 1,510 checks. Optional audio-decoder and animated-frame plugin tests are in `AudioDecode.Tests` and `FramePlugin.Tests`.

## Build the card-art runtime

Install CPython 3.12.14 x64. From the repository root, install the pinned packages into a temporary dependency folder and stage the runtime:

```powershell
python -m pip install --target .\python-deps -r .\requirements-art-runtime.txt
.\Build-ArtRuntime.ps1 -PythonSource 'C:\Path\To\Python312' -DependencySource '.\python-deps'
.\Build-Source.ps1 -OutputDirectory 'build-with-art'
```

`PythonSource` must contain `python.exe`, `python3.dll`, `python312.dll`, the Visual C++ runtime DLLs, `Lib` and `DLLs`. The runtime builder verifies that UnityPy and Pillow can be imported. The generated runtime is a build artifact and is intentionally excluded from source control.

## Optional BepInEx plugins

The deck-visibility, custom-audio and animated-frame plugins are optional. Their source is in `Plugin`, `AudioPlugin`, `FramePlugin` and `Experiments\FieryFiendAnimated\Plugin`. Build them against a local GWENT installation that already has BepInEx IL2CPP:

```powershell
$client = 'C:\Path\To\GWENT'
dotnet build Plugin\GwentCardEditor.DeckVisibility.csproj -c Release -p:ClientPath="$client"
dotnet build AudioPlugin\GwentCardEditor.CustomAudio.csproj -c Release -p:ClientPath="$client"
dotnet build FramePlugin\FieryFiend.csproj -c Release -p:ClientPath="$client"
```

The game and BepInEx assemblies are external build inputs; they are not included in this source repository. The standard editor build does not need them.

The Rogue Mage frame image assets are also not included in this source repository. The application builds without them, but the built-in Rogue Mage frame previews and those frame choices will be unavailable until legally obtained assets are placed in `Assets\RogueFrames`.

## Dependencies and scope

The editor and tests use .NET framework libraries. The audio decoder uses the NuGet package `NAudio.WinMM` 2.2.1. The art runtime dependencies are pinned in `requirements-art-runtime.txt`. Third-party package notices and license files are retained alongside their dependencies in the release build.

The source package must not contain a user's `data_definitions`, extracted client files, personal backups, test-client archives or generated release builds. The editor reads client files only when a user selects a client at runtime.
