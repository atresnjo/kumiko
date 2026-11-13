# Unwind failure scenarios, before implementation

The reference is Raksha T's public "inverted arc carousel and a scroll morph that
turns into top menu" video (x.com/rakshaa_t/status/2114705398519529714). Frames
were studied locally; no media or artwork is copied into this repository.

- The fifth gallery card intercepts earlier cards, the study fails to open on
  Breathwork (the third of five routines), or returning loses the selection.
- Side cards stand upright or sag below the selected card. They must hang on an
  inverted arc: rising toward the edges and tilting away from the centre in
  proportion to their distance, the left card clockwise and the right one
  counter-clockwise.
- A vertical drag on a card starts a carousel swipe instead of scrolling the
  page. A horizontal drag scrolls the page or fails to follow the finger.
- Release ignores velocity, a cancelled drag commits, dragging overshoots the
  first or last routine without resistance, or a drag starting during the settle
  jumps to the old target instead of the presented position.
- Scrolling does not morph continuously: cards must shrink into orbs, straighten,
  drop their copy and pagination, and pin to the top with Sleep/Breathe/Outside
  labels at the collapse distance. Only the selected orb keeps its colour and
  ring. Reversing the scroll restores the carousel exactly.
- Releasing the page between the two states leaves a half-collapsed header; it
  must settle to the nearer state, biased by fling direction.
- The pinned header captures touches outside its cards or orbs, so the week,
  wins and notebook controls under its frame stop responding or scrolling.
- Tapping a side orb or side card does not select it; tapping the selected
  card's play button does not disclose demo scope.
- Unconnected controls (notifications, profile, play, write, voice, see all,
  saved and insights tabs) imply real services instead of opening the study's
  native explanation drawer.
- Narrow and tablet sizes overflow the cards, collide with the greeting, or
  unbound the card width. Detached hosts leave springs or display links running.
- Native callbacks escape exceptions, the scroll delegate outlives the hero, or
  recorded sessions fail to replay to identical pixels.

Verify through the detached simulator flow with controlled native recognizer
input, programmatic scroll offsets, native hit testing, phone/narrow/tablet
view-layer PNGs, session replay and `unwind-report.json`. Recorded simulator
motion is visual evidence only; it does not certify physical-device feel.
