# AnimalTown.Updater

Standalone GameCube updater written in C# with DolphinDotNet. The output is
`animal-town-updater.dol`. AnimalTown's title menu launches this executable and
passes its running build version. The updater discovers the highest stable
AnimalTown image on GC Loader storage, downloads the new stable release beside
it with the published filename, switches GC Loader to that file and verifies
physical disc readback, then deletes the selected old image and exits into Swiss
to autoload the downloaded ISO.

The complete updater executable is copied into MEM1 before launch; its console
uses the IPL font and has no disc assets. It explicitly unmounts `dvd:/` before
mounting the writable GC Loader card or changing an installation,
and refuses to continue if the disc remains mounted. This preserves the source-
disc release fix from [PR #2](https://github.com/capz/AnimalTown.Updater/pull/2).
Image replacement then needs no further executable or asset reads from that
disc. The download streams to SD; it is never buffered in RAM in full.

The scan walks the GC Loader volume and considers versioned
`AnimalTown-MAJOR.MINOR.PATCH.iso` and `.nkit.iso` filenames. Versions compare
numerically, so `0.11.10` sorts after `0.11.9`. Commit-hash/development suffixes,
backups and partial downloads are excluded. The selected image must also have
an AnimalTown header matching its filename. Equal versions use a deterministic,
ASCII-case-insensitive path ordering. The selected file is the installation
to replace: GC Loader does not reveal which physical filename booted the game.
The game does not require an `image-path.txt` mapping to start this updater.

The download feed selects a published numeric-version
[AnimalTown release](https://github.com/capz/AnimalTown/releases), with a direct
`AnimalTown-<version>.nkit.iso` asset and GitHub SHA-256 digest. Hash-suffixed
development releases are excluded. Numeric main builds remain eligible even
while GitHub marks them as prereleases pending hardware acceptance. Metadata is
read in bounded, paginated responses. An equal or newer installed stable version
is kept.

The ISO streams into checked contiguous storage with a fixed 16 KiB application
buffer. The SDK build reserves a 1 MiB managed heap for CA/HTTP/JSON working data. Its length, GitHub SHA-256 digest, AnimalTown game ID and embedded version
must match before deletion. The new ISO keeps GitHub's filename in the selected
old image's directory; it is never renamed to the old filename. Contiguous-file
publication and card readback complete before GC Loader's hardware image mapping
is changed. The SDK then reads the physical DVD interface and compares the whole
boot header with the verified file. Only a successful map-and-readback permits
deletion of the selected old ISO. An already-current installation is selected
and verified in the same way without deleting it. Fragmented images cannot be
selected by this updater; a newly downloaded image uses contiguous allocation.
Other installations and development images are left alone. A failed download,
verification, disc selection, helper preparation or cancellation preserves the
selected old ISO. A failed selection can leave the drive mapping changed; both
files remain available for manual recovery.

## SD card setup

Use direct GC Loader firmware boot with writable SD-sector support, a FAT32 or
exFAT card, and a working Broadband Adapter with Internet/DNS access. The game
image needs a stable versioned filename for discovery. For example:

```text
/games/AnimalTown/AnimalTown-0.11.36.nkit.iso
/animal-town-updater.dol
/animaltown-updater/swiss.dol
/animaltown-updater/roots.pem
/animaltown-updater/entropy.bin
/animaltown-updater/utc-offset-minutes.txt
```

The updater DOL is normally embedded in the game image; the SD-root DOL is a
fallback for discs assembled without it. Settings remain in the SD-root
`animaltown-updater/` folder even when the image is in a subfolder.

Install a Swiss DOL from an official Swiss release with the setup tool.
The SDK stages and validates that executable before deleting an ISO, then passes
`Autoload=gcldr:/.../<published filename>` to Swiss. The updater selects and
verifies the new physical mapping before deleting the old image. Swiss performs
its normal image/ODE setup and apploader boot after the handoff. No
replacement-game raw DOL is launched against the old disc mapping.

HTTPS requires explicit trusted CA roots, fresh private entropy and a correct
clock. On a trusted computer, with the SD mounted, run:

```sh
python3 tools/provision.py /path/to/SD --swiss /path/to/swiss.dol --roots /path/to/ca-bundle.pem --utc-offset-minutes 60
```

Later provisioning can reuse the installed Swiss helper by omitting `--swiss`.
The optional `--image` argument remains for older games that require a legacy
mapping; the new updater ignores that mapping and discovers stable images.

The game handoff contains three separate argv entries: updater DOL path,
`--animaltown-update=2`, and running build version. Legacy v1 requests remain
readable for compatibility. The updater's SD scan chooses the installation;
the launch version does not select a filesystem path.

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

The build pins the SDK's verified physical image selection from
[PR #178](https://github.com/capz/DolphinDotNet/pull/178) by commit in
`sdk.lock.json`, creates a local NuGet feed, runs the host tests and uses the
SDK's native build pipeline. This avoids waiting for a published SDK package.
The SDK pin is `178d6cdaa3c08134d2a22330659000592c9ff5b0`; the project-local
launch-argument bridge preserves and reads the versioned request.
An existing clean SDK checkout at that exact revision can be supplied
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
selection, exclusion of development builds, numeric version comparisons,
directory scanning, asset identity, selection-before-deletion ordering and
injected selection/deletion failures, plus launch-path validation and protocol
handoff.
Native GameCube compilation is a separate build gate. Physical GC Loader writes,
BBA HTTPS throughput, Swiss autoload/ODE selection, media removal and power
interruption need hardware acceptance. FAT directory
renames are not guaranteed power-loss atomic. Never remove power/card during
installation.

Publication and verified physical disc selection precede deletion, so a power
cut can leave both complete images;
the next scan selects the highest valid stable version. A partial download is
not an installed candidate. Existing valid new-image collisions are verified
before reuse, and damaged collisions stop the update rather than overwriting
an unrelated file. The updater does not touch memory-card town or dev-config
saves. Legacy `.bak` and `.update` files are not stable-image candidates.

Completion requests the SDK's Swiss handoff and returns successfully from Main,
releasing updater UI/network resources before Swiss starts the selected image.
A missing or invalid Swiss helper stops the operation before old-image deletion.

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
