# Commit History Report

Commits after `48e9e2a`, chronological order.

---

## Phase 1 — Claude Code Tooling Setup

### `0cd2d37` — Add qgit, qtest commands and tester agent for Claude Code
**What:** Added `.claude/agents/tester.md`, `.claude/commands/qgit.md`, `.claude/commands/qtest.md`.  
**Why:** Bootstrap Claude Code productivity tools — `qgit` for structured commit workflows, `qtest` to delegate test writing to a specialized tester subagent.

### `40d7e2a` — Allow dotnet test in Claude Code auto-approve list
**What:** Added `dotnet test` to `.claude/settings.local.json` auto-approve.  
**Why:** Claude needs to run tests without prompting the user on every invocation.

### `ddade91` — Restrict qgit to safe git commands only
**What:** Added explicit allowlist/blocklist to `qgit.md` — permits read/additive git commands, prohibits `reset`, `restore`, `clean`, and force variants.  
**Why:** Prevent accidental working-tree destruction from a runaway automated commit workflow.

### `d0b3fd9` — Auto-approve read-only and additive git commands
**What:** Added a set of git sub-commands to the auto-approve list in settings.  
**Why:** Reduce friction for Claude running `git status`, `git log`, etc. during normal operation.

### `3e46ba3` — Fix qgit to run git commands without cd to avoid approval prompts
**What:** Rewrote `qgit.md` command invocations to use absolute paths instead of `cd` prefix.  
**Why:** `cd <dir> && git ...` triggered extra permission prompts; running git directly with no directory change avoids them.

### `523af47` — Set tester agent color to cyan
**What:** Added `color: cyan` frontmatter to `.claude/agents/tester.md`.  
**Why:** Visual distinction in the Claude Code UI when the tester subagent is active.

### `b831b11` — Broaden git auto-approve rule to Bash(git:*)
**What:** Replaced individually listed git sub-command rules with a single `Bash(git:*)` prefix rule.  
**Why:** Blanket approval of all git reads/additives; avoids maintaining a growing list as new commands are needed.

### `87fc1f9` — Add dotnet new console to Claude Code auto-approve list
**What:** Added `dotnet new console` to auto-approve.  
**Why:** Claude creates console projects during scaffolding without interruption.

### `0de10e3` — Add dotnet add to Claude Code auto-approve list
**What:** Added `dotnet add` to auto-approve.  
**Why:** Claude adds NuGet packages without prompting during dependency setup.

### `b8e8ae0` — Add dotnet restore and list to Claude Code auto-approve list
**What:** Added `dotnet restore` and `dotnet list` to auto-approve.  
**Why:** Routine package restoration and dependency listing needed during build cycles.

### `2879a7c` — Allow dotnet new classlib in Claude Code auto-approve
**What:** Added `dotnet new classlib` to auto-approve.  
**Why:** Claude creates class library projects (e.g., Musix.Network) during scaffolding.

---

## Phase 2 — Audio Capture Exploration

### `559ce9f` — Document ProcessLoopbackCapture with detailed inline comments
**What:** Rewrote `Musix/Audio/ProcessLoopbackCapture.cs` with 149 added lines of inline documentation.  
**Why:** The WASAPI loopback capture code has non-obvious behavior (COM threading, buffer handling, format negotiation). Comments capture the reasoning for future maintainers.

### `7380808` — Add FrameOutputNode with QuantumProcessed event and AudioFrame type
**What:** Added `AudioFrame` (readonly record struct with PCM bytes + WaveFormat + Duration), `FrameOutputNode` (subscribes to `ProcessLoopbackCapture.DataAvailable`, enqueues non-silent frames into a bounded `Channel<AudioFrame>` with DropOldest policy, fires `QuantumProcessed` each quantum). 22 tests added.  
**Why:** Needed a clean abstraction layer between raw WASAPI capture events and downstream consumers. Silent frames are filtered to avoid encoding/transmitting silence. Bounded channel with DropOldest prevents unbounded memory growth under slow consumers.

### `c914e20` — Wire FrameOutputNode into Program and display raw float samples
**What:** Replaced the manual `DataAvailable` byte-counter in `Program.cs` with `FrameOutputNode`. `QuantumProcessed` handler casts raw bytes to `ReadOnlySpan<float>` via `MemoryMarshal` and prints the first 8 samples.  
**Why:** Validate that the `FrameOutputNode` abstraction works end-to-end in a live capture session; demonstrate PCM data is accessible.

### `938597c` — Add interactive 10-second MP3 capture test
**What:** `CaptureToMp3Tests` enumerates active audio sessions, prompts user to pick one, captures 10 seconds, writes timestamped `.mp3`. Reconstructs plain IEEE float `WaveFormat` from `WaveFormatExtensible` to satisfy `LameMP3FileWriter`.  
**Why:** End-to-end pipeline smoke test without formal assertions. `LameMP3FileWriter` rejects `WaveFormatExtensible` — the format reconstruction works around that constraint.

---

## Phase 3 — Codec Integration (Concentus / Opus)

