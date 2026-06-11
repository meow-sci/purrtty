# Third-Party Notices

purrtty incorporates the following third-party components.

## Ghostty.Vt (libghostty-vt-dotnet)

- **Path in repo:** `vendor/Ghostty.Vt/`
- **Upstream:** https://github.com/deblasis/libghostty-vt-dotnet
- **License:** MIT (see `vendor/Ghostty.Vt/LICENSE`)
- **Description:** Managed .NET bindings for libghostty-vt. Vendored and extended by purrtty.

## libghostty-vt (Ghostty)

- **Upstream:** https://github.com/ghostty-org/ghostty
- **License:** MIT
- **Description:** The native VT engine. Pinned (not forked); built from source and
  distributed as prebuilt shared libraries under `vendor/Ghostty.Vt/native/<rid>/`
  (osx-arm64, win-x64, linux-x64).
