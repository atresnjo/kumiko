using System.Numerics;
using System.Text;
using static Kumiko.Rendering.D3D11Native;

namespace Kumiko.Rendering;

// Thin wrappers over the ID3D11Device and ID3D11DeviceContext vtable slots used by the renderer.
internal sealed unsafe partial class D3D11
{
    static byte[] Compile(string source, string entry, string target)
    {
        var text = Encoding.UTF8.GetBytes(source);
        var entryName = Encoding.ASCII.GetBytes(entry + "\0");
        var targetName = Encoding.ASCII.GetBytes(target + "\0");
        nint code = 0, errors = 0;
        try
        {
            int result;
            // D3DCOMPILE_OPTIMIZATION_LEVEL3; no source name, so no checkout path is embedded.
            fixed (byte* sourceBytes = text, entryBytes = entryName, targetBytes = targetName)
                result = D3DCompile(sourceBytes, (nuint)text.Length, null, null, 0, entryBytes, targetBytes, 1u << 15, 0, &code, &errors);
            if (result < 0 || code == 0)
                throw new InvalidOperationException($"Direct3D 11 shader {entry}: {Text(errors)} (HRESULT 0x{result:X8})");
            return Bytes(code);
        }
        finally
        {
            ReleaseObject(ref errors);
            ReleaseObject(ref code);
        }
    }

    nint CreateVertexShader(byte[] code) => CreateShader(DeviceSlot.CreateVertexShader, code);
    nint CreatePixelShader(byte[] code) => CreateShader(DeviceSlot.CreatePixelShader, code);

    nint CreateShader(int slot, byte[] code)
    {
        nint result = 0;
        fixed (byte* bytes = code)
            Check(((delegate* unmanaged[Stdcall]<nint, byte*, nuint, nint, nint*, int>)Slot(device, slot))(device, bytes, (nuint)code.Length, 0, &result), "Direct3D 11 shader creation failed.");
        return result;
    }

