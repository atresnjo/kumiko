using Kumiko;
using TraceWaterfall;

var initial = TraceApp.Init with { Dark = args.Contains("--dark", StringComparer.Ordinal) };
if (args.Contains("--complete", StringComparer.Ordinal))
    initial = initial with
    {
        Now = 92000,
        Playing = false
    };
if (args.Contains("--paused", StringComparer.Ordinal))
    initial = initial with
    {
        Playing = false
    };
using var program = TraceApp.CreateProgram(initial);
Dev.Run("Trace Waterfall · Kumiko", 1360, 1040, program, args,
    new("Trace Waterfall", 1360, 1040, 840, 600, Theme: new Palette(initial.Dark).Kit.Theme));
