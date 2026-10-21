#!/usr/bin/bash -p
# Actual main M68K prefix controls plus all prior observer controls. Task native-reset-prefix-20261021.
set -euo pipefail
root=${BASH_SOURCE[0]%/*}; root=$(cd -P -- "$root" && pwd)
source_dir=${1-}; toolchain=${2-}; scratch=${3-}
[[ "$source_dir" = /* && "$toolchain" = /* && "$scratch" = /* && -d "$scratch" ]] || exit 2
"$root/m68k-stall2-run.sh" "$source_dir" "$toolchain" "$scratch"
/usr/bin/env -i PATH=/usr/bin:/bin \
  LD_LIBRARY_PATH="$toolchain/clang/usr/lib/x86_64-linux-gnu:$toolchain/clang/usr/lib/llvm-16/lib" \
  "$toolchain/clang/usr/bin/clang-16" -std=c99 -DLSB_FIRST -DcdStream=cdStream -DGPGX_RESET_PREFIX_ENABLED \
  -DINLINE='static __inline__' -include string.h \
  -O2 -Wall -Wextra -Werror -Wno-unused-function -Wno-sign-compare \
  -I"$source_dir/waterbox/emulibc" \
  -I"$source_dir/waterbox/gpgx/util" \
  -I"$source_dir/waterbox/gpgx/Genesis-Plus-GX/core" \
  -I"$source_dir/waterbox/gpgx/Genesis-Plus-GX/core/cart_hw" \
  -I"$source_dir/waterbox/gpgx/Genesis-Plus-GX/core/cart_hw/svp" \
  -I"$source_dir/waterbox/gpgx/Genesis-Plus-GX/core/cd_hw" \
  -I"$source_dir/waterbox/gpgx/Genesis-Plus-GX/core/debug" \
  -I"$source_dir/waterbox/gpgx/Genesis-Plus-GX/core/input_hw" \
  -I"$source_dir/waterbox/gpgx/Genesis-Plus-GX/core/m68k" \
  -I"$source_dir/waterbox/gpgx/Genesis-Plus-GX/core/ntsc" \
  -I"$source_dir/waterbox/gpgx/Genesis-Plus-GX/core/sound" \
  -I"$source_dir/waterbox/gpgx/Genesis-Plus-GX/core/z80" \
  -I"$source_dir/waterbox/gpgx/cinterface" \
  "$source_dir/waterbox/gpgx/cinterface/audio_trace.c" \
  "$source_dir/waterbox/gpgx/Genesis-Plus-GX/core/m68k/m68kcpu.c" \
  "$source_dir/waterbox/gpgx/cinterface/reset_prefix.c" \
  "$root/reset_prefix_harness.c" \
  -o "$scratch/native-selftest/reset-prefix-harness"
"$scratch/native-selftest/reset-prefix-harness"