### `085bfa8` — Add Concentus and SineWaveGenerator to main project
**What:** Added `Concentus` NuGet package and `SineWaveGenerator` (produces interleaved `float[]` at arbitrary freq/rate/channels).  
**Why:** Concentus is the pure-C# Opus codec needed for audio encoding. `SineWaveGenerator` provides a deterministic test signal for codec validation without requiring a live capture.

### `0b247f3` — Add Opus encode/decode roundtrip test with 440 Hz sine wave
**What:** Encodes 50 × 960-sample frames at 128 kbps, decodes back, asserts per-sample error < 0.02 after correcting for 312-sample encoder lookahead. Prints compression ratio (~16×).  
**Why:** Verify Concentus works correctly in this project before building the live pipeline on top of it. The lookahead correction is non-obvious and documented in the test.

### `a8c4bd7` — Remove UseWindowsSdk to fix Concentus assembly reference
**What:** Removed `<UseWindowsSdk>` from `Musix/Musix.csproj`.  
**Why:** `UseWindowsSdk` activates Windows App SDK package graph resolution, which silently drops pure-managed packages (`net8.0` targets) from the compiler's `/reference` list even when they appear in the NuGet assets file. The project uses only P/Invoke and ComImport — no WinRT projections — so the property was never needed and was breaking Concentus resolution.

### `b49ad27` — Add live Opus encoding pipeline to Program.cs
**What:** Added three adapter classes:
- `FrameSampleProvider` — adapts pushed `AudioFrame` float samples into NAudio's `ISampleProvider` pull model
- `SampleAccumulator` — buffers irregular resampled quanta and dispenses exactly 960-sample chunks (Opus frame size)
- `OpusAudioEncoder` — wraps `IOpusEncoder`, keeps packet buffer off hot path

Wired `ProcessLoopbackCapture → resample (WdlResamplingSampleProvider, device rate → 48 kHz) → SampleAccumulator → OpusAudioEncoder` in `Program.cs`.  
**Why:** Opus requires exactly 48 kHz input in 960-sample frames. Device capture rate varies by hardware, so resampling is mandatory. The push→pull impedance mismatch between WASAPI events and NAudio's pull model requires `FrameSampleProvider`. `SampleAccumulator` handles the frame boundary alignment.

### `96c2741` — Decode each encoded packet and play back decoded audio via WasapiOut
**What:** Added local monitor loop: each Opus packet decoded immediately after encoding, PCM fed into `BufferedWaveProvider` backed by `WasapiOut` on default device.  
**Why:** Proves the codec roundtrip end-to-end with audible output (~20 ms codec delay). Validates the full encode→decode chain before adding network transport.

---

## Phase 4 — Network Transport Layer

### `d5868b8` — Add TCP host and listener proof-of-concept console apps
**What:** Added `Musix.Host` (opens `TcpListener` on port 5000, accepts one connection, sends "hello" once/second) and `Musix.Listener` (connects via `TcpClient`, prints each line with timestamp). Both added to `Musix.slnx`.  
**Why:** Establish the two-process TCP communication skeleton before wiring up the audio pipeline. Validate cancellation handling and connection-closed behavior in isolation.

### `06f55f1` — Switch TCP experiment to length-prefixed binary messaging
**What:** Replaced line-oriented string loop with binary framing: 4-byte big-endian length prefix + variable payload. Host sends 10 frames with random sizes (100–4000 bytes). Listener reads prefix with `ReadExactlyAsync`, allocates exact payload, confirms sizes.  
**Why:** Audio packets are binary and variable-length. Line-oriented text framing is unsuitable. Length-prefixed framing is the pattern the full audio pipeline will use — this commit validates the framing logic before AudioPacket is introduced.

### `f229f7f` — Add Musix.Network library with AudioPacket and PacketWriter
**What:** New `Musix.Network` class library with `AudioPacket` (type/sequence/timestamp/payload fields) and `PacketWriter` (serializes to `Stream` with length-prefixed binary format).  
**Why:** Extracts packet serialization into a shared library so Host, Listener, and main capture project can all use the same wire format without code duplication.

### `24df7e1` — Add PacketReader with async header-then-payload deserialization
**What:** `PacketReader.ReadAsync` reads fixed 18-byte header (type/sequence/timestamp/length) in one `ReadExactlyAsync`, then reads payload.  
**Why:** Exact symmetric inverse of `PacketWriter`. Two-step read (fixed header then variable payload) avoids allocating a large buffer upfront and is efficient for streaming TCP.

### `f85520f` — Reference Musix.Network from Host and Listener projects
**What:** Added project references to `Musix.Network` in both `.csproj` files.  
**Why:** Gives `Musix.Host` and `Musix.Listener` access to `AudioPacket`, `PacketWriter`, `PacketReader` without code duplication.

### `d359855` — Add PacketWriter/PacketReader round-trip tests
**What:** Three test cases (typical payload, empty payload, max field values + 1024-byte random payload) using `MemoryStream`.  
**Why:** Verify wire format correctness before touching real TCP. `MemoryStream` makes these fast and hardware-free.

---

