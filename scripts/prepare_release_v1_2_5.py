#!/usr/bin/env python3
from pathlib import Path

replacements = {
    "module/Voidstep/SubModule.xml": [("1.2.4", "1.2.5")],
    "scripts/build-ci.ps1": [("1.2.4", "1.2.5")],
    "scripts/package-source.ps1": [("1.2.4", "1.2.5")],
    ".github/workflows/release.yml": [("1.2.4", "1.2.5")],
    "src/Voidstep/VoidstepSubModule.cs": [("1.2.4", "1.2.5")],
    "src/Voidstep/VoidstepMissionBehavior.cs": [("1.2.4", "1.2.5")],
    "scripts/verify_mastery_unlock_invariants.py": [
        ("runtime version literals match v1.2.4", "runtime version literals match v1.2.5"),
        ("Voidstep v1.2.4 active", "Voidstep v1.2.5 active"),
        ("Voidstep v1.2.4 submodule loaded.", "Voidstep v1.2.5 submodule loaded."),
    ],
    "build.ps1": [("$Version = \"1.2.3\"", "$Version = \"1.2.5\"")],
    "src/Voidstep/Voidstep.csproj": [
        ("<Version>1.2.3</Version>", "<Version>1.2.5</Version>"),
        ("<AssemblyVersion>1.2.3.0</AssemblyVersion>", "<AssemblyVersion>1.2.5.0</AssemblyVersion>"),
        ("<FileVersion>1.2.3.0</FileVersion>", "<FileVersion>1.2.5.0</FileVersion>"),
    ],
    "src/Voidstep.Core/Voidstep.Core.csproj": [
        ("<Version>1.2.3</Version>", "<Version>1.2.5</Version>"),
        ("<AssemblyVersion>1.2.3.0</AssemblyVersion>", "<AssemblyVersion>1.2.5.0</AssemblyVersion>"),
        ("<FileVersion>1.2.3.0</FileVersion>", "<FileVersion>1.2.5.0</FileVersion>"),
    ],
}

for name, pairs in replacements.items():
    path = Path(name)
    text = path.read_text(encoding="utf-8")
    for old, new in pairs:
        if old not in text:
            raise SystemExit(f"expected release token missing in {name}: {old}")
        text = text.replace(old, new)
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)

changelog_section = """## 1.2.5

- Added weapon-specific Voidstep Cleave swing presentation for mounted and unmounted one-handed, two-handed and polearm weapons.
- Kept Cleave damage resolution at the established 0.22 seconds while scaling the portion of the weapon animation used to the configured sweep breadth.
- Lowered mounted Cleave trails from rider-chest height to an infantry-torso strike plane and scaled visual reach from the currently wielded weapon length.
- Fixed live Cleave targeting dropping enemies that moved behind the previous angular boundary during the strike; newly observed targets in an already-swept sector are now immediately eligible.
- Re-resolved the wielded melee weapon immediately before Cleave execution while retaining the captured weapon as a safe fallback.
- Removed cross-ability mastery prerequisites so Blink, Windblast, Bend Time, Domino and Dark Vision no longer require Cleave or another unrelated ability path.
- Made Deep Reservoir independent and limited advanced mastery prerequisites to the preceding skill in the same path.
- Changed Singularity to require one rank in each of the six ability foundations; Avatar of the Void retains Singularity 5 and Unbound Power 5.
- Preserved every existing mastery XP value, invested rank, serialized skill ID and v1 save key; no save migration, refund or remapping is required.
- Added regression coverage for mounted Cleave timing and moving-target acquisition, independent mastery foundations, stable serialized IDs and save compatibility.

"""
changelog = Path("CHANGELOG.md")
text = changelog.read_text(encoding="utf-8")
if "## 1.2.5" not in text:
    header = "# Changelog\n\n"
    if not text.startswith(header):
        raise SystemExit("unexpected changelog header")
    text = header + changelog_section + text[len(header):]
    with changelog.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)

release_dir = Path("release")
release_dir.mkdir(exist_ok=True)
notes = """# Voidstep v1.2.5

- Added weapon-specific Voidstep Cleave swing presentation for mounted and unmounted one-handed, two-handed and polearm weapons.
- Kept the actual Cleave hit sweep fast and reliable while making the displayed swing reflect the configured sweep breadth.
- Lowered mounted Cleave effects toward infantry torso height and made their reach reflect the currently wielded weapon.
- Fixed moving enemies being permanently discarded after crossing the live sweep boundary, improving mounted Cleave hit reliability.
- Removed unrelated mastery dependencies: Bend Time no longer requires Windblast, and Blink, Windblast, Domino and Dark Vision no longer require Cleave investment.
- Made Deep Reservoir independent and kept advanced upgrades within their own ability or resource path.
- Reworked Singularity into a shallow convergence requirement across the six ability foundations while retaining the deliberate Avatar of the Void capstone requirements.
- Preserved all existing mastery XP, investments, serialized skill IDs and save keys. Existing saves load without migration or forced respec.
- Added dedicated regression and package validation for the Cleave and mastery changes.

Target: Mount & Blade II: Bannerlord 1.3.15. The Old Realms 1.16 integration remains optional.
"""
with (release_dir / "v1.2.5.md").open("w", encoding="utf-8", newline="\n") as handle:
    handle.write(notes)

marker = """{
  "version": "v1.2.5",
  "channel": "stable",
  "target": "merged-main",
  "description": "Stable release improving mounted Cleave weapon presentation and moving-target reliability while removing unrelated mastery prerequisites with full existing-save compatibility."
}
"""
with (release_dir / "v1.2.5.json").open("w", encoding="utf-8", newline="\n") as handle:
    handle.write(marker)

clean_build_workflow = """name: Windows build and package

on:
  workflow_dispatch:
  pull_request:
    branches: [main, develop]
  push:
    branches: [develop]

permissions:
  contents: read

concurrency:
  group: ${{ github.workflow }}-${{ github.event.pull_request.number || github.ref }}
  cancel-in-progress: true

jobs:
  build:
    runs-on: windows-2022
    steps:
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2
        with:
          ref: ${{ github.event.pull_request.head.sha || github.sha }}
          persist-credentials: false
      - uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5.4.0
        with:
          dotnet-version: 8.0.x
          cache: true
          cache-dependency-path: |
            src/**/*.csproj
            tests/**/*.csproj
      - uses: actions/setup-python@a26af69be951a213d495a4c3e4e4022e16d87065 # v5.6.0
        with:
          python-version: '3.12'
      - name: Build, test, package and verify
        shell: pwsh
        run: ./scripts/build-ci.ps1 -Configuration Release
      - name: Upload installable module
        uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4.6.2
        with:
          name: Voidstep-v1.2.5-Bannerlord-1.3.15
          path: |
            dist/Voidstep-v1.2.5-Bannerlord-1.3.15.zip
            dist/SHA256SUMS.txt
            dist/SOURCE_SHA256.txt
          if-no-files-found: error
"""
with Path(".github/workflows/build.yml").open("w", encoding="utf-8", newline="\n") as handle:
    handle.write(clean_build_workflow)
