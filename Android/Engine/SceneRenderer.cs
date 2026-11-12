using System.Diagnostics;
using System.Numerics;
using Android.Opengl;
using Java.Nio;
using Javax.Microedition.Khronos.Egl;
using Javax.Microedition.Khronos.Opengles;

namespace MobileEngine.Engine;

public class GpuMesh
{
    public FloatBuffer Verts = null!;
    public ShortBuffer Indices = null!;
    public int Count;

    public static GpuMesh Upload(float[] v, short[] i)
    {
        var m = new GpuMesh { Count = i.Length };
        m.Verts = ByteBuffer.AllocateDirect(v.Length * 4)!.Order(ByteOrder.NativeOrder())!.AsFloatBuffer()!;
        m.Verts.Put(v); m.Verts.Position(0);
        m.Indices = ByteBuffer.AllocateDirect(i.Length * 2)!.Order(ByteOrder.NativeOrder())!.AsShortBuffer()!;
        m.Indices.Put(i); m.Indices.Position(0);
        return m;
    }
}

public class SceneRenderer : Java.Lang.Object, GLSurfaceView.IRenderer
{
    const string VertexSrc = @"#version 300 es
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
uniform mat4 uMvp;
uniform mat4 uNormal;
out vec3 vN;
void main() {
    vN = mat3(uNormal) * aNormal;
    gl_Position = uMvp * vec4(aPos, 1.0);
}";

    const string FragmentSrc = @"#version 300 es
precision mediump float;
in vec3 vN;
uniform vec3 uColor;
uniform vec3 uLight;
uniform float uSel;
uniform float uUnlit;
out vec4 fragColor;
void main() {
    vec3 n = normalize(vN);
    float d = max(dot(n, normalize(uLight)), 0.0);
    vec3 c = uUnlit > 0.5 ? uColor : uColor * (0.25 + 0.75 * d);
    c = mix(c, vec3(1.0, 0.85, 0.2), 0.35 * uSel);
    fragColor = vec4(c, 1.0);
}";

    readonly EditorState _ed;
    readonly Dictionary<MeshType, GpuMesh> _meshes = new();
    int _prog, _uMvp, _uNormal, _uColor, _uLight, _uSel, _uUnlit;
    FloatBuffer? _grid;
    int _gridVerts;
    long _last;

    public Matrix4x4 View = Matrix4x4.Identity, Proj = Matrix4x4.Identity;
    public volatile int Width = 1, Height = 1;

    public SceneRenderer(EditorState ed) { _ed = ed; }

    public void OnSurfaceCreated(IGL10? gl, Javax.Microedition.Khronos.Egl.EGLConfig? config)
    {
        GLES30.GlClearColor(0.16f, 0.17f, 0.2f, 1f);
        GLES30.GlEnable(GLES30.GlDepthTest);

        int vs = Compile(GLES30.GlVertexShader, VertexSrc);
        int fs = Compile(GLES30.GlFragmentShader, FragmentSrc);
        _prog = GLES30.GlCreateProgram();
        GLES30.GlAttachShader(_prog, vs);
        GLES30.GlAttachShader(_prog, fs);
        GLES30.GlLinkProgram(_prog);
        _uMvp = GLES30.GlGetUniformLocation(_prog, "uMvp");
        _uNormal = GLES30.GlGetUniformLocation(_prog, "uNormal");
        _uColor = GLES30.GlGetUniformLocation(_prog, "uColor");
        _uLight = GLES30.GlGetUniformLocation(_prog, "uLight");
        _uSel = GLES30.GlGetUniformLocation(_prog, "uSel");
        _uUnlit = GLES30.GlGetUniformLocation(_prog, "uUnlit");

        _meshes.Clear();
        foreach (MeshType t in Enum.GetValues(typeof(MeshType)))
        {
            var (v, i) = MeshFactory.Create(t);
            _meshes[t] = GpuMesh.Upload(v, i);
        }

        var g = new List<float>();
        for (int k = -10; k <= 10; k++)
        {
            g.AddRange(new float[] { k, 0.01f, -10, 0, 1, 0, k, 0.01f, 10, 0, 1, 0 });
            g.AddRange(new float[] { -10, 0.01f, k, 0, 1, 0, 10, 0.01f, k, 0, 1, 0 });
        }
        _gridVerts = g.Count / 6;
        _grid = ByteBuffer.AllocateDirect(g.Count * 4)!.Order(ByteOrder.NativeOrder())!.AsFloatBuffer()!;
        _grid.Put(g.ToArray()); _grid.Position(0);
        _last = Stopwatch.GetTimestamp();
    }

