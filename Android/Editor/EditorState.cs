using System.Numerics;
using MobileEngine.Engine;
using Spinner = MobileEngine.Engine.Spinner;

namespace MobileEngine.Editor;

public class EditorState
{
    public Scene Scene = new();
    public OrbitCamera Camera = new();
    public volatile bool Playing;
    string? _snapshot;
    int? _selectedId;

    public void LoadDefaultScene()
    {
        var ground = Scene.Create("Ground", MeshType.Plane, new Vector3(0.35f, 0.42f, 0.35f));
        ground.Scale = new Vector3(10, 1, 10);

        var cube = Scene.Create("Cube", MeshType.Cube, new Vector3(0.85f, 0.25f, 0.25f));
        cube.Position = new Vector3(0, 0.5f, 0);
        cube.Add<Spinner>();

        var sphere = Scene.Create("Sphere", MeshType.Sphere, new Vector3(0.25f, 0.45f, 0.9f));
        sphere.Position = new Vector3(2, 3, 0);
        sphere.Add<Rigidbody>();
    }

    public void StartPlay()
    {
        lock (Scene.Sync)
        {
            _snapshot = Scene.ToJson();
            _selectedId = Scene.Selected?.Id;
            Playing = true;
        }
    }

    public void StopPlay()
    {
        lock (Scene.Sync)
        {
            Playing = false;
            if (_snapshot == null) return;
            Scene.FromJson(_snapshot);
            Scene.Selected = Scene.Entities.FirstOrDefault(e => e.Id == _selectedId);
        }
    }
}
