# Updater throughput: limits, measurements and staged plan

Baseline: updater `c03ea7d8bf1073e89497dc4c1db29328164875a0`, including the
background-executor fix and live speed display. The user's approximately
200 **kB/s** report predates those fixes; it is not a measured result from this
baseline. 200 kB/s is 1.6 Mb/s in the updater's new display.

## Maximum: which limit?

The BBA's 100 Mbit/s Ethernet rating is **12.5 MB/s raw**, not an attainable
HTTPS download rate. The libogc2 BBA driver selects the serial EXI bus at
32 MHz: **4 MB/s raw (32 Mbit/s)**, before EXI commands, Ethernet/IP/TCP
headers, receive/transmit turnarounds and ACKs. SD output, TLS, hashing, memory
copies and scheduling reduce delivered payload throughput further. The CPU
phases run serially in the download worker; isolated phase rates cannot simply
be treated as independent end-to-end limits.

A more immediate WAN limit is the receive window. The **actual pinned toolchain
`libbba.a`** initializes `tcp_alloc` with **2,920 bytes** of receive window and
1,460-byte MSS. This is verified in disassembly, rather than assumed from the
latest upstream header. See [binary evidence](throughput-evidence/bba-build.json)
and [tcp_alloc](throughput-evidence/pinned-tcp-alloc.txt). The reference source
also defines `TCP_WND = 2 * TCP_MSS` in
[lwipopts.h](https://github.com/extremscorner/libogc2/blob/8b8113b4fc173cfe751d8f9c13ab91081abf2ae8/lwip/include/lwip/lwipopts.h#L108)
and [32 MHz EXI selection](https://github.com/extremscorner/libogc2/blob/8b8113b4fc173cfe751d8f9c13ab91081abf2ae8/lwip/arch/gc/netif/gcif.c#L210).
This reference revision is not claimed to reproduce the entire binary archive.

For one connection, receive-window / effective RTT gives this ideal bound:

| RTT | 2,920-byte window bound | Payload bits/s |
| --- | ---: | ---: |
| 1 ms | 2.92 MB/s | 23.36 Mb/s |
| 5 ms | 584 kB/s | 4.67 Mb/s |
| 10 ms | 292 kB/s | 2.34 Mb/s |
| 15 ms | 195 kB/s | 1.56 Mb/s |
| 40 ms | 73 kB/s | 0.58 Mb/s |

Loss, delayed ACKs, a partially occupied window and application pacing can lower
these numbers. The table is a model, not a measurement of the user's CDN RTT or
proof that this explains the previous observation. A fast household Internet
connection does not remove either EXI or this TCP-window limit.

**A maximum sustainable updater rate is not established yet.** It must be below
the 4 MB/s EXI bound, and may be much lower with software TLS and the GC Loader
card. A 1 MB/s payload rate is a useful next hardware experiment target, not a
promise. With the current window even that needs RTT below approximately 2.9 ms.

## Experiments and retained change

The fixture compiles the baseline and production SHA through the exact pinned
SDK AOT, allocates a 1 MiB managed heap, verifies both digests against an
independent SHA-256 digest, warms both implementations and alternates execution
order. Each of five pairs processes identical 256 KiB inputs in 16 KiB chunks.
The PowerPC compiler uses `-O2`, matching the production native optimization
level. Dolphin 2503 executes the resulting GameCube DOL; timings come from the
guest monotonic clock and are read from guest memory only after a successful
completion. Null graphics keeps presentation outside this isolated CPU test.

| Experiment | Baseline median | Candidate median | Decision |
| --- | ---: | ---: | --- |
| PowerPC AOT SHA, 256 KiB | 162.903 ms | 124.231 ms | Retain: **23.7% less hashing time** |
| Host AOT SHA, 256 KiB | 4.379 ms | 3.444 ms | Supporting check only |
| Host checked-writer 4 KiB versus 16 KiB writes, 32 MiB | 95.714 ms | 88.065 ms | Reject: 8.0% median saving, below gate; two large regressions; no SD measurement |

[Raw PowerPC evidence](throughput-evidence/powerpc-sha.json) includes all pairs,
DOL digest, SDK identity and source digest. [Host SHA](throughput-evidence/host-sha.json)
and [storage probe](throughput-evidence/host-storage.json) retain their complete samples. The retained implementation transforms
complete 64-byte SHA blocks directly from the input array, buffering only partial
blocks. The digest, finalization rules and fixed working memory are preserved.
It benefits download hashing and later image/Swiss verification.

This is a **hash-phase improvement, not a measured 23.7% download improvement**.
For illustration only, at 200 kB/s the measured guest hash cost would account for
about 12.4% of elapsed transfer time. Reducing that component by 23.7% would save
about 3% of total time if everything else stayed constant. Actual hardware and
pipeline behavior must be measured. The component acceptance gate was a
repeatable >10% time reduction on PowerPC with correct digests and unchanged
working-memory bounds; it passed in all five pairs.

A storage probe uses the real pinned SDK `dnd_contiguous_write`, identical bytes,
32 MiB per sample, alternating order, equal checksums and an fsync in both
variants. It deliberately bypasses allocation/publication so it isolates write
batch size. It does not validate GC Loader allocation or emulate SD behavior.
The cloud filesystem produced inconsistent pairwise outcomes; that does not
justify changing the updater's streaming/cancellation behavior.

## Plan, in order of likely impact

1. **Measure the new baseline on the console.** Same stable asset, card, wired
   LAN and CDN endpoint; record payload speed over the middle of the transfer,
   full download time and verification time for at least three runs. Compare
   alternating baseline/candidate DOLs. Capture the TCP SYN/SYN-ACK, advertised
   window, RTT, retransmissions and link duplex via a mirrored switch port.
   Measure local-LAN HTTPS separately to distinguish WAN/window and CPU/card
   costs. No reachable console is available in this cloud session.
2. **Retain the proven hash optimization now.** Done, with digest boundary,
   offset, partial-block and finalization regression checks. Do not remove
   published SHA-256 verification, TLS certificate checks, contiguous-file
   completion/readback or physical image verification.
3. **Test a rebuilt BBA receive window before further UI tuning.** Build the
   pinned libbba source with 16 KiB, then 32 KiB windows. At 40 ms those provide
   model bounds of 410 and 819 kB/s. A 1 MB/s target at that latency needs
   at least 40,000 bytes; test up to **65,535**, not 65,536, without TCP window
   scaling (the window is 16-bit). Audit pbuf pool, out-of-order queue and simultaneous socket
   memory first, then verify the advertised window in packets. This needs a
   reproducible SDK/toolchain change: the updater cannot fix the compiled
   `TCP_WND` by allocating a larger C# buffer. The SDK does not expose a receive
   buffer setting that establishes a larger TCP window. Do not binary-patch the
   library or force duplex. Raise the window only after memory, cancellation,
   packet-loss and sustained hardware-transfer tests pass.
4. **Benchmark bounded larger HTTPS reads.** The pinned `HttpBodyStream` limits
   each operation and native read to 4 KiB, allocates a completion operation for
   each read and uses an 8 KiB input buffer. The updater already supplies 16 KiB,
   so merely increasing its array changes nothing. An opt-in 16 KiB SDK read
   quantum may reduce scheduler turns and TLS copies. Preserve short-read,
   chunk framing, size limits, timeout and cancellation semantics. Compare real
   HTTPS with the same certificate validation and payload digest, plus managed
   heap high-water and UI responsiveness, before changing the default.
5. **Benchmark SD coalescing on GC Loader.** Reuse the existing 16 KiB application
   buffer, flush full batches and the final tail, and keep hash/progress byte
   accounting exact. This can reduce calls from four writes to one but adds
   complexity and may provide no benefit when network-bound. No extra image
   buffer, asynchronous filesystem concurrency or bypass of the strict writer.
6. **Only add receive/write overlap if timings show it matters.** The current
   SDK serializes managed execution and native storage calls can block; another
   `async` method alone does not create hardware I/O overlap. Consider a bounded
   native prefetch/queue only with SDK-specific evidence, task affinity tests and
   a total MEM1 budget. Keep VSync enabled.

For steps 3–6, retain a candidate only if the median of at least three paired
hardware transfers improves payload throughput by >10%, no pair materially
regresses and integrity/cancellation/UI checks pass. Record exact DOL/source,
SDK/toolchain, asset digest, network conditions and card. Until that evidence
exists these are experiments, not enabled tuning knobs or achieved speeds.

## Reproduce the CPU and storage experiments

Use .NET 8 and a clean checkout of `sdk.lock.json`'s revision:

```sh
python3 tools/benchmark-throughput.py --sdk-source /path/to/locked-sdk \
  --output out/throughput-host --storage-probe
```

For PowerPC, use the pinned toolchain image recorded in the evidence and the
SDK's corrected EXI/VIDEO libogc overlay (generated by its normal native build):

```sh
python3 tools/benchmark-throughput.py --sdk-source /path/to/locked-sdk \
  --output out/throughput-ppc --target powerpc --devkitpro /opt/devkitpro \
  --libogc-overlay /path/to/native/build/exi-overlay
python3 tests/run-throughput-dolphin.py --sdk-source /path/to/locked-sdk \
  --work out/throughput-ppc --output out/throughput-ppc/dolphin
```

Dolphin must support the headless platform and GDB. In a container allow 1 GiB
shared memory (`--shm-size=1g`); the default 64 MiB caused SIGBUS during setup.
The fixture fails on digest mismatch, native exception, incomplete guest stage,
unexpected array metadata or absent timings. It never reports a network rate.
The separate SDK compiler checkout is read but not modified. The benchmark
requires the baseline commit in local Git history (`git fetch --unshallow` for
shallow clones).
