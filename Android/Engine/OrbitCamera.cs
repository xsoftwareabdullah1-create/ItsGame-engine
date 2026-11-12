using System.Numerics;

namespace MobileEngine.Engine;

public class OrbitCamera
{
    public Vector3 Target = new(0, 0.5f, 0);
    public float Yaw = 35, Pitch = 25, Distance = 9;

    public Vector3 Position
    {
        get
        {
            const float d2r = MathF.PI / 180f;
            float y = Yaw * d2r, p = Pitch * d2r;
            return Target + Distance * new Vector3(MathF.Cos(p) * MathF.Sin(y), MathF.Sin(p), MathF.Cos(p) * MathF.Cos(y));
        }
    }

    public void Orbit(float dxPixels, float dyPixels)
    {
        Yaw -= dxPixels * 0.4f;
        Pitch = Math.Clamp(Pitch + dyPixels * 0.4f, -85f, 85f);
    }

    public Matrix4x4 View() => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);

    /// <summary>OpenGL-style perspective (depth -1..1) in System.Numerics row-vector layout.</summary>
    public static Matrix4x4 Perspective(float fovY, float aspect, float n, float f)
    {
        float t = 1f / MathF.Tan(fovY * 0.5f);
        return new Matrix4x4(
            t / aspect, 0, 0, 0,
            0, t, 0, 0,
            0, 0, (f + n) / (n - f), -1,
            0, 0, 2f * f * n / (n - f), 0);
    }
}
