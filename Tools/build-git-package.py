#!/usr/bin/env python3
"""Assemble a self-contained UPM Git package; upstream code and old releases stay unchanged."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess


UPSTREAM = {
    "UniTask": ("com.cysharp.unitask", "2.5.11", "2e993ff18f28c931602a07292df0b0804eebef99", "MIT"),
    "R3Unity": ("com.cysharp.r3", "1.3.0", "329ca7915713417f2e0837b0e0a80b4da074db4a", "MIT"),
    "YooAsset": ("com.tuyoogame.yooasset", "2.3.17", "101236db8a58a327071ff1ab35d28bc1535ca1c1", "Apache-2.0"),
}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def copy(source, destination):
    for path in (source, *source.parents):
        if path.is_symlink():
            raise ValueError("Linked source: " + str(source))
    destination.parent.mkdir(parents=True, exist_ok=True)
    if source.is_dir():
        if any(p.is_symlink() for p in source.rglob("*")):
            raise ValueError("Linked source tree.")
        shutil.copytree(source, destination)
    else:
        shutil.copy2(source, destination)


def json_file(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n")


def asset(source, destination):
    copy(source, destination)
    meta = Path(str(source) + ".meta")
    if meta.exists():
        copy(meta, Path(str(destination) + ".meta"))


def package_documentation(repository, destination):
    destination.mkdir(parents=True, exist_ok=False)
    for path in sorted((repository / "Documentation").glob("*.md")):
        text = path.read_text()
        text = text.replace("(../Evidence/", "(https://github.com/agioracle/UnityMiniGameFramework/blob/main/Evidence/")
        (destination / path.name).write_text(text)


def build(source, repository, version):
    source, repository = source.absolute(), repository.absolute()
    if not re.fullmatch(r"0\.\d+\.\d+", version):
        raise ValueError("Explicit preview version required.")
    if not (repository / ".git").exists():
        raise ValueError("Existing publication repository required.")
    tags = subprocess.run(["git", "-C", str(repository), "tag", "--list", "v" + version],
                          check=True, capture_output=True, text=True).stdout.strip()
    if tags:
        raise FileExistsError("Version already tagged; do not reassemble published content.")
    required = [repository / "Documentation" / name for name in ("Installation.md", "ApiUsage.md", "Upgrade.md", "Support.md")]
    required += [repository / name for name in ("LICENSE", "CHANGELOG.md", "THIRD_PARTY_NOTICES.md")]
    required += [repository / "Release~" / name for name in ("GitPackageREADME.md", "ThirdPartyLicenses/UniTask-LICENSE.txt", "ThirdPartyLicenses/R3-LICENSE.txt")]
    if any(not path.is_file() for path in required):
        raise FileNotFoundError("Current repository documentation or license inputs are missing.")
    target = repository / "Releases" / version / "package"
    if target.exists():
        raise FileExistsError("Immutable assembly target already exists.")
    framework = source / "Packages/com.minigame.framework"
    manifest = json.loads((source / "Packages/manifest.json").read_text())["dependencies"]
    locked = json.loads((source / "Packages/packages-lock.json").read_text())["dependencies"]
    caches = {}
    for label, (name, pinned_version, commit, license_name) in UPSTREAM.items():
        if not manifest[name].endswith("#" + commit) or locked[name].get("hash") != commit:
            raise ValueError("Upstream source drift: " + name)
        matches = list((source / "Library/PackageCache").glob(name + "@*"))
        matches = [p for p in matches if json.loads((p / "package.json").read_text())["version"] == pinned_version]
        if len(matches) != 1:
            raise ValueError("Expected one resolved upstream package: " + name)
        caches[label] = matches[0]
    metadata = json.loads((framework / "package.json").read_text())
    metadata["version"] = version
    metadata["license"] = "MIT"
    metadata["repository"] = {"type": "git", "url": "https://github.com/agioracle/UnityMiniGameFramework.git"}
    metadata["description"] = "Self-contained Unity services with pinned upstream libraries; install from one Git URL with a version tag."
    for name in ("com.cysharp.unitask", "com.cysharp.r3", "com.minigame.dependencies.r3", "com.tuyoogame.yooasset"):
        metadata["dependencies"].pop(name, None)
    metadata["dependencies"]["com.unity.scriptablebuildpipeline"] = "1.21.25"
    metadata["samples"] = [{"displayName": "Basic Playable", "description": "Optional standalone gameplay and rule tests", "path": "Samples~/Basic"}]
    target.mkdir(parents=True)
    for name in ("Runtime", "Editor", "Tests"):
        asset(framework / name, target / name)
    asset(framework / "package.json", target / "package.json")
    json_file(target / "package.json", metadata)
    copy(repository / "LICENSE", target / "LICENSE")
    package_documentation(repository, target / "Documentation~")
    readme = (repository / "Release~/GitPackageREADME.md").read_text()
    (target / "README.md").write_text(readme.replace("{{VERSION}}", version))
    copy(repository / "CHANGELOG.md", target / "CHANGELOG.md")
    copy(repository / "THIRD_PARTY_NOTICES.md", target / "THIRD_PARTY_NOTICES.md")
    records = []
    for label, (name, pinned_version, commit, license_name) in UPSTREAM.items():
        cache = caches[label]
        destination = target / "ThirdParty" / label
        for part in ("Runtime", "Editor"):
            asset(cache / part, destination / part)
        if label == "YooAsset":
            asset(cache / "LICENSE.md", destination / "LICENSE.md")
        else:
            copy(repository / ("Release~/ThirdPartyLicenses/" + ("UniTask" if label == "UniTask" else "R3") + "-LICENSE.txt"), destination / "LICENSE.txt")
        copy(cache / "package.json", target / "Documentation~" / (label + "-upstream-package.json"))
        records.append({"name": name, "version": pinned_version, "commit": commit, "source": manifest[name], "license": license_name,
                        "files": {str(p.relative_to(destination)): sha(p) for p in sorted(destination.rglob("*")) if p.is_file()}})
    core = source / "Packages/com.minigame.dependencies.r3"
    for part in ("Runtime", "Notices"):
        asset(core / part, target / "ThirdParty/R3Core" / part)
    copy(core / "dependencies.lock.json", target / "ThirdParty/R3Core/dependencies.lock.json")
    records.append({"name": "R3 core and official NuGet dependencies", "version": "1.3.0", "files": {str(p.relative_to(target / "ThirdParty/R3Core")): sha(p) for p in sorted((target / "ThirdParty/R3Core").rglob("*")) if p.is_file()}})
    json_file(target / "Documentation~/third-party-lock.json", records)
    support = target / "Runtime/Support/TextMesh Pro"
    for part in ("Fonts", "Resources", "Shaders", "Sprites"):
        asset(source / "Assets/TextMesh Pro" / part, support / part)
    fixture = target / "Tests/Fixtures"
    for relative in ("Assets/Examples/Basic/Content/Services/DemoEnglish.asset", "Assets/Examples/Basic/Content/Fonts/NotoSans-Regular.ttf", "Assets/Examples/Basic/Content/Fonts/OFL.txt"):
        p = source / relative
        asset(p, fixture / p.name)
    sample = target / "Samples~/Basic"
    basic = source / "Assets/Examples/Basic"
    for part in ("Domain", "Tests/EditMode"):
        asset(basic / part, sample / part)
    for part in ("Presentation/BasicGameView.cs", "Presentation/BasicLevelConfig.cs", "Presentation/BasicProgress.cs", "Presentation/MiniGame.Basic.Presentation.asmdef", "Scenes/Boot.unity", "Content/Rounded.asset", "Tests/PlayMode/PlayableLoopTests.cs", "Tests/PlayMode/MiniGame.Basic.PlayMode.Tests.asmdef"):
        asset(basic / part, sample / part)
    receipt = {"version": version, "package": metadata["name"], "gitUrl": "https://github.com/agioracle/UnityMiniGameFramework.git?path=/Packages/com.minigame.framework#v" + version,
               "dependencies": metadata["dependencies"], "upstream": records,
               "files": {str(p.relative_to(target)): sha(p) for p in sorted(target.rglob("*")) if p.is_file()}}
    json_file(target.parent / "assembly.json", receipt)
    print(json.dumps({"package": str(target), "version": version, "files": len(receipt["files"]), "gitUrl": receipt["gitUrl"]}, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--repository", type=Path, required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    build(args.source, args.repository, args.version)


if __name__ == "__main__":
    main()
