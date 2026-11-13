using System.Numerics;
using System.Runtime.InteropServices;
using Kumiko.Platform;
using Kumiko.UI;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Rendering;

public sealed unsafe class MetalRenderer : IDisposable
{
    const string Metal = "/System/Library/Frameworks/Metal.framework/Metal";
    [DllImport(Metal)]
    static extern nint MTLCreateSystemDefaultDevice();
    [DllImport("/usr/lib/system/libdispatch.dylib")]
    static extern nint dispatch_data_create(nint buffer, nuint size, nint queue, nint destructor);
    [DllImport("/usr/lib/system/libdispatch.dylib")]
    static extern void dispatch_release(nint data);
    readonly nint device, queue, pipeline, glassPipeline, downPipeline, blurPipeline;
    readonly nint[] buffers = new nint[3], commands = new nint[3];
    readonly int[] capacities = new int[3];
    nint atlasTexture, glassA, glassB;
    int atlasSize, atlasVersion = -1, nextSlot, glassWidth, glassHeight;
    /// <summary>The atlas the texture holds; renderers can draw several contexts.</summary>
    Atlas? syncedAtlas;
    bool disposed;
    // What the layer shows: the slot whose instance buffer still holds its paint list, and the other frame inputs.
    (int Slot, int Count, Atlas? Atlas, int AtlasVersion, int AtlasWidth, Size Size, float Scale, Color Clear) shown = (-1, 0, null, 0, 0, default, 0, default);
    /// <summary>Bytes the per-frame instance buffers keep reserved between frames.</summary>
    internal long RetainedBufferBytes => Enumerable.Range(0, buffers.Length).Where(i => buffers[i] != 0).Sum(i => (long)capacities[i]);
    /// <summary>Frames encoded for the layer; frames identical to the one on screen are not counted.</summary>
    internal int PresentedFrames
    {
        get; private set;
    }
    internal nint Layer
    {
        get;
    }

    public MetalRenderer()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Metal requires macOS.");
        NativeLibrary.Load("/System/Library/Frameworks/QuartzCore.framework/QuartzCore");
        using var pool = new AutoreleasePool();
        device = MTLCreateSystemDefaultDevice();
        if (device == 0)
            throw new InvalidOperationException("No Metal device available.");
        queue = O.O(device, "newCommandQueue");
        nint error = 0;
        nint library;
#if KUMIKO_PRECOMPILED_METAL
        using(var stream=typeof(MetalRenderer).Assembly.GetManifestResourceStream("Kumiko.Shaders.metallib")??throw new InvalidOperationException("Embedded Metal library missing."))
        {
            var bytes=new byte[stream.Length];stream.ReadExactly(bytes);
            fixed(byte* p=bytes)
            {
                var data=dispatch_data_create((nint)p,(nuint)bytes.Length,0,0);
                try { library=O.O(device,"newLibraryWithData:error:",data,(nint)(&error)); }
                finally { dispatch_release(data); }
            }
        }
#else
        library = O.O(device, "newLibraryWithSource:options:error:", O.TempString(MetalShader.Source), 0, (nint)(&error));
#endif
        if (library == 0)
            throw new InvalidOperationException("Metal shader: " + O.Text(O.O(error, "localizedDescription")));
        try
        {
            pipeline = MakePipeline(library, "fs");
            glassPipeline = MakePipeline(library, "fs_glass");
            downPipeline = MakeKernel(library, "glass_down");
            blurPipeline = MakeKernel(library, "glass_blur");
        }
        finally
        {
            O.Release(library);
        }

