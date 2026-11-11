"""Runs DeskPicker for a README recording, with made-up profiles.

The real profiles.json holds real logins and commit emails, so the recording uses a
copy of the app in .work/demo/ with demo_profiles.json beside it instead. The copy is
built from src/ when it is missing or older than the source.

    python demo.py           build if needed, then run it (what readmotion.json runs)
    python demo.py --build   just build it (do this before capture: building is slow)
"""
import json
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
DEMO = HERE / ".work" / "demo"
EXE = DEMO / "DeskPicker.exe"


def stale() -> bool:
    if not EXE.exists():
        return True
    built = EXE.stat().st_mtime
    return any(p.stat().st_mtime > built for p in (REPO / "src").glob("*.*"))


def build() -> None:
    if stale():
        subprocess.run(["dotnet", "publish", str(REPO / "src" / "DeskPicker.csproj"), "-c", "Release",
                        "-r", "win-x64", "-o", str(DEMO), "-nologo"], check=True)


def seed() -> None:
    profiles = json.loads((HERE / "demo_profiles.json").read_text(encoding="utf-8"))
    for p in profiles:
        # each demo profile gets a data folder of its own, inside .work
        p["dataDir"] = str(DEMO / "data" / p.pop("folder"))
    (DEMO / "profiles.json").write_text(json.dumps(profiles, indent=2), encoding="utf-8")


if __name__ == "__main__":
    build()
    if "--build" in sys.argv:
        sys.exit(0)
    shutil.rmtree(DEMO / "data", ignore_errors=True)
    seed()
    sys.exit(subprocess.call([str(EXE)], cwd=DEMO))
