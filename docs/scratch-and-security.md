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
