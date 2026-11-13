using System.Numerics;
using System.Runtime.InteropServices;
using Kumiko.UI;
using static Kumiko.Rendering.D3D11Native;

namespace Kumiko.Rendering;
/// <summary>
/// Direct3D 11 instanced quads and glass, after OpenGl.cs. A hardware device is preferred; WARP,
/// the software rasterizer present on every Windows 10+ install, serves VDI, RDP and VMs.
/// </summary>
internal sealed unsafe partial class D3D11 : IDisposable
{
    // The HLSL Uniforms constant buffer, packed in 16-byte registers.
    [StructLayout(LayoutKind.Sequential)]
    struct Constants
    {
        public Vector2 Viewport, Origin, Size;
        public float Down, Scale;
        public Vector4 Area;
        public Vector2 Direction;
        public int SampleDown, Radius;
        public float Sigma, Unused0, Unused1, Unused2;
    }

    nint device, context, layout, vertexShader, pixelShader, blend, rasterizer, sampler, constantBuffer, instanceBuffer;
    nint atlasTexture, atlasView, frameTexture, frameTarget, frameView, staging;
    int instanceCapacity, width, height, atlasSize, atlasVersion = -1;
    Atlas? currentAtlas;
    Constants constants;
    bool disposed;
    public string Driver { get; private set; } = "";
    public bool IsWarp
    {
        get; private set;
    }
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
    /// <summary>Remaining device references after disposal; nonzero means a COM object leaked.</summary>
    internal uint RemainingDeviceReferences
    {
        get; private set;
    }

