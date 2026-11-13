# Slack catch-up behavior to verify

- The gallery exposes a Slack card and preserves all three child studies.
- Five channel cards start at channel-5, with one readable/accessible active card.
- Vertical scrolling inside a card wins over a mostly vertical pan.
- A horizontal pan tracks both axes and reverses tilt when grabbed below center.
- A short or cancelled pan springs back without changing channel status/count.
- A quarter-screen crossing updates the progress ring and directional tint;
  returning below threshold rearms threshold feedback.
- Right commits Read, left commits Keep Unread, consuming exactly one card.
- Release carries vertical velocity while the card clears the viewport.
- Footer actions use the same deck path and reject duplicate/stale commits.
- Undo restores the actual dismissed card, clears its disposition and reverses
  the counter direction, including after all five cards are completed.
- Remaining cards advance/scale as the active card leaves.
- A new finger can interrupt a return spring without a position jump.
- Resize cancels obsolete motion and retains logical selection.
- Hidden/Reduce Motion hosts settle immediately, with no permanent frame timer.
- Completion, Done, Back, replay and re-entry remain usable.
- Phone/narrow/tablet view-layer PNGs and session/JSON evidence are repeatable.

Controlled recognizer input verifies native callback/compositor behavior;
physical touch feel, tactile haptics and VoiceOver traversal need hands-on checks.
