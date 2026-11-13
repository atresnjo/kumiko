using System.Runtime.InteropServices;
using System.Text;

namespace Kumiko.Rendering;

internal sealed unsafe partial class OpenGl
{
    readonly delegate* unmanaged<uint, uint> createShader;
    readonly delegate* unmanaged<uint, int, byte**, int*, void> shaderSource;
    readonly delegate* unmanaged<uint, void> compileShader, deleteShader, linkProgram, deleteProgram, useProgram;
    readonly delegate* unmanaged<uint, uint, int*, void> getShader, getProgram;
    readonly delegate* unmanaged<uint, int, int*, byte*, void> shaderLog, programLog;
    readonly delegate* unmanaged<uint> createProgram;
    readonly delegate* unmanaged<uint, uint, void> attachShader;
    readonly delegate* unmanaged<uint, byte*, int> uniformLocation;
    readonly delegate* unmanaged<int, float, float, void> uniform2f;
    readonly delegate* unmanaged<int, float, void> uniform1f;
    readonly delegate* unmanaged<int, float, float, float, float, void> uniform4f;
    readonly delegate* unmanaged<int, int, void> uniform1i;
    readonly delegate* unmanaged<int, uint*, void> genBuffers, genVertexArrays, genTextures, genFramebuffers, deleteBuffers, deleteVertexArrays, deleteTextures, deleteFramebuffers;
    readonly delegate* unmanaged<uint, uint, void> bindBuffer, bindTexture, bindFramebuffer;
    readonly delegate* unmanaged<uint, void> bindVertexArray, enable, disable, enableAttribute, activeTexture;
    readonly delegate* unmanaged<uint, nint, void*, uint, void> bufferData;
    readonly delegate* unmanaged<uint, int, uint, byte, int, void*, void> attributePointer;
    readonly delegate* unmanaged<uint, uint, void> attributeDivisor, blendFunc;
    readonly delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> texImage;
    readonly delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> texSubImage;
    readonly delegate* unmanaged<uint, uint, int, void> texParameter;
    readonly delegate* unmanaged<uint, uint, uint, uint, int, void> framebufferTexture;
    readonly delegate* unmanaged<uint, uint> framebufferStatus;
    readonly delegate* unmanaged<int, int, int, int, void> viewport;
    readonly delegate* unmanaged<float, float, float, float, void> clearColor;
    readonly delegate* unmanaged<uint, void> clear;
    readonly delegate* unmanaged<uint, int, int, int, void> drawInstanced;
    readonly delegate* unmanaged<uint, int*, void> getInteger;
    readonly delegate* unmanaged<uint, byte*> getString;
    readonly delegate* unmanaged<int, int, int, int, uint, uint, void*, void> readPixels;
    readonly delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void> blit;

