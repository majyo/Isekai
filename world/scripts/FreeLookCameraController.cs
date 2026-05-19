using Godot;

namespace Isekai.World;

public sealed partial class FreeLookCameraController : Node3D
{
    [Export] public string ConfigPath { get; set; } = WorldMapConfig.DefaultResourcePath;

    [Export(PropertyHint.Range, "1,5000,1")]
    public float PanSpeed { get; set; } = 900.0f;

    [Export(PropertyHint.Range, "10,1000,1")]
    public float ZoomStepHeight { get; set; } = 160.0f;

    [Export(PropertyHint.Range, "50,5000,1")]
    public float MinHeight { get; set; } = 520.0f;

    [Export(PropertyHint.Range, "100,10000,1")]
    public float MaxHeight { get; set; } = 1800.0f;

    [Export(PropertyHint.Range, "15,80,0.1")]
    public float PitchDegrees { get; set; } = 55.0f;

    [Export(PropertyHint.Range, "-180,180,0.1")]
    public float YawDegrees { get; set; } = 0.0f;

    [Export(PropertyHint.Range, "0,2000,1")]
    public float BoundaryPadding { get; set; } = 64.0f;

    private WorldMapConfig _config;

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _config = ResourceLoader.Load<WorldMapConfig>(ConfigPath);

        if (_config == null)
        {
            WorldMapDebugLogger.Warn($"Could not load world map config at '{ConfigPath}'. Camera bounds disabled.");
        }

        ApplyFixedRotation();
        GlobalPosition = ClampCameraPosition(GlobalPosition);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mouseButton)
        {
            return;
        }

        if (mouseButton.ButtonIndex == MouseButton.WheelUp)
        {
            ZoomByHeight(-ZoomStepHeight);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (mouseButton.ButtonIndex == MouseButton.WheelDown)
        {
            ZoomByHeight(ZoomStepHeight);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        var movement = GetPanDirection();

        if (movement == Vector3.Zero)
        {
            return;
        }

        var nextPosition = GlobalPosition + movement.Normalized() * PanSpeed * (float)delta;
        GlobalPosition = ClampCameraPosition(nextPosition);
    }

    private void ZoomByHeight(float heightDelta)
    {
        var currentPosition = GlobalPosition;
        var focusPoint = GetGroundFocusPoint(currentPosition);
        var nextHeight = Mathf.Clamp(currentPosition.Y + heightDelta, MinHeight, MaxHeight);

        if (Mathf.IsEqualApprox(nextHeight, currentPosition.Y))
        {
            return;
        }

        var forward = GetForwardDirection();

        if (Mathf.Abs(forward.Y) < 0.001f)
        {
            currentPosition.Y = nextHeight;
            GlobalPosition = ClampCameraPosition(currentPosition);
            return;
        }

        var distanceFromFocus = (nextHeight - focusPoint.Y) / -forward.Y;
        var nextPosition = focusPoint - forward * distanceFromFocus;
        GlobalPosition = ClampCameraPosition(nextPosition);
    }

    private Vector3 GetPanDirection()
    {
        var forward = GetHorizontalDirection(GetForwardDirection(), new Vector3(0.0f, 0.0f, -1.0f));
        var right = GetHorizontalDirection(GlobalTransform.Basis.X, Vector3.Right);
        var movement = Vector3.Zero;

        if (Input.IsPhysicalKeyPressed(Key.W))
        {
            movement += forward;
        }

        if (Input.IsPhysicalKeyPressed(Key.S))
        {
            movement -= forward;
        }

        if (Input.IsPhysicalKeyPressed(Key.D))
        {
            movement += right;
        }

        if (Input.IsPhysicalKeyPressed(Key.A))
        {
            movement -= right;
        }

        return movement;
    }

    private Vector3 ClampCameraPosition(Vector3 position)
    {
        position.Y = Mathf.Clamp(position.Y, MinHeight, MaxHeight);

        if (_config == null)
        {
            return position;
        }

        var focusPoint = GetGroundFocusPoint(position);
        var halfWorldSize = _config.WorldSize * 0.5f;
        var clampedFocusX = ClampToRange(focusPoint.X, -halfWorldSize.X + BoundaryPadding, halfWorldSize.X - BoundaryPadding);
        var clampedFocusZ = ClampToRange(focusPoint.Z, -halfWorldSize.Y + BoundaryPadding, halfWorldSize.Y - BoundaryPadding);
        var focusDelta = new Vector3(clampedFocusX - focusPoint.X, 0.0f, clampedFocusZ - focusPoint.Z);

        return position + focusDelta;
    }

    private Vector3 GetGroundFocusPoint(Vector3 cameraPosition)
    {
        var forward = GetForwardDirection();

        if (Mathf.Abs(forward.Y) < 0.001f)
        {
            return new Vector3(cameraPosition.X, 0.0f, cameraPosition.Z);
        }

        var distanceToGround = -cameraPosition.Y / forward.Y;
        var focusPoint = cameraPosition + forward * distanceToGround;
        return new Vector3(focusPoint.X, 0.0f, focusPoint.Z);
    }

    private Vector3 GetForwardDirection()
    {
        return -GlobalTransform.Basis.Z;
    }

    private static Vector3 GetHorizontalDirection(Vector3 direction, Vector3 fallback)
    {
        var horizontal = new Vector3(direction.X, 0.0f, direction.Z);

        if (horizontal == Vector3.Zero)
        {
            return fallback;
        }

        return horizontal.Normalized();
    }

    private void ApplyFixedRotation()
    {
        Rotation = new Vector3(Mathf.DegToRad(-PitchDegrees), Mathf.DegToRad(YawDegrees), 0.0f);
    }

    private static float ClampToRange(float value, float min, float max)
    {
        if (min > max)
        {
            return (min + max) * 0.5f;
        }

        return Mathf.Clamp(value, min, max);
    }
}
