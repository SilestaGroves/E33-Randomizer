# External tools

Command line tools by Truman Kilen (trumank), built from source and copied next to `E33Randomizer.exe` on build.

| Tool | Used for | Source | Version | Commit | License |
|---|---|---|---|---|---|
| `retoc.exe` | Packing the generated assets into `.pak/.utoc/.ucas` | https://github.com/trumank/retoc | v0.1.5 | `d034ade` | MIT ([licenses/retoc-LICENSE](licenses/retoc-LICENSE)) |
| `uesave.exe` | Save file patching (Misc tab, ScriptsAndHelpers) | https://github.com/trumank/uesave-rs | v0.7.1 + exact float parsing (see below) | `c6bb98d` | MIT ([licenses/uesave-LICENSE](licenses/uesave-LICENSE)) |
| `repak.exe` | Not called by the randomizer; included for working with `.pak` files by hand | https://github.com/trumank/repak | v0.2.3 | `e215472` | MIT or Apache-2.0 ([licenses](licenses)) |

SHA-256:

```
38899186a98aa2a42838478adc65f9be5f82f0908f8a8e4dc40d7ab04090612b  retoc.exe
0563862db3a4772b9bb92e1b32d7c6c31b4f101b08d871fc0bbadd3a9b3a0f7b  uesave.exe
c3d534b9c4e345e0b036b45a25096d23afa3aaeb847445e08ecddcb8f2e934f2  repak.exe
```

## Rebuilding

Built with rustc 1.98.1, toolchain `stable-x86_64-pc-windows-gnu` (needs no Visual Studio), without debug info:

```bash
git clone --depth 1 --branch v0.1.5 https://github.com/trumank/retoc.git
cd retoc
CARGO_PROFILE_RELEASE_STRIP=symbols CARGO_PROFILE_RELEASE_DEBUG=false cargo +stable-x86_64-pc-windows-gnu build --release --locked -p retoc_cli
```

The same for `repak` (`-p repak_cli`) and `uesave-rs` (`-p uesave_cli`). The executables only depend on DLLs that ship with Windows 10 and later.

uesave 0.7 writes saves as JSON in a different format than older versions; `SaveFilePatcher` and the scripts in ScriptsAndHelpers support both.

### uesave: exact float parsing

The official uesave build parses floating point numbers from JSON slightly inexactly, so converting a save to JSON and back changes some coordinates in their last digit. This build enables serde_json's `float_roundtrip` feature, after which `to-json` + `from-json` gives back a byte-identical save. Before building, change this line in `uesave_cli/Cargo.toml`:

```toml
serde_json = { version = "1.0.111", features = ["preserve_order", "float_roundtrip"] }
```
