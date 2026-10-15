#!/usr/bin/env bash
set -euo pipefail
root="$PWD"
prefix="$root/firmware-build/prefix"
mkdir -p "$prefix" "$root/firmware-build/source" "$root/firmware-build/source-archives"
export PATH="$prefix/bin:$PATH"
export PKG_CONFIG_PATH="$prefix/lib/pkgconfig"
export CPPFLAGS="-I$prefix/include"
export LDFLAGS="-L$prefix/lib"
export CFLAGS="-O2 -D_WIN32_WINNT=0x0A00"
for project in libplist libimobiledevice-glue libusbmuxd libtatsu libimobiledevice libirecovery idevicerestore; do
  revision=$(python -c 'import json,sys; print(json.load(open(sys.argv[1]))[sys.argv[2]])' Tools/FirmwareTools/pins.json "$project")
  git clone --filter=blob:none "https://github.com/libimobiledevice/$project.git" "firmware-build/source/$project"
  cd "firmware-build/source/$project"
  git checkout --detach "$revision"
  test "$(git rev-parse HEAD)" = "$revision"
  if [ "$project" = idevicerestore ]; then
    python "$root/Tools/FirmwareTools/patch_restore.py" src/idevicerestore.c
  fi
  git archive --format=tar --prefix="$project/" -o "$root/firmware-build/source-archives/$project.tar" HEAD
  if [ "$project" = idevicerestore ]; then
    tar -cf "$root/firmware-build/source-archives/$project.tar" --transform="s,^,$project/," --exclude=.git .
  fi
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
pacman -Q > firmware-build/msys2-packages.txt
python Tools/FirmwareTools/package.py
