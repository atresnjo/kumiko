# Kumiko.Icons.Hugeicons

6,067 free Hugeicons in the Stroke Rounded style, bundled offline under MIT.
Targets .NET 10 alongside Kumiko and supports trimming and NativeAOT.

```sh
dotnet add package Kumiko.Icons.Hugeicons
```

```csharp
using Kumiko.Icons;

c.Icon(Hugeicons.Star, 20, c.Theme.Accent, key: "star", accessibleName: "Favorite");
c.Kit.Button("Add company", new(Icon: Hugeicons.Add01), key: "add");
c.Kit.MenuItem("Open company", new(Hugeicons.ArrowUpRight01), key: "open");
```

Referencing a typed field registers the `hugeicons:` source automatically. There
is no runtime download, Node dependency, font installation or platform-symbol
fallback. Every icon uses Kumiko's native SVG backend (AppKit, librsvg or
Direct2D) and its icon atlas, cached by name, size and display scale. Paint-time
tint follows the requested foreground, including theme changes. The SVG style
uses a 1.5-unit stroke on a 24-unit viewBox. Icons retain their supplied direction;
choose the opposite arrow explicitly when adapting reading-direction controls.

For dynamic selection, `Hugeicons.Names` lists sorted kebab-case local names and
`Hugeicons.Get("arrow-up-right-01")` returns an icon identifier. Unknown names
throw `KeyNotFoundException`. In strict Kumiko views, prefer typed fields; resolve
dynamic names before the view and keep the resulting string in immutable state.

## Shipping only the icons an app uses

Trimming and NativeAOT keep every embedded resource, so by default a published
app carries all 6,067 SVGs (about 6.5 MB). An app that draws a known set can list
it by kebab-case local name in its project file:

```xml
<ItemGroup>
  <KumikoHugeicon Include="star;add-01;arrow-up-right-01" />
</ItemGroup>
```

With at least one item:

- The build fails on a name that is not in the catalogue.
- `Names` lists only the selected icons. `Get`, `GetSvg` and drawing any other
  icon throw `KeyNotFoundException` naming `KumikoHugeicon`. This happens in
  every configuration, including `dotnet run`, so a missing entry shows up
  before publishing.
- Trimmed and NativeAOT publishing removes every unselected SVG. The selection
  is passed to the trimmer as feature switches (`Kumiko.Icons.Hugeicons.Subset`
  and `Kumiko.Icons.Hugeicons.Icon.<name>`). These switches drive the
  `ILLink.Substitutions.xml` file embedded in this library, which is not itself
  shipped in the published app.

Items belong to the executable project, because runtime configuration comes from
the entry assembly. A test host that runs the app in process should import the
same item list. Without items nothing changes: the full catalogue is listed and
shipped. The typed `Hugeicons.*` fields remain compiled in either way (about 0.5 MB
of NativeAOT strings and initializer code).

For inline web rendering, `Hugeicons.GetSvg("star")` returns the same SVG with
`currentColor`. Raw files are also in `assets/hugeicons/` in the NuGet archive;
they are not copied into a consumer's output unless explicitly extracted. Retain
the notice in `third_party/licenses/Hugeicons-MIT.txt` when redistributing them.

The upstream source is exclusively `@hugeicons/core-free-icons` **4.3.5**, MIT
licensed by Hugeicons. No Pro assets are included. `upstream.json` records the
source URL, integrity hash, icon count and deterministic SVG digest. Builds use the
checked-in SVGs and C# fields.

Verification: run `tests/Kumiko.E2E` for a full rendered contact sheet and
per-icon coverage JSON, tint/scale screenshots, and Kit semantic activation.
CRMLite provides a complete example with light/dark offscreen behavior flows.
To run only the package checks:

```sh
dotnet run --project tests/Kumiko.E2E -c Release -- artifacts/hugeicons/icons --hugeicons-only
```
