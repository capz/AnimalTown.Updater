# Sequential throughput implementation

These changes are implemented and validated in order: TCP window, SDK HTTP
reads, then SD write batching. Console throughput remains a hardware acceptance
item; host and binary checks do not establish an achieved BBA download rate.

## Stage 1: TCP window

The updater enables a 32,768-byte TCP receive window via the SDK's source-built
BBA overlay. Other SDK apps retain their existing library unless they opt in.
`tools/build.py --tcp-window 0` provides a legacy-library control; 16,384, 32,768
and 65,535-byte variants are supported. 65,536 is rejected by the SDK builder.

The pinned toolchain identifies libogc2 r2442.094b250, matching the overlay's full
source revision 094b250f1e978d37789f18ffc2d3c9c2325062f8. All BBA translation units
are rebuilt together, without installing/replacing system libraries. Global
128 × 1,600-byte pbuf and 128-segment pools and the 192 KiB lwIP heap are unchanged.
The managed heap remains 1 MiB. Receive credit can consume more of the existing
pool under concurrent traffic; the updater uses one active asset body.

Validation: pinned-source library rebuild, unchanged public network ABI header,
SDK compiler/bundle and native updater builds, 368 updater checks and native
TLS bootstrap/worker/scan/launch regressions. Linked PowerPC code is inspected
for the configured initial window and TCP output. The attempted standalone
Dolphin raw-TCP debugger fixture was inconclusive and is not counted as packet
or throughput acceptance. Real SYN/window, loss recovery and sustained BBA
throughput still need console testing. The ideal window/RTT bound at 40 ms grows
from 73 kB/s to 819 kB/s; this is a model, not measured speed.

## Stage 2: SDK reads

After stage 1 passed, the SDK gained `HttpClient.StreamReadBufferSize` (default
4096; valid 4096..16384), captured at request start. The updater selects 16384.
The stream input grows from 8 to 16 KiB for this opt-in. Both its native TLS read
and managed operation honor the quantum; short reads remain legal. The managed
heap cap is unchanged.

Real local verified HTTPS through Debug/Release AOT and GC stress compared the
same 65,567-byte payload: Content-Length used 17 -> 5 reads; chunked used 21 -> 9.
Bytes matched exactly, maximum reads were 16384/16378. TLS trust/time/hostname,
redirect/credential isolation, downgrade, truncation and cancellation regressions
also passed. These are operation counts, not hardware download rates.

## Stage 3: SD batching

Only after the stage-2 native DOL and HTTPS checks passed, the download loop was
changed to coalesce payload into 16 KiB writes plus a final tail. A fixed 32 KiB
workspace preserves full 16 KiB SDK requests even when a preceding read is short;
filling only the remainder of a 16 KiB buffer would split SDK operations again.
This adds 16 KiB of bounded workspace and a small reused block descriptor, without
raising the 1 MiB heap cap. Tail bytes move with the SDK's overlap-safe Array.Copy.

Successful writes advance hashing/progress; EOF/length/digest and cancellation
are checked before contiguous-file CompleteAsync. The caller retains publication,
card readback, physical image verification and the original-image deletion rules.
Callbacks consume the block synchronously; no filesystem concurrency is added.

460 managed checks cover one-byte/4 KiB/16 KiB reads, boundaries, exact bytes,
tails, progress, cancellation, digest/length and read/write errors. The production
batcher and SHA also pass native SDK AOT at release O3/LTO, normally and with GC
stress. For 65,567 bytes: 17 4-KiB payload reads coalesce into 5 writes; irregular
5,003-byte reads also yield 5 writes, and full-size reads remain 5 writes. Every
SDK request remains 16 KiB. Native test heap used 314640 bytes normally and 66840
after GC stress (236 collections), within 1048576 capacity. These are synthetic
stream/write counts, not real SD timing or an end-to-end BBA speed claim.
