using System.Numerics;
using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Opengl;
using Android.Views;
using Android.Widget;
using MobileEngine.Engine;

namespace MobileEngine.Editor;

[Activity(Label = "Mobile Engine", MainLauncher = true,
          Theme = "@android:style/Theme.Material.NoActionBar",
          ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
public class MainActivity : Activity
{
    const int MATCH = -1, WRAP = -2;

    EditorState _ed = null!;
    SceneRenderer _renderer = null!;
    GLSurfaceView _gl = null!;
    LinearLayout _content = null!;
    Button _playBtn = null!;
    ScaleGestureDetector _scale = null!;
    int _tab;
    float _lastX, _lastY, _downX, _downY;
    long _downT;
    bool _moved;

    int Dp(int v) => (int)(v * Resources!.DisplayMetrics!.Density);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _ed = new EditorState();
        _ed.LoadDefaultScene();
        _scale = new ScaleGestureDetector(this, new PinchListener(_ed.Camera));

        var root = new FrameLayout(this);
        _gl = new GLSurfaceView(this);
        _gl.SetEGLContextClientVersion(3);
        _gl.SetEGLConfigChooser(8, 8, 8, 8, 16, 0);
        _renderer = new SceneRenderer(_ed);
        _gl.SetRenderer(_renderer);
        _gl.Touch += OnGlTouch;
        root.AddView(_gl, new FrameLayout.LayoutParams(MATCH, MATCH));

        root.AddView(BuildToolbar(), new FrameLayout.LayoutParams(MATCH, WRAP) { Gravity = GravityFlags.Top });

        int panelH = (int)(Resources!.DisplayMetrics!.HeightPixels * 0.38);
        root.AddView(BuildPanel(), new FrameLayout.LayoutParams(MATCH, panelH) { Gravity = GravityFlags.Bottom });

        SetContentView(root);
        Refresh();
    }

    protected override void OnPause() { base.OnPause(); _gl.OnPause(); }
    protected override void OnResume() { base.OnResume(); _gl.OnResume(); }

    // ---------- Touch: drag = orbit, pinch = zoom, tap = select ----------
    void OnGlTouch(object? sender, View.TouchEventArgs e)
    {
        var ev = e.Event!;
        _scale.OnTouchEvent(ev);
        switch (ev.ActionMasked)
        {
            case MotionEventActions.Down:
                _lastX = _downX = ev.GetX(); _lastY = _downY = ev.GetY();
                _downT = ev.EventTime; _moved = false;
                break;
            case MotionEventActions.PointerDown:
                _moved = true;
                break;
            case MotionEventActions.PointerUp:
                {
                    int idx = ev.ActionIndex == 0 ? 1 : 0;
                    _lastX = ev.GetX(idx); _lastY = ev.GetY(idx);
                    break;
                }
            case MotionEventActions.Move:
                if (ev.PointerCount == 1 && !_scale.IsInProgress)
                {
                    float dx = ev.GetX() - _lastX, dy = ev.GetY() - _lastY;
                    _lastX = ev.GetX(); _lastY = ev.GetY();
                    if (Math.Abs(ev.GetX() - _downX) + Math.Abs(ev.GetY() - _downY) > Dp(12)) _moved = true;
                    if (_moved) _ed.Camera.Orbit(dx, dy);
                }
                break;
            case MotionEventActions.Up:
                if (!_moved && ev.EventTime - _downT < 300)
                {
                    var hit = _ed.Scene.Pick(ev.GetX(), ev.GetY(), _renderer.Width, _renderer.Height,
                                             _renderer.View, _renderer.Proj);
                    lock (_ed.Scene.Sync) _ed.Scene.Selected = hit;
                    Refresh();
                }
                break;
        }
        e.Handled = true;
    }

    class PinchListener : ScaleGestureDetector.SimpleOnScaleGestureListener
    {
        readonly OrbitCamera _cam;
        public PinchListener(OrbitCamera cam) { _cam = cam; }
        public override bool OnScale(ScaleGestureDetector detector)
        {
            _cam.Distance = Math.Clamp(_cam.Distance / detector.ScaleFactor, 2f, 60f);
            return true;
        }
    }

    // ---------- UI building ----------
    Button Btn(string text, Action onClick)
    {
        var b = new Button(this) { Text = text };
        b.SetAllCaps(false);
        b.TextSize = 12;
        b.Click += (s, a) => onClick();
        return b;
    }

    TextView Label(string text, int sp = 14)
    {
        var t = new TextView(this) { Text = text };
        t.TextSize = sp;
        t.SetTextColor(Color.White);
        return t;
    }

    View BuildToolbar()
    {
        var sv = new HorizontalScrollView(this);
        sv.SetBackgroundColor(Color.Argb(210, 30, 30, 36));
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.AddView(Btn("+Cube", () => AddEntity(MeshType.Cube)));
        row.AddView(Btn("+Sphere", () => AddEntity(MeshType.Sphere)));
        row.AddView(Btn("+Plane", () => AddEntity(MeshType.Plane)));
        row.AddView(Btn("Delete", DeleteSelected));
        row.AddView(Btn("Focus", FocusSelected));
        _playBtn = Btn("▶ Play", TogglePlay);
        row.AddView(_playBtn);
        row.AddView(Btn("Save", SaveScene));
        row.AddView(Btn("Load", LoadScene));
        sv.AddView(row);
        return sv;
    }

    View BuildPanel()
    {
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        panel.SetBackgroundColor(Color.Argb(235, 30, 30, 36));

        var tabs = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        tabs.AddView(Btn("Hierarchy", () => { _tab = 0; Refresh(); }), new LinearLayout.LayoutParams(0, WRAP, 1f));
        tabs.AddView(Btn("Inspector", () => { _tab = 1; Refresh(); }), new LinearLayout.LayoutParams(0, WRAP, 1f));
        panel.AddView(tabs);

        var sv = new ScrollView(this);
        _content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _content.SetPadding(Dp(8), Dp(4), Dp(8), Dp(4));
        sv.AddView(_content);
        panel.AddView(sv, new LinearLayout.LayoutParams(MATCH, 0, 1f));
        return panel;
    }

    void Refresh()
    {
        _content.RemoveAllViews();
        if (_tab == 0) BuildHierarchy(); else BuildInspector();
    }

    void BuildHierarchy()
    {
        List<Entity> list; Entity? sel;
        lock (_ed.Scene.Sync) { list = _ed.Scene.Entities.ToList(); sel = _ed.Scene.Selected; }
        foreach (var e in list)
        {
            var ent = e;
            var tv = Label((e == sel ? "▶ " : "    ") + e.Name, 16);
            tv.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
            tv.Click += (s, a) =>
            {
                lock (_ed.Scene.Sync) _ed.Scene.Selected = ent;
                _tab = 1; Refresh();
            };
            _content.AddView(tv);
        }
    }

    void BuildInspector()
    {
        Entity? e;
        lock (_ed.Scene.Sync) e = _ed.Scene.Selected;
        if (e == null) { _content.AddView(Label("Nothing selected. Tap an object in the scene or the Hierarchy.")); return; }

        _content.AddView(Label(e.Name, 18));
        Section("Position");
        Stepper("X", () => e.Position.X, v => e.Position.X = v, 0.5f);
        Stepper("Y", () => e.Position.Y, v => e.Position.Y = v, 0.5f);
        Stepper("Z", () => e.Position.Z, v => e.Position.Z = v, 0.5f);
        Section("Rotation (deg)");
        Stepper("X", () => e.Rotation.X, v => e.Rotation.X = v, 15f);
        Stepper("Y", () => e.Rotation.Y, v => e.Rotation.Y = v, 15f);
        Stepper("Z", () => e.Rotation.Z, v => e.Rotation.Z = v, 15f);
        Section("Scale");
        Stepper("X", () => e.Scale.X, v => e.Scale.X = Math.Max(0.1f, v), 0.25f);
        Stepper("Y", () => e.Scale.Y, v => e.Scale.Y = Math.Max(0.1f, v), 0.25f);
        Stepper("Z", () => e.Scale.Z, v => e.Scale.Z = Math.Max(0.1f, v), 0.25f);

        var mr = e.Get<MeshRenderer>();
        if (mr != null)
        {
            Section("Mesh Renderer color");
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            row.AddView(Btn("Red", () => mr.Color = new Vector3(0.9f, 0.2f, 0.2f)));
            row.AddView(Btn("Green", () => mr.Color = new Vector3(0.2f, 0.8f, 0.3f)));
            row.AddView(Btn("Blue", () => mr.Color = new Vector3(0.25f, 0.45f, 0.95f)));
            row.AddView(Btn("Yellow", () => mr.Color = new Vector3(0.95f, 0.85f, 0.2f)));
            row.AddView(Btn("White", () => mr.Color = new Vector3(0.95f, 0.95f, 0.95f)));
            var hs = new HorizontalScrollView(this);
            hs.AddView(row);
            _content.AddView(hs);
        }

        Section("Components");
        bool hasSpin = e.Get<Spinner>() != null, hasRb = e.Get<Rigidbody>() != null;
        _content.AddView(Btn(hasSpin ? "Spinner: ON" : "Spinner: OFF", () =>
        {
            lock (_ed.Scene.Sync) { if (hasSpin) e.Remove<Spinner>(); else e.Add<Spinner>(); }
            Refresh();
        }));
        _content.AddView(Btn(hasRb ? "Rigidbody (gravity): ON" : "Rigidbody (gravity): OFF", () =>
        {
            lock (_ed.Scene.Sync) { if (hasRb) e.Remove<Rigidbody>(); else e.Add<Rigidbody>(); }
            Refresh();
        }));
    }

    void Section(string title)
    {
        var t = Label(title, 13);
        t.SetTextColor(Color.Argb(255, 150, 190, 255));
        t.SetPadding(0, Dp(8), 0, 0);
        _content.AddView(t);
    }

    void Stepper(string label, Func<float> get, Action<float> set, float step)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        var val = Label(get().ToString("0.00"));
        val.Gravity = GravityFlags.Center;
        row.AddView(Label(label), new LinearLayout.LayoutParams(Dp(40), WRAP));
        row.AddView(Btn("-", () => { lock (_ed.Scene.Sync) set(get() - step); val.Text = get().ToString("0.00"); }));
        row.AddView(val, new LinearLayout.LayoutParams(Dp(70), WRAP));
        row.AddView(Btn("+", () => { lock (_ed.Scene.Sync) set(get() + step); val.Text = get().ToString("0.00"); }));
        _content.AddView(row);
    }

    // ---------- Toolbar actions ----------
    void AddEntity(MeshType m)
    {
        lock (_ed.Scene.Sync)
        {
            var e = _ed.Scene.Create(null, m);
            if (m != MeshType.Plane) e.Position = new Vector3(0, 0.5f, 0);
            _ed.Scene.Selected = e;
        }
        _tab = 1; Refresh();
    }

    void DeleteSelected()
    {
        lock (_ed.Scene.Sync)
            if (_ed.Scene.Selected != null) _ed.Scene.Remove(_ed.Scene.Selected);
        Refresh();
    }

    void FocusSelected()
    {
        lock (_ed.Scene.Sync)
            if (_ed.Scene.Selected != null) _ed.Camera.Target = _ed.Scene.Selected.Position;
    }

    void TogglePlay()
    {
        if (_ed.Playing) _ed.StopPlay(); else _ed.StartPlay();
        _playBtn.Text = _ed.Playing ? "■ Stop" : "▶ Play";
        Refresh();
    }

    string ScenePath =>
        System.IO.Path.Combine(GetExternalFilesDir(null)?.AbsolutePath ?? FilesDir!.AbsolutePath, "scene.json");

    void Toast(string msg) => Android.Widget.Toast.MakeText(this, msg, ToastLength.Long)?.Show();

    void SaveScene()
    {
        try { System.IO.File.WriteAllText(ScenePath, _ed.Scene.ToJson()); Toast("Saved: " + ScenePath); }
        catch (Exception ex) { Toast("Save failed: " + ex.Message); }
    }

    void LoadScene()
    {
        try
        {
            if (_ed.Playing) TogglePlay();
            if (!System.IO.File.Exists(ScenePath)) { Toast("No saved scene yet"); return; }
            _ed.Scene.FromJson(System.IO.File.ReadAllText(ScenePath));
            Toast("Loaded"); Refresh();
        }
        catch (Exception ex) { Toast("Load failed: " + ex.Message); }
    }
}
