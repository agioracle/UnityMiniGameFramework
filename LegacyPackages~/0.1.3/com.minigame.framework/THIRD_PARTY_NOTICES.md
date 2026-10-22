# Third-party dependencies

The project uses the following upstream dependencies. The framework itself is newly implemented and does not embed QFramework, TEngine or UnityGameFramework runtime code.

| Dependency | Version / revision | Source / license |
| --- | --- | --- |
| UniTask | 2.5.11 / 2e993ff18f28c931602a07292df0b0804eebef99 | Cysharp/UniTask, MIT; license retained in upstream Unity package |
| R3 Unity | 1.3.0 / 329ca7915713417f2e0837b0e0a80b4da074db4a | Cysharp/R3, MIT |
| R3 core | 1.3.0, .NET Standard 2.1 | Official NuGet; original DLL and notices in com.minigame.dependencies.r3 |
| Microsoft R3 dependencies | TimeProvider 8.0.0, AsyncInterfaces 6.0.0, Annotations 5.0.0, Unsafe 6.0.0, Channels 8.0.0 | Official NuGet; exact source URLs, archive hashes and notices in com.minigame.dependencies.r3 |
| YooAsset | 2.3.17 / 101236db8a58a327071ff1ab35d28bc1535ca1c1 | tuyoogame/YooAsset, Apache-2.0; upstream notices retained |
| Noto Sans Regular | Downloaded from notofonts/noto-fonts official hinted TTF distribution | SIL Open Font License 1.1; OFL.txt next to sample font source; DemoEnglish/DemoFrench are generated TMP glyph subsets |
| TextMesh Pro essential resources | Unity package 3.0.7 distribution | Original Unity package/resource notices retained |
| Unity official packages | Existing manifest plus SBP resolved by YooAsset | Respective Unity package licenses; not assumed to be MIT |
| Google.Protobuf (optional) | 3.36.2, original .NET Standard 2.0 assembly | Official NuGet, BSD-3-Clause; license and hashes in OptionalPackages~/com.minigame.transport.protobuf |
| protoc (development tool) | 36.2, macOS arm64 | Official protocolbuffers/protobuf release, BSD-3-Clause; archive/tool hashes in the optional package lock; executable remains under Builds/Dependencies, not in Player |

NuGet archives and generated test files are local build artifacts under Builds. Do not strip their original license/notice files when redistributing the installed assemblies. Dependency versions are candidates for platform validation; Editor compilation and tests do not establish WebGL, IL2CPP or a particular mini-game SDK compatibility.

MessagePack and platform SDKs are not installed by this P1 dependency set. No platform payment keys, credentials or game business data belong in these packages.
