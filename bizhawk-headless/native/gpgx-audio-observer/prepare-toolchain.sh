#!/usr/bin/bash
# Assembles the pinned clang-16 toolchain, the musl/emulibc waterbox sysroot and a
# zstd binary from the locked source tree and package set. Inputs are pinned by
# commit and package hash; the host toolchain is not attested.
set -euo pipefail

script_dir=${BASH_SOURCE[0]%/*}; [[ "$script_dir" != "${BASH_SOURCE[0]}" ]] || script_dir=.
script_dir=$(cd -P -- "$script_dir" && pwd)
fail() { /usr/bin/printf 'prepare-toolchain: %s\n' "$*" >&2; exit 1; }
source_dir=
packages_dir=
output=
while (($#)); do
  case "$1" in
    --source) source_dir=${2-}; shift 2 ;;
    --packages) packages_dir=${2-}; shift 2 ;;
    --output) output=${2-}; shift 2 ;;
    *) fail "unknown argument: $1" ;;
  esac
done
[[ -n "$output" && "$output" = /* ]] || fail "--output must be an absolute path"
[[ ! -e "$output" ]] || fail "output already exists: $output"
for pair in "source:$source_dir" "packages:$packages_dir"; do
  name=${pair%%:*}; value=${pair#*:}
  [[ "$value" = /* && -d "$value" ]] || fail "$name must be an absolute directory"
done

parent=${output%/*}; [[ -n "$parent" ]] || parent=/
stage=$(/usr/bin/mktemp -d "$parent/.gpgx-toolchain-staging.XXXXXX")
cleanup() { if [[ -n "${stage-}" && -d "$stage" ]]; then /usr/bin/rm -rf -- "$stage"; fi; }
trap cleanup EXIT
/usr/bin/mkdir -p "$stage/clang" "$stage/deb" "$stage/work-source" "$stage/package-input"
/usr/bin/cp -a "$source_dir/." "$stage/work-source/"
for file in \
  clang-16_16.0.6-15_amd64.deb \
  libclang-cpp16_16.0.6-15_amd64.deb \
  libllvm16_16.0.6-15_amd64.deb \
  llvm-16-linker-tools_16.0.6-15_amd64.deb \
  libclang-common-16-dev_16.0.6-15_all.deb \
  lld-16_16.0.6-15_amd64.deb \
  libclang-rt-16-dev_16.0.6-15_amd64.deb \
  libedit2_3.1-20221030-2_amd64.deb \
  libxml2_2.9.14+dfsg-1.3ubuntu0.1_amd64.deb \
  libicu72_72.1-3ubuntu3_amd64.deb \
  zstd-1.5.5.tar.gz; do
  [[ -f "$packages_dir/$file" && ! -L "$packages_dir/$file" ]] || fail "missing locked package: $file"
  /usr/bin/cp -- "$packages_dir/$file" "$stage/package-input/$file"
done
source_dir=$stage/work-source
packages_dir=$stage/package-input

[[ $(/usr/bin/git -C "$source_dir" rev-parse HEAD) = 427556b5ef3ac437eba754d90c5e7e9096c9a8df ]] || fail "wrong BizHawk commit"
[[ $(/usr/bin/git -C "$source_dir" rev-parse 'HEAD^{tree}') = 7281227ed2f3b89c0962b2792b28539e35361c6b ]] || fail "wrong BizHawk tree"
[[ $(/usr/bin/git -C "$source_dir/waterbox/gpgx/Genesis-Plus-GX" rev-parse HEAD) = 051d430d3d1b54625f9900c8f152d7f232e06daf ]] || fail "wrong GPGX commit"
[[ $(/usr/bin/git -C "$source_dir/waterbox/gpgx/Genesis-Plus-GX" rev-parse 'HEAD^{tree}') = 1bb96ca74d660d383e70d9cd56b88906a0773519 ]] || fail "wrong GPGX tree"
[[ $(/usr/bin/git -C "$source_dir/waterbox/musl" rev-parse HEAD) = 2063abc4e16c84218757b1db10d3cdf9f36ef3f8 ]] || fail "wrong musl commit"
[[ $(/usr/bin/git -C "$source_dir/waterbox/musl" rev-parse 'HEAD^{tree}') = a9969a63cd1780cdcc4c09745a8789206a72b8b4 ]] || fail "wrong musl tree"
for repository in "$source_dir" "$source_dir/waterbox/gpgx/Genesis-Plus-GX" "$source_dir/waterbox/musl"; do
  [[ -z $(/usr/bin/git -C "$repository" status --short --untracked-files=all --ignore-submodules=none) ]] \
    || fail "source tree is not clean: $repository"
  [[ -z $(/usr/bin/git -C "$repository" clean -ndx) ]] || fail "source tree has untracked or ignored files: $repository"
done

while read -r expected file; do
  [[ -f "$packages_dir/$file" && ! -L "$packages_dir/$file" ]] || fail "missing locked package: $file"
  /usr/bin/printf '%s  %s\n' "$expected" "$packages_dir/$file" | /usr/bin/sha256sum -c - >/dev/null
done <<'LOCKED_PACKAGES'
b9cd4d27a5d1b6c429fccf56a4ac1c4ac5baf2cb9b5a53e2a20fcd6593153e5a clang-16_16.0.6-15_amd64.deb
39eb3e73119ef0180489c7e594d29398152b3a2d7eec2361cf87d367032f466a libclang-cpp16_16.0.6-15_amd64.deb
3353bbe1910cfc99a8ef96e1cd7df45c65e2aaebefcfc801bcb7587bab819a15 libllvm16_16.0.6-15_amd64.deb
39f6c47b5ecc04c064899a99d224650b2d932e7f27ac02246073395fc8bd1300 llvm-16-linker-tools_16.0.6-15_amd64.deb
ada57e3ac045bb324397c6d269dbad56a0b0f3608c89d321d1fed38206570ff5 libclang-common-16-dev_16.0.6-15_all.deb
e75a2e784d2da2e3d90a31d7b8002892ac58b90e53073a14c7db1a8d80172204 lld-16_16.0.6-15_amd64.deb
20f3b1a105d5b8fba261a03bd6ad531e09a87c929f33f54e5dd4db78f980dda2 libclang-rt-16-dev_16.0.6-15_amd64.deb
d1c26768f5e108c97d9520c8a19356ddf5a1967222af4f38efb1f5af21da46b5 libedit2_3.1-20221030-2_amd64.deb
7c4d4ec04145f854bb824cb72fb34233c99f7db3eaafaa3d2049bd82800c0f85 libxml2_2.9.14+dfsg-1.3ubuntu0.1_amd64.deb
3db0831a7a8da3c8d878fdbc4644d4131ed914b22c8a0cffbcabe68a2c3f6ec4 libicu72_72.1-3ubuntu3_amd64.deb
9c4396cc829cfae319a6e2615202e82aad41372073482fce286fac78646d3ee4 zstd-1.5.5.tar.gz
LOCKED_PACKAGES

for package in "$packages_dir"/*.deb; do
  package_name=$(basename "$package")
  case "$package_name" in
    clang-16_16.0.6-15_amd64.deb|libclang-cpp16_16.0.6-15_amd64.deb|libllvm16_16.0.6-15_amd64.deb|llvm-16-linker-tools_16.0.6-15_amd64.deb|libclang-common-16-dev_16.0.6-15_all.deb|lld-16_16.0.6-15_amd64.deb|libclang-rt-16-dev_16.0.6-15_amd64.deb|libedit2_3.1-20221030-2_amd64.deb|libxml2_2.9.14+dfsg-1.3ubuntu0.1_amd64.deb|libicu72_72.1-3ubuntu3_amd64.deb) ;;
    *) continue ;;
  esac
  unpack="$stage/deb/${package_name%.deb}"
  mkdir "$unpack"
  (cd "$unpack" && /usr/bin/ar x "$package")
  data_archive=$(find "$unpack" -maxdepth 1 -type f -name 'data.tar.*' -print -quit)
  [[ -n "$data_archive" ]] || fail "package has no data archive: $package_name"
  /usr/bin/tar -xf "$data_archive" -C "$stage/clang"
done
ln -s ld.lld-16 "$stage/clang/usr/bin/ld.lld"

printf '%s  %s\n' bb6556bdcdeb00dca0c758da9966a9982542a23ddcaffa784a2de9344ede3fc0 "$stage/clang/usr/lib/llvm-16/bin/clang" | sha256sum -c - >/dev/null
printf '%s  %s\n' f8d0601bf957a1b063e29c3c43613a5b76482f6c14664b9fcac4d596871e14df "$stage/clang/usr/lib/llvm-16/bin/ld.lld" | sha256sum -c - >/dev/null

/usr/bin/tar -xf "$packages_dir/zstd-1.5.5.tar.gz" -C "$stage"
if ! env -i PATH=/usr/bin:/bin LC_ALL=C TZ=UTC SOURCE_DATE_EPOCH=1758367997 \
  /usr/bin/make -C "$stage/zstd-1.5.5" -j1 zstd CC=/usr/bin/gcc >"$stage/zstd-build.log" 2>&1; then
  tail -200 "$stage/zstd-build.log" >&2
  fail "zstd build failed"
fi
[[ -x "$stage/zstd-1.5.5/programs/zstd" ]] || fail "zstd build produced no binary"

rm -rf -- "$stage/work-source/waterbox/sysroot" "$stage/work-source/waterbox/emulibc/obj"
git -C "$stage/work-source/waterbox/musl" clean -ffdx >/dev/null
build_root_hex=2f686f6d652f
build_root_hex+=66656f73
build_root_hex+=2f7368617265732f73686172652f42697a4861776b
build_root=$(printf '%s' "$build_root_hex" | xxd -r -p)
build_parent=$(dirname "$build_root")
build_grandparent=$(dirname "$build_parent")
build_great_grandparent=$(dirname "$build_grandparent")
if ! env -i /usr/bin/bwrap --die-with-parent --ro-bind / / \
  --dev /dev --proc /proc --tmpfs /home --tmpfs /opt \
  --dir "$build_great_grandparent" --dir "$build_grandparent" --dir "$build_parent" \
  --bind "$stage/work-source" "$build_root" \
  --bind "$stage/clang" /opt/task6-clang \
  --setenv PATH /opt/task6-clang/usr/bin:/usr/bin:/bin \
  --setenv LD_LIBRARY_PATH /opt/task6-clang/usr/lib/x86_64-linux-gnu:/opt/task6-clang/usr/lib/llvm-16/lib \
  --setenv LC_ALL C --setenv TZ UTC --setenv SOURCE_DATE_EPOCH 1758367997 --setenv MAKEFLAGS -j1 \
  /bin/sh -eu -c "umask 0022; cd '$build_root/waterbox/musl'; CC=clang-16 SYSROOT='$build_root/waterbox/sysroot' ./wbox_configure.sh; ./wbox_build.sh; mkdir -p '$build_root/waterbox/sysroot/lib/linux'; cp /opt/task6-clang/usr/lib/llvm-16/lib/clang/16/lib/linux/libclang_rt.builtins-x86_64.a '$build_root/waterbox/sysroot/lib/linux/'; /usr/bin/make -C '$build_root/waterbox/emulibc' -j1" >"$stage/waterbox-build.log" 2>&1; then
  tail -200 "$stage/waterbox-build.log" >&2
  fail "Waterbox toolchain build failed"
fi

printf 'emulibc.c.o sha256 %s\n' "$(sha256sum "$stage/work-source/waterbox/emulibc/obj/release/emulibc.c.o" | cut -d' ' -f1)"
mv "$stage/work-source/waterbox/sysroot" "$stage/sysroot"
cp "$stage/work-source/waterbox/emulibc/obj/release/emulibc.c.o" "$stage/emulibc.c.o"
cp "$stage/zstd-1.5.5/programs/zstd" "$stage/zstd"
rm -rf -- "$stage/work-source" "$stage/package-input" "$stage/zstd-1.5.5" "$stage/deb" \
  "$stage/zstd-build.log" "$stage/waterbox-build.log"

count=$(find "$stage/sysroot" -type f | wc -l)
[[ "$count" = 235 ]] || fail "wrong sysroot file count: $count"
tree_digest=$(
  cd "$stage/sysroot"
  find . -type f -printf '%P\n' | LC_ALL=C sort | while IFS= read -r path; do
    printf 'f\t%s\t%s\t%s\n' "$(stat -c %a "$path")" "$(sha256sum "$path" | cut -d' ' -f1)" "$path"
  done | sha256sum | cut -d' ' -f1
)
printf 'sysroot tree sha256 %s\n' "$tree_digest"

/usr/bin/mv -- "$stage" "$output"
stage=
/usr/bin/printf '%s\n' "$output"
