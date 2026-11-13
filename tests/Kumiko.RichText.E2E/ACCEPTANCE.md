# Rich text failure scenarios (before implementation)

- Editing splits a combining sequence, emoji ZWJ or surrogate pair; hit testing and selection disagree.
- Formatting is lost across typing, selection replacement, rich paste, undo or redo.
- A caret at a link/code edge inherits the link into unrelated text.
- Joining/splitting paragraphs loses heading/list/task styles or leaves invalid attachment spans.
- Typing during IME preview enters history; cancellation changes content; commit creates two undo steps.
- Bidi keyboard/caret movement uses logical order where native shaping requires visual order.
- Accessibility selection/value replacement bypasses the same editor commands or reports composition as committed.
- Large documents instantiate every paragraph, or offscreen caret movement cannot reveal its destination.
- Malformed/oversized imports, DTD/entities, scripts, unsafe URLs or attachment bytes execute code or trigger I/O.
- Rich clipboard exchange accesses the system clipboard under Tester or reuses data from another test.
- Model restoration/time travel retains stale composition or dispatches a stale editor callback.
- Two consumer integrations depend on application-specific editor internals.

Tests use Tester input, native text shaping and private transfer data only.
PNG/tree/JSON artifacts prove offscreen rendering; they do not claim real-screen
OS IME, assistive-technology or clipboard execution.

Before attachment/layout correction: inline attachments must reserve their actual
box width/height, wrap at complete graphemes, select/delete as one character and
render a bounded image or named placeholder. Rich spans must wrap using their
attributed advances rather than the plain body font. No import may decode an
unbounded raster during a view build.

Before import/equality correction: encoded rich content including attachment
bytes must compare by value after decoding. Imported formatting boundaries may
not bisect graphemes; null attachment metadata or default byte arrays must fail
as import errors. Before bidi geometry correction: a selected Hebrew grapheme's
painted highlight must overlap the native-shaped text beside the visual caret,
including during marked-text composition, rather than mirror to the left edge.
Opposite-direction IME graphemes inside a bidi run must have separate marks;
ambiguous strong caret offsets must not duplicate a grapheme's selection range.

## Independent review failure scenarios (round 1)

P1: Down to a wrapped-line start must paint there, and a second Down must advance
one visual line. Home/End, Shift selection, horizontal movement and IME preview
must agree on wrap-boundary affinity, which must survive model/history replay.
P2: format changes before typing/IME/pasting combining marks or ZWJ sequences
must never reject valid Unicode. Joining clusters across either edit edge must
choose deterministic whole-grapheme attributes and a valid selection; undo/redo
must restore exact valid values. Malformed imports remain rejected.
P3: repeated Right navigation at 100/1000/3000 characters must retain correct
caret positions without rebuilding paragraph displays per candidate. Record
repeatable warm interaction timings and PNG/tree output before and after fixes;
never use an exact wall-clock threshold as the correctness assertion.
P4: every supported combination exported to Markdown must import its plain text
and bold/italic/strike/code/link attributes, including escaped nested delimiters
and adjacent runs. Unterminated syntax stays literal and imports never fetch.

## Independent review failure scenarios (round 2)

R2-P1: uniform single-style Hebrew must move visually across both soft-wrap
boundaries and paragraph boundaries in either direction. Test End/Left/Left,
Home/Right, reverse traversal, Shift selection and composition on the destination
line; retain LTR and grapheme behavior. Record caret JSON plus PNG/tree artifacts.
R2-P2: own-export Markdown must preserve leading/trailing spaces and tabs, literal
escapes/entities, all supported inline attribute combinations, and default or
supported heading/list/task/quote/indent paragraph styles. Italic leading spaces
must not become a bullet. Keep malformed/untrusted import handling unchanged.

RTL Shift selection must also collapse toward the visual arrow, rather than
using the LTR logical start/end rule. Verify before changing collapse handling.

## Independent review failure scenarios (round 3)

R3-P1: native visual endpoint geometry must determine Left/Right collapse inside
LTR islands of RTL paragraphs AND RTL islands of LTR paragraphs, independent of
selection direction. Keep uniform RTL, cross-line and wrap-affinity behavior.
R3-P2: own-export code paragraphs containing literal triple/longer backticks,
embedded fences, blank content/lines and adjacent plain/code paragraphs must
roundtrip text and supported code style. Shorter fence lines inside a longer
fence remain literal; malformed/unclosed imports stay bounded and inert.
