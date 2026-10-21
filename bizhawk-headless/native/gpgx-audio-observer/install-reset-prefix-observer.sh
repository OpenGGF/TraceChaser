#!/usr/bin/env bash
# Install the separately labelled constructor-reset diagnostic beside stock; task native-reset-prefix-20261021.
# This verifies build provenance, not the host image, and never changes the existing observer artifact locks.
set -euo pipefail
root=$(cd -P -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
build= stock= output=
while (($#)); do
  case "$1" in
    --build) build=${2-}; shift 2 ;;
    --stock) stock=${2-}; shift 2 ;;
    --output) output=${2-}; shift 2 ;;
    *) echo "install-reset-prefix-observer: unknown argument $1" >&2; exit 2 ;;
  esac
done
"$root/../../../bizhawk/preflight_bizhawk_2_11.sh" --bizhawk-home "$stock"
python3 - "$root" "$build" "$stock" "$output" <<'PY'
import hashlib,json,os,shutil,sys
from pathlib import Path
root,build,stock,output=map(Path,sys.argv[1:])
for p in (build,stock,output):
    if not p.is_absolute(): raise SystemExit('All paths must be absolute')
if output.exists() or output.is_symlink(): raise SystemExit('Output already exists')
stage=Path(str(output)+'.staging')
if stage.exists() or stage.is_symlink(): raise SystemExit('Staging already exists')
identity=json.loads((build/'identity.json').read_text())
if identity['schema']!='openggf.gpgx-reset-prefix-observer-build.v1' or identity['series']!='reset-prefix':
    raise SystemExit('Build is not the reset-prefix diagnostic')
if identity['reset_prefix_abi']!={'version':1,'record_bytes':160,'capacity':128}: raise SystemExit('ABI differs')
lock=json.loads((root/'source-lock.json').read_text())
for key in ('bizhawk','gpgx','musl'):
    if identity['source'][key+'_commit']!=lock[key]['commit']: raise SystemExit('Source pin differs: '+key)
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
for filename,prefix in [('gpgx.wbx','decompressed'),('gpgx.wbx.zst','compressed')]:
    p=build/filename
    if sha(p)!=identity[prefix+'_sha256'] or p.stat().st_size!=identity[prefix+'_size']:
        raise SystemExit('Build bytes differ: '+filename)
patches=sorted(root.glob('000[1-6]-*.patch'))
if identity['patches']!=[{'file':p.name,'sha256':sha(p)} for p in patches] or len(patches)!=6:
    raise SystemExit('Patch series differs')
if not 0<identity['invisible_section_bytes']<=4194304: raise SystemExit('Invisible state exceeds budget')
shutil.copytree(stock,stage,symlinks=True)
shutil.copy2(build/'gpgx.wbx.zst',stage/'dll/gpgx.wbx.zst')
provenance=stage/'gpgx-audio-observer-source'
provenance.mkdir(exist_ok=True)
for p in patches+[root/'source-lock.json',root/'README.md',build/'identity.json']:
    shutil.copy2(p,provenance/p.name)
for name in ('BizHawk-LICENSE','GPGX-LICENSE.txt','musl-COPYRIGHT','zstd-LICENSE'):
    shutil.copy2(build/name,provenance/name)
os.rename(stage,output)
print('install-reset-prefix-observer:',output,'core',identity['compressed_sha256'])
PY
