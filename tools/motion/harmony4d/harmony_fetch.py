"""Downloads only the 3D pose files of Harmony4D sequences and decodes them without unpickling.

Each archive's directory is read over HTTP; the pose files of a sequence lie close together in
the archive, so they are fetched in a few byte ranges rather than one request per file. Each
.npy is a pickled dict of two (17, 4) float64 arrays; the arrays are taken straight from the
pickle stream's byte strings after checking that it refers to nothing but numpy's array types.

usage: harmony_fetch.py OUT_DIR ARCHIVE [ARCHIVE ...]   (ARCHIVE like train/03_grappling2.zip)
Writes OUT_DIR/<archive stem>/<sequence>.npz with arrays a, b (frames, 17, 4) and frame numbers.
"""
import os
import pickletools
import struct
import sys
import zlib

import numpy as np
import requests
from remotezip import RemoteZip

BASE = "https://huggingface.co/datasets/Jyun-Ting/Harmony4D/resolve/main/"
ALLOWED = {"numpy dtype", "numpy ndarray", "numpy.core.multiarray _reconstruct"}


def decode(raw):
    """The two people's (17, 4) arrays from one pose .npy, by name."""
    if raw[:6] != b"\x93NUMPY":
        raise ValueError("not an npy file")
    major = raw[6]
    header = int.from_bytes(raw[8:10], "little") if major == 1 else int.from_bytes(raw[8:12], "little")
    start = (10 if major == 1 else 12) + header
    names, blobs, orders = [], [], []
    last_bool = None
    for op, arg, _ in pickletools.genops(raw[start:]):
        if op.name in ("GLOBAL", "STACK_GLOBAL"):
            if arg not in ALLOWED:
                raise ValueError(f"unexpected pickle global {arg!r}")
        elif op.name in ("SHORT_BINUNICODE", "BINUNICODE") and isinstance(arg, str) and arg.startswith("aria"):
            names.append(arg)
        elif op.name in ("NEWTRUE", "NEWFALSE"):
            last_bool = op.name == "NEWTRUE"
        elif op.name in ("SHORT_BINBYTES", "BINBYTES") and len(arg) == 17 * 4 * 8:
            # ndarray state is (version, shape, dtype, is_fortran, data): the flag comes just before the data.
            blobs.append(arg)
            orders.append(last_bool)
    if len(names) != len(blobs) or None in orders:
        raise ValueError("names, arrays and order flags do not pair up")
    people = {}
    for name, blob, fortran in zip(names, blobs, orders):
        values = np.frombuffer(blob, "<f8")
        people[name] = (values.reshape(4, 17).T if fortran else values.reshape(17, 4)).copy()
    return people


def fetch(archive, out_dir):
    url = BASE + archive
    stem = os.path.splitext(os.path.basename(archive))[0]
    target = os.path.join(out_dir, stem)
    os.makedirs(target, exist_ok=True)
    with RemoteZip(url) as z:
        entries = [i for i in z.infolist() if "/processed_data/poses3d/" in i.filename and i.filename.endswith(".npy")]
    by_sequence = {}
    for info in entries:
        by_sequence.setdefault(info.filename.split("/")[1], []).append(info)
    session = requests.Session()
    for sequence, infos in sorted(by_sequence.items()):
        path = os.path.join(target, sequence + ".npz")
        if os.path.exists(path):
            continue
        infos.sort(key=lambda i: i.header_offset)
        # Contiguous runs (gaps under 1 MB) fetched in one request each.
        runs, run = [], [infos[0]]
        for info in infos[1:]:
            end = run[-1].header_offset + 30 + len(run[-1].filename.encode()) + 1024 + run[-1].compress_size
            if info.header_offset - end < (1 << 20):
                run.append(info)
            else:
                runs.append(run)
                run = [info]
        runs.append(run)
        frames = {}
        for run in runs:
            lo = run[0].header_offset
            hi = run[-1].header_offset + 30 + len(run[-1].filename.encode()) + 4096 + run[-1].compress_size
            data = session.get(url, headers={"Range": f"bytes={lo}-{hi}"}, timeout=120).content
            for info in run:
                o = info.header_offset - lo
                if data[o:o + 4] != b"PK\x03\x04":
                    raise ValueError(f"{info.filename}: no local header")
                name_len, extra_len = struct.unpack("<HH", data[o + 26:o + 30])
                body = data[o + 30 + name_len + extra_len:o + 30 + name_len + extra_len + info.compress_size]
                raw = body if info.compress_type == 0 else zlib.decompress(body, -15)
                frames[int(os.path.basename(info.filename)[:-4])] = decode(raw)
        numbers = sorted(frames)
        names = sorted(frames[numbers[0]])
        if len(names) != 2:
            print(f"  {sequence}: {len(names)} people, skipped")
            continue
        a = np.stack([frames[n][names[0]] for n in numbers])
        b = np.stack([frames[n][names[1]] for n in numbers])
        np.savez(path, a=a, b=b, frames=np.array(numbers), names=np.array(names))
        print(f"  {stem}/{sequence}: {len(numbers)} frames, {len(runs)} requests")


out = sys.argv[1]
for archive in sys.argv[2:]:
    print(archive)
    fetch(archive, out)
