using static Kumiko.Rendering.D3D11Native;

namespace Kumiko.Rendering;

// Glass on the GPU, after OpenGl.Glass.cs: downsample the frame so far into float textures, blur
// them separably, then shade the pane with the quad vertex shader. Nothing returns to the CPU.
internal sealed unsafe partial class D3D11
{
    nint passShader, downShader, blurShader, glassShader;
    nint glassA, glassB, glassTargetA, glassTargetB, glassViewA, glassViewB;
    int glassWidth, glassHeight;

    void InitializeGlass(string source)
    {
        passShader = CreateVertexShader(Compile(source, "passVertex", "vs_4_0"));
        downShader = CreatePixelShader(Compile(source, "downMain", "ps_4_0"));
        blurShader = CreatePixelShader(Compile(source, "blurMain", "ps_4_0"));
        glassShader = CreatePixelShader(Compile(source, "glassMain", "ps_4_0"));
    }

    void ReserveGlass(int w, int h)
    {
        if (w <= glassWidth && h <= glassHeight)
            return;
        ReleaseGlassTextures();
        glassWidth = Math.Max(glassWidth, w);
        glassHeight = Math.Max(glassHeight, h);
        glassA = CreateTexture(glassWidth, glassHeight, Float4, TargetBinding | ShaderBinding, null);
        glassB = CreateTexture(glassWidth, glassHeight, Float4, TargetBinding | ShaderBinding, null);
        glassTargetA = CreateView(DeviceSlot.CreateRenderTargetView, glassA);
        glassTargetB = CreateView(DeviceSlot.CreateRenderTargetView, glassB);
        glassViewA = CreateView(DeviceSlot.CreateShaderResourceView, glassA);
        glassViewB = CreateView(DeviceSlot.CreateShaderResourceView, glassB);
    }

    void DrawGlass(Instance instance, float scale)
    {
        var bounds = CpuRenderer.Bounds(width, height, instance, scale);
        if (bounds.Area == 0)
            return;
        // Match the CPU filter's clipped backdrop, downsampling and kernel exactly.
        float blur = instance.BorderWidth * scale;
        int down = 1;
        while (down < 8 && blur / (down * 2) >= 2)
            down *= 2;
        float sigma = MathF.Sqrt(Math.Max(0, blur * blur - down * down / 12f)) / down;
        int radius = (int)MathF.Ceiling(sigma * 3), reach = (radius + 1) * down;
        int left = Math.Max(0, bounds.X0 - reach), top = Math.Max(0, bounds.Y0 - reach);
        int right = Math.Min(width, bounds.X1 + reach), bottom = Math.Min(height, bounds.Y1 + reach);
        int w = (right - left + down - 1) / down, h = (bottom - top + down - 1) / down;
        ReserveGlass(w, h);
        SetBlend(0);
        SetLayout(0, TriangleList);
        SetViewport(w, h);
        constants.Area = new(left, top, right, bottom);
        constants.SampleDown = down;
        constants.Size = new(w, h);
        constants.Radius = radius;
        constants.Sigma = sigma;
        SetShaders(passShader, downShader);
        Pass(glassTargetA, frameView);
        if (sigma > 0)
        {
            SetShaders(passShader, blurShader);
            constants.Direction = new(1, 0);
            Pass(glassTargetB, glassViewA);
            constants.Direction = new(0, 1);
            Pass(glassTargetA, glassViewB);
        }
        Target(frameTarget, glassViewA);
        SetViewport(width, height);
        SetBlend(blend);
        SetLayout(layout, TriangleStrip);
        SetShaders(vertexShader, glassShader);
        constants.Origin = new(left, top);
        constants.Down = down;
        constants.Scale = scale;
        UpdateConstants();
        Upload(new ReadOnlySpan<Instance>(in instance));
        DrawInstanced(1);
        SetShaders(vertexShader, pixelShader);
    }

    // One full-viewport triangle into target, reading view.
    void Pass(nint target, nint view)
    {
        UpdateConstants();
        Target(target, view);
        Draw(3);
    }

    void ReleaseGlassTextures()
    {
        ReleaseObject(ref glassViewA);
        ReleaseObject(ref glassViewB);
        ReleaseObject(ref glassTargetA);
        ReleaseObject(ref glassTargetB);
        ReleaseObject(ref glassA);
        ReleaseObject(ref glassB);
    }

    void DisposeGlass()
    {
        ReleaseGlassTextures();
        ReleaseObject(ref glassShader);
        ReleaseObject(ref blurShader);
        ReleaseObject(ref downShader);
        ReleaseObject(ref passShader);
    }
}
