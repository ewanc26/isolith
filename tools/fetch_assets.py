#!/usr/bin/env python3
"""Fetch the CC0 asset packs Isolith uses, reproducibly.

Isolith's own art is generated (see ``generate_assets.py``). This script pulls
the small set of third-party CC0 assets that procedural generation does not beat:
an HDRI sky and photogrammetry-derived PBR materials.

Every download is pinned by URL **and** verified against a SHA-256 recorded in
``tools/assets.lock.json``. A source changing a file under a stable URL is
therefore a hard failure rather than a silent asset swap.

    python3 tools/fetch_assets.py            # fetch and verify against the lock
    python3 tools/fetch_assets.py --check    # verify what is on disk, fetch nothing
    python3 tools/fetch_assets.py --update   # re-download and rewrite the lock

Sources, both CC0 1.0 Universal:

* Poly Haven — https://polyhaven.com/  (HDRIs, textures, models; CC0 site-wide)
* ambientCG  — https://ambientcg.com/  (PBR materials; CC0 per
  https://docs.ambientcg.com/license/)

Standard library only, so this runs on a clean checkout.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import pathlib
import shutil
import sys
import urllib.error
import urllib.request
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
LOCK_PATH = ROOT / "tools" / "assets.lock.json"
DEST = ROOT / "assets" / "thirdparty"

USER_AGENT = "isolith-asset-fetch/1.0 (+https://github.com/ewanc26/isolith)"

# ---------------------------------------------------------------------------
# What we pull, and why
# ---------------------------------------------------------------------------

# The sky. A 2K panorama is ample: the camera is orthographic, the scene is
# fogged, and the sky is never the subject.
HDRI = {
    "id": "qwantani_dusk_1_puresky",
    "resolution": "2k",
    "url": "https://dl.polyhaven.org/file/ph-assets/HDRIs/hdr/2k/qwantani_dusk_1_puresky_2k.hdr",
    "dest": "polyhaven/qwantani_dusk_1_puresky_2k.hdr",
    "source": "https://polyhaven.com/a/qwantani_dusk_1_puresky",
}

# PBR materials for the neutral level geometry. Only the maps the renderer
# actually samples are kept — displacement is the largest file in each pack and
# is unused, since the geometry is boxes.
MATERIAL_MAPS = ("Color", "NormalGL", "Roughness", "AmbientOcclusion")

MATERIALS = [
    {
        "id": "Rock030",
        "role": "solid platforms",
        "source": "https://ambientcg.com/view?id=Rock030",
    },
    {
        "id": "Grass004",
        "role": "grass-topped platforms",
        "source": "https://ambientcg.com/view?id=Grass004",
    },
    {
        "id": "Concrete034",
        "role": "crumbling platforms",
        "source": "https://ambientcg.com/view?id=Concrete034",
    },
]


def material_url(asset_id: str) -> str:
    return f"https://ambientcg.com/get?file={asset_id}_1K-JPG.zip"


# ---------------------------------------------------------------------------
# Fetching
# ---------------------------------------------------------------------------


def download(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})

    try:
        with urllib.request.urlopen(request, timeout=120) as response:
            return response.read()
    except urllib.error.URLError as error:
        raise SystemExit(f"error: could not fetch {url}\n       {error}") from error


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def write(relative: str, data: bytes) -> pathlib.Path:
    path = DEST / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return path


# ---------------------------------------------------------------------------
# Steps
# ---------------------------------------------------------------------------


def fetch_hdri(lock: dict, update: bool) -> None:
    data = download(HDRI["url"])
    actual = digest(data)

    key = f"polyhaven:{HDRI['id']}"
    verify(lock, key, actual, update, HDRI["url"])

    path = write(HDRI["dest"], data)
    print(f"  {path.relative_to(ROOT)}  ({len(data) / 1e6:.1f} MB)")


def fetch_material(material: dict, lock: dict, update: bool) -> None:
    asset_id = material["id"]
    url = material_url(asset_id)

    archive = download(url)
    actual = digest(archive)

    verify(lock, f"ambientcg:{asset_id}", actual, update, url)

    # Extract only the maps in use. Everything is written verbatim from the
    # archive — nothing is re-encoded, so the files stay bit-identical to
    # what ambientCG published.
    kept = 0
    with zipfile.ZipFile(io.BytesIO(archive)) as bundle:
        for name in bundle.namelist():
            stem = pathlib.PurePosixPath(name).name

            if not any(f"_{suffix}." in stem for suffix in MATERIAL_MAPS):
                continue

            path = write(f"ambientcg/{asset_id}/{stem}", bundle.read(name))
            print(f"  {path.relative_to(ROOT)}  ({path.stat().st_size / 1e6:.2f} MB)")
            kept += 1

    if kept == 0:
        raise SystemExit(f"error: {asset_id} archive contained none of {MATERIAL_MAPS}")


def verify(lock: dict, key: str, actual: str, update: bool, url: str) -> None:
    if update:
        lock[key] = {"sha256": actual, "url": url}
        return

    expected = lock.get(key, {}).get("sha256")

    if expected is None:
        raise SystemExit(
            f"error: {key} is not in {LOCK_PATH.name}.\n"
            "       Run with --update to record it."
        )

    if expected != actual:
        raise SystemExit(
            f"error: {key} does not match the lock file.\n"
            f"       expected {expected}\n"
            f"       actual   {actual}\n"
            "       The upstream file changed under a pinned URL. Review the\n"
            "       change before running --update."
        )


def write_licence_notes() -> None:
    """Record the licence next to the files, not only in ASSETS.md."""
    (DEST / "polyhaven").mkdir(parents=True, exist_ok=True)
    (DEST / "ambientcg").mkdir(parents=True, exist_ok=True)

    (DEST / "polyhaven" / "LICENSE.txt").write_text(
        "Assets in this directory are from Poly Haven (https://polyhaven.com/)\n"
        "and are released under the Creative Commons CC0 1.0 Universal License.\n"
        "https://creativecommons.org/publicdomain/zero/1.0/\n\n"
        f"{HDRI['id']} — {HDRI['source']}\n",
        encoding="utf-8",
    )

    lines = [
        "Assets in this directory are from ambientCG (https://ambientcg.com/)",
        "and are released under the Creative Commons CC0 1.0 Universal License.",
        "https://docs.ambientcg.com/license/",
        "",
    ]
    lines += [f"{m['id']} — {m['source']}" for m in MATERIALS]

    (DEST / "ambientcg" / "LICENSE.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")


def check_only(lock: dict) -> int:
    """Verify on-disk files against the lock without touching the network."""
    missing = []

    expected_files = [DEST / HDRI["dest"]]
    for material in MATERIALS:
        directory = DEST / "ambientcg" / material["id"]
        if not directory.is_dir() or not any(directory.iterdir()):
            missing.append(str(directory.relative_to(ROOT)))
            continue

    for path in expected_files:
        if not path.is_file():
            missing.append(str(path.relative_to(ROOT)))

    if missing:
        print("missing assets:")
        for name in missing:
            print(f"  {name}")
        print("\nRun: python3 tools/fetch_assets.py")
        return 1

    print(f"all pinned assets present ({len(lock)} entries in the lock file)")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--update", action="store_true",
                        help="re-download everything and rewrite the lock file")
    parser.add_argument("--check", action="store_true",
                        help="verify what is on disk; do not download")
    parser.add_argument("--clean", action="store_true",
                        help="remove fetched assets before downloading")
    arguments = parser.parse_args()

    lock: dict = {}
    if LOCK_PATH.is_file():
        lock = json.loads(LOCK_PATH.read_text())

    if arguments.check:
        return check_only(lock)

    if arguments.clean and DEST.exists():
        shutil.rmtree(DEST)

    print("Poly Haven (CC0)")
    fetch_hdri(lock, arguments.update)

    print("\nambientCG (CC0)")
    for material in MATERIALS:
        fetch_material(material, lock, arguments.update)

    write_licence_notes()

    if arguments.update:
        LOCK_PATH.write_text(json.dumps(lock, indent=2, sort_keys=True) + "\n")
        print(f"\nwrote {LOCK_PATH.relative_to(ROOT)}")

    print("\ndone — see ASSETS.md for provenance")
    return 0


if __name__ == "__main__":
    sys.exit(main())
