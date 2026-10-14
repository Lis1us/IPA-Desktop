#!/usr/bin/env bash
set -euo pipefail
root="$PWD"
prefix="$root/device-build/prefix"
mkdir -p "$prefix" "$root/device-build/source" "$root/device-build/source-archives"
export PATH="$prefix/bin:$PATH"
export PKG_CONFIG_PATH="$prefix/lib/pkgconfig"
export CPPFLAGS="-I$prefix/include"
export LDFLAGS="-L$prefix/lib"
export CFLAGS="-O2 -D_WIN32_WINNT=0x0A00"
for project in libplist libimobiledevice-glue libusbmuxd libtatsu libimobiledevice ideviceinstaller; do
  revision=$(python -c 'import json,sys; print(json.load(open(sys.argv[1]))[sys.argv[2]])' Tools/DeviceTools/pins.json "$project")
  git clone --filter=blob:none "https://github.com/libimobiledevice/$project.git" "device-build/source/$project"
  cd "device-build/source/$project"
  git checkout --detach "$revision"
  test "$(git rev-parse HEAD)" = "$revision"
  git archive --format=tar --prefix="$project/" -o "$root/device-build/source-archives/$project.tar" HEAD
  NOCONFIGURE=1 ./autogen.sh
  options=(--prefix="$prefix" --enable-shared --disable-static)
  case "$project" in
    libplist) options+=(--without-cython --without-tests --without-tools);;
    libimobiledevice) options+=(--without-cython --without-readline --with-openssl);;
  esac
  ./configure "${options[@]}"
  make -j2
  make install
  cd "$root"
done
pacman -Q > device-build/msys2-packages.txt
python Tools/DeviceTools/package.py
