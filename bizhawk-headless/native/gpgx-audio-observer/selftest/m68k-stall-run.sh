#!/usr/bin/bash -p
# Native selftests for the 0001+0002+0003+0004 series: the three existing observer runners (ordinary, S3K parity,
# S3K PCM) and the independent stall ABI v1 harness. Arguments match run.sh.
set -euo pipefail
root=${BASH_SOURCE[0]%/*}; root=$(cd -P -- "$root" && pwd)
source_dir=${1-}; toolchain=${2-}; scratch=${3-}
[[ "$source_dir" = /* && "$toolchain" = /* && "$scratch" = /* && -d "$scratch" ]] || exit 2
"$root/s3k-pcm-run.sh" "$source_dir" "$toolchain" "$scratch"
/usr/bin/cp -- "$root/m68k_stall_harness.c" "$scratch/native-selftest/"
/usr/bin/env -i PATH=/usr/bin:/bin \
  LD_LIBRARY_PATH="$toolchain/clang/usr/lib/x86_64-linux-gnu:$toolchain/clang/usr/lib/llvm-16/lib" \
  "$toolchain/clang/usr/bin/clang-16" -std=c99 -DLSB_FIRST -O2 -Wall -Wextra -Werror \
  "$scratch/native-selftest/audio_trace.c" "$scratch/native-selftest/m68k_stall_harness.c" \
  -o "$scratch/native-selftest/m68k-stall-harness"
"$scratch/native-selftest/m68k-stall-harness"
