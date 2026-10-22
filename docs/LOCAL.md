# Running structure-set-qa locally

This project has two environments:

- **Vendor-independent development:** Windows, macOS, or Linux with .NET 8. Build the F# core, validate clinician YAML, and run synthetic QA. **No Eclipse or ESAPI required.**
- **Clinical adapter development:** a separately configured Eclipse ESAPI plug-in script on a workstation with the correct vendor assemblies. The current repository does not yet provide a compiled ESAPI plug-in entry point or a local clinical QA window.

## 1. Clone

In PowerShell on a Windows development machine:

```powershell
git --version
dotnet --version
mkdir C:\Dev\projects -Force
cd C:\Dev\projects
git clone https://github.com/emintavlayan/structure-set-qa.git
cd structure-set-qa
```

Install the .NET 8 SDK if `dotnet --version` reports no SDK or an SDK that cannot target `net8.0`: https://dotnet.microsoft.com/en-us/download/dotnet/8.0

You can use Neovim (`nvim .`), Visual Studio, or another editor.

## 2. Compile and test the pure engine

```powershell
dotnet test tests/StructureSetQa.Core.Tests/StructureSetQa.Core.Tests.fsproj
```

All tests are synthetic. This needs no patient or treatment-planning-system data.

## 3. Work with clinical YAML rules

Edit the files in `rules/`, with the [rulebook guide](RULES.md) as a reference:

```powershell
dotnet run --project src/StructureSetQa.Cli -- validate
```

This reads `rules/structure-ids.yaml`, `geometry.yaml`, `relationships.yaml`, and `vertebrae.yaml`; any syntax or semantic error returns exit code 1 with a message. Run it after every rule edit.

## 4. Test how names will be recognized

```powershell
dotnet run --project src/StructureSetQa.Cli -- inventory samples/structure-ids.txt
```

Edit `samples/structure-ids.txt` with **structure names only**, one ID per line. No patient identifiers, patient data, or exported images are necessary. The program reports recognized, excluded, unknown, and ambiguous labels, plus duplicates.

## 5. Execute a synthetic structure-set QA report

```powershell
dotnet run --project src/StructureSetQa.Cli -- demo
```

This constructs synthetic organ centers and vertebral labels, runs the actual YAML-configured QA rules, and prints pass / review / not evaluated / error findings. The synthetic rectum deliberately has three connected components, to demonstrate an actionable warning.

The demo exercises the same pure rule evaluator that the ESAPI adapter will use, but **does not inspect the current Eclipse patient or read a DICOM RT structure set**.

All three commands accept an optional rules-directory argument:

```powershell
dotnet run --project src/StructureSetQa.Cli -- validate rules
dotnet run --project src/StructureSetQa.Cli -- demo rules
dotnet run --project src/StructureSetQa.Cli -- inventory samples/structure-ids.txt rules
```

Run these commands from the repository root, or provide a full path to your rules directory and sample ID file.

## 6. Day-to-day Git workflow

```powershell
git status
git pull --ff-only
git switch -c rules/rectum-revision
# edit rules/geometry.yaml ...
dotnet run --project src/StructureSetQa.Cli -- validate
dotnet test tests/StructureSetQa.Core.Tests/StructureSetQa.Core.Tests.fsproj
git add rules/geometry.yaml
git commit -m "Adjust rectum topology screening rules"
git push -u origin rules/rectum-revision
```

Open a pull request so clinical colleagues can review the rule and its rationale. Avoid committing protected clinical data into the repository.

## 7. Current ESAPI integration

The `src/StructureSetQa.Esapi` directory contains read-only adapter source and a workflow that loads the same YAML. It is not currently an executable project: there is no full vendor-referenced .NET Framework project file or Eclipse plug-in entry point yet.

The next integration task is to build the plug-in against the locally installed ESAPI version, extract the current `ScriptContext.StructureSet`, and present findings in a read-only QA report. Keep vendor dependencies outside `StructureSetQa.Core`.