    internal D3D11(bool forceWarp, nint window = 0)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Direct3D 11 requires Windows.");
        try
        {
            CreateDevice(forceWarp);
            Initialize();
            if (window != 0)
                CreateSwapChain(window);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    void CreateDevice(bool forceWarp)
    {
        string hardwareFailure = "";
        if (!forceWarp)
        {
            int result = Create(Hardware);
            if (result >= 0)
            {
                Driver = "Direct3D 11 / " + AdapterName();
                return;
            }

            hardwareFailure = $" (hardware unavailable: HRESULT 0x{result:X8})";
        }

        Check(Create(Warp), "Direct3D 11 WARP device creation failed.");
        IsWarp = true;
        Driver = "Direct3D 11 WARP" + hardwareFailure;
    }

    int Create(int driver)
    {
        // 11_1, 11_0, 10_1 and 10_0 all run the shader model 4.0 programs. BGRA support is flag 0x20.
        int* levels = stackalloc int[]
        {
            0xb100,
            0xb000,
            0xa100,
            0xa000
        };
        nint created = 0, immediate = 0;
        int level;
        int result = D3D11CreateDevice(0, driver, 0, 0x20, levels, 4, 7, &created, &level, &immediate);
        if ((uint)result == 0x80070057)
            result = D3D11CreateDevice(0, driver, 0, 0x20, levels + 1, 3, 7, &created, &level, &immediate);
        if (result >= 0)
        {
            device = created;
            context = immediate;
        }

        return result;
    }

    string AdapterName()
    {
        nint dxgi = Query(device, DxgiDevice), adapter = 0;
        try
        {
            Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(dxgi, DxgiSlot.GetAdapter))(dxgi, &adapter), "DXGI adapter query failed.");
            AdapterDescription description;
            Check(((delegate* unmanaged[Stdcall]<nint, AdapterDescription*, int>)Slot(adapter, DxgiSlot.GetDescription))(adapter, &description), "DXGI adapter description failed.");
            var name = new string(description.Description).TrimEnd('\0').Trim();
            return name.Length > 0 ? name : "unnamed adapter";
        }
        finally
        {
            ReleaseObject(ref adapter);
            ReleaseObject(ref dxgi);
        }
    }

    void Initialize()
    {
        string source = D3D11Shader.Source + D3D11GlassShader.Source;
        var vertex = Compile(source, "vertexMain", "vs_4_0");
        vertexShader = CreateVertexShader(vertex);
        pixelShader = CreatePixelShader(Compile(source, "pixelMain", "ps_4_0"));
        layout = CreateLayout(vertex);
        InitializeGlass(source);
        var blending = new BlendDescription
        {
            Enable = 1,
            Source = 2,
            Destination = 6,
            Operation = 1,
            SourceAlpha = 2,
            DestinationAlpha = 6,
            OperationAlpha = 1,
            WriteMask = 0xf
        };
        blend = CreateState(DeviceSlot.CreateBlendState, &blending);
        // Solid fill, no culling (the strip's winding is irrelevant), depth clip on.
        var raster = new RasterizerDescription
        {
            Fill = 3,
            Cull = 1,
            DepthClip = 1
        };
        rasterizer = CreateState(DeviceSlot.CreateRasterizerState, &raster);
        // Linear, clamp-to-edge, as OpenGl.Texture configures the atlas.
        var sampling = new SamplerDescription
        {
            Filter = 0x14,
            AddressU = 3,
            AddressV = 3,
            AddressW = 3,
            MaxAnisotropy = 1,
            Comparison = 1,
            MaxLod = float.MaxValue
        };
        sampler = CreateState(DeviceSlot.CreateSamplerState, &sampling);
        constantBuffer = CreateBuffer(new()
        {
            ByteWidth = (uint)sizeof(Constants),
            Usage = DefaultUsage,
            BindFlags = ConstantBinding
        }, null);
    }

    nint CreateLayout(byte[] vertex)
    {
        var semantic = "INSTANCE\0"u8;
        var elements = stackalloc InputElement[7];
        fixed (byte* name = semantic)
        {
            for (uint i = 0; i < 7; i++)
                elements[i] = new()
                {
                    Semantic = name,
                    SemanticIndex = i,
                    Format = Float4,
                    Offset = i * 16,
                    Classification = 1,
                    StepRate = 1
                };
            nint result = 0;
            fixed (byte* code = vertex)
                Check(((delegate* unmanaged[Stdcall]<nint, InputElement*, uint, byte*, nuint, nint*, int>)Slot(device, DeviceSlot.CreateInputLayout))(device, elements, 7, code, (nuint)vertex.Length, &result), "Direct3D 11 input layout creation failed.");
            return result;
        }
    }

    public void Render(Context view, bool present)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        int w = Math.Max(1, (int)MathF.Ceiling(view.Size.Width * view.Scale));
        int h = Math.Max(1, (int)MathF.Ceiling(view.Size.Height * view.Scale));
        if (w != width || h != height)
            ResizeFrame(w, h);
        var background = view.Theme.Background;
        float* clear = stackalloc float[]
        {
            background.R * background.A,
            background.G * background.A,
            background.B * background.A,
            background.A
        };
        ClearTarget(frameTarget, clear);
        SyncAtlas(view.TextSystem.Atlas);
        BindQuads();
        // Use the logical viewport, including any fractional last physical pixel, as OpenGl does.
        constants.Viewport = new(width / view.Scale, height / view.Scale);
        UpdateConstants();
        var instances = view.Instances.ToArray();
        // After a spike, a frame far smaller than the buffer releases it; its first batch allocates one that fits.
        if (Instance.Retained(instanceCapacity, instances.Length) < instanceCapacity)
        {
            ReleaseObject(ref instanceBuffer);
            instanceCapacity = 0;
        }

        int from = 0;
        for (int i = 0; i < instances.Length; i++)
        {
            if (instances[i].Kind != 4)
                continue;
            Batch(instances.AsSpan(from, i - from));
            DrawGlass(instances[i], view.Scale);
            from = i + 1;
        }

        Batch(instances.AsSpan(from));
        if (present)
            Present();
    }

    void BindQuads()
    {
        Target(frameTarget, atlasView);
        SetViewport(width, height);
        SetBlend(blend);
        SetLayout(layout, TriangleStrip);
        SetShaders(vertexShader, pixelShader);
        ((delegate* unmanaged[Stdcall]<nint, nint, void>)Slot(context, ContextSlot.RSSetState))(context, rasterizer);
        nint value = sampler;
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)Slot(context, ContextSlot.PSSetSamplers))(context, 0, 1, &value);
        value = constantBuffer;
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)Slot(context, ContextSlot.VSSetConstantBuffers))(context, 0, 1, &value);
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)Slot(context, ContextSlot.PSSetConstantBuffers))(context, 0, 1, &value);
    }

    void Batch(ReadOnlySpan<Instance> instances)
    {
        if (instances.Length == 0)
            return;
        Upload(instances);
        nint view = atlasView;
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)Slot(context, ContextSlot.PSSetShaderResources))(context, 0, 1, &view);
        DrawInstanced(instances.Length);
    }

    // As MetalRenderer: a new, grown or rebuilt atlas is sent whole; otherwise only the rows written since our copy.
    void SyncAtlas(Atlas atlas)
    {
        if (atlasTexture == 0 || atlasSize != atlas.Width)
        {
            ReleaseObject(ref atlasView);
            ReleaseObject(ref atlasTexture);
            fixed (byte* bytes = atlas.Bytes)
                atlasTexture = CreateTexture(atlas.Width, atlas.Height, Bgra, ShaderBinding, bytes);
            atlasView = CreateView(DeviceSlot.CreateShaderResourceView, atlasTexture);
            atlasSize = atlas.Width;
            currentAtlas = atlas;
            atlasVersion = atlas.Version;
            FullAtlasUploads++;
            return;
        }

        if (currentAtlas == atlas && atlasVersion == atlas.Version)
            return;
        if (currentAtlas != atlas || atlasVersion < atlas.Rebuilt)
        {
            fixed (byte* bytes = atlas.Bytes)
                UpdateTexture(atlasTexture, bytes, atlas.Width * 4);
            currentAtlas = atlas;
            atlasVersion = atlas.Version;
            FullAtlasUploads++;
            return;
        }

        var (top, bottom) = atlas.Changed(atlasVersion);
        atlasVersion = atlas.Version;
        if (bottom <= top)
            return;
        int stride = atlas.Width * 4;
        // D3D11_BOX: left, top, front, right, bottom, back; the source pointer addresses the box's first row.
        var box = new Box
        {
            Top = (uint)top,
            Right = (uint)atlas.Width,
            Bottom = (uint)bottom,
            Back = 1
        };
        fixed (byte* bytes = atlas.Bytes)
            ((delegate* unmanaged[Stdcall]<nint, nint, uint, Box*, void*, uint, uint, void>)Slot(context, ContextSlot.UpdateSubresource))(context, atlasTexture, 0, &box, bytes + top * stride, (uint)stride, 0);
        AtlasRowUploads++;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Box
    {
        public uint Left, Top, Front, Right, Bottom, Back;
    }

    void ResizeFrame(int w, int h)
    {
        ReleaseObject(ref staging);
        ReleaseObject(ref frameView);
        ReleaseObject(ref frameTarget);
        ReleaseObject(ref frameTexture);
        width = w;
        height = h;
        frameTexture = CreateTexture(w, h, Bgra, TargetBinding | ShaderBinding, null);
        frameTarget = CreateView(DeviceSlot.CreateRenderTargetView, frameTexture);
        frameView = CreateView(DeviceSlot.CreateShaderResourceView, frameTexture);
    }

    // Copies the frame through a staging texture. Rows are top-down BGRA, premultiplied, as Pixels.
    public Pixels Readback()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ReadbackCount++;
        if (staging == 0)
            staging = CreateTexture(width, height, Bgra, 0, null, StagingUsage, CpuRead);
        ((delegate* unmanaged[Stdcall]<nint, nint, nint, void>)Slot(context, ContextSlot.CopyResource))(context, staging, frameTexture);
        MappedSubresource mapped;
        Check(((delegate* unmanaged[Stdcall]<nint, nint, uint, int, uint, MappedSubresource*, int>)Slot(context, ContextSlot.Map))(context, staging, 0, MapRead, 0, &mapped), "Direct3D 11 readback failed.");
        var pixels = Pixels.Create(width, height);
        try
        {
            int row = width * 4;
            for (int y = 0; y < height; y++)
                new ReadOnlySpan<byte>(mapped.Data + (long)y * mapped.RowPitch, row).CopyTo(pixels.Bytes.AsSpan(y * row, row));
        }
        finally
        {
            ((delegate* unmanaged[Stdcall]<nint, nint, uint, void>)Slot(context, ContextSlot.Unmap))(context, staging, 0);
        }

        return pixels;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        // Unbind, release, then flush: a flip-model swap chain must be destroyed before the window
        // can receive a replacement after device loss.
        if (context != 0)
            ((delegate* unmanaged[Stdcall]<nint, void>)Slot(context, ContextSlot.ClearState))(context);
        DisposeSwapChain();
        DisposeGlass();
        ReleaseObject(ref staging);
        ReleaseObject(ref frameView);
        ReleaseObject(ref frameTarget);
        ReleaseObject(ref frameTexture);
        ReleaseObject(ref atlasView);
        ReleaseObject(ref atlasTexture);
        ReleaseObject(ref instanceBuffer);
        ReleaseObject(ref constantBuffer);
        ReleaseObject(ref sampler);
        ReleaseObject(ref rasterizer);
        ReleaseObject(ref blend);
        ReleaseObject(ref layout);
        ReleaseObject(ref pixelShader);
        ReleaseObject(ref vertexShader);
        if (context != 0)
            ((delegate* unmanaged[Stdcall]<nint, void>)Slot(context, ContextSlot.Flush))(context);
        ReleaseObject(ref context);
        RemainingDeviceReferences = ReleaseObject(ref device);
    }
}
