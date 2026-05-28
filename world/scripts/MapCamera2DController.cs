using Godot;

namespace Isekai.World;

public sealed partial class MapCamera2DController : Node2D
{
    [Export] public NodePath CameraPath { get; set; } = "camera";

    [Export(PropertyHint.Range, "1,4000,1")]
    public float MoveSpeed { get; set; } = 900.0f;

    [Export(PropertyHint.Range, "0.05,4,0.01")]
    public float MinZoom { get; set; } = 0.18f;

    [Export(PropertyHint.Range, "0.05,4,0.01")]
    public float MaxZoom { get; set; } = 2.50f;

    [Export(PropertyHint.Range, "0.01,0.5,0.01")]
    public float ZoomStep { get; set; } = 0.12f;

    [Export] public MouseButton DragButton { get; set; } = MouseButton.Right;

    private Camera2D _camera;
    private bool _isDragging;

    public override void _Ready()
    {
        _camera = GetNodeOrNull<Camera2D>(CameraPath) ?? GetViewport().GetCamera2D();

        if (_camera != null)
        {
            _camera.Enabled = true;
            _camera.Zoom = ClampZoom(_camera.Zoom);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == DragButton)
            {
                _isDragging = mouseButton.Pressed;
                GetViewport().SetInputAsHandled();
                return;
            }

            if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                ZoomBy(1.0f + ZoomStep);
                GetViewport().SetInputAsHandled();
                return;
            }

            if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                ZoomBy(1.0f - ZoomStep);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (_isDragging && @event is InputEventMouseMotion mouseMotion)
        {
            var zoom = _camera?.Zoom ?? Vector2.One;
            Position -= mouseMotion.Relative / zoom;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        var movement = Vector2.Zero;

        if (Input.IsPhysicalKeyPressed(Key.W))
        {
            movement.Y -= 1.0f;
        }

        if (Input.IsPhysicalKeyPressed(Key.S))
        {
            movement.Y += 1.0f;
        }

        if (Input.IsPhysicalKeyPressed(Key.A))
        {
            movement.X -= 1.0f;
        }

        if (Input.IsPhysicalKeyPressed(Key.D))
        {
            movement.X += 1.0f;
        }

        if (movement == Vector2.Zero)
        {
            return;
        }

        var zoom = _camera?.Zoom.X ?? 1.0f;
        Position += movement.Normalized() * MoveSpeed * (float)delta / Mathf.Max(0.001f, zoom);
    }

    private void ZoomBy(float factor)
    {
        _camera ??= GetNodeOrNull<Camera2D>(CameraPath) ?? GetViewport().GetCamera2D();

        if (_camera == null)
        {
            return;
        }

        _camera.Zoom = ClampZoom(_camera.Zoom * factor);
    }

    private Vector2 ClampZoom(Vector2 zoom)
    {
        var clamped = Mathf.Clamp(zoom.X, MinZoom, MaxZoom);
        return new Vector2(clamped, clamped);
    }
}