## Phase 5 — Full Pipeline Integration

### `0643241` — Expose Musix internals to Musix.Host via InternalsVisibleTo
**What:** Added `<InternalsVisibleTo Include="Musix.Host" />` to `Musix/Musix.csproj`.  
**Why:** `Musix.Host` needs access to internal audio pipeline classes (`ProcessLoopbackCapture`, etc.) without making them fully public.

### `f0a8974` — Replace Host TCP stub with WASAPI capture and Opus broadcast pipeline
**What:** Added `AudioBroadcaster` (manages connected TCP clients, sends length-prefixed `AudioPacket`s). Wired full pipeline in `Program.cs`: `ProcessLoopbackCapture → resample → SampleAccumulator → Opus encode → AudioBroadcaster.SendFrame`.  
**Why:** This is the core Host functionality — capture system audio, encode with Opus, broadcast over TCP to all connected listeners.

### `2cad766` — Add JitterBuffer for sequence-ordered packet reassembly with gap logging
**What:** `JitterBuffer` (in `Musix.Network`) holds packets in a `SortedDictionary` keyed by sequence number, dequeues in order, logs gaps.  
**Why:** TCP delivers in order but network conditions can cause reordering at higher layers. The JitterBuffer ensures the consumer always gets packets in sequence, with gap detection for diagnostics.

### `e293590` — Replace Listener TCP stub with Opus decode and WasapiOut playback pipeline
**What:** Full listener pipeline: receive loop feeds `PacketReader.ReadAsync` into `JitterBuffer`; consumer task dequeues every 20ms, decodes via Concentus (PLC on null packet), writes PCM to `BufferedWaveProvider` backed by `WasapiOut`.  
**Why:** This is the core Listener functionality — receive Opus packets over TCP, reassemble in order, decode and play back audio. PLC (Packet Loss Concealment) fills gaps with synthesized audio instead of silence.

### `4481f1e` — Accept host and port as CLI args in Musix.Listener
**What:** `Musix.Listener` now reads host/port from `args[0]`/`args[1]` instead of hardcoded values.  
**Why:** Enables connecting to any host, not just localhost. Required for real-world use across machines.

---

## Phase 6 — Bug Fixes & Audio Quality

### `72e8850` — Add pre-buffer priming to JitterBuffer
**What:** `JitterBuffer` now waits for `targetDepth` packets before beginning dequeue, then syncs `_nextSeq` to the first real sequence number received.  
**Why:** Without priming, the consumer loop dequeued immediately at `seq=0` before any packets arrived, injecting PLC silence ahead of real audio. This caused playback to sound slow/offset because the stream started with synthesized silence frames.

### `07a4e7d` — Fix listener playback: timer resolution, buffer size, PLC gating
**What:** Four fixes:
1. `timeBeginPeriod(1)` P/Invoke — forces 1ms Windows timer resolution
2. `BufferDuration` 200ms → 500ms
3. `DiscardOnBufferOverflow = false`
4. Skip PLC injection when `BufferedWaveProvider` has >100ms buffered

**Why:**
- Default Windows timer resolution is ~15.6ms; `Task.Delay(20)` was firing at ~31ms intervals, causing the playback buffer to slowly drain and underrun.
- Larger buffer gives more headroom against network jitter.
- `DiscardOnBufferOverflow = true` was dropping real audio frames when the buffer was momentarily full.
- PLC accumulation when the jitter buffer was priming caused silence pile-up; gating on >100ms buffered prevents injecting silence when audio is already flowing.

### `df324e9` — Disable Nagle's algorithm on host-side accepted TCP connections
**What:** Set `NoDelay = true` on `TcpClient` in `AudioBroadcaster`.  
**Why:** Nagle's algorithm batches small packets to reduce TCP overhead. Opus packets are small (~100–300 bytes), so Nagle was coalescing 5–10 packets into bursts separated by ~200ms gaps instead of one packet every 20ms. This caused choppy playback.

### `10196ff` — Fix listener periodic choppiness: NoDelay, consumer loop, pcm alloc
**What:** Three fixes in `Musix.Listener/Program.cs`:
1. `NoDelay = true` on the listener socket (mirrors host-side Nagle fix)
2. Replaced `Task.Delay(20)` consumer with buffer-fill-driven loop: fills to 150ms target, then backs off with 10ms sleeps
3. Pre-allocated `pcmByteBuffer`; replaced per-frame `ToArray()` with zero-alloc `CopyTo`

**Why:**
- Listener socket also needed Nagle disabled — without it, ACKs could be delayed, inducing Nagle delays on the host side.
- Fixed-interval `Task.Delay(20)` accumulated ~1ms overhead per frame, slowly draining the playback buffer until underrun every few seconds. Buffer-fill-driven approach eliminates clock drift entirely.
- `ToArray()` on a `Span<byte>` allocated 384 KB/s of heap garbage, pressuring the GC during playback.

### `2ab3808` — Fix build errors
**What:** Minor fix in `Musix.Listener/Program.cs` (1 line).  
**Why:** Build broken by a previous change; this restores compilation.
