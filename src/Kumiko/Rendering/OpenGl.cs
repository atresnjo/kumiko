using System.Runtime.InteropServices;
using System.Text;
using Kumiko.UI;

namespace Kumiko.Rendering;
/// <summary>Instanced GPU quads and GPU backdrop/optical passes for glass.</summary>
internal sealed unsafe partial class OpenGl : IDisposable
{
    uint program, vertices, buffer, atlasTexture, frameTexture, framebuffer;
    int width, height, atlasSize, atlasVersion = -1;
    Atlas? currentAtlas;
    Size renderSize;
    bool disposed;
    public string Driver { get; private set; } = "";
    internal uint FrameTexture => frameTexture;
    internal int Width => width;
    internal int Height => height;
    internal int ReadbackCount
    {
        get; private set;
    }
    /// <summary>Whole-atlas and changed-row texture uploads so far, for verification.</summary>
    internal int FullAtlasUploads
    {
        get; private set;
    }
    internal int AtlasRowUploads
    {
        get; private set;
    }

    void Initialize()
    {
        Driver = Marshal.PtrToStringUTF8((nint)getString(0x1f01)) ?? "OpenGL";
        program = Program(OpenGlShader.Vertex, OpenGlShader.Fragment);
        InitializeGlass();
        uint value;
        genVertexArrays(1, &value);
        vertices = value;
        genBuffers(1, &value);
        buffer = value;
        genTextures(1, &value);
        atlasTexture = value;
        genTextures(1, &value);
        frameTexture = value;
        genFramebuffers(1, &value);
        framebuffer = value;
        bindVertexArray(vertices);
        bindBuffer(0x8892, buffer);
        for (uint i = 0; i < 7; i++)
        {
            enableAttribute(i);
            attributePointer(i, 4, 0x1406, 0, sizeof(Instance), (void*)(i * 16));
            attributeDivisor(i, 1);
        }

        Texture(atlasTexture);
        Texture(frameTexture);
    }

    uint Program(string vertexSource, string fragmentSource)
    {
        uint vertex = Compile(0x8b31, vertexSource), fragment = 0, result = 0;
        try
        {
            fragment = Compile(0x8b30, fragmentSource);
            result = createProgram();
            attachShader(result, vertex);
            attachShader(result, fragment);
            linkProgram(result);
            int linked = 0;
            getProgram(result, 0x8b82, &linked);
            if (linked == 0)
                throw new InvalidOperationException("OpenGL link: " + Log(result, false));
            return result;
        }
        catch
        {
            if (result != 0)
                deleteProgram(result);
            throw;
        }
        finally
        {
            deleteShader(vertex);
            if (fragment != 0)
                deleteShader(fragment);
        }
    }

    void Texture(uint texture)
    {
        bindTexture(0x0de1, texture);
        texParameter(0x0de1, 0x2801, 0x2601);
        texParameter(0x0de1, 0x2800, 0x2601);
        texParameter(0x0de1, 0x2802, 0x812f);
        texParameter(0x0de1, 0x2803, 0x812f);
    }

    uint Compile(uint kind, string source)
    {
        uint shader = createShader(kind);
        var bytes = Encoding.UTF8.GetBytes(source + "\0");
        fixed (byte* ptr = bytes)
        {
            byte* text = ptr;
            shaderSource(shader, 1, &text, null);
        }

        compileShader(shader);
        int compiled = 0;
        getShader(shader, 0x8b81, &compiled);
        if (compiled == 0)
        {
            string error = Log(shader, true);
            deleteShader(shader);
            throw new InvalidOperationException("OpenGL shader: " + error);
        }

        return shader;
    }

    string Log(uint shader, bool isShader)
    {
        var bytes = new byte[8192];
        fixed (byte* ptr = bytes)
        {
            int length = 0;
            if (isShader)
                shaderLog(shader, bytes.Length, &length, ptr);
            else
                programLog(shader, bytes.Length, &length, ptr);
            return Encoding.UTF8.GetString(bytes, 0, Math.Clamp(length, 0, bytes.Length));
        }
    }

