using System.Numerics;
using System.Text.Json;

namespace MobileEngine.Engine;

public enum MeshType { Cube, Sphere, Plane }

public abstract class Component
{
    public Entity Entity = null!;
    public virtual void Update(float dt) { }
}

public class MeshRenderer : Component
{
    public MeshType Mesh;
    public Vector3 Color = new(0.8f, 0.8f, 0.8f);
}

/// <summary>Rotates the entity around Y. Example of a "script" component.</summary>
public class Spinner : Component
{
    public float Speed = 90f;
    public override void Update(float dt) => Entity.Rotation.Y += Speed * dt;
}

/// <summary>Simple gravity + bounce on a floor at y = 0.</summary>
public class Rigidbody : Component
{
    public float VelocityY;
    public override void Update(float dt)
    {
        VelocityY -= 9.81f * dt;
        Entity.Position.Y += VelocityY * dt;
        float half = Entity.Scale.Y * 0.5f;
        if (Entity.Position.Y < half)
        {
            Entity.Position.Y = half;
            VelocityY = -VelocityY * 0.5f;
            if (MathF.Abs(VelocityY) < 0.3f) VelocityY = 0;
        }
    }
}

public class Entity
{
    public int Id;
    public string Name = "Entity";
    public Vector3 Position;
    public Vector3 Rotation;               // degrees
    public Vector3 Scale = Vector3.One;
    public List<Component> Components = new();

    public T? Get<T>() where T : Component
    {
        foreach (var c in Components) if (c is T t) return t;
        return null;
    }

    public T Add<T>() where T : Component, new()
    {
        var c = new T { Entity = this };
        Components.Add(c);
        return c;
    }

    public void Remove<T>() where T : Component => Components.RemoveAll(c => c is T);

    public Matrix4x4 Model
    {
        get
        {
            const float d2r = MathF.PI / 180f;
            return Matrix4x4.CreateScale(Scale)
                 * Matrix4x4.CreateFromYawPitchRoll(Rotation.Y * d2r, Rotation.X * d2r, Rotation.Z * d2r)
                 * Matrix4x4.CreateTranslation(Position);
        }
    }
}

public class EntityData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public float[] Pos { get; set; } = new float[3];
    public float[] Rot { get; set; } = new float[3];
    public float[] Scale { get; set; } = { 1, 1, 1 };
    public string? Mesh { get; set; }
    public float[]? Color { get; set; }
    public float? Spin { get; set; }
    public bool Physics { get; set; }
}

public class SceneData { public List<EntityData> Entities { get; set; } = new(); }

public class Scene
{
    public readonly object Sync = new();
    public List<Entity> Entities = new();
    public Entity? Selected;
    int _next = 1;

    static float[] V(Vector3 v) => new[] { v.X, v.Y, v.Z };
    static Vector3 P(float[] a) => new(a[0], a[1], a[2]);

    public Entity Create(string? name, MeshType mesh, Vector3? color = null)
    {
        lock (Sync)
        {
            var e = new Entity { Id = _next++ };
            e.Name = name ?? $"{mesh} {e.Id}";
            var mr = e.Add<MeshRenderer>();
            mr.Mesh = mesh;
            mr.Color = color ?? new Vector3(0.8f, 0.8f, 0.8f);
            Entities.Add(e);
            return e;
        }
    }

    public void Remove(Entity e)
    {
        lock (Sync)
        {
            Entities.Remove(e);
            if (Selected == e) Selected = null;
        }
    }

    public void Update(float dt)
    {
        lock (Sync)
            foreach (var e in Entities.ToList())
                foreach (var c in e.Components.ToList())
                    c.Update(dt);
    }

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string ToJson()
    {
        lock (Sync)
        {
            var data = new SceneData();
            foreach (var e in Entities)
            {
                var d = new EntityData
                {
                    Id = e.Id, Name = e.Name,
                    Pos = V(e.Position), Rot = V(e.Rotation), Scale = V(e.Scale),
                };
                var mr = e.Get<MeshRenderer>();
                if (mr != null) { d.Mesh = mr.Mesh.ToString(); d.Color = V(mr.Color); }
                var sp = e.Get<Spinner>();
                if (sp != null) d.Spin = sp.Speed;
                d.Physics = e.Get<Rigidbody>() != null;
                data.Entities.Add(d);
            }
            return JsonSerializer.Serialize(data, Options);
        }
    }

    public void FromJson(string json)
    {
        var data = JsonSerializer.Deserialize<SceneData>(json) ?? new SceneData();
        lock (Sync)
        {
            Entities.Clear();
            Selected = null;
            _next = 1;
            foreach (var d in data.Entities)
            {
                var e = new Entity
                {
                    Id = d.Id, Name = d.Name,
                    Position = P(d.Pos), Rotation = P(d.Rot), Scale = P(d.Scale),
                };
                if (d.Mesh != null)
                {
                    var mr = e.Add<MeshRenderer>();
                    mr.Mesh = Enum.Parse<MeshType>(d.Mesh);
                    if (d.Color != null) mr.Color = P(d.Color);
                }
                if (d.Spin != null) e.Add<Spinner>().Speed = d.Spin.Value;
                if (d.Physics) e.Add<Rigidbody>();
                Entities.Add(e);
                _next = Math.Max(_next, d.Id + 1);
            }
        }
    }

    /// <summary>Ray-pick the nearest entity. Uses bounding spheres, and a rectangle for planes.</summary>
    public Entity? Pick(float sx, float sy, int w, int h, Matrix4x4 view, Matrix4x4 proj)
    {
        if (!Matrix4x4.Invert(view * proj, out var inv)) return null;
        float nx = 2f * sx / w - 1f, ny = 1f - 2f * sy / h;
        Vector3 Unproject(float z)
        {
            var v = Vector4.Transform(new Vector4(nx, ny, z, 1f), inv);
            return new Vector3(v.X, v.Y, v.Z) / v.W;
        }
        var o = Unproject(-1f);
        var d = Vector3.Normalize(Unproject(1f) - o);

        lock (Sync)
        {
            Entity? best = null;
            float bestT = float.MaxValue;
            foreach (var e in Entities)
            {
                var mr = e.Get<MeshRenderer>();
                if (mr == null) continue;
                float t;
                if (mr.Mesh == MeshType.Plane)
                {
                    if (MathF.Abs(d.Y) < 1e-5f) continue;
                    t = (e.Position.Y - o.Y) / d.Y;
                    if (t <= 0) continue;
                    var hit = o + d * t;
                    if (MathF.Abs(hit.X - e.Position.X) > e.Scale.X * 0.5f) continue;
                    if (MathF.Abs(hit.Z - e.Position.Z) > e.Scale.Z * 0.5f) continue;
                }
                else
                {
                    float r = MathF.Max(e.Scale.X, MathF.Max(e.Scale.Y, e.Scale.Z))
                              * (mr.Mesh == MeshType.Cube ? 0.87f : 0.5f);
                    var oc = o - e.Position;
                    float b = Vector3.Dot(oc, d);
                    float c = Vector3.Dot(oc, oc) - r * r;
                    float disc = b * b - c;
                    if (disc < 0) continue;
                    float sq = MathF.Sqrt(disc);
                    t = -b - sq;
                    if (t < 0) t = -b + sq;
                    if (t < 0) continue;
                }
                if (t < bestT) { bestT = t; best = e; }
            }
            return best;
        }
    }
}
