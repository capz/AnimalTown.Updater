namespace AnimalTown.Updater;

internal sealed class SwissBuild
{
    internal readonly int Revision,Length;
    internal readonly string Digest;
    internal SwissBuild(int revision,int length,string digest){Revision=revision;Length=length;Digest=digest;}
}

// Official normal DOL members, verified against the release's own checksums.
// Header fields describe loading, not program identity; packed Swiss builds
// have no reliable plaintext name. Keep these rows synchronized with the
// provenance manifest and the standalone provisioning tool.
internal static class SwissBuilds
{
    internal static readonly SwissBuild[] Official={
        new SwissBuild(2119,1350216,"3a19ea4b814ed7d43d7dbc81c49f981c215698cfaae6ed7bca471517a74aa1d1"),
        new SwissBuild(2092,790920,"b844ece3877621f6256a1eb8d196af9d0a3b58c3e17bde7e94d239b5bb668fff"),
        new SwissBuild(2073,787720,"5553ad6641716ea7d1ceb155490f18485096e3ffe25617f1901c6c0500e2fa75"),
        new SwissBuild(2041,778728,"3a7b8e2ce6148ea33254298eead5c79488d0affd35a1c6dad1766af20a0527d2"),
        new SwissBuild(2018,777736,"a90723468cd6239252b45e608e02075b10c4aa8c891fa21fa1de6257dd603fcf"),
        new SwissBuild(1984,767176,"bcffa33a98dc5f9910592b1458a1a5b765a2d4c5689bd679740d6a20594ab64b"),
        new SwissBuild(1983,767272,"e4d8c28d4640890ce3229042e33cca52260e8a5e884131a0b446a80dcd18de31"),
        new SwissBuild(1957,739496,"89dd5fab2a85250af8dd61ba53f7d92e79d3931c625edd7c775d6f7cbb2d865a"),
        new SwissBuild(1913,734824,"e878a1c6dada59f912332abc469ab6c833d0eb6ca39cf3fcf0dcb9dee5e919f2"),
        new SwissBuild(1899,735048,"5942a67ef31c9cbeea1a9fb43e23757ac73bd9b22853683696763992302a12a2"),
        new SwissBuild(1889,734728,"8ce96ff4e4682dbfef8067c021a82c078cc12936674d6969e6d9a939a94382ae"),
        new SwissBuild(1867,729160,"eec706e0c7b4cff943e5d7d3a2dba72b1855d8b8f58c016b6fea4ba773d2b611")
    };
}
