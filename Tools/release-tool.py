#!/usr/bin/env python3
"""Build and install verified local UPM release suites without editing user assets."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import tarfile
import tempfile


PACKAGES = ("com.minigame.framework", "com.minigame.dependencies.r3")
EXTERNAL = ("com.cysharp.unitask", "com.cysharp.r3", "com.tuyoogame.yooasset", "com.unity.ugui", "com.unity.textmeshpro")
BASE = Path(__file__).resolve().parents[1]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def safe(root, path):
    root, path = Path(root).absolute(), Path(path).absolute()
    if ".." in root.parts or ".." in path.parts or path == root or not path.is_relative_to(root):
        raise ValueError("Path must be a normalized child of the declared root.")
    for node in (path, *path.parents):
        if node.is_symlink():
            raise ValueError("Symbolic links are not allowed.")
    return path


def read_json(path):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("Duplicate JSON key: " + key)
            result[key] = value
        return result
    return json.loads(path.read_text(), object_pairs_hook=unique)


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n")


def copy_file(source, destination):
    safe(Path(source).absolute().parent, Path(source).absolute())
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)


def copy_tree(source, destination):
    safe(Path(source).absolute().parent, Path(source).absolute())
    if not source.is_dir() or source.is_symlink():
        raise ValueError("Expected source directory: " + str(source))
    for path in source.rglob("*"):
        if path.is_symlink():
            raise ValueError("Linked source: " + str(path))
    shutil.copytree(source, destination)


def copy_asset(source, relative, destination):
    path = safe(source, source / relative)
    target = destination / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    copy_file(path, target)
    meta = Path(str(path) + ".meta")
    if meta.exists():
        copy_file(meta, Path(str(target) + ".meta"))


def inventory(root):
    result = {}
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise ValueError("Linked content: " + str(path))
        if path.is_file():
            result[str(path.relative_to(root))] = digest(path)
    return result


def managed_files(target, expected):
    actual = inventory(target)
    if any(actual.get(name) != sha for name, sha in expected.items()):
        raise ValueError("Managed package content changed.")
    extras = {}
    for name in set(actual) - set(expected):
        path = target / name
        if not name.endswith(".meta") or not Path(str(path)[:-5]).exists() or not re.search(r"^guid: [0-9a-f]{32}$", path.read_text(), re.M):
            raise ValueError("Unexpected file in managed package: " + name)
        extras[name] = path
    return extras


def verify(suite):
    suite = Path(suite).absolute()
    if suite.is_symlink():
        raise ValueError("Linked suite.")
    data = read_json(suite / "release.json")
    if data.get("schema") != 1 or data.get("packages") != list(PACKAGES):
        raise ValueError("Unsupported release suite.")
    expected = data["files"]
    if not isinstance(expected, dict) or not expected:
        raise ValueError("Empty release inventory.")
    for name, sha in expected.items():
        path = safe(suite, suite / name)
        if not path.is_file() or digest(path) != sha:
            raise ValueError("Release hash mismatch: " + name)
    actual = set(inventory(suite)) - {"release.json"}
    if actual != set(expected):
        raise ValueError("Release has missing or unexpected files.")
    for name in PACKAGES:
        package = read_json(suite / "Packages" / name / "package.json")
        if package["name"] != name:
            raise ValueError("Package identity mismatch.")
    return data


def assemble(source, repo):
    source, repo = Path(source).absolute(), Path(repo).absolute()
    if not (repo / ".git").exists() or not (repo / "LICENSE").is_file():
        raise ValueError("Explicit existing licensed repository required.")
    version = read_json(source / "Packages/com.minigame.framework/package.json")["version"]
    if not re.fullmatch(r"0\.\d+\.\d+", version):
        raise ValueError("This release line requires an explicit 0.x.y preview version.")
    license_text = (repo / "LICENSE").read_text()
    if not license_text.startswith("MIT License"):
        raise ValueError("Expected the user's existing MIT repository license.")
    release_root = safe(repo, repo / "Releases")
    release_root.mkdir(exist_ok=True)
    suite = safe(release_root, release_root / version)
    if suite.exists():
        raise FileExistsError("Release versions are immutable: " + version)
    temporary = Path(tempfile.mkdtemp(prefix=".assemble-", dir=release_root))
    try:
        for name in PACKAGES:
            copy_tree(source / "Packages" / name, temporary / "Packages" / name)
        framework = temporary / "Packages/com.minigame.framework"
        (framework / "LICENSE").write_text(license_text)
        copy_file(source / "THIRD_PARTY_NOTICES.md", framework / "THIRD_PARTY_NOTICES.md")
        copy_tree(source / "Release~/Documentation", framework / "Documentation~")
        copy_file(source / "Release~/PackageREADME.md", framework / "README.md")
        copy_file(source / "Release~/CHANGELOG.md", framework / "CHANGELOG.md")
        metadata = read_json(framework / "package.json")
        metadata.update(license="MIT", repository={"type": "git", "url": "https://github.com/agioracle/UnityMiniGameFramework.git"},
                        samples=[{"displayName": "Basic Playable", "description": "Optional standalone gameplay and rule tests", "path": "Samples~/Basic"}])
        write_json(framework / "package.json", metadata)
        support = framework / "Runtime/Support"
        support.mkdir()
        tmp = source / "Assets/TextMesh Pro"
        for folder in ("Fonts", "Resources", "Shaders", "Sprites"):
            copy_tree(tmp / folder, support / "TextMesh Pro" / folder)
            copy_file(tmp / (folder + ".meta"), support / "TextMesh Pro" / (folder + ".meta"))
        fixture = framework / "Tests/Fixtures"
        fixture.mkdir()
        for relative in ("Assets/Examples/Basic/Content/Services/DemoEnglish.asset", "Assets/Examples/Basic/Content/Fonts/NotoSans-Regular.ttf", "Assets/Examples/Basic/Content/Fonts/OFL.txt"):
            p = source / relative
            copy_file(p, fixture / p.name)
            copy_file(Path(str(p) + ".meta"), fixture / (p.name + ".meta"))
        sample = framework / "Samples~/Basic"
        sample.mkdir(parents=True)
        basic = source / "Assets/Examples/Basic"
        for relative in ("Domain", "Tests/EditMode"):
            copy_tree(basic / relative, sample / relative)
        for relative in ("Presentation/BasicGameView.cs", "Presentation/BasicLevelConfig.cs", "Presentation/BasicProgress.cs", "Presentation/MiniGame.Basic.Presentation.asmdef", "Scenes/Boot.unity", "Content/Rounded.asset", "Tests/PlayMode/PlayableLoopTests.cs", "Tests/PlayMode/MiniGame.Basic.PlayMode.Tests.asmdef"):
            copy_asset(basic, relative, sample)
        copy_tree(source / "Assets/ReleaseValidation", temporary / "Validation~/Consumer")
        copy_file(source / "Tools/release-tool.py", temporary / "release-tool.py")
        manifest = read_json(source / "Packages/manifest.json")["dependencies"]
        write_json(temporary / "dependencies.json", {name: manifest[name] for name in EXTERNAL})
        dependencies = temporary / "Packages/com.minigame.dependencies.r3"
        (dependencies / "LICENSE").write_text(license_text)
        write_json(temporary / "release.json", {"schema": 1, "version": version, "packages": list(PACKAGES),
                   "support": {"unity": "2022.3.62f3", "backend": "macOS Mono", "status": "preview"},
                   "sourceVersion": version, "files": inventory(temporary)})
        verify(temporary)
        temporary.rename(suite)
    except BaseException:
        shutil.rmtree(temporary)
        raise
    archive = release_root / ("UnityMiniGameFramework-" + version + ".tar.gz")
    with tarfile.open(archive, "w:gz") as stream:
        stream.add(suite, arcname="UnityMiniGameFramework-" + version)
    print(json.dumps({"suite": str(suite), "archive": str(archive), "sha256": digest(archive)}, indent=2))
    return suite


def install(suite, project, sample=False):
    suite, project = Path(suite).absolute(), Path(project).absolute()
    release = verify(suite)
    if not (project / "Assets").is_dir() or not (project / "ProjectSettings/ProjectVersion.txt").is_file():
        raise ValueError("Expected an existing Unity project.")
    if (project / "Temp/UnityLockfile").exists():
        raise ValueError("Close the target Unity Editor before installing.")
    safe(project, project / "Assets")
    packages = safe(project, project / "Packages")
    packages.mkdir(exist_ok=True)
    manifest_path = safe(packages, packages / "manifest.json")
    manifest = read_json(manifest_path) if manifest_path.exists() else {"dependencies": {}}
    expected = read_json(suite / "dependencies.json")
    for name, value in expected.items():
        existing = manifest.get("dependencies", {}).get(name)
        if existing is not None and existing != value:
            raise ValueError("Dependency conflict; resolve explicitly: " + name)
    state_path = safe(packages, packages / "minigame-install.json")
    generated = {}
    state = read_json(state_path) if state_path.exists() else None
    if state is not None and state.get("schema") != 1:
        raise ValueError("Unsupported installed state.")
    current = {}
    for name in PACKAGES:
        target = packages / name
        safe(packages, target)
        if target.exists():
            expected_files = state.get("files", {}).get(name) if state else None
            if not expected_files:
                raise ValueError("Unmanaged package; refusing overwrite: " + name)
            generated[name] = managed_files(target, expected_files)
        existing = manifest.get("dependencies", {}).get(name)
        if existing is not None and existing not in ("file:" + name, "file:./" + name):
            raise ValueError("Remove the existing external package declaration explicitly: " + name)
        current[name] = inventory(suite / "Packages" / name)
    sample_target = project / "Assets/Samples/MiniGame Framework/Basic"
    if sample and sample_target.exists():
        raise ValueError("Sample already exists; never overwrite user sample edits.")
    if sample:
        safe(project / "Assets", sample_target)
    transaction = Path(tempfile.mkdtemp(prefix=".minigame-", dir=packages))
    old_manifest = manifest_path.read_bytes() if manifest_path.exists() else None
    old_state = state_path.read_bytes() if state_path.exists() else None
    changed = []
    sample_created = False
    committed = False
    rollback_completed = False
    try:
        for name in PACKAGES:
            staging = transaction / ("new-" + name)
            copy_tree(suite / "Packages" / name, staging)
            for relative, original in generated.get(name, {}).items():
                destination = staging / relative
                if Path(str(destination)[:-5]).exists() and not destination.exists():
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    copy_file(original, destination)
            current[name] = inventory(staging)
        for name in PACKAGES:
            target = packages / name
            if target.exists():
                target.rename(transaction / ("old-" + name))
            changed.append(name)
            (transaction / ("new-" + name)).rename(target)
        manifest.setdefault("dependencies", {}).update(expected)
        for name in PACKAGES:
            manifest["dependencies"][name] = "file:" + name
        write_json(manifest_path, manifest)
        if sample:
            sample_target.parent.mkdir(parents=True, exist_ok=True)
            sample_created = True
            copy_tree(suite / "Packages/com.minigame.framework/Samples~/Basic", sample_target)
        write_json(state_path, {"schema": 1, "version": release["version"], "files": current})
        committed = True
    except BaseException:
        for name in reversed(changed):
            if (packages / name).exists():
                shutil.rmtree(packages / name)
            old = transaction / ("old-" + name)
            if old.exists():
                old.rename(packages / name)
        for path, value in ((manifest_path, old_manifest), (state_path, old_state)):
            if value is None:
                path.unlink(missing_ok=True)
            else:
                path.write_bytes(value)
        if sample_created and sample_target.exists():
            shutil.rmtree(sample_target)
        rollback_completed = True
        raise
    finally:
        if committed or rollback_completed:
            shutil.rmtree(transaction)
        else:
            print("Installation recovery required; backups preserved: " + str(transaction))
    print(json.dumps({"installed": release["version"], "project": str(project), "sampleImported": sample}, indent=2))


def publish_root(suite, repository):
    suite, repository = Path(suite).absolute(), Path(repository).absolute()
    release = verify(suite)
    if not (repository / ".git").exists() or not (repository / "LICENSE").is_file():
        raise ValueError("Existing licensed Git repository required.")
    suite_relative = safe(repository, suite).relative_to(repository)
    safe(repository, repository / "distribution.json")
    if (repository / "distribution.json").exists():
        raise FileExistsError("Distribution marker already exists; review before republishing.")
    targets = [(suite / "Packages", repository / "Packages"),
               (suite / "Validation~", repository / "Validation~"),
               (suite / "dependencies.json", repository / "dependencies.json")]
    for source, target in targets:
        safe(repository, target)
        if target.exists():
            raise FileExistsError("Repository publish destination exists; review changes instead of overwriting: " + str(target))
    for source, target in targets:
        if source.is_dir():
            copy_tree(source, target)
        else:
            copy_file(source, target)
    marker = {"schema": 1, "version": release["version"], "suite": str(suite_relative),
              "packages": {name: inventory(repository / "Packages" / name) for name in PACKAGES},
              "scope": "Local prepared repository, no Git commit/tag/push or remote release performed"}
    write_json(repository / "distribution.json", marker)
    print(json.dumps({"preparedRepository": str(repository), "version": release["version"]}, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    build = sub.add_parser("assemble")
    build.add_argument("--source", type=Path, required=True)
    build.add_argument("--repository", type=Path, required=True)
    publish = sub.add_parser("publish-root")
    publish.add_argument("suite", type=Path)
    publish.add_argument("repository", type=Path)
    for action in ("verify", "install"):
        p = sub.add_parser(action)
        p.add_argument("suite", type=Path)
        if action == "install":
            p.add_argument("project", type=Path)
            p.add_argument("--sample", action="store_true")
    args = parser.parse_args()
    if args.action == "assemble":
        assemble(args.source, args.repository)
    elif args.action == "verify":
        print(json.dumps(verify(args.suite), indent=2))
    elif args.action == "publish-root":
        publish_root(args.suite, args.repository)
    else:
        install(args.suite, args.project, args.sample)


if __name__ == "__main__":
    main()
