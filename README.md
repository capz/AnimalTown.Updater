# AnimalTown.Updater

Standalone GameCube updater written in C# with DolphinDotNet. The output is
`animal-town-updater.dol`. AnimalTown's title menu launches this executable and
passes its running build version. The updater discovers the highest stable
AnimalTown image on GC Loader storage, downloads the new stable release beside
it with the published filename, switches GC Loader to that file and verifies
physical disc readback, then deletes the selected old image and exits into Swiss
to boot the downloaded ISO without stopping at its game-information screen.

Swiss receives separate `Autoload=<image>`, `AutoBoot=Yes`, `BS2Boot=No` and
`Prefer Clean Boot=No` arguments, with its executable path as argv[0]. Both a
newly installed image and the already-current path use this handoff. Swiss is
still the image loader; its own startup or error screens can appear, and holding
B retains Swiss's intentional autoboot override. This restarts the game; it does
not restore the previous process's live memory.

## Download scheduling

The update workflow starts explicitly on the SDK background executor. Its
continuations, HTTP reads, hashing and SD writes remain there while the UI runs
on the application executor. `Scheduler.ProductiveWaits` releases the managed
execution gate during synchronous GPU/VSync waits. VSync stays enabled and
controller cancellation stays on the UI executor.

`async/await` by itself does not make this change: starting the old workflow on
the UI executor captured that executor for its continuations, and productive
frame waits are opt-in in the pinned SDK. The SDK HTTP stream returns at most
4 KiB per read despite the updater's 16 KiB application buffer. One such read
per 50/60 Hz UI frame gives approximately 200/240 KiB/s, consistent with the
reported roughly 200 kB/s. This is a scheduling explanation, not a measured
hardware diagnosis or a configured BBA bandwidth limit.

The BBA has a 10/100 Mbit/s Ethernet link, but EXI transfers, TCP receive-window
size/latency, TLS and SHA-256 work on the GameCube CPU, and SD writes also limit
end-to-end HTTPS throughput. This change does not force link speed/duplex, alter
TCP windows, weaken HTTPS verification or increase the 1 MiB managed heap.
Physical throughput still needs console measurement.

`tests/test_native_worker.py` executes the production worker wrapper with the
real pinned SDK AOT scheduler and execution gate. A bounded scheduling workload
advanced 128 steps over 10 simulated UI frames, with progress during seven
20 ms presentation waits; it also checks continuation context, cancellation
and error propagation. These are host scheduling results, not BBA transfer
rates. Native scan tests additionally retain real updater reachability, the
1 MiB heap and simulated 60 Hz UI waits across wide, candidate-heavy and deep
directory trees.

