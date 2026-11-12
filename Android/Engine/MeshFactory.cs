using System.Numerics;

namespace MobileEngine.Engine;

/// <summary>Interleaved vertices: x y z nx ny nz.</summary>
public static class MeshFactory
{
    public static (float[] verts, short[] idx) Create(MeshType t) => t switch
    {
        MeshType.Cube => Cube(),
        MeshType.Sphere => Sphere(),
        _ => Plane(),
    };

    static void AddVert(List<float> v, Vector3 p, Vector3 n)
    {
        v.Add(p.X); v.Add(p.Y); v.Add(p.Z);
        v.Add(n.X); v.Add(n.Y); v.Add(n.Z);
    }

    static void AddQuad(List<float> v, List<short> i, Vector3 n, Vector3 u, Vector3 w)
    {
        short b = (short)(v.Count / 6);
        var c = n * 0.5f;
        AddVert(v, c - u * 0.5f - w * 0.5f, n);
        AddVert(v, c + u * 0.5f - w * 0.5f, n);
        AddVert(v, c + u * 0.5f + w * 0.5f, n);
        AddVert(v, c - u * 0.5f + w * 0.5f, n);
        i.AddRange(new short[] { b, (short)(b + 1), (short)(b + 2), b, (short)(b + 2), (short)(b + 3) });
    }

    static (float[], short[]) Cube()
    {
        var v = new List<float>(); var i = new List<short>();
        AddQuad(v, i, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        AddQuad(v, i, -Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        AddQuad(v, i, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ);
        AddQuad(v, i, -Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ);
        AddQuad(v, i, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);
        AddQuad(v, i, -Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);
        return (v.ToArray(), i.ToArray());
    }

    static (float[], short[]) Plane()
    {
        var v = new List<float>(); var i = new List<short>();
        AddQuad(v, i, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ);
        return (v.ToArray(), i.ToArray());
    }

    static (float[], short[]) Sphere()
    {
        const int stacks = 14, slices = 24;
        var v = new List<float>(); var i = new List<short>();
        for (int a = 0; a <= stacks; a++)
        {
            float phi = MathF.PI * a / stacks;
            for (int b = 0; b <= slices; b++)
            {
                float th = 2f * MathF.PI * b / slices;
                var n = new Vector3(MathF.Sin(phi) * MathF.Cos(th), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(th));
                AddVert(v, n * 0.5f, n);
            }
        }
        for (int a = 0; a < stacks; a++)
            for (int b = 0; b < slices; b++)
            {
                short p0 = (short)(a * (slices + 1) + b);
                short p1 = (short)(p0 + slices + 1);
                i.AddRange(new short[] { p0, p1, (short)(p0 + 1), (short)(p0 + 1), p1, (short)(p1 + 1) });
            }
        return (v.ToArray(), i.ToArray());
    }
}
