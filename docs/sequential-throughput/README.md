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
