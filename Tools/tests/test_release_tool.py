import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).absolute().parents[2]
SPEC = importlib.util.spec_from_file_location("release_tool", ROOT / "Tools/release-tool.py")
TOOL = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(TOOL)


class ReleaseToolTests(unittest.TestCase):
    def setUp(self):
        folder = ROOT / "Builds/Generators"
        folder.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(dir=folder)
        self.root = Path(self.temporary.name)
        self.project = self.root / "Project"
        (self.project / "Assets").mkdir(parents=True)
        (self.project / "ProjectSettings").mkdir()
        (self.project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2022.3.62f3\n")
        TOOL.write_json(self.project / "Packages/manifest.json", {"dependencies": {"user.package": "1.0.0"}, "custom": True})
        self.old = self.suite("0.1.1")
        self.new = self.suite("0.1.2")

    def tearDown(self):
        self.temporary.cleanup()

    def suite(self, version):
        path = self.root / version
        for name in TOOL.PACKAGES:
            TOOL.write_json(path / "Packages" / name / "package.json", {"name": name, "version": version})
            (path / "Packages" / name / "source.cs").write_text(version)
        (path / "Packages/com.minigame.framework/Samples~/Basic").mkdir(parents=True)
        (path / "Packages/com.minigame.framework/Samples~/Basic/user.txt").write_text("sample")
        TOOL.write_json(path / "dependencies.json", {"external": "1.0.0"})
        TOOL.write_json(path / "release.json", {"schema": 1, "version": version, "packages": list(TOOL.PACKAGES), "files": TOOL.inventory(path)})
        return path

    def test_install_upgrade_and_rollback_preserve_user_and_sample(self):
        TOOL.install(self.old, self.project, True)
        sample = self.project / "Assets/Samples/MiniGame Framework/Basic/user.txt"
        sample.write_text("user edit")
        TOOL.install(self.new, self.project)
        self.assertEqual("0.1.2", TOOL.read_json(self.project / "Packages/minigame-install.json")["version"])
        TOOL.install(self.old, self.project)
        self.assertEqual("user edit", sample.read_text())
        self.assertTrue(TOOL.read_json(self.project / "Packages/manifest.json")["custom"])

    def test_edited_package_and_dependency_conflicts_are_rejected_before_changes(self):
        TOOL.install(self.old, self.project)
        manifest = self.project / "Packages/manifest.json"
        previous = manifest.read_bytes()
        (self.project / "Packages/com.minigame.framework/source.cs").write_text("user")
        with self.assertRaises(ValueError):
            TOOL.install(self.new, self.project)
        self.assertEqual(previous, manifest.read_bytes())
        value = TOOL.read_json(manifest)
        value["dependencies"]["external"] = "2.0.0"
        TOOL.write_json(manifest, value)
        with self.assertRaises(ValueError):
            TOOL.install(self.new, self.project)

    def test_transaction_failure_restores_old_packages_manifest_and_state(self):
        TOOL.install(self.old, self.project)
        before = TOOL.inventory(self.project)
        original = TOOL.write_json
        def fail(path, data):
            if path.name == "manifest.json":
                raise OSError("controlled write failure")
            original(path, data)
        with patch.object(TOOL, "write_json", side_effect=fail):
            with self.assertRaises(OSError):
                TOOL.install(self.new, self.project)
        self.assertEqual(before, TOOL.inventory(self.project))

    def test_failed_rollback_preserves_old_backup(self):
        TOOL.install(self.old, self.project)
        original = TOOL.shutil.rmtree
        def fail_delete(path, *args, **kwargs):
            if Path(path) == self.project / "Packages/com.minigame.dependencies.r3":
                raise OSError("controlled rollback failure")
            return original(path, *args, **kwargs)
        with patch.object(TOOL, "write_json", side_effect=OSError("controlled write failure")), patch.object(TOOL.shutil, "rmtree", side_effect=fail_delete):
            with self.assertRaises(OSError):
                TOOL.install(self.new, self.project)
        backups = list((self.project / "Packages").glob(".minigame-*/old-com.minigame.framework/source.cs"))
        self.assertEqual(1, len(backups))
        self.assertEqual("0.1.1", backups[0].read_text())

    def test_unity_generated_meta_preserved_across_upgrade(self):
        TOOL.install(self.old, self.project)
        meta = self.project / "Packages/com.minigame.framework/source.cs.meta"
        meta.write_text("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n")
        before = meta.read_bytes()
        TOOL.install(self.new, self.project)
        self.assertEqual(before, meta.read_bytes())

    def test_tampered_release_and_links_and_open_editor_rejected(self):
        (self.old / "Packages/com.minigame.framework/source.cs").write_text("tampered")
        with self.assertRaises(ValueError):
            TOOL.verify(self.old)
        linked = self.root / "linked"
        linked.symlink_to(self.new, target_is_directory=True)
        with self.assertRaises(ValueError):
            TOOL.verify(linked)
        with self.assertRaises(ValueError):
            TOOL.safe(self.root, self.root / "../escape")
        source = self.root / "asset"
        source.symlink_to(self.new / "dependencies.json")
        with self.assertRaises(ValueError):
            TOOL.copy_file(source, self.root / "copy")
        (self.project / "Temp").mkdir()
        (self.project / "Temp/UnityLockfile").write_text("locked")
        with self.assertRaises(ValueError):
            TOOL.install(self.new, self.project)

    def test_publish_root_matches_frozen_package_and_rejects_existing_destination(self):
        repository = self.root / "Repository"
        (repository / ".git").mkdir(parents=True)
        (repository / "LICENSE").write_text("MIT License\n")
        suite = repository / "Releases/0.1.2"
        TOOL.copy_tree(self.new, suite)
        (suite / "Validation~/Consumer").mkdir(parents=True)
        (suite / "Validation~/Consumer/probe.txt").write_text("fixture")
        release = TOOL.read_json(suite / "release.json")
        release["files"] = {name: value for name, value in TOOL.inventory(suite).items() if name != "release.json"}
        TOOL.write_json(suite / "release.json", release)
        TOOL.publish_root(suite, repository)
        self.assertEqual(TOOL.inventory(suite / "Packages"), TOOL.inventory(repository / "Packages"))
        self.assertEqual("Releases/0.1.2", TOOL.read_json(repository / "distribution.json")["suite"])
        with self.assertRaises(FileExistsError):
            TOOL.publish_root(suite, repository)
        other = self.root / "Other"
        (other / ".git").mkdir(parents=True)
        (other / "LICENSE").write_text("MIT License\n")
        with self.assertRaises(ValueError):
            TOOL.publish_root(suite, other)
        self.assertFalse((other / "Packages").exists())


if __name__ == "__main__":
    unittest.main()
