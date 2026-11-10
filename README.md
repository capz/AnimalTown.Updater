# AnimalTown.Updater

Standalone GameCube updater written in C# with DolphinDotNet. The output is
`animal-town-updater.dol`. AnimalTown's title menu launches this executable and
returns from Main; the updater hands off to Swiss after completing its work.

It reads the installed version from `gcloader:/AnimalTown.iso`, checks the newest
published [AnimalTown release](https://github.com/capz/AnimalTown/releases),
including prereleases, and selects the direct `AnimalTown-<version>.nkit.iso`
asset. The major counter and strictly older feature/fix counters prevent downgrades.
Feature/fix counters are independent: when one rises and the other falls across
branch releases, publication order decides. The final component is an opaque
commit identity. Equal versions do not download again.

The ISO streams into checked contiguous storage with a fixed 16 KiB application
buffer. The SDK build reserves a 1 MiB managed heap for CA/HTTP/JSON working data. Its length, GitHub SHA-256 digest, AnimalTown game ID and embedded version
must match before replacement. The published download keeps GitHub's filename.
Installation renames `AnimalTown.iso` to `AnimalTown.iso.bak`, then the completed
release file to `AnimalTown.iso`. A previous backup is retired only while the
current original still exists. A failed second rename restores the original; a
recognized marker lets the next updater launch recover a cut between renames.
The final backup remains available for manual recovery.

## SD card setup

Use a GC Loader/firmware combination exposing writable SD sectors, with a FAT32
or exFAT card, and a working Broadband Adapter with Internet/DNS access. Place:

```text
/AnimalTown.iso
/animal-town-updater.dol
/swiss.dol
/animaltown-updater/roots.pem
/animaltown-updater/entropy.bin
/animaltown-updater/utc-offset-minutes.txt
```

`swiss.dol` must be an argv-capable [Swiss](https://github.com/emukidid/swiss-gc)
build supporting NKit and GC Loader. Swiss is installed separately. The SDK
passes `Autoload=gcldr:/AnimalTown.iso` without modifying Swiss configuration.
Loading an extracted game DOL alone would omit the ODE/disc boot setup.

HTTPS requires explicit trusted CA roots, fresh private entropy and a correct
clock. On a trusted computer, with the SD mounted, run:

```sh
python3 tools/provision.py /path/to/SD --roots /path/to/ca-bundle.pem --utc-offset-minutes 60
```

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
package. An existing clean SDK checkout at that exact revision can be supplied
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
at each install mutation. Native GameCube compilation is a separate build gate.
Physical GC Loader writes, BBA HTTPS throughput, the installed Swiss handoff,
media removal and power interruption need hardware acceptance. FAT directory
renames are not guaranteed power-loss atomic. Never remove power/card during
installation. A malformed recovery marker fails closed for inspection.

If booting the game after an interrupted update is impossible, launch this DOL
manually in Swiss; it will restore a missing canonical ISO from the recognized
transaction's backup. Alternatively restore `AnimalTown.iso.bak` manually on a
computer. Verification failures leave the original untouched and retain an
existing downloaded file for inspection; remove a mismatching download on a PC
before retrying. The updater does not touch memory-card town or dev-config saves.
