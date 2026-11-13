using Kumiko.UI;

namespace Kumiko.Rendering;

internal sealed unsafe partial class OpenGl
{
    uint glassProgram, downProgram, blurProgram, passVertices;
    uint glassA, glassB, glassFramebufferA, glassFramebufferB;
    int glassWidth, glassHeight;

    void InitializeGlass()
    {
        glassProgram = Program(OpenGlShader.Vertex, OpenGlGlassShader.Fragment);
        downProgram = Program(OpenGlGlassShader.PassVertex, OpenGlGlassShader.Down);
        blurProgram = Program(OpenGlGlassShader.PassVertex, OpenGlGlassShader.Blur);
        uint value;
        genVertexArrays(1, &value);
        passVertices = value;
        genTextures(1, &value);
        glassA = value;
        genTextures(1, &value);
        glassB = value;
        genFramebuffers(1, &value);
        glassFramebufferA = value;
        genFramebuffers(1, &value);
        glassFramebufferB = value;
        Texture(glassA);
        Texture(glassB);
    }

    void ReserveGlass(int w, int h)
    {
        if (w <= glassWidth && h <= glassHeight)
            return;
        glassWidth = Math.Max(glassWidth, w);
        glassHeight = Math.Max(glassHeight, h);
        void Allocate(uint texture, uint target)
        {
            bindTexture(0x0de1, texture);
            texImage(0x0de1, 0, 0x8814, glassWidth, glassHeight, 0, 0x1908, 0x1406, null);
            bindFramebuffer(0x8d40, target);
            framebufferTexture(0x8d40, 0x8ce0, 0x0de1, texture, 0);
            if (framebufferStatus(0x8d40) != 0x8cd5)
                throw new InvalidOperationException("OpenGL glass framebuffer is incomplete.");
        }
        Allocate(glassA, glassFramebufferA);
        Allocate(glassB, glassFramebufferB);
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
        disable(0x0be2);
        bindVertexArray(passVertices);
        viewport(0, 0, w, h);
        useProgram(downProgram);
        uniform4f(Uniform("area", downProgram), left, top, right, bottom);
        uniform1i(Uniform("down", downProgram), down);
        uniform1i(Uniform("frameHeight", downProgram), height);
        Pass(downProgram, frameTexture, glassFramebufferA);
        if (sigma > 0)
        {
            useProgram(blurProgram);
            uniform2f(Uniform("size", blurProgram), w, h);
            uniform1i(Uniform("radius", blurProgram), radius);
            uniform1f(Uniform("sigma", blurProgram), sigma);
            uniform2f(Uniform("direction", blurProgram), 1, 0);
            Pass(blurProgram, glassA, glassFramebufferB);
            uniform2f(Uniform("direction", blurProgram), 0, 1);
            Pass(blurProgram, glassB, glassFramebufferA);
        }
        bindFramebuffer(0x8d40, framebuffer);
        viewport(0, 0, width, height);
        enable(0x0be2);
        useProgram(glassProgram);
        // Use the logical viewport, including any fractional last physical pixel.
        uniform2f(Uniform("viewport", glassProgram), renderSize.Width, renderSize.Height);
        uniform2f(Uniform("origin", glassProgram), left, top);
        uniform2f(Uniform("size", glassProgram), w, h);
        uniform1f(Uniform("down", glassProgram), down);
        uniform1f(Uniform("scale", glassProgram), scale);
        uniform1i(Uniform("source", glassProgram), 0);
        bindTexture(0x0de1, glassA);
        bindVertexArray(vertices);
        bindBuffer(0x8892, buffer);
        bufferData(0x8892, sizeof(Instance), &instance, 0x88e0);
        drawInstanced(5, 0, 4, 1);
        useProgram(program);
    }

    void Pass(uint targetProgram, uint source, uint target)
    {
        bindFramebuffer(0x8d40, target);
        bindTexture(0x0de1, source);
        uniform1i(Uniform("source", targetProgram), 0);
        drawInstanced(4, 0, 3, 1);
    }

    void DisposeGlass()
    {
        foreach (uint shader in new[] { glassProgram, downProgram, blurProgram })
            if (shader != 0)
                deleteProgram(shader);
        uint value = passVertices;
        if (value != 0)
            deleteVertexArrays(1, &value);
        value = glassA;
        if (value != 0)
            deleteTextures(1, &value);
        value = glassB;
        if (value != 0)
            deleteTextures(1, &value);
        value = glassFramebufferA;
        if (value != 0)
            deleteFramebuffers(1, &value);
        value = glassFramebufferB;
        if (value != 0)
            deleteFramebuffers(1, &value);
    }
}
