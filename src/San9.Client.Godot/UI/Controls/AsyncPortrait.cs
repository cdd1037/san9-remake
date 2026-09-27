using Godot;
namespace San9.Client.Godot;

public partial class AsyncPortrait : TextureRect
{
    public TextureService Service { get; init; } = null!;
    public San9.Content.Images.PortraitView View { get; init; } = San9.Content.Images.PortraitView.Full;
    public Func<bool>? IsDeceased { get; init; }
    private ShaderMaterial? _deceasedMaterial;
    private bool _deceased;
    private readonly San9.Rendering.ResourceSelection<TextureScope> _selection = new();
    private TextureScope? _scope => _selection.Current;
    private string? _role;
    private bool _ready;
    private Exception? _error;
    public string? Role { get => _role; set { if (_role == value) return; _role = value; if (IsInsideTree()) Bind(); } }
    public override void _Ready() { ExpandMode = ExpandModeEnum.IgnoreSize; StretchMode = StretchModeEnum.KeepAspectCentered; Bind(); }
    private void Bind()
    {
        Texture = null;
        _selection.Select(_role, role => { var scope = Service.CreatePortraitScope(role, View, this); scope.Request([role]); return scope; });
        if (_scope is not null) Texture = _scope[_role!];
    }
    public override void _Draw()
    {
        if (_scope is not null && !_scope.IsReady)
        {
            DrawRect(new(Vector2.Zero, Size), _scope.Error is null ? new Color("343a40") : new Color("603838"));
            if (_scope.Error is not null) DrawLine(Vector2.Zero, Size, new Color("e0a0a0"), 2);
        }
    }
    public override void _Process(double delta)
    {
        bool deceased = IsDeceased?.Invoke() ?? false;
        if (deceased != _deceased)
        {
            _deceased = deceased;
            if (deceased && _deceasedMaterial is null)
                _deceasedMaterial = new ShaderMaterial { Shader = new Shader { Code = "shader_type canvas_item; void fragment() { vec4 c = COLOR; float gray = dot(c.rgb, vec3(0.299, 0.587, 0.114)); COLOR = vec4(vec3(gray), c.a); }" } };
            Material = deceased ? _deceasedMaterial : null;
        }
        bool ready = _scope?.IsReady ?? true; var error = _scope?.Error;
        if (ready == _ready && error == _error) return;
        _ready = ready; _error = error; QueueRedraw();
    }
    public override void _ExitTree()
    {
        Texture = null; Material = null; _selection.Dispose();
        if (_deceasedMaterial is not null) { _deceasedMaterial.Shader.Dispose(); _deceasedMaterial.Dispose(); _deceasedMaterial = null; }
    }
}
