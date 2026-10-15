# Experimental firmware tools for IPA Desktop

This isolated bundle is built from pinned libimobiledevice sources via MSYS2 MinGW x64.
The GUI does not download arbitrary executables and regular DeviceTools updates do not replace this bundle.

idevicerestore carries our narrow --ipa-desktop-update-only patch. Before restore transfers, it rejects an erase fallback, custom firmware, unknown device/version, a downgrade, or a build identity other than the exact Customer Upgrade Install variant. This is an extra guard, not a guarantee against hardware, USB or restore failures.

Sources (including the applied patch), licenses, source revisions, SHA-256 manifest and MSYS2 package versions accompany each experimental artifact. Upstream components retain their individual LGPL/GPL licenses; the GUI's MIT license does not replace them.

Upstream:
- https://github.com/libimobiledevice/idevicerestore
- https://github.com/libimobiledevice/libirecovery
- https://github.com/libimobiledevice/libimobiledevice
- https://github.com/libimobiledevice/libplist
- https://github.com/libimobiledevice/libimobiledevice-glue
- https://github.com/libimobiledevice/libusbmuxd
- https://github.com/libimobiledevice/libtatsu

MSYS2 dependency sources and recipes: https://github.com/msys2/MINGW-packages and https://repo.msys2.org/mingw/sources/.

Build: see .github/workflows/firmware-preview.yml and run bash Tools/FirmwareTools/build.sh from the repository root.
No hardware restore is performed in CI. Windows launch tests cannot establish compatibility with every iPhone or iOS version.
