# AnimalTown.Updater

Standalone GameCube updater written in C# with DolphinDotNet. The output is
`animal-town-updater.dol`. AnimalTown's title menu launches this executable and
passes its running build version. The updater mounts the launched disc, reads
its root `version.md` through a 64-byte bound and confirms the launch identity.
It compares that numeric version with the compact online release feed and
downloads a newer release into `/games` on the chosen SD card. Swiss boots the
verified file. For a GC Loader destination, the updater also switches the
physical drive to the image and verifies disc readback before starting Swiss.
By default the updater keeps existing images and skips the installed-image scan.

Swiss receives separate `Autoload=<image>`, `AutoBoot=Yes`, `BS2Boot=No` and
`Prefer Clean Boot=No` arguments, with its executable path as argv[0]. Both a
newly installed image uses this handoff. An already-current or newer running
disc instead reports success and waits for B to exit, without guessing a path
to autoload. Swiss is
still the image loader; its own startup or error screens can appear, and holding
B retains Swiss's intentional autoboot override. This restarts the game; it does
not restore the previous process's live memory.

## Choose a download destination

At startup the updater checks GC Loader SD, SD2SP2 and SD Gecko in Slots A and B.
It uses the only writable card automatically. With multiple writable cards,
use **D-pad Up/Down** to highlight a card and press **A** to confirm or **B** to
cancel. The update keeps the published filename in the selected card's `/games`
directory. The card needs enough contiguous free space for the release.

## Automatic cleanup of older copies

Press **X** on the updater screen to turn **Delete older AnimalTown copies** on
or off. The toggle starts off each time you launch the updater and stays
available while it checks, downloads and verifies the release.

With cleanup on, the updater scans the selected card after verifying the new
image and preparing its boot handoff. It removes older numbered AnimalTown ISO and GCM
images, including renamed copies, based on their disc headers. The selected
image, equal or newer releases, development builds and recovery files are kept.
If cleanup stops on a storage error, the updater shows the affected path and
lets you press **A** to start the installed update.

## Live download speed and time remaining

While the transfer phase is active, its stage header shows recent payload
throughput, sampled every half second from the byte counter and monotonic clock.
Rates below 1,000,000 bits/s use `kb/s`; rates from 1 Mbit/s use `Mb/s`, including
the BBA's 100 Mbit/s boundary. Units are decimal bits, not bytes: 200 kB/s is
`1.6 Mb/s`. One decimal is truncated after selecting the unit. A stalled sample
shows zero; verification, cancellation and errors hide the speed immediately.
This measures delivered payload progress, including network, storage and hashing
pacing, rather than Ethernet link speed. No extra network reads or per-frame
rate-string formatting are needed.

Below the progress bar, the updater estimates how much download time remains
using a smoothed rate from the same samples. The estimate appears once bytes
start arriving and clears after 1.5 seconds without progress or when the
download ends. The screen shows the active stage during verification and restart.

See [throughput limits, measured optimization and next experiments](docs/updater-throughput.md)
for the BBA/EXI/TCP ceilings and reproducible native comparisons.

See [sequential throughput changes](docs/sequential-throughput/README.md) for the
enabled TCP window, SDK read size and SD batching checkpoints.

## Download scheduling

The update workflow starts explicitly on the SDK background executor. Its
continuations, HTTP reads, hashing and SD writes remain there while the UI runs
on the application executor. `Scheduler.ProductiveWaits` releases the managed
execution gate during synchronous GPU/VSync waits. VSync stays enabled and
controller cancellation stays on the UI executor.

`async/await` by itself does not make this change: starting the old workflow on
the UI executor captured that executor for its continuations, and productive
frame waits are opt-in in the pinned SDK. The legacy SDK HTTP stream returned at most
4 KiB per read despite the updater's 16 KiB application buffer. This build opts
in to 16 KiB SDK body reads. One such read
per 50/60 Hz UI frame gives approximately 200/240 KiB/s, consistent with the
reported roughly 200 kB/s. This is a scheduling explanation, not a measured
hardware diagnosis or a configured BBA bandwidth limit.

The BBA has a 10/100 Mbit/s Ethernet link, but EXI transfers, TCP receive-window
size/latency, TLS and SHA-256 work on the GameCube CPU, and SD writes also limit
end-to-end HTTPS throughput. The throughput build opts into a 32 KiB TCP receive window. It does not force
link speed/duplex, weaken HTTPS verification or increase the 1 MiB managed heap.
Physical throughput still needs console measurement.

