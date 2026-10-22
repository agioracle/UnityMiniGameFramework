# R3 core dependency bundle

This embedded dependency package contains **unmodified official NuGet assemblies**, not a reimplementation of R3. Unity integration remains the separately pinned upstream `com.cysharp.r3` package.

| Package | Version | Selected target |
| --- | --- | --- |
| R3 | 1.3.0 | netstandard2.1 |
| Microsoft.Bcl.TimeProvider | 8.0.0 | netstandard2.0 |
| Microsoft.Bcl.AsyncInterfaces | 6.0.0 | netstandard2.1 |
| System.ComponentModel.Annotations | 5.0.0 | netstandard2.1 |
| System.Runtime.CompilerServices.Unsafe | 6.0.0 | netstandard2.0 |
| System.Threading.Channels | 8.0.0 | netstandard2.1 |

The upstream R3 Unity assembly explicitly references R3, TimeProvider and AsyncInterfaces. NuGet metadata and included license/third-party notices are retained under `Notices/`. Archive source URLs and SHA-256 values are in `dependencies.lock.json`.

Unity assembly-version validation is not disabled. No Unity or .NET built-in assemblies are replaced. Framework and platform compatibility must be verified against actual Unity compilation and IL2CPP builds; successful .NET loading alone is insufficient.

Licenses: R3 is MIT, copyright Cysharp; these Microsoft libraries declare MIT with their own notices. Preserve the bundled notices when redistributing.
