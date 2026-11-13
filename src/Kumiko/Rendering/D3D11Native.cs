using System.Runtime.InteropServices;

namespace Kumiko.Rendering;

// Direct3D 11, DXGI and D3DCompiler through raw COM vtables, as WindowsSvg calls Direct2D.
// Structures and slot numbers follow the Windows SDK headers d3d11.h, dxgi.h, dxgi1_2.h and d3dcommon.h.
internal static unsafe class D3D11Native
{
    internal static readonly Guid DxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    internal static readonly Guid DxgiFactory2 = new("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
    internal static readonly Guid Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    internal const int Bgra = 87, Float4 = 2, Hardware = 1, Warp = 5;
    internal const uint VertexBinding = 0x1, ConstantBinding = 0x4, ShaderBinding = 0x8, TargetBinding = 0x20;
    internal const int DefaultUsage = 0, DynamicUsage = 2, StagingUsage = 3;
    internal const uint CpuWrite = 0x10000, CpuRead = 0x20000;
    internal const int MapRead = 1, MapWriteDiscard = 4, TriangleList = 4, TriangleStrip = 5;

    // IUnknown and IDXGIObject.
    internal const int QueryInterface = 0, Release = 2, GetParent = 6;

    internal static class DeviceSlot
    {
        internal const int CreateBuffer = 3, CreateTexture2D = 5, CreateShaderResourceView = 7, CreateRenderTargetView = 9;
        internal const int CreateInputLayout = 11, CreateVertexShader = 12, CreatePixelShader = 15;
        internal const int CreateBlendState = 20, CreateRasterizerState = 22, CreateSamplerState = 23;
    }

    // ID3D11DeviceContext: IUnknown 0-2, ID3D11DeviceChild 3-6, then the d3d11.h declaration order.
    internal static class ContextSlot
    {
        internal const int VSSetConstantBuffers = 7, PSSetShaderResources = 8, PSSetShader = 9, PSSetSamplers = 10;
        internal const int VSSetShader = 11, Draw = 13, Map = 14, Unmap = 15, PSSetConstantBuffers = 16;
        internal const int IASetInputLayout = 17, IASetVertexBuffers = 18, DrawInstanced = 21, IASetPrimitiveTopology = 24;
        internal const int OMSetRenderTargets = 33, OMSetBlendState = 35, RSSetState = 43, RSSetViewports = 44;
        internal const int CopyResource = 47, UpdateSubresource = 48, ClearRenderTargetView = 50, ClearState = 110, Flush = 111;
    }

    // IDXGIDevice/IDXGIAdapter 7-8; IDXGIFactory 8, IDXGIFactory2 15; IDXGISwapChain 8, 9 and 13.
    internal static class DxgiSlot
    {
        internal const int GetAdapter = 7, GetDescription = 8, MakeWindowAssociation = 8, CreateSwapChainForHwnd = 15;
        internal const int Present = 8, GetBuffer = 9, ResizeBuffers = 13;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BufferDescription
    {
        public uint ByteWidth;
        public int Usage;
        public uint BindFlags, CpuAccess, MiscFlags, StructureStride;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TextureDescription
    {
        public uint Width, Height, MipLevels, ArraySize;
        public int Format;
        public uint SampleCount, SampleQuality;
        public int Usage;
        public uint BindFlags, CpuAccess, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SubresourceData
    {
        public void* Memory;
        public uint Pitch, SlicePitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MappedSubresource
    {
        public byte* Data;
        public uint RowPitch, DepthPitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InputElement
    {
        public byte* Semantic;
        public uint SemanticIndex;
        public int Format;
        public uint Slot, Offset;
        public int Classification;
        public uint StepRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Viewport
    {
        public float X, Y, Width, Height, MinDepth, MaxDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SamplerDescription
    {
        public int Filter, AddressU, AddressV, AddressW;
        public float MipBias;
        public uint MaxAnisotropy;
        public int Comparison;
        public fixed float Border[4];
        public float MinLod, MaxLod;
    }

    // D3D11_BLEND_DESC: two BOOLs and eight 32-byte render-target entries; only entry 0 is used.
    [StructLayout(LayoutKind.Sequential, Size = 264)]
    internal struct BlendDescription
    {
        public int AlphaToCoverage, IndependentBlend;
        public int Enable, Source, Destination, Operation, SourceAlpha, DestinationAlpha, OperationAlpha;
        public byte WriteMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RasterizerDescription
    {
        public int Fill, Cull, FrontCounterClockwise, DepthBias;
        public float DepthBiasClamp, SlopeScaledDepthBias;
        public int DepthClip, Scissor, Multisample, AntialiasedLine;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SwapChainDescription
    {
        public uint Width, Height;
        public int Format, Stereo;
        public uint SampleCount, SampleQuality, BufferUsage, BufferCount;
        public int Scaling, SwapEffect, AlphaMode;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AdapterDescription
    {
        public fixed char Description[128];
        public uint VendorId, DeviceId, SubsystemId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
    }

    [DllImport("d3d11.dll")]
    internal static extern int D3D11CreateDevice(
        nint adapter,
        int driverType,
        nint software,
        uint flags,
        int* levels,
        uint levelCount,
        uint sdkVersion,
        nint* device,
        int* level,
        nint* context
    );

    [DllImport("d3dcompiler_47.dll")]
    internal static extern int D3DCompile(
        byte* source,
        nuint length,
        byte* name,
        void* defines,
        nint include,
        byte* entry,
        byte* target,
        uint flags,
        uint effectFlags,
        nint* code,
        nint* errors
    );

    internal static void* Slot(nint value, int index) => (*(void***)value)[index];

    internal static uint ReleaseObject(ref nint value)
    {
        if (value == 0)
            return 0;
        uint remaining = ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(value, Release))(value);
        value = 0;
        return remaining;
    }

    internal static nint Query(nint value, Guid id)
    {
        nint result = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(value, QueryInterface))(value, &id, &result), "Direct3D 11 interface query failed.");
        return result;
    }

    // DXGI_ERROR_DEVICE_REMOVED, _HUNG, _RESET and _DRIVER_INTERNAL_ERROR invalidate every device object.
    internal static bool IsDeviceLost(int result) => (uint)result is 0x887a0005 or 0x887a0006 or 0x887a0007 or 0x887a0020;

    internal static void Check(int result, string message)
    {
        if (result >= 0)
            return;
        string text = $"{message} (HRESULT 0x{result:X8})";
        if (IsDeviceLost(result))
            throw new D3D11DeviceLostException(text);
        throw new InvalidOperationException(text);
    }

    // The compiler's error blob (ID3DBlob: GetBufferPointer 3, GetBufferSize 4) is ANSI text.
    internal static string Text(nint blob)
    {
        if (blob == 0)
            return "";
        var data = ((delegate* unmanaged[Stdcall]<nint, byte*>)Slot(blob, 3))(blob);
        var length = ((delegate* unmanaged[Stdcall]<nint, nuint>)Slot(blob, 4))(blob);
        return Marshal.PtrToStringAnsi((nint)data, (int)length).TrimEnd('\0', '\n', '\r');
    }

    internal static byte[] Bytes(nint blob)
    {
        var data = ((delegate* unmanaged[Stdcall]<nint, byte*>)Slot(blob, 3))(blob);
        var length = ((delegate* unmanaged[Stdcall]<nint, nuint>)Slot(blob, 4))(blob);
        return new ReadOnlySpan<byte>(data, checked((int)length)).ToArray();
    }
}

/// <summary>The Direct3D device was removed or reset; every object created from it is unusable.</summary>
internal sealed class D3D11DeviceLostException(string message) : InvalidOperationException(message);
