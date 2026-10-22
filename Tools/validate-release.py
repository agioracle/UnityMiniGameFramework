#!/usr/bin/env python3
"""Validate actual suite installation and an unchanged consumer in a fresh Unity project."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import uuid
import xml.etree.ElementTree as ET


ROOT = Path(__file__).absolute().parents[1]
UNITY = "/Applications/Unity/Hub/Editor/2022.3.62f3/Unity.app/Contents/MacOS/Unity"
SPEC = importlib.util.spec_from_file_location("release_tool", ROOT / "Tools/release-tool.py")
TOOL = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(TOOL)


def suite_install(suite, project, sample=False):
    command = ["python3", str(suite / "release-tool.py"), "install", str(suite), str(project)]
    if sample:
        command.append("--sample")
    subprocess.run(command, check=True, env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"})


def run_unity(project, output, name, extra, read_only=False):
    command = [UNITY, "-batchmode", "-projectPath", str(project), "-logFile", str(output / (name + ".log")), *extra]
    environment = dict(os.environ)
    if read_only:
        environment["MINIGAME_UPGRADE_READ_ONLY"] = "1"
    environment.pop("MINIGAME_LONG_FRAME_OBSERVATION", None)
    with (output / (name + ".process.log")).open("wb") as stream:
        result = subprocess.run(command, stdout=stream, stderr=subprocess.STDOUT, env=environment, timeout=600)
    (output / (name + ".exit")).write_text(str(result.returncode))
    if result.returncode != 0:
        raise RuntimeError("Unity failed: " + name)
    return result.returncode


def tests(project, output, name, platform, filter_text, read_only=False):
    path = output / (name + ".xml")
    run_unity(project, output, name, ["-runTests", "-testPlatform", platform, "-testFilter", filter_text, "-testResults", str(path)], read_only)
    result = ET.parse(path).getroot()
    if result.get("result") != "Passed" or int(result.get("failed", "0")) != 0 or int(result.get("total", "0")) < 1 or int(result.get("skipped", "0")):
        raise RuntimeError("Test selection did not pass: " + name)
    discovered = {item.get("fullname") for item in result.iter("test-case")}
    required = "MiniGame.ReleaseValidation.Tests.ReleaseUpgradeTests.ImportedSampleWinsAndProgressSurvivesPackageUpgrade" if platform == "PlayMode" else None
    if required and required not in discovered:
        raise RuntimeError("Required release consumer test was not discovered: " + required)
    return {"name": name, "platform": platform, "xml": str(path), "total": int(result.get("total")), "passed": int(result.get("passed")), "exit": 0}


def snapshot(project):
    paths = [project / "Assets", project / "ProjectSettings", project / "Builds/ConsumerSave", project / "Builds/SampleSave"]
    return {str(path.relative_to(project)): TOOL.digest(path) for root in paths for path in root.rglob("*") if path.is_file()}


def validate(baseline, upgrade=None, existing=None):
    run_id = time.strftime("%Y%m%d-%H%M%S", time.gmtime()) + "-" + uuid.uuid4().hex[:8]
    output = ROOT / "Builds/TestResults/Release" / run_id
    output.mkdir(parents=True, exist_ok=False)
    project = existing or ROOT / "BuildWorkspaces~/release-consumer" / run_id
    summary = {"schema": 1, "status": "Running", "project": str(project), "tests": [], "scope": "Local immutable UPM suites; fresh project, unchanged consumer, real sample, Mono Player; not performance/AOT/device approval"}
    try:
        if existing is None:
            if project.exists():
                raise FileExistsError("Use a new workspace.")
            project.mkdir(parents=True)
            (project / "Assets").mkdir()
            (project / "ProjectSettings").mkdir()
            (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: " + TOOL.verify(baseline)["support"]["unity"] + "\n")
            TOOL.write_json(project / "Packages/manifest.json", {"dependencies": {"com.unity.test-framework": "1.1.33"}, "testables": ["com.minigame.framework"]})
            suite_install(baseline, project, True)
            TOOL.copy_tree(baseline / "Validation~/Consumer", project / "Assets/Consumer")
            TOOL.write_json(project / "release-validation.json", {"source": str(baseline), "intent": "fresh install then upgrade"})
            summary["tests"].append(tests(project, output, "baseline-editmode", "EditMode", "MiniGame.Framework.Tests.FoundationTests;MiniGame.Framework.Tests.StoreTests"))
            summary["tests"].append(tests(project, output, "baseline-playmode", "PlayMode", "MiniGame.Framework.Tests.RuntimeLifecycleTests;MiniGame.Framework.Tests.FrameworkGapTests;MiniGame.Examples.Basic.Tests.PlayableLoopTests;MiniGame.ReleaseValidation.Tests.ReleaseUpgradeTests"))
            summary["consumerState"] = snapshot(project)
        else:
            state_path = project / "Builds/baseline-state.json"
            state = TOOL.read_json(state_path)
            if snapshot(project) != state:
                raise RuntimeError("Consumer/sample/settings/save changed before upgrade.")
            summary["consumerState"] = state
        TOOL.write_json(project / "Builds/baseline-state.json", summary["consumerState"])
        if upgrade is not None:
            summary["baselineVersion"] = TOOL.verify(baseline)["version"]
            summary["upgradeVersion"] = TOOL.verify(upgrade)["version"]
            suite_install(upgrade, project)
            summary["tests"].append(tests(project, output, "upgraded-editmode", "EditMode", "MiniGame.Framework.Tests.FoundationTests;MiniGame.Framework.Tests.StoreTests", True))
            summary["tests"].append(tests(project, output, "upgraded-playmode", "PlayMode", "MiniGame.Framework.Tests.RuntimeLifecycleTests;MiniGame.Framework.Tests.FrameworkGapTests;MiniGame.Examples.Basic.Tests.PlayableLoopTests;MiniGame.ReleaseValidation.Tests.ReleaseUpgradeTests", True))
            if snapshot(project) != summary["consumerState"]:
                raise RuntimeError("Package upgrade changed consumer, sample, settings or save bytes.")
            run_unity(project, output, "player-build", ["-quit", "-executeMethod", "MiniGame.ReleaseValidation.Editor.ReleaseValidationBuilder.Build"])
            receipt = TOOL.read_json(project / "Builds/player-build.json")
            app = Path(receipt["output"])
            executable = app / "Contents/MacOS" / "ReleaseConsumer"
            if not executable.is_file():
                choices = list((app / "Contents/MacOS").iterdir())
                if len(choices) != 1:
                    raise RuntimeError("Ambiguous Player executable.")
                executable = choices[0]
            player_evidence = output / "player-smoke.json"
            with (output / "player.log").open("wb") as stream:
                result = subprocess.run([str(executable), "-releaseEvidence", str(player_evidence), "-screen-fullscreen", "0", "-screen-width", "640", "-screen-height", "480"], stdout=stream, stderr=subprocess.STDOUT, timeout=60)
            (output / "player.exit").write_text(str(result.returncode))
            if result.returncode != 0 or TOOL.read_json(player_evidence).get("passed") is not True:
                raise RuntimeError("Built Player did not pass actual sample/save smoke.")
            summary["player"] = {"receipt": receipt, "smoke": str(player_evidence), "exit": 0}
            suite_install(baseline, project)
            summary["tests"].append(tests(project, output, "rollback-playmode", "PlayMode", "MiniGame.ReleaseValidation.Tests.ReleaseUpgradeTests", True))
            if snapshot(project) != summary["consumerState"]:
                raise RuntimeError("Rollback changed consumer, sample, settings or saves.")
            summary["upgradeAndRollbackPreservedConsumer"] = True
        summary["status"] = "Passed"
    except BaseException as error:
        summary["status"] = "Failed"
        summary["error"] = str(error)
        raise
    finally:
        TOOL.write_json(output / "summary.json", summary)
        print(str(output / "summary.json"), flush=True)
    return project


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", type=Path)
    parser.add_argument("--upgrade", type=Path)
    parser.add_argument("--existing", type=Path)
    args = parser.parse_args()
    validate(args.baseline.absolute(), args.upgrade.absolute() if args.upgrade else None, args.existing.absolute() if args.existing else None)


if __name__ == "__main__":
    main()