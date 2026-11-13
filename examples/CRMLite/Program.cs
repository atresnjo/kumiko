using System.Globalization;
using CRMLite;
using Kumiko;

// The demo copy is English; --kumiko-culture still overrides number and date formats.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

var dark = args.Contains("--dark", StringComparer.Ordinal);
// One backend instance serves both service contracts. Views only receive immutable values.
var backend = new FakeCrmBackend();
var initial = args.Contains("--demo", StringComparer.Ordinal) ? Data.Initial(dark) : CrmApp.SignedOut(dark);
var route = RouteInput.Parse(Dev.Option(args, "--route"), Dev.Option(args, "--page"), Dev.Option(args, "--company"));
CrmDesktopHost.Run("CRMLite", initial, backend, route, args, new("CRMLite", 1560, 980, 960, 640, Theme: new Palette(dark).Kit.Theme));