    public void OnSurfaceChanged(IGL10? gl, int width, int height)
    {
        Width = Math.Max(1, width); Height = Math.Max(1, height);
        GLES30.GlViewport(0, 0, width, height);
    }

    public void OnDrawFrame(IGL10? gl)
    {
        long now = Stopwatch.GetTimestamp();
        float dt = Math.Min((now - _last) / (float)Stopwatch.Frequency, 0.05f);
        _last = now;

        GLES30.GlClear(GLES30.GlColorBufferBit | GLES30.GlDepthBufferBit);

        var scene = _ed.Scene;
        lock (scene.Sync)
        {
            if (_ed.Playing) scene.Update(dt);

            View = _ed.Camera.View();
            Proj = OrbitCamera.Perspective(60f * MathF.PI / 180f, (float)Width / Height, 0.1f, 200f);
            var vp = View * Proj;

            GLES30.GlUseProgram(_prog);
            GLES30.GlUniform3f(_uLight, 0.4f, 0.8f, 0.5f);

            // grid
            GLES30.GlUniformMatrix4fv(_uMvp, 1, false, Arr(vp), 0);
            GLES30.GlUniformMatrix4fv(_uNormal, 1, false, Arr(Matrix4x4.Identity), 0);
            GLES30.GlUniform3f(_uColor, 0.4f, 0.4f, 0.45f);
            GLES30.GlUniform1f(_uSel, 0f);
            GLES30.GlUniform1f(_uUnlit, 1f);
            _grid!.Position(0);
            GLES30.GlVertexAttribPointer(0, 3, GLES30.GlFloat, false, 24, _grid);
            GLES30.GlEnableVertexAttribArray(0);
            _grid.Position(3);
            GLES30.GlVertexAttribPointer(1, 3, GLES30.GlFloat, false, 24, _grid);
            GLES30.GlEnableVertexAttribArray(1);
            GLES30.GlDrawArrays(GLES30.GlLines, 0, _gridVerts);

            // entities
            GLES30.GlUniform1f(_uUnlit, 0f);
            foreach (var e in scene.Entities)
            {
                var mr = e.Get<MeshRenderer>();
                if (mr == null) continue;
                var mesh = _meshes[mr.Mesh];
                var model = e.Model;
                Matrix4x4.Invert(model, out var inv);
                GLES30.GlUniformMatrix4fv(_uMvp, 1, false, Arr(model * vp), 0);
                GLES30.GlUniformMatrix4fv(_uNormal, 1, false, Arr(Matrix4x4.Transpose(inv)), 0);
                GLES30.GlUniform3f(_uColor, mr.Color.X, mr.Color.Y, mr.Color.Z);
                GLES30.GlUniform1f(_uSel, scene.Selected == e ? 1f : 0f);

                mesh.Verts.Position(0);
                GLES30.GlVertexAttribPointer(0, 3, GLES30.GlFloat, false, 24, mesh.Verts);
                mesh.Verts.Position(3);
                GLES30.GlVertexAttribPointer(1, 3, GLES30.GlFloat, false, 24, mesh.Verts);
                mesh.Indices.Position(0);
                GLES30.GlDrawElements(GLES30.GlTriangles, mesh.Count, GLES30.GlUnsignedShort, mesh.Indices);
            }
        }
    }

    static float[] Arr(Matrix4x4 m) => new[]
    {
        m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44,
    };

    static int Compile(int type, string source)
    {
        int shader = GLES30.GlCreateShader(type);
        GLES30.GlShaderSource(shader, source);
        GLES30.GlCompileShader(shader);
        var status = new int[1];
        GLES30.GlGetShaderiv(shader, GLES30.GlCompileStatus, status, 0);
        if (status[0] == 0)
            throw new InvalidOperationException("Shader error: " + GLES30.GlGetShaderInfoLog(shader));
        return shader;
    }
}