    int Uniform(string name, uint target = 0)
    {
        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* ptr = bytes)
            return uniformLocation(target == 0 ? program : target, ptr);
    }

    public void Render(Context context, bool present)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        int previous = 0;
        getInteger(0x8ca6, &previous);
        try
        {
            activeTexture(0x84c0);
            int w = Math.Max(1, (int)MathF.Ceiling(context.Size.Width * context.Scale));
            int h = Math.Max(1, (int)MathF.Ceiling(context.Size.Height * context.Scale));
            bindFramebuffer(0x8d40, framebuffer);
            if (w != width || h != height)
            {
                width = w;
                height = h;
                bindTexture(0x0de1, frameTexture);
                texImage(0x0de1, 0, 0x8058, width, height, 0, 0x80e1, 0x1401, null);
                framebufferTexture(0x8d40, 0x8ce0, 0x0de1, frameTexture, 0);
                if (framebufferStatus(0x8d40) != 0x8cd5)
                    throw new InvalidOperationException("OpenGL framebuffer is incomplete.");
            }

            viewport(0, 0, width, height);
            renderSize = new(width / context.Scale, height / context.Scale);
            disable(0x0c11);
            disable(0x0b71);
            disable(0x0bd0);
            disable(0x8db9);
            enable(0x0be2);
            blendFunc(1, 0x0303);
            var background = context.Theme.Background;
            clearColor(background.R * background.A, background.G * background.A, background.B * background.A, background.A);
            clear(0x4000);
            useProgram(program);
            uniform2f(Uniform("viewport"), renderSize.Width, renderSize.Height);
            uniform1i(Uniform("atlas"), 0);
            bindVertexArray(vertices);
            bindTexture(0x0de1, atlasTexture);
            SyncAtlas(context.TextSystem.Atlas);

            var instances = context.Instances.ToArray();
            int from = 0;
            for (int i = 0; i < instances.Length; i++)
            {
                if (instances[i].Kind != 4)
                    continue;
                Batch(instances.AsSpan(from, i - from));
                DrawGlass(instances[i], context.Scale);
                from = i + 1;
            }

            Batch(instances.AsSpan(from));
            if (present)
            {
                bindFramebuffer(0x8ca8, framebuffer);
                bindFramebuffer(0x8ca9, (uint)previous);
                blit(0, 0, width, height, 0, 0, width, height, 0x4000, 0x2600);
            }
        }
        finally
        {
            bindFramebuffer(0x8d40, (uint)previous);
        }
    }

    /// <summary>
    /// Uploads the atlas to the bound texture as MetalRenderer does: everything when the atlas object changed, grew or
    /// was rebuilt since our copy, otherwise only the rows written since, so text scrolling into view stays cheap.
    /// </summary>
    void SyncAtlas(Atlas atlas)
    {
        if (atlas == currentAtlas && atlas.Version == atlasVersion)
            return;
        if (atlas != currentAtlas || atlas.Width != atlasSize || atlasVersion < atlas.Rebuilt)
        {
            fixed (byte* bytes = atlas.Bytes)
                texImage(0x0de1, 0, 0x8058, atlas.Width, atlas.Height, 0, 0x80e1, 0x1401, bytes);
            currentAtlas = atlas;
            atlasSize = atlas.Width;
            atlasVersion = atlas.Version;
            FullAtlasUploads++;
            return;
        }
        var (top, bottom) = atlas.Changed(atlasVersion);
        atlasVersion = atlas.Version;
        if (bottom <= top)
            return;
        // BGRA rows are 4-byte aligned, so the default GL_UNPACK_ALIGNMENT of 4 reads them tightly packed.
        int stride = atlas.Width * 4;
        fixed (byte* bytes = atlas.Bytes)
            texSubImage(0x0de1, 0, 0, top, atlas.Width, bottom - top, 0x80e1, 0x1401, bytes + top * stride);
        AtlasRowUploads++;
    }

    void Batch(ReadOnlySpan<Instance> instances)
    {
        if (instances.Length == 0)
            return;
        bindTexture(0x0de1, atlasTexture);
        bindBuffer(0x8892, buffer);
        fixed (Instance* data = instances)
            bufferData(0x8892, instances.Length * sizeof(Instance), data, 0x88e0);
        drawInstanced(5, 0, 4, instances.Length);
    }

    public Pixels Readback()
    {
        ReadbackCount++;
        int previous = 0;
        getInteger(0x8ca6, &previous);
        bindFramebuffer(0x8d40, framebuffer);
        var pixels = Pixels.Create(width, height);
        fixed (byte* bytes = pixels.Bytes)
            readPixels(0, 0, width, height, 0x80e1, 0x1401, bytes);
        bindFramebuffer(0x8d40, (uint)previous);
        Flip(pixels.Bytes, width, height);
        return pixels;
    }

    static void Flip(byte[] bytes, int width, int height)
    {
        var row = new byte[width * 4];
        for (int y = 0; y < height / 2; y++)
        {
            var top = bytes.AsSpan(y * row.Length, row.Length);
            var bottom = bytes.AsSpan((height - 1 - y) * row.Length, row.Length);
            top.CopyTo(row);
            bottom.CopyTo(top);
            row.CopyTo(bottom);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        DisposeGlass();
        uint value = vertices;
        if (value != 0)
            deleteVertexArrays(1, &value);
        value = buffer;
        if (value != 0)
            deleteBuffers(1, &value);
        value = atlasTexture;
        if (value != 0)
            deleteTextures(1, &value);
        value = frameTexture;
        if (value != 0)
            deleteTextures(1, &value);
        value = framebuffer;
        if (value != 0)
            deleteFramebuffers(1, &value);
        if (program != 0)
            deleteProgram(program);
    }
}
