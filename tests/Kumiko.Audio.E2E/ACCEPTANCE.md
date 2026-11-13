# Audio failure scenarios (before implementation)

- Malformed/truncated WAV sizes read outside the RIFF chunk, wrap integers, or allocate unbounded memory.
- Integer/float PCM, stereo frame alignment, clipping and gain produce incorrect output.
- A streaming decoder is read on two workers or disposed before its final read finishes.
- Pause/seek/stop race output; queued old audio plays after a seek.
- A zero-length looping source spins forever; EOF never transitions to ended.
- Cancellation/disposal leaves an output/device open or invokes a released callback.
- A paused player polls or a device failure disappears without a typed event.
- Offscreen tests accidentally open an output device or publish system media metadata.
- Media callbacks escape native code, or retained callbacks act after disposal.

The E2E fixture uses explicit silent decoder/output boundaries and never opens a
native device or system media session. Native adapter classes execute only with
private substituted queue/C calls. Native devices require separate OS execution
evidence. Fixture success is never native playback verification.

Before cancellable decoder correction: disposal while a live decoder awaits
data must cancel and join that read before disposing decoder/output. Legacy
synchronous decoders remain bounded-read adapters; their default asynchronous
entry point must check cancellation before reading.

## Independent review failure scenarios (round 1)

S1: pause, stop and seek must interrupt cancellable held decoder reads AND output
writes without disposing their owners. Queued command cancellation must complete
promptly and never execute later. Pause/resume must retain unsubmitted frames and
not skip source data; seek/stop discard old buffered frames. Cancel an EOF drain
without incorrectly reporting Ended. Exercise overlapping controls and disposal.

S2: buffer reuse is not speaker completion. EOF must request non-immediate stop,
remain incomplete until the running-property notification reports stopped, then
release resources. Cancellation must interrupt that drain; late property/buffer
callbacks must not access released contexts. Use an injected private queue, never
an actual audio device or system media session.

## Independent review failure scenarios (round 2)

R2-S1: volume/loop changes and already-playing Play must finish while a drain is
held, without cancelling, resetting or completing pending output. Natural drain
completion remains owned; transport commands still interrupt read/write/drain.
R2-S2: cancelled blocking ALSA drains returning either success or an error must
raise cancellation before interpreting errors or preparing the device. Pause
must remain usable and never publish EOF/Completion. Exercise the actual managed
Linux output with a substituted private C boundary, not a device. Also guard
player EOF against an injected output returning normally after cancellation.

## Independent review failure scenarios (round 3)

R3-S1: hold actual LinuxAudioOutput's native write using the private C boundary.
Volume/loop/already-playing Play must complete while the write stays held and its
samples remain pinned/unchanged across GC. Pause/seek/stop/disposal must cancel
then JOIN the write before reset, pin/buffer reuse or close. Hold native unwinding
after drop to prove joining. Cover native error, zero and successful return after
cancellation; none may publish a device failure or false EOF. No ALSA/device.
