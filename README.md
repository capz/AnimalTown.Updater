# AnimalTown.Updater

Standalone GameCube updater written in C# with DolphinDotNet. The output is
`animal-town-updater.dol`. AnimalTown's title menu launches this executable and
passes the selected installation's full GC Loader image path and running build
version. After a verified installation, the updater shows **Power off/on** so
the GC Loader firmware can reopen the replaced image on the next boot.

The complete updater executable is copied into MEM1 before launch; its console
uses the IPL font and has no disc assets. It explicitly unmounts `dvd:/` before
mounting the writable GC Loader card or recovering an interrupted installation,
and refuses to continue if the disc remains mounted. This preserves the source-
disc release fix from [PR #2](https://github.com/capz/AnimalTown.Updater/pull/2).
Image replacement then needs no further executable or asset reads from that
disc. The download streams to SD; it is never buffered in RAM in full.

It reads the installed version from the exact image path passed by AnimalTown,
checks the newest
published [AnimalTown release](https://github.com/capz/AnimalTown/releases),
including prereleases, and selects the direct `AnimalTown-<version>.nkit.iso`
asset. The newest GitHub release is authoritative: different installed/release identities
trigger an update, and equal identities skip downloading. Numeric counters can
decrease after a feature branch is squash-merged, and the final component is an
opaque commit hash, so neither is used as a total ordering.

The ISO streams into checked contiguous storage with a fixed 16 KiB application
buffer. The SDK build reserves a 1 MiB managed heap for CA/HTTP/JSON working data. Its length, GitHub SHA-256 digest, AnimalTown game ID and embedded version
must match before replacement. The download keeps GitHub's filename in a sibling
`.animaltown-update/` staging directory. Installation moves the selected image
to that same path plus `.bak`, then moves the verified release to the selected
image's original path. The recovery marker is that path plus `.update`. A
previous backup is retired only while the
current original still exists. A failed second rename restores the original; a
recognized marker lets the next updater launch recover a cut between renames.
The final backup remains available for manual recovery.

## SD card setup

Use direct GC Loader firmware boot with writable SD-sector support, a FAT32 or
exFAT card, and a working Broadband Adapter with Internet/DNS access. The game
image can keep its chosen filename and folder. For example:

```text
/games/AnimalTown/My Town.nkit.iso
/animal-town-updater.dol
/animaltown-updater/image-path.txt
/animaltown-updater/roots.pem
/animaltown-updater/entropy.bin
/animaltown-updater/utc-offset-minutes.txt
```

The updater DOL is normally embedded in the game image; the SD-root DOL is a
fallback for discs assembled without it. Settings remain in the SD-root
`animaltown-updater/` folder even when the image is in a subfolder.

The documented GC Loader API does not expose the active image's physical
filename. Provision an explicit installation mapping from the trusted computer
that manages the card. `--image` accepts an SD-relative host path, or an absolute
host path inside that SD root, and writes its canonical `gcloader:/...` URI to
`animaltown-updater/image-path.txt`:

HTTPS requires explicit trusted CA roots, fresh private entropy and a correct
clock. On a trusted computer, with the SD mounted, run:

```sh
python3 tools/provision.py /path/to/SD --image 'games/AnimalTown/My Town.nkit.iso' --roots /path/to/ca-bundle.pem --utc-offset-minutes 60
```

This record selects the installation to update. AnimalTown mounts the GC Loader
card and checks the target's GameCube header, `AMTE01` identity and exact current
build version before launching the updater. Available native image arguments or
image-mount metadata can also supply a path; conflicting sources stop Update.
Matching headers cannot distinguish identical copies on a card, so maintain the
mapping for the installation you intend to replace. Update the mapping after
moving or renaming it. With no usable path, the title reports the problem and
does not launch the updater; it never guesses a default filename.

The handoff contains four separate argv entries: updater DOL path,
`--animaltown-update=1`, full image URI, and running build version. The updater
checks that request and revalidates the installed identity/version before
recovery and replacement. Directly starting the updater without that request
stops with a launch-information error.

Set the GameCube clock accurately. The offset is the console clock's offset from
UTC (Amsterdam winter: 60, summer: 120); update it if the console clock or daylight
saving offset changes. The script can use this PC's CA bundle and local UTC
offset by default. Keep the supplied CA bundle within the SDK's 256 KiB limit.

The script generates 48 secret bytes using the host OS CSPRNG. The updater deletes
the seed before using it and clears its array; **each HTTPS attempt consumes one
seed, including failures**. Re-provision before another attempt. Seeds are never
built into the DOL or uploaded as artifacts. The SDK currently has no verified
on-console entropy source, so unattended repeated updates require a future
trusted entropy provider. There is no certificate-validation bypass or HTTP
fallback. Keep the provisioned SD private and safely eject it.

## Build

Install .NET SDK 8+, Python 3, Git, and either Docker or the pinned
libogc2/devkitPPC toolchain used by DolphinDotNet. Then:

```sh
python3 tools/build.py --backend Docker
```

The build pins the SDK changes from [PR #163](https://github.com/capz/DolphinDotNet/pull/163)
and [PR #164](https://github.com/capz/DolphinDotNet/pull/164) by commit in `sdk.lock.json`, creates a local NuGet feed, runs the host tests and
uses the SDK's native build pipeline. This avoids waiting for a published SDK
package. The SDK pin remains `81d74a8a80e67ac7b4a0be3c269ea5506b8a9647`;
the project-local launch-argument bridge preserves and reads the four-entry
request without upgrading that SDK revision. An existing clean SDK checkout at that exact revision can be supplied
with `--sdk-source /path/to/DolphinDotNet`. Outputs and checksums are under
`out/release`. Main-branch CI publishes the DOL/ELF, build manifest and hashes as a
GitHub prerelease; PR CI retains review artifacts. No private provisioned files
are included.

For host checks after preparing the local feed:

```sh
dotnet run --project tests/Updater.Tests.csproj -c Release
```

## Validation and recovery

The automated checks cover SHA-256 padding/multiblock vectors, direct NKit release
selection, prereleases, version comparisons, asset identity, and injected failure
at each install mutation, plus launch-path validation and protocol handoff.
Native GameCube compilation is a separate build gate. Physical GC Loader writes,
BBA HTTPS throughput, firmware reopening after restart, media removal and power
interruption need hardware acceptance. FAT directory
renames are not guaranteed power-loss atomic. Never remove power/card during
installation. A malformed recovery marker fails closed for inspection.

On a later valid launch, the updater recovers a recognized interrupted
transaction using the selected image's `.update` marker and `.bak` file. If the
game cannot boot to make that request, restore the selected path's `.bak` to its
original filename on a computer. Keep unrecognized markers for inspection.
Download/hash rejection happens before installation renames; a failed download
can remain in the sibling staging folder for inspection. The updater does not
touch memory-card town or dev-config saves.

Completion leaves the console on the restart instruction. Power off and on,
then reopen the updated image in GC Loader firmware. The updater does not
attempt an automatic disc/raw-DOL boot after replacing the backing image.

The updater console uses the native IPL font, a light gray canvas and dark gray
text, a vertically centered status block and a 48-pixel safe area on every side.
Both native SD readback and SHA-256 verification yield to the UI and honor
cancellation before publication. Transfer progress displays both percent and binary MiB received/total. Loading
dots fill from left to right, then clear in the same order on a repeating cycle.
Failures retain the stopped phase and last operation, with exception
classification even when the native message is empty, plus the relevant path or
native result when available. Diagnostic text wraps inside the safe area and
does not display a stack trace or assume a backup is intact.
`tests/UiPreview` renders the actual console with deterministic sample states for
Dolphin visual inspection, without requiring a writable GC Loader or network.
