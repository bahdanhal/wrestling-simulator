"""Installs MakeHuman CC0 asset packs into MPFB's user data folder.

Run inside Blender with MPFB2 installed:
    blender --background --python tools/characters/install_assets.py -- PACK.zip [PACK.zip ...]
"""
import importlib
import sys


def mpfb(module, name):
    """MPFB is an extension, so its package path is only known at run time."""
    for loaded in list(sys.modules):
        if loaded.endswith(module):
            return getattr(importlib.import_module(loaded), name)
    raise ImportError(f"MPFB module {module} is not loaded; is the MPFB extension installed and enabled?")


def main():
    packs = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not packs:
        raise SystemExit("Pass the asset pack zip files after '--'.")

    asset_service = mpfb("mpfb.services.assetservice", "AssetService")
    location_service = mpfb("mpfb.services.locationservice", "LocationService")
    target = location_service.get_user_data()
    for pack in packs:
        problem = asset_service.check_asset_pack_zip(pack)
        if problem is not None and not problem.startswith("STRUCTURE") and problem != "MACOS":
            raise SystemExit(f"{pack}: not a usable asset pack ({problem}).")
        error = asset_service.fix_and_extract_asset_pack_zip(pack, target)
        if error:
            raise SystemExit(f"{pack}: extraction failed ({error}).")
        print(f"Installed {pack} into {target}")

    asset_service.rescan_pack_metadata()
    asset_service.update_all_asset_lists()
    print("Installed packs:", ", ".join(asset_service.get_pack_names()))


main()