    internal OpenGl(Func<string, nint> load)
    {
        createShader = (delegate* unmanaged<uint, uint>)load("glCreateShader");
        shaderSource = (delegate* unmanaged<uint, int, byte**, int*, void>)load("glShaderSource");
        compileShader = (delegate* unmanaged<uint, void>)load("glCompileShader");
        deleteShader = (delegate* unmanaged<uint, void>)load("glDeleteShader");
        getShader = (delegate* unmanaged<uint, uint, int*, void>)load("glGetShaderiv");
        getProgram = (delegate* unmanaged<uint, uint, int*, void>)load("glGetProgramiv");
        shaderLog = (delegate* unmanaged<uint, int, int*, byte*, void>)load("glGetShaderInfoLog");
        programLog = (delegate* unmanaged<uint, int, int*, byte*, void>)load("glGetProgramInfoLog");
        createProgram = (delegate* unmanaged<uint>)load("glCreateProgram");
        attachShader = (delegate* unmanaged<uint, uint, void>)load("glAttachShader");
        linkProgram = (delegate* unmanaged<uint, void>)load("glLinkProgram");
        deleteProgram = (delegate* unmanaged<uint, void>)load("glDeleteProgram");
        useProgram = (delegate* unmanaged<uint, void>)load("glUseProgram");
        uniformLocation = (delegate* unmanaged<uint, byte*, int>)load("glGetUniformLocation");
        uniform2f = (delegate* unmanaged<int, float, float, void>)load("glUniform2f");
        uniform1f = (delegate* unmanaged<int, float, void>)load("glUniform1f");
        uniform4f = (delegate* unmanaged<int, float, float, float, float, void>)load("glUniform4f");
        uniform1i = (delegate* unmanaged<int, int, void>)load("glUniform1i");
        genBuffers = (delegate* unmanaged<int, uint*, void>)load("glGenBuffers");
        genVertexArrays = (delegate* unmanaged<int, uint*, void>)load("glGenVertexArrays");
        genTextures = (delegate* unmanaged<int, uint*, void>)load("glGenTextures");
        genFramebuffers = (delegate* unmanaged<int, uint*, void>)load("glGenFramebuffers");
        deleteBuffers = (delegate* unmanaged<int, uint*, void>)load("glDeleteBuffers");
        deleteVertexArrays = (delegate* unmanaged<int, uint*, void>)load("glDeleteVertexArrays");
        deleteTextures = (delegate* unmanaged<int, uint*, void>)load("glDeleteTextures");
        deleteFramebuffers = (delegate* unmanaged<int, uint*, void>)load("glDeleteFramebuffers");
        bindBuffer = (delegate* unmanaged<uint, uint, void>)load("glBindBuffer");
        bindTexture = (delegate* unmanaged<uint, uint, void>)load("glBindTexture");
        bindFramebuffer = (delegate* unmanaged<uint, uint, void>)load("glBindFramebuffer");
        bindVertexArray = (delegate* unmanaged<uint, void>)load("glBindVertexArray");
        activeTexture = (delegate* unmanaged<uint, void>)load("glActiveTexture");
        enable = (delegate* unmanaged<uint, void>)load("glEnable");
        disable = (delegate* unmanaged<uint, void>)load("glDisable");
        enableAttribute = (delegate* unmanaged<uint, void>)load("glEnableVertexAttribArray");
        bufferData = (delegate* unmanaged<uint, nint, void*, uint, void>)load("glBufferData");
        attributePointer = (delegate* unmanaged<uint, int, uint, byte, int, void*, void>)load("glVertexAttribPointer");
        attributeDivisor = (delegate* unmanaged<uint, uint, void>)load("glVertexAttribDivisor");
        blendFunc = (delegate* unmanaged<uint, uint, void>)load("glBlendFunc");
        texImage = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)load("glTexImage2D");
        texSubImage = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)load("glTexSubImage2D");
        texParameter = (delegate* unmanaged<uint, uint, int, void>)load("glTexParameteri");
        framebufferTexture = (delegate* unmanaged<uint, uint, uint, uint, int, void>)load("glFramebufferTexture2D");
        framebufferStatus = (delegate* unmanaged<uint, uint>)load("glCheckFramebufferStatus");
        viewport = (delegate* unmanaged<int, int, int, int, void>)load("glViewport");
        clearColor = (delegate* unmanaged<float, float, float, float, void>)load("glClearColor");
        clear = (delegate* unmanaged<uint, void>)load("glClear");
        drawInstanced = (delegate* unmanaged<uint, int, int, int, void>)load("glDrawArraysInstanced");
        getInteger = (delegate* unmanaged<uint, int*, void>)load("glGetIntegerv");
        getString = (delegate* unmanaged<uint, byte*>)load("glGetString");
        readPixels = (delegate* unmanaged<int, int, int, int, uint, uint, void*, void>)load("glReadPixels");
        blit = (delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void>)load("glBlitFramebuffer");
        try
        {
            Initialize();
        }
        catch
        {
            Dispose();
            throw;
        }
    }
}
