# Third-party dependencies

The framework's own code uses MIT (Copyright 2026 Agi). This does not replace upstream licenses.

| Content | Version | Original license / location |
| --- | --- | --- |
| UniTask Runtime/Editor | 2.5.11, commit 2e993ff18f28c931602a07292df0b0804eebef99 | MIT, ThirdParty/UniTask/LICENSE.txt |
| R3 Unity Runtime/Editor | 1.3.0, commit 329ca7915713417f2e0837b0e0a80b4da074db4a | MIT, ThirdParty/R3Unity/LICENSE.txt |
| R3 core + Microsoft NuGet DLLs | R3 1.3.0, TimeProvider8.0.0, AsyncInterfaces6.0.0, Annotations5.0.0, Unsafe6.0.0, Channels8.0.0 | Original licenses and notices under ThirdParty/R3Core/Notices; sources/archive hashes in dependencies.lock.json |
| YooAsset Runtime/Editor | 2.3.17, commit 101236db8a58a327071ff1ab35d28bc1535ca1c1 | Apache-2.0, ThirdParty/YooAsset/LICENSE.md |
| Noto Sans Regular fixture | Official notofonts/noto-fonts source | SIL OFL1.1, Tests/Fixtures/OFL.txt |
| TMP Essential Resources | Original Unity TMP3.0.7 resources | Original resource/Unity license notices retained under Runtime/Support/TextMesh Pro |
| Unity modules, uGUI, TMP, SBP | Official top-level package dependencies | Respective Unity licenses, fetched from official registry |

Upstream runtime/editor source, DLLs, asmdef and Unity metadata are copied unmodified. Their nested package manifests are stored as provenance under Documentation~ rather than active child UPM packages; examples/docs/templates are not installed as dependencies. File hashes and Git commits are recorded in Documentation~/third-party-lock.json. No public registry credential, private MCP or platform SDK is included.

Do not install independent UniTask/R3/YooAsset/R3 DLL duplicates alongside this bundled package. PackageInfo for YooAsset's assembly resolves the outer package identity; use the recorded upstream version rather than that UI version for dependency diagnostics. No upstream runtime algorithms have been modified.
