#!/usr/bin/env python3
"""Provision private, one-use HTTPS input on the GC Loader SD using a trusted PC."""
import argparse
import datetime
import os
from pathlib import Path
import ssl

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('sd_root',type=Path)
    parser.add_argument('--roots',type=Path,default=Path(ssl.get_default_verify_paths().cafile or 'roots.pem'))
    parser.add_argument('--utc-offset-minutes',type=int,default=int(datetime.datetime.now().astimezone().utcoffset().total_seconds()//60),help='UTC offset of the correctly set GameCube local clock (default: this PC)')
    args=parser.parse_args()
    roots=args.roots.read_bytes()
    if not 0<len(roots)<=262144 or b'-----BEGIN CERTIFICATE-----' not in roots:
        parser.error('provide a PEM CA bundle of at most 256 KiB')
    if not -840<=args.utc_offset_minutes<=840:
        parser.error('UTC offset must be within -840..840 minutes')
    folder=args.sd_root/'animaltown-updater';folder.mkdir(parents=True,exist_ok=True)
    seed=folder/'entropy.bin'
    if seed.exists():parser.error('an unconsumed seed already exists; it can be used for the next updater launch')
    (folder/'roots.pem').write_bytes(roots)
    (folder/'utc-offset-minutes.txt').write_text(str(args.utc_offset_minutes))
    # Exclusive creation prevents accidental replacement of an unused seed.
    descriptor=os.open(seed,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
    with os.fdopen(descriptor,'wb') as stream:
        stream.write(os.urandom(48));stream.flush();os.fsync(stream.fileno())
    print('Provisioned one updater launch. Keep this SD private; safely eject it.')
if __name__=='__main__':main()