`tests/test_native_worker.py` executes the production worker wrapper with the
real pinned SDK AOT scheduler and execution gate. A bounded scheduling workload
advanced 128 steps over 10 simulated UI frames, with progress during seven
20 ms presentation waits; it also checks continuation context, cancellation
and error propagation. These are host scheduling results, not BBA transfer
rates. Native tests retain real updater reachability, the 1 MiB heap and
simulated 60 Hz UI waits. The version-file flow must leave even a higher-version
ISO unread while checking the disc file and finding Swiss at the drive root.

The complete updater executable, including its public HTTPS trust anchors, is
copied into MEM1 before launch; its console uses the IPL font and needs no disc
assets after the initial `version.md` read. It explicitly unmounts `dvd:/` before
mounting SD cards or changing an installation,
and refuses to continue if the disc remains mounted. This preserves the source-
disc release fix from [PR #2](https://github.com/capz/AnimalTown.Updater/pull/2).
Image replacement then needs no further executable or asset reads from that
disc. The download streams to SD; it is never buffered in RAM in full.

The game release builder freezes its version before assembling the image and
writes exactly that ASCII value to `version.md`: no BOM, newline or Markdown
formatting. Disc validation checks it against the build identity and boot title.
Renaming an ISO does not affect the mounted file. A missing, invalid, oversized
or mismatched version file fails before writable storage or network setup.
The updater uses the launched disc for the installed version check.

The updater reads GitHub's `/repos/capz/AnimalTown/releases/latest` API once,
with a 32 KiB HTTP response cap before decoding and at most 64 asset records.
The release workflow publishes only four binary ZIP/ISO assets and limits its
public notes to 1 KiB; detailed notes and reports live inside `-depot.zip`.
This keeps metadata within the updater's existing 1 MiB heap and avoids the old
4,329-character release-note string failure. It validates the numeric tag,
exact ISO basename, HTTPS repository/tag/asset URL, size and SHA-256. Missing,
oversized or malformed metadata fails before downloading or replacing images.
Equal/newer running numeric versions are kept; debug releases are rejected.

No loose `update.json` release asset is required. Older updaters that fetch
`releases/latest/download/update.json` need one manual ISO upgrade to the first
release containing this updater. Memory-card save formats are unchanged. The
old compact-feed parser remains covered for existing data; it is not used for
new release discovery.

The ISO streams into checked contiguous storage with a fixed 32 KiB coalescing workspace and 16 KiB reads/writes. The SDK build reserves a 1 MiB managed heap for CA/HTTP/JSON working data. Its length, GitHub SHA-256 digest, AnimalTown game ID and embedded version
must match before activation. The new ISO keeps GitHub's filename in `/games`;
it is never renamed to an old filename. Contiguous-file
publication and card readback complete before activation. On a GC Loader
destination, the SDK changes the drive mapping and compares the physical disc's
boot header with the verified file. Swiss handles SD2SP2 and SD Gecko booting.
A pre-existing destination is never
truncated: only an exact size/digest/version match can be reused. Otherwise
the update stops and preserves it. A failed download, verification, selection,
helper preparation or cancellation before installation preserves every prior
image. A failed GC Loader selection can leave the drive mapping changed. Files remain available for
manual recovery.

Writable SD operations run inside the SDK's automatic-defragmentation scope,
with a 64 KiB minimum file size. The previous policy and threshold are restored
on success, failure or cancellation. This policy applies to updater-owned writes;
read-only discovery does not relocate installed images. Automatic cleanup of
ordinary written files is best effort. ISO downloads still use the stricter
contiguous reservation/publication API and fail safely if that requirement cannot
be met; automatic defragmentation is not a fragmented-download fallback.

## SD card setup

Use a writable FAT32 or exFAT SD card and a working Broadband Adapter with
Internet/DNS access. A GC Loader destination requires firmware with writable
SD-sector support. The game
image must contain `version.md` matching its embedded release version; its
basename can be changed. For example:

```text
/games/AnimalTown/AnimalTown-0.11.36.nkit.iso
/boot.dol
```

The updater DOL is normally embedded in the game image; the SD-root DOL is a
fallback for discs assembled without it. No HTTPS configuration folder, seed
file or PC provisioning is required. The public CA bundle is embedded in the
updater itself, so unmounting or replacing the source ISO cannot remove it.

Place a supported official normal Swiss DOL at the root of an available SD card.
The updater checks the selected card first. Its filename can
stay `boot.dol`, `swiss_r2119.dol`, `SWISS.DOL` or another `.dol` name. The updater
checks file contents, chooses the newest recognized revision on each card and
uses a deterministic path ordering for duplicates. The old
`animaltown-updater/swiss.dol` location remains a content-validated fallback
when a card has no supported root DOL.

DOL headers describe executable layout and do not contain a Swiss identity.
Official Swiss payloads are compressed, so the updater validates layout/launch
arguments and streams a SHA-256 fingerprint through a fixed 16 KiB buffer.
The catalog covers 12 official normal releases, including r2119; exact revisions
and official checksum provenance are in [build/swiss-builds.json](build/swiss-builds.json).
New or custom builds need a catalog update. Use the archive's normal
`DOL/swiss_rXXXX.dol`, since legacy-LZ variants cannot accept launch arguments.
No Swiss binary is bundled with the updater.

The SDK stages and validates that executable before downloading an ISO, then passes
`Autoload=gcldr:/.../<published filename>` to Swiss. The updater selects and
verifies the new physical mapping after download verification. Swiss performs
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
`--animaltown-update=3`, and running build version. Legacy v1/v2 envelopes remain
readable, but a disc version file is required and legacy paths do not select a
replacement/deletion target. The mounted version file supplies the comparison;
all downloads use `/games`.

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
selection, exact bounded version-file reads, numeric version comparisons,
selected-volume `/games` paths, asset identity, scoped automatic defragmentation and policy
restoration, plus launch-path validation and protocol handoff. Cleanup checks
verify removal of older renamed copies and preservation of other images and
recovery data. Build guards keep historical image selection and replacement
helpers out of the production updater's AOT reachability.
Trust checks verify all 118 roots and their fingerprints, reject stale
generated data, and exercise the exact generated bytes through native AOT with
the real 1 MiB managed heap and GC stress. The SDK checks local-clock certificate
intervals, entropy-provider failure paths and secret-buffer clearing separately.
Native GameCube compilation is a separate build gate. Physical GC Loader writes,
BBA HTTPS throughput, Swiss autoload/ODE selection, media removal and power
interruption need hardware acceptance. FAT directory
renames are not guaranteed power-loss atomic. Never remove power/card during
installation.

The build also runs the production updater startup and console drawing code through
the SDK's native AOT runtime with the actual 1 MiB managed heap. Wide directories,
thousands of stable/development images and 30 nested directories must complete
with bounded retained memory and closed file handles, opening zero installed
ISO/GCM files. It checks that the version file is read before the writable mount
and that the disc has been released before Swiss root enumeration. It also
checks one-card and multiple-card startup, destination selection and release of
mounted volumes when the update stops. The pinned SDK tests its
real FAT32/exFAT driver separately: the original driver's 10-level exFAT path
limit reported `OutOfMemoryException` without exhausting memory. Its replacement
supports 64 levels and reports a path-depth error beyond that limit. Failures
show the current operation/path, and native exceptions retain their
diagnostic text even when managed memory is exhausted.

Publication and boot preparation finish before optional cleanup begins.
After a power cut, launch an existing complete game and retry; its version file
determines whether a newer release is required. Partial downloads are never
booted. Existing valid new-image collisions are verified
before reuse, and damaged collisions stop the update rather than overwriting
an unrelated file. The updater does not touch memory-card town or dev-config
saves or legacy `.bak`/`.update` recovery files.

Completion requests the SDK's Swiss handoff and returns successfully from Main,
releasing updater UI/network resources before Swiss starts the selected image.
A missing or invalid Swiss helper stops the operation before downloading.

The updater console uses the native IPL font, a light gray canvas and dark gray
text, a vertically centered status block and a 48-pixel safe area on every side.
Both native SD readback and SHA-256 verification yield to the UI and honor
cancellation before publication. Transfer progress displays both percent and binary MiB received/total. Loading
dots fill from left to right, then clear in the same order on a repeating cycle.
When a newer release supplies a **What's new** summary, up to four lines appear
above its progress bar. The summary comes from the same release metadata as
the version and ISO information.
Failures retain the stopped phase and last operation, with exception
classification even when the native message is empty, plus the relevant path or
native result when available. Diagnostic text wraps inside the safe area and
does not display a stack trace or assume a backup is intact.
`tests/UiPreview` renders the actual console with deterministic sample states for
Dolphin visual inspection, without requiring a writable GC Loader or network.