    // CreateBlendState, CreateRasterizerState and CreateSamplerState share one shape.
    nint CreateState(int slot, void* description)
    {
        nint result = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, void*, nint*, int>)Slot(device, slot))(device, description, &result), "Direct3D 11 state creation failed.");
        return result;
    }

    nint CreateBuffer(BufferDescription description, void* data)
    {
        var initial = new SubresourceData { Memory = data };
        nint result = 0;
        Check(
            ((delegate* unmanaged[Stdcall]<nint, BufferDescription*, SubresourceData*, nint*, int>)Slot(device, DeviceSlot.CreateBuffer))(device, &description, data == null ? null : &initial, &result),
            "Direct3D 11 buffer creation failed."
        );
        return result;
    }

    nint CreateTexture(int w, int h, int format, uint binding, void* data, int usage = DefaultUsage, uint access = 0)
    {
        var description = new TextureDescription
        {
            Width = (uint)w,
            Height = (uint)h,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleCount = 1,
            Usage = usage,
            BindFlags = binding,
            CpuAccess = access
        };
        var initial = new SubresourceData { Memory = data, Pitch = (uint)w * 4 };
        nint result = 0;
        Check(
            ((delegate* unmanaged[Stdcall]<nint, TextureDescription*, SubresourceData*, nint*, int>)Slot(device, DeviceSlot.CreateTexture2D))(device, &description, data == null ? null : &initial, &result),
            "Direct3D 11 texture creation failed."
        );
        return result;
    }

    // CreateShaderResourceView and CreateRenderTargetView; a null description views the whole texture.
    nint CreateView(int slot, nint resource)
    {
        nint result = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, nint, void*, nint*, int>)Slot(device, slot))(device, resource, null, &result), "Direct3D 11 view creation failed.");
        return result;
    }

    void UpdateTexture(nint texture, void* data, int pitch)
        => ((delegate* unmanaged[Stdcall]<nint, nint, uint, void*, void*, uint, uint, void>)Slot(context, ContextSlot.UpdateSubresource))(context, texture, 0, null, data, (uint)pitch, 0);

    void UpdateConstants()
    {
        fixed (Constants* data = &constants)
            UpdateTexture(constantBuffer, data, 0);
    }

    void ClearTarget(nint target, float* color)
        => ((delegate* unmanaged[Stdcall]<nint, nint, float*, void>)Slot(context, ContextSlot.ClearRenderTargetView))(context, target, color);

    // Unbind the input first: the runtime nulls a shader view whose resource is also the bound target.
    void Target(nint target, nint view)
    {
        nint none = 0;
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)Slot(context, ContextSlot.PSSetShaderResources))(context, 0, 1, &none);
        ((delegate* unmanaged[Stdcall]<nint, uint, nint*, nint, void>)Slot(context, ContextSlot.OMSetRenderTargets))(context, 1, &target, 0);
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, void>)Slot(context, ContextSlot.PSSetShaderResources))(context, 0, 1, &view);
    }

    void SetViewport(int w, int h)
    {
        var viewport = new Viewport { Width = w, Height = h, MaxDepth = 1 };
        ((delegate* unmanaged[Stdcall]<nint, uint, Viewport*, void>)Slot(context, ContextSlot.RSSetViewports))(context, 1, &viewport);
    }

    // A null blend state disables blending, as glDisable(GL_BLEND) does for the backdrop passes.
    void SetBlend(nint state)
        => ((delegate* unmanaged[Stdcall]<nint, nint, float*, uint, void>)Slot(context, ContextSlot.OMSetBlendState))(context, state, null, 0xffffffff);

    void SetLayout(nint inputLayout, int topology)
    {
        ((delegate* unmanaged[Stdcall]<nint, nint, void>)Slot(context, ContextSlot.IASetInputLayout))(context, inputLayout);
        ((delegate* unmanaged[Stdcall]<nint, int, void>)Slot(context, ContextSlot.IASetPrimitiveTopology))(context, topology);
    }

    void SetShaders(nint vertex, nint pixel)
    {
        ((delegate* unmanaged[Stdcall]<nint, nint, nint*, uint, void>)Slot(context, ContextSlot.VSSetShader))(context, vertex, null, 0);
        ((delegate* unmanaged[Stdcall]<nint, nint, nint*, uint, void>)Slot(context, ContextSlot.PSSetShader))(context, pixel, null, 0);
    }

    // Streams instances through a dynamic per-instance vertex buffer, growing it by powers of two.
    void Upload(ReadOnlySpan<Instance> instances)
    {
        if (instances.Length > instanceCapacity)
        {
            ReleaseObject(ref instanceBuffer);
            instanceCapacity = Instance.Retained(instanceCapacity, instances.Length);
            instanceBuffer = CreateBuffer(new()
            {
                ByteWidth = (uint)(instanceCapacity * sizeof(Instance)),
                Usage = DynamicUsage,
                BindFlags = VertexBinding,
                CpuAccess = CpuWrite
            }, null);
        }
        MappedSubresource mapped;
        Check(
            ((delegate* unmanaged[Stdcall]<nint, nint, uint, int, uint, MappedSubresource*, int>)Slot(context, ContextSlot.Map))(context, instanceBuffer, 0, MapWriteDiscard, 0, &mapped),
            "Direct3D 11 instance upload failed."
        );
        fixed (Instance* data = instances)
            Buffer.MemoryCopy(data, mapped.Data, (long)instanceCapacity * sizeof(Instance), (long)instances.Length * sizeof(Instance));
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, void>)Slot(context, ContextSlot.Unmap))(context, instanceBuffer, 0);
        nint buffer = instanceBuffer;
        uint stride = (uint)sizeof(Instance), offset = 0;
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, nint*, uint*, uint*, void>)Slot(context, ContextSlot.IASetVertexBuffers))(context, 0, 1, &buffer, &stride, &offset);
    }

    // Four strip vertices per instance, as glDrawArraysInstanced(GL_TRIANGLE_STRIP, 0, 4, count).
    void DrawInstanced(int count)
        => ((delegate* unmanaged[Stdcall]<nint, uint, uint, uint, uint, void>)Slot(context, ContextSlot.DrawInstanced))(context, 4, (uint)count, 0, 0);

    void Draw(uint vertices)
        => ((delegate* unmanaged[Stdcall]<nint, uint, uint, void>)Slot(context, ContextSlot.Draw))(context, vertices, 0);
}