        Layer = O.New("CAMetalLayer");
        O.V(Layer, "setDevice:", device);
        O.V(Layer, "setPixelFormat:", (nuint)80);
        O.V(Layer, "setFramebufferOnly:", (byte)0);
        O.V(Layer, "setOpaque:", (byte)1);
        O.V(Layer, "setMaximumDrawableCount:", (nuint)3);
    }

    nint MakePipeline(nint library, string fragment)
    {
        var vertex = O.O(library, "newFunctionWithName:", O.TempString("vs"));
        var frag = O.O(library, "newFunctionWithName:", O.TempString(fragment));
        var desc = O.New("MTLRenderPipelineDescriptor");
        try
        {
            O.V(desc, "setVertexFunction:", vertex);
            O.V(desc, "setFragmentFunction:", frag);
            var attachment = O.O(O.O(desc, "colorAttachments"), "objectAtIndexedSubscript:", (nuint)0);
            O.V(attachment, "setPixelFormat:", (nuint)80);
            O.V(attachment, "setBlendingEnabled:", (byte)1);
            O.V(attachment, "setSourceRGBBlendFactor:", (nuint)1);
            O.V(attachment, "setSourceAlphaBlendFactor:", (nuint)1);
            O.V(attachment, "setDestinationRGBBlendFactor:", (nuint)5);
            O.V(attachment, "setDestinationAlphaBlendFactor:", (nuint)5);
            nint error = 0;
            var result = O.O(device, "newRenderPipelineStateWithDescriptor:error:", desc, (nint)(&error));
            return result != 0 ? result : throw new InvalidOperationException("Metal pipeline: " + O.Text(O.O(error, "localizedDescription")));
        }
        finally
        {
            O.Release(vertex);
            O.Release(frag);
            O.Release(desc);
        }
    }

    nint MakeKernel(nint library, string name)
    {
        var fn = O.O(library, "newFunctionWithName:", O.TempString(name));
        nint error = 0;
        try
        {
            var result = O.O(device, "newComputePipelineStateWithFunction:error:", fn, (nint)(&error));
            return result != 0 ? result : throw new InvalidOperationException(name);
        }
        finally
        {
            O.Release(fn);
        }
    }

    nint Texture(int width, int height, nuint format = 80, nuint usage = 5, nuint storage = 0)
    {
        var desc = O.O(O.Class("MTLTextureDescriptor"), "texture2DDescriptorWithPixelFormat:width:height:mipmapped:", format, (nuint)width, (nuint)height, (byte)0);
        O.V(desc, "setUsage:", usage);
        O.V(desc, "setStorageMode:", storage);
        var result = O.O(device, "newTextureWithDescriptor:", desc);
        return result != 0 ? result : throw new InvalidOperationException("Metal texture allocation failed.");
    }

    void SyncAtlas(Atlas atlas, nint command)
    {
        if (atlasTexture == 0 || atlasSize != atlas.Width)
        {
            WaitAll();
            O.Release(atlasTexture);
            atlasTexture = Texture(atlas.Width, atlas.Width, 80, 1);
            atlasSize = atlas.Width;
            atlasVersion = -1;
        }

        if (atlas == syncedAtlas && atlas.Version == atlasVersion)
            return;
        if (atlas != syncedAtlas || atlasVersion < atlas.Rebuilt)
        {
            syncedAtlas = atlas;
            WaitAll();
            fixed (byte* p = atlas.Bytes)
                O.V(atlasTexture, "replaceRegion:mipmapLevel:withBytes:bytesPerRow:", new Region(0, 0, 0, (nuint)atlas.Width, (nuint)atlas.Width, 1), (nuint)0, (nint)p, (nuint)(atlas.Width * 4));
            atlasVersion = atlas.Version;
            return;
        }
        // New glyphs land in rows no earlier frame drew from. Copying just those rows from a fresh buffer on the
        // GPU timeline keeps frames in flight and avoids re-sending the whole atlas while text scrolls into view.
        var (top, bottom) = atlas.Changed(atlasVersion);
        atlasVersion = atlas.Version;
        if (bottom <= top)
            return;
        int stride = atlas.Width * 4;
        nint staging;
        fixed (byte* p = atlas.Bytes)
            staging = O.O(device, "newBufferWithBytes:length:options:", (nint)(p + top * stride), (nuint)((bottom - top) * stride), (nuint)0);
        var blit = O.O(command, "blitCommandEncoder");
        O.CopyToTexture(blit, staging, (nuint)stride, new Region(0, 0, 0, (nuint)atlas.Width, (nuint)(bottom - top), 1), atlasTexture, (nuint)top);
        O.V(blit, "endEncoding");
        O.Release(staging);
    }

    nint Upload(IReadOnlyList<Instance> instances, int slot)
    {
        if (commands[slot] != 0)
        {
            O.V(commands[slot], "waitUntilCompleted");
            O.Release(commands[slot]);
            commands[slot] = 0;
        }

        // The slot's previous command has completed, so its buffer can be replaced by one sized for this frame.
        var capacity = Instance.Retained(capacities[slot] / sizeof(Instance), instances.Count);
        if (capacity * sizeof(Instance) != capacities[slot])
        {
            O.Release(buffers[slot]);
            buffers[slot] = 0;
            capacities[slot] = checked(capacity * sizeof(Instance));
        }

        if (instances.Count == 0)
            return 0;
        if (buffers[slot] == 0)
            buffers[slot] = O.O(device, "newBufferWithLength:options:", (nuint)capacities[slot], (nuint)0);
        var target = new Span<Instance>((void*)O.O(buffers[slot], "contents"), instances.Count);
        if (instances is List<Instance> list)
            CollectionsMarshal.AsSpan(list).CopyTo(target);
        else
            for (int i = 0; i < instances.Count; i++)
                target[i] = instances[i];
        return buffers[slot];
    }

    nint Pass(nint command, nint texture, Color? clear)
    {
        var desc = O.O(O.Class("MTLRenderPassDescriptor"), "renderPassDescriptor");
        var attachment = O.O(O.O(desc, "colorAttachments"), "objectAtIndexedSubscript:", (nuint)0);
        O.V(attachment, "setTexture:", texture);
        O.V(attachment, "setLoadAction:", (nuint)(clear.HasValue ? 2 : 1));
        O.V(attachment, "setStoreAction:", (nuint)1);
        if (clear is { } c)
            O.V(attachment, "setClearColor:", new ClearColor(c.R * c.A, c.G * c.A, c.B * c.A, c.A));
        return O.O(command, "renderCommandEncoderWithDescriptor:", desc);
    }

    void DrawRange(nint encoder, nint state, nint buffer, Size size, int first, int count)
    {
        var viewport = new Vector2(size.Width, size.Height);
        O.V(encoder, "setRenderPipelineState:", state);
        O.V(encoder, "setVertexBuffer:offset:atIndex:", buffer, (nuint)0, (nuint)0);
        O.V(encoder, "setVertexBytes:length:atIndex:", (nint)(&viewport), (nuint)8, (nuint)1);
        O.V(encoder, "setFragmentBuffer:offset:atIndex:", buffer, (nuint)0, (nuint)0);
        O.V(encoder, "setFragmentTexture:atIndex:", atlasTexture, (nuint)0);
        O.V(encoder, "setFragmentTexture:atIndex:", atlasTexture, (nuint)1);
        O.V(encoder, "drawPrimitives:vertexStart:vertexCount:instanceCount:baseInstance:", (nuint)4, (nuint)0, (nuint)4, (nuint)count, (nuint)first);
    }

    nint Encode(nint texture, IReadOnlyList<Instance> instances, Atlas atlas, Size size, float scale, Color clear, int slot)
    {
        var buffer = Upload(instances, slot);
        var command = O.O(queue, "commandBuffer");
        SyncAtlas(atlas, command);
        int first = 0;
        bool cleared = false;
        while (true)
        {
            int end = first;
            while (end < instances.Count && instances[end].Kind != 4)
                end++;
            var encoder = Pass(command, texture, cleared ? null : clear);
            cleared = true;
            if (end > first)
                DrawRange(encoder, pipeline, buffer, size, first, end - first);
            O.V(encoder, "endEncoding");
            if (end == instances.Count)
                break;
            var pane = Backdrop(command, texture, instances[end], scale);
            encoder = Pass(command, texture, null);
            O.V(encoder, "setFragmentBytes:length:atIndex:", (nint)(&pane), (nuint)sizeof(Pane), (nuint)1);
            O.V(encoder, "setFragmentTexture:atIndex:", glassA, (nuint)2);
            DrawRange(encoder, glassPipeline, buffer, size, end, 1);
            O.V(encoder, "endEncoding");
            first = end + 1;
        }

        return command;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BackdropArgs
    {
        public int X0, Y0, X1, Y1, Width, Height, Down, Radius;
        public float Sigma;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Pane
    {
        public Vector2 Origin, Size;
        public float Down, Scale;
    }

    Pane Backdrop(nint command, nint texture, Instance i, float scale)
    {
        int fw = (int)O.N(texture, "width"), fh = (int)O.N(texture, "height"), down = 1;
        float blur = i.BorderWidth * scale;
        while (down < 8 && blur / (down * 2) >= 2)
            down *= 2;
        float sigma = MathF.Sqrt(Math.Max(0, blur * blur - down * down / 12f)) / down;
        int radius = (int)MathF.Ceiling(sigma * 3), reach = (radius + 1) * down;
        int x0 = Math.Clamp((int)MathF.Floor(i.Rect.X * scale - reach), 0, fw - 1), y0 = Math.Clamp((int)MathF.Floor(i.Rect.Y * scale - reach), 0, fh - 1);
        int x1 = Math.Clamp((int)MathF.Ceiling((i.Rect.X + i.Rect.Z) * scale + reach), x0 + 1, fw), y1 = Math.Clamp((int)MathF.Ceiling((i.Rect.Y + i.Rect.W) * scale + reach), y0 + 1, fh);
        int w = (x1 - x0 + down - 1) / down, h = (y1 - y0 + down - 1) / down;
        if (glassA == 0 || w > glassWidth || h > glassHeight)
        {
            glassWidth = Math.Max(w, glassWidth);
            glassHeight = Math.Max(h, glassHeight);
            O.Release(glassA);
            O.Release(glassB);
            glassA = Texture(glassWidth, glassHeight, 70, 3, 2);
            glassB = Texture(glassWidth, glassHeight, 70, 3, 2);
        }

        var a = new BackdropArgs
        {
            X0 = x0,
            Y0 = y0,
            X1 = x1,
            Y1 = y1,
            Width = w,
            Height = h,
            Down = down,
            Radius = radius,
            Sigma = sigma
        };
        var enc = O.O(command, "computeCommandEncoder");
        void Compute(nint state, nint src, nint dst, int dx, int dy)
        {
            O.V(enc, "setComputePipelineState:", state);
            O.V(enc, "setTexture:atIndex:", src, (nuint)0);
            O.V(enc, "setTexture:atIndex:", dst, (nuint)1);
            var copy = a;
            O.V(enc, "setBytes:length:atIndex:", (nint)(&copy), (nuint)sizeof(BackdropArgs), (nuint)0);
            int* direction = stackalloc int[2]
            {
                dx,
                dy
            };
            O.V(enc, "setBytes:length:atIndex:", (nint)direction, (nuint)8, (nuint)1);
            O.V(enc, "dispatchThreadgroups:threadsPerThreadgroup:", new ThreadSize((nuint)((w + 15) / 16), (nuint)((h + 15) / 16), 1), new ThreadSize(16, 16, 1));
        }

        Compute(downPipeline, texture, glassA, 0, 0);
        if (sigma > 0)
        {
            Compute(blurPipeline, glassA, glassB, 1, 0);
            Compute(blurPipeline, glassB, glassA, 0, 1);
        }

        O.V(enc, "endEncoding");
        return new()
        {
            Origin = new(x0, y0),
            Size = new(w, h),
            Down = down,
            Scale = scale
        };
    }

    public Pixels Render(IReadOnlyList<Instance> instances, Atlas atlas, Size size, float scale, Color clear)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var pool = new AutoreleasePool();
        int w = Math.Max(1, (int)MathF.Ceiling(size.Width * scale)), h = Math.Max(1, (int)MathF.Ceiling(size.Height * scale));
        var texture = Texture(w, h);
        try
        {
            int slot = nextSlot++ % 3;
            if (slot == shown.Slot)
                shown.Slot = -1;
            var command = Encode(texture, instances, atlas, size, scale, clear, slot);
            O.V(command, "commit");
            O.V(command, "waitUntilCompleted");
            if (O.N(command, "status") == 5)
                throw new InvalidOperationException("Metal command failed: " + O.Text(O.O(O.O(command, "error"), "localizedDescription")));
            var output = Pixels.Create(w, h);
            fixed (byte* p = output.Bytes)
                O.V(texture, "getBytes:bytesPerRow:fromRegion:mipmapLevel:", (nint)p, (nuint)(w * 4), new Region(0, 0, 0, (nuint)w, (nuint)h, 1), (nuint)0);
            return output;
        }
        finally
        {
            O.Release(texture);
        }
    }

    internal void Present(IReadOnlyList<Instance> instances, Atlas atlas, Size size, float scale, Color clear, bool transaction = false)
    {
        // Apple GPU drivers charge ~230 MB to the process for about a second after each render pass, so a frame
        // that would redraw the image already on screen is not encoded at all.
        if (!transaction && Shows(instances, atlas, size, scale, clear))
            return;
        using var pool = new AutoreleasePool();
        O.V(Layer, "setContentsScale:", (double)scale);
        O.V(Layer, "setDrawableSize:", new NativeSize(size.Width * scale, size.Height * scale));
        O.V(Layer, "setPresentsWithTransaction:", transaction ? (byte)1 : (byte)0);
        var drawable = O.O(Layer, "nextDrawable");
        if (drawable == 0)
            return;
        PresentedFrames++;
        int slot = nextSlot++ % 3;
        var command = Encode(O.O(drawable, "texture"), instances, atlas, size, scale, clear, slot);
        shown = (slot, instances.Count, atlas, atlas.Version, atlas.Width, size, scale, clear);
        if (!transaction)
            O.V(command, "presentDrawable:", drawable);
        commands[slot] = O.O(command, "retain");
        O.V(command, "commit");
        if (transaction)
        {
            // Match the drawable to AppKit's geometry transaction during resize.
            O.V(command, "waitUntilScheduled");
            O.V(drawable, "present");
        }
    }

    /// <summary>Whether the layer already shows exactly this frame; the shown slot's buffer is compared byte for byte.</summary>
    bool Shows(IReadOnlyList<Instance> instances, Atlas atlas, Size size, float scale, Color clear)
    {
        var s = shown;
        if (s.Slot < 0 || s.Count != instances.Count || s.Atlas != atlas || s.AtlasVersion != atlas.Version || s.AtlasWidth != atlas.Width)
            return false;
        if (s.Size != size || s.Scale != scale || s.Clear != clear)
            return false;
        if (s.Count == 0)
            return true;
        var previous = new ReadOnlySpan<Instance>((void*)O.O(buffers[s.Slot], "contents"), s.Count);
        if (instances is List<Instance> list)
            return MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(list)).SequenceEqual(MemoryMarshal.AsBytes(previous));
        for (int i = 0; i < s.Count; i++)
        {
            var instance = instances[i];
            if (!MemoryMarshal.AsBytes(new ReadOnlySpan<Instance>(in instance)).SequenceEqual(MemoryMarshal.AsBytes(previous.Slice(i, 1))))
                return false;
        }

        return true;
    }

    /// <summary>Makes the next Present encode a frame even if it matches the one last shown.</summary>
    internal void Redraw() => shown.Slot = -1;
    void WaitAll()
    {
        foreach (var command in commands)
            if (command != 0)
                O.V(command, "waitUntilCompleted");
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        WaitAll();
        foreach (var command in commands)
            O.Release(command);
        foreach (var buffer in buffers)
            O.Release(buffer);
        foreach (var obj in new[]
        {
            Layer,
            glassA,
            glassB,
            atlasTexture,
            downPipeline,
            blurPipeline,
            glassPipeline,
            pipeline,
            queue,
            device
        }

        )
            O.Release(obj);
    }
}
