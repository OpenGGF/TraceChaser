# Scratch and security

TraceChaser is source-only. Never commit or publish ROMs, BK2 movies, savestates,
BizHawk distributions, native build output, trace captures, process logs, or
machine-specific paths.

## Directory boundary

Use three distinct absolute roots:

1. the TraceChaser checkout (producer source);
2. the consumer checkout and its fixture/movie inputs; and
3. durable external scratch/evidence storage.

The third must be outside the first two, including through symlinks and other
aliases. A safe layout is `/srv/trace-research/{inputs,scratch,evidence}` with
the source checkouts elsewhere. Candidate paths must not already exist because
publication uses no-replace semantics. Do not use `/tmp` for long native runs;
it may be a size-limited memory filesystem.

Before recording, verify the resolved roots and free space:

```bash
realpath /absolute/TraceChaser
realpath /absolute/OpenGGF
realpath /srv/trace-research/scratch
df -h /srv/trace-research/scratch
```

The path guards reject protected aliases, but they do not turn an untrusted
host into a trusted one.

## Override-resume atomic bundle publication

The override-resume publisher is a Linux x86-64-only commit protocol. It opens
the repository and fixed consumer parity root from a retained `/` dirfd with
`openat2(RESOLVE_BENEATH|RESOLVE_NO_SYMLINKS)`, takes an exclusive advisory
lock on the retained parity-root fd, and builds the complete fixed S1/S2 bundle
under a private mode-0700 sibling. The four mode-0600 leaves are written and
validated through retained directory fds and every leaf and private directory
is fsynced. One `renameat2(RENAME_NOREPLACE)` publishes the bundle directory;
the parity root is then fsynced. A root-fsync failure after that rename reports
`committed but durability unconfirmed` and never removes the complete visible
bundle. Precommit failures may leave a private mode-0700 sibling, but do not
create or modify the public bundle name. An existing or competing public name
is left untouched.

This protocol has an environmental precondition. Every supported publisher
must cooperate in the parity-root lock, and the authoritative repository,
parity root, and their ancestors must remain protected and namespace-stable
against rename and mount mutation while their pathnames are authoritative.
Linux dirfds, `openat2`, `statx`, and advisory locking cannot contain a hostile
same-credential actor allowed to rename those paths or change their mounts
after validation. That actor is outside the supported threat model.

## Secrets and copyrighted inputs

Treat ROMs and movies as private inputs. Keep them out of shell history where
practical, restrict directory permissions, and never paste hashes or paths into
public logs unless their disclosure has been reviewed. Trace output can reveal
route/input behavior even when it contains no ROM bytes; review evidence before
publication.

The repository policy scans the current Git index and all reachable history
for forbidden artifacts, oversized payloads, binary signatures, and machine
paths. Deleting a leaked file in a later commit is not enough: stop and repair
history before any push.

## Dependency trust

Acquire BizHawk only through the exact runtime lock. Offline archives receive
the same full-archive hash check before extraction. Preflight is read-only, but
EmuHawk portable mode writes beside its executable during Lua use, so give Lua
captures a user-owned installation rather than a shared read-only cache.

Run capture tools in a clean environment. In particular, loader-injection
variables can execute before a dynamically linked shell gets control. If the
parent environment is not trusted, use an external clean launcher or isolated
host; a script cannot retroactively secure its own startup.
