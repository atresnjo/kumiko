---
name: Kumiko iOS Components
description: A system UIKit control catalog and list/detail example.
spacing:
  page: "16pt"
  body-gap: "12pt"
  header-gap: "8pt"
---

# Design System: Kumiko iOS Components

## Appearance

UIKit owns appearance, feedback, fonts, symbols and semantic colors. The app
adds no custom visual theme. `CatalogApp` is the initial screen; `ComponentsApp`
remains the original list/detail example and is covered by its own E2E flow.

The host uses `UIColor.SystemBackground`. Body text uses the preferred Body
font, headings use the matching title style, and supporting labels use Caption 1
and `UIColor.SecondaryLabel`. Fonts follow Dynamic Type. Buttons, bars, switches,
sliders, steppers, pickers, menus and the page control retain their iOS styling.
The editor uses a secondary system background. The separator uses the system
separator color. Meaningful SF Symbols receive an accessible name.

## Catalog layout

The scene controller places `NativeView` inside the safe-area frame. A system
navigation bar precedes a flexible vertical scroll view; a toolbar follows it.
Content padding is 16pt and the regular vertical gap is 12pt. Selection/input
comes first, with visible labels beside switches/steppers and above wheel/date
pickers and the editor. A separator introduces status and content controls.
Inputs reserve at least 44pt vertically. Long content scrolls rather than
compressing controls. Bar commands and segmented choices use short labels.

## Catalog behavior

- **Inputs:** native edits dispatch messages into the immutable model. Controls
  keep their keys during rebuild/resize. Choice messages use stable domain IDs.
- **Status:** Volume also drives progress. Activity starts/stops UIKit's activity
  indicator. Page selection changes the model's zero-based page index.
- **Menu:** Reset values restores the initial model, Reverse choices reorders
  both choice controls, and Unavailable remains disabled. Reset is destructive.
- **Navigation:** About opens a native sheet with an explicit Close action.
- **Toolbar:** Lock inputs disables editing; Activity toggles native animation;
  Show or hide removes and recreates the inputs from their model values.

## List/detail example

The original example places Add item above a growing inset-grouped UITableView
and the item count below it. Rows use stable domain IDs, automatic height,
selection checkmarks and retained scrolling. Empty content explains recovery.
Selection opens a UISheetPresentationController with native detents, grabber,
dragging and dismissal. Its header pairs title and Close; a flexible spacer
keeps Remove item at the bottom. Removing the selected item closes the sheet.
Underlying controls become inert and hidden from accessibility while it is open.
The detached sheet preview is bounded by host width and height.

## Verification boundary

`check-ios-button.py --catalog-only` produces native-event, replay, PNG and
geometry evidence without showing a window. Captures temporarily attach views
to a private hidden window to initialize UIKit layout, then render only their
layers. Layer rendering does not composite all iOS glass and wheel transforms;
these PNGs are layout evidence, not complete on-device appearance approval.
Physical gestures, keyboard presentation, VoiceOver and composited effects still
need device-level verification.