The complete updater executable, including its public HTTPS trust anchors, is
copied into MEM1 before launch; its console uses the IPL font and needs no disc
assets after launch. It explicitly unmounts `dvd:/` before
mounting the writable GC Loader card or changing an installation,
and refuses to continue if the disc remains mounted. This preserves the source-
disc release fix from [PR #2](https://github.com/capz/AnimalTown.Updater/pull/2).
Image replacement then needs no further executable or asset reads from that
disc. The download streams to SD; it is never buffered in RAM in full.

The scan walks the GC Loader volume and reads `.iso` and `.gcm` image headers,
including `.nkit.iso`, regardless of their basename. AnimalTown already embeds
`AnimalTown / MAJOR.MINOR.PATCH` at boot-header offset `0x20`, before NKit metadata
at `0x200`; renaming the file does not change this identity. The embedded version
is authoritative, even when the filename claims another version. Versions compare
numerically, so `0.11.10` sorts after `0.11.9`. Embedded commit-hash/development
versions, foreign or truncated headers, backups and partial downloads are excluded,
as are updater and SDK transaction/staging paths. The scan reuses one 1,088-byte
header buffer and closes each file before proceeding. Equal versions use a deterministic,
ASCII-case-insensitive path ordering. The selected file is the installation
to replace: GC Loader does not reveal which physical filename booted the game.
The game does not require an `image-path.txt` mapping to start this updater.

The updater reads
[`releases/latest/download/update.json`](https://github.com/capz/AnimalTown/releases/latest/download/update.json),
a maximum 2 KiB document published with the numbered main release. It contains
only its tag and exact ISO basename, HTTPS URL, size and SHA-256. GitHub's latest
regular release excludes hash-suffixed PR prereleases; the updater also validates
the numeric tag and exact repository/tag/asset URL itself. An equal or newer
installed numeric version is kept, including across the old patch numbering and
new automatically incremented minor numbering (`0.11.38` to `0.12.0`).

The former releases API scan buffered entire pages and parsed eligible releases
twice. Its SDK string decoder also materialized skipped release notes; a 4,329-
character note exceeded the 4,096-character string limit. The compact feed
contains no notes, author objects or unrelated assets, is parsed once, and has
a 2 KiB HTTP buffered-response cap before JSON decoding. There is no fallback to
the large releases API. A missing, malformed or oversized feed fails before any
download or image replacement. Requires an AnimalTown main release that publishes
the compact feed (starting at `0.12.0`).

The ISO streams into checked contiguous storage with a fixed 16 KiB application
buffer. The SDK build reserves a 1 MiB managed heap for CA/HTTP/JSON working data. Its length, GitHub SHA-256 digest, AnimalTown game ID and embedded version
must match before deletion. The new ISO keeps GitHub's filename in the selected
old image's directory; it is never renamed to the old filename. Contiguous-file
publication and card readback complete before GC Loader's hardware image mapping
is changed. The SDK then reads the physical DVD interface and compares the whole
boot header with the verified file. Only a successful map-and-readback permits
deletion of the selected old ISO. When no newer release is needed, the selected
image is revalidated and handed directly to Swiss without physical remapping or
file deletion. This preserves Swiss's normal handling of manually copied,
possibly fragmented images. New-version replacements still require successful
physical selection/readback before deleting the old ISO; newly downloaded images
use contiguous allocation. A failed replacement selection retains both files.
Other installations and development images are left alone. A failed download,
verification, disc selection, helper preparation or cancellation preserves the
selected old ISO. A failed selection can leave the drive mapping changed; both
files remain available for manual recovery.

Writable SD operations run inside the SDK's automatic-defragmentation scope,
with a 64 KiB minimum file size. The previous policy and threshold are restored
on success, failure or cancellation. This policy applies to updater-owned writes;
read-only discovery does not relocate installed images. Automatic cleanup of
ordinary written files is best effort. ISO downloads still use the stricter
contiguous reservation/publication API and fail safely if that requirement cannot
be met; automatic defragmentation is not a fragmented-download fallback.

## SD card setup

Use direct GC Loader firmware boot with writable SD-sector support, a FAT32 or
exFAT card, and a working Broadband Adapter with Internet/DNS access. The game
image must contain a stable embedded release version and retain an `.iso` or
`.gcm` extension; its basename can be changed. For example:

```text
/games/AnimalTown/AnimalTown-0.11.36.nkit.iso
/boot.dol
```

The updater DOL is normally embedded in the game image; the SD-root DOL is a
fallback for discs assembled without it. No HTTPS configuration folder, seed
file or PC provisioning is required. The public CA bundle is embedded in the
updater itself, so unmounting or replacing the source ISO cannot remove it.

Place a supported official normal Swiss DOL at the drive root. Its filename can
stay `boot.dol`, `swiss_r2119.dol`, `SWISS.DOL` or another `.dol` name. The updater
checks file contents, chooses the newest recognized revision and uses a
deterministic path ordering for duplicates. It does not search subdirectories
for Swiss. The old `animaltown-updater/swiss.dol` location remains a
content-validated fallback when no supported root DOL is found.

DOL headers describe executable layout and do not contain a Swiss identity.
Official Swiss payloads are compressed, so the updater validates layout/launch
arguments and streams a SHA-256 fingerprint through a fixed 16 KiB buffer.
The catalog covers 12 official normal releases, including r2119; exact revisions
and official checksum provenance are in [build/swiss-builds.json](build/swiss-builds.json).
New or custom builds need a catalog update. Use the archive's normal
`DOL/swiss_rXXXX.dol`, since legacy-LZ variants cannot accept launch arguments.
No Swiss binary is bundled with the updater.

The SDK stages and validates that executable before deleting an ISO, then passes
`Autoload=gcldr:/.../<published filename>` to Swiss. The updater selects and
verifies the new physical mapping before deleting the old image. Swiss performs
its normal image/ODE setup and apploader boot after the handoff. No
replacement-game raw DOL is launched against the old disc mapping.

HTTPS uses 118 pinned Mozilla/certifi public CA roots, stored as 178,673 bytes
in the DOL. The source revision, exact source and embedded SHA-256 digests, and
MPL-2.0 notice are recorded in [assets/https/source.json](assets/https/source.json)
and [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). Builds verify the bundle
without importing the build computer's trust store or proxy certificates.

Every HTTPS initialization obtains 48 fresh bytes with
`PlatformEntropy.GetBytesAsync`. The SDK collects on the native service executor
using its bounded jitterentropy provider, including startup and continuous source
health checks. Failure, timeout or cancellation stops TLS initialization and
clears temporary output; successful seed material is consumed and cleared by
TLS configuration. No fixed seed is embedded, read from SD, reused between
launches or published. Retrying an update collects fresh material automatically.
Physical GameCube raw-source and restart qualification remains part of hardware
acceptance: passing health checks or running in Dolphin does not establish the
hardware source's entropy rate.

Set the GameCube calendar to the correct current local date and time. The
console does not provide a reliable timezone, so the SDK treats the calendar as
a possible UTC interval from 14 hours earlier to 14 hours later. Every
certificate in the validated chain must remain valid across that entire interval;
normal chain and hostname checks still apply. Both endpoints must fit the
supported 2024–2038 time range. This strict check can reject a certificate near
the beginning or end of its validity even when an exact UTC clock would accept
it. There is no guessed timezone, trust in an HTTP Date header, certificate
validation bypass or HTTP fallback.

`tools/provision.py` is retained only for older updater releases that require
SD-based HTTPS inputs. The current updater ignores its `roots.pem`,
`entropy.bin`, `utc-offset-minutes.txt` and legacy `image-path.txt` files. To
install Swiss for this updater, copy a supported normal official DOL to the drive
root as described above; running the legacy provisioner is unnecessary.

The game handoff contains three separate argv entries: updater DOL path,
`--animaltown-update=2`, and running build version. Legacy v1 requests remain
readable for compatibility. The updater's SD scan chooses the installation;
the launch version does not select a filesystem path.

## Build

Install .NET SDK 8+, Python 3, Git, and either Docker or the pinned
libogc2/devkitPPC toolchain used by DolphinDotNet. Then:

```sh
python3 tools/build.py --backend Docker
```

The build pins the SDK revision in `sdk.lock.json`, including verified physical
image selection, bounded FAT32/exFAT traversal and self-contained HTTPS support.
It creates a local NuGet feed, checks generated public trust data, runs the host
tests and uses the SDK's native build pipeline. This avoids waiting for a
published SDK package. The project-local launch-argument bridge preserves and
reads the versioned request.
An existing clean SDK checkout at that exact revision can be supplied
with `--sdk-source /path/to/DolphinDotNet`. Outputs and checksums are under
`out/release`. Main-branch CI publishes the DOL/ELF, build manifest and hashes as a
GitHub prerelease; PR CI retains review artifacts. Public CA provenance and
third-party notices accompany the release; private entropy is generated only at
runtime.

For host checks after preparing the local feed:

```sh
dotnet run --project tests/Updater.Tests.csproj -c Release
```

## Validation and recovery

The automated checks cover SHA-256 padding/multiblock vectors, direct NKit release
selection, renamed ISO/GCM discovery, exclusion of embedded development versions,
numeric version comparisons, directory scanning, asset identity, scoped automatic
defragmentation and policy restoration, selection-before-deletion ordering and
injected selection/deletion failures, plus launch-path validation and protocol
handoff. Trust checks verify all 118 roots and their fingerprints, reject stale
generated data, and exercise the exact generated bytes through native AOT with
the real 1 MiB managed heap and GC stress. The SDK checks local-clock certificate
intervals, entropy-provider failure paths and secret-buffer clearing separately.
Native GameCube compilation is a separate build gate. Physical GC Loader writes,
BBA HTTPS throughput, Swiss autoload/ODE selection, media removal and power
interruption need hardware acceptance. FAT directory
renames are not guaranteed power-loss atomic. Never remove power/card during
installation.

The build also runs the production updater scan and console drawing code through
the SDK's native AOT runtime with the actual 1 MiB managed heap. Wide directories,
thousands of stable/development images and 30 nested directories must complete
with bounded retained memory and closed file handles. The pinned SDK tests its
real FAT32/exFAT driver separately: the original driver's 10-level exFAT path
limit reported `OutOfMemoryException` without exhausting memory. Its replacement
supports 64 levels and reports a path-depth error beyond that limit. Scan failures
show the current directory or image path, and native exceptions retain their
diagnostic text even when managed memory is exhausted.

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
