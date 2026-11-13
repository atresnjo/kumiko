CapWords port failure scenarios, recorded before implementation:
- A card selects the wrong word, dispatches twice, or Back fails to restore the grid.
- Image/date/count disappear or teleport rather than using native spring animations.
- Reversing a transition jumps to its previous destination; resizing leaves obsolete animation geometry.
- Duplicate shared tags silently alias views; an incompatible native kind is reused.
- Detail entrances restart on unrelated renders; delayed content remains invisible.
- Reduce Motion still moves content; detached/hidden hosts schedule native animations.
- Disabled, removed or disposed image buttons dispatch stale actions.
- Idle hosts render continuously or retained animation callbacks survive disposal.
- Phone/narrow/tablet layout clips content; session replay changes final pixels.
Artifacts: list/detail/back PNGs, native transition configuration JSON, session JSONL, replay PNG, phone/narrow/tablet captures.

Review failure scenarios, before the correction:
- Short presets truncate a native spring before settling, producing an endpoint snap.
- Native composed buttons provide no visible pressed state or navigation haptics.

Gallery and Alma failure scenarios, recorded before implementation:
- Either gallery card opens the wrong study, or a study has no path back to the gallery.
- Switching studies resets their state, loses CapWords artwork, or lets a hidden Alma timer keep advancing.
- Alma only fades between static screenshots rather than following an actual circular path with rotation/scale.
- Wrapping the last nutrient to the first jumps, grows the retained view count, or leaves a blank card.
- Pause/step controls race the timer, restart on every render, or dispatch stale messages after leaving.
- Resize or Reduce Motion leaves cards off-path or retains obsolete compositor animations.
- Phone/narrow/tablet cards clip titles or intercept taps intended for their parent gallery card.
- Composed child subscriptions fail to wake the parent or session replay changes the selected example.
- Native font designs measure differently from their rendered labels.
Artifacts: gallery/study PNG+TXT, switching session JSONL, replay PNG, Alma compositor intermediates and JSON report.

Model-owned gallery navigation failure scenarios, recorded before the migration:
- Root rebuilds `NavigationHistory` from the selected screen on every render, so
  every destination shares one visit identity and a history request throws.
- Opening a study from the gallery does not leave a Back entry to the gallery;
  a study's own back arrow pushes the gallery instead of returning to its visit.
- Returning to the gallery and opening another study leaves an abandoned forward
  entry, or reopening the same study reuses its abandoned visit.
- Stale, foreign-host or nonadjacent history requests change the selected study.
- A study's standalone root builds its one-route history per render instead of
  keeping it in that study's model.
- Replay of a gallery session produces different visits, a different screen or
  different pixels. Subscriptions follow a screen other than the current visit.
