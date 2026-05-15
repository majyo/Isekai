using Godot;

namespace Isekai.World;

public sealed partial class FreeLookCameraController : Node3D
{
    [Export(PropertyHint.Range, "1,2000,1")]
    public float MoveSpeed { get; set; } = 420.0f;

    [Export(PropertyHint.Range, "1,10,0.1")]
    public float SprintMultiplier { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "0.01,1,0.01")]
    public float MouseSensitivity { get; set; } = 0.12f;

    [Export(PropertyHint.Range, "-89,0,0.1")]
    public float MinPitchDegrees { get; set; } = -89.0f;

    [Export(PropertyHint.Range, "0,89,0.1")]
    public float MaxPitchDegrees { get; set; } = 89.0f;

    private float _yawRadians;
    private float _pitchRadians;
    private bool _isLooking;

    public override void _Ready()
    {
        _yawRadians = Rotation.Y;
        _pitchRadians = Rotation.X;
        ApplyRotation();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right } mouseButton)
        {
            _isLooking = mouseButton.Pressed;
            Input.MouseMode = _isLooking ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!_isLooking || @event is not InputEventMouseMotion mouseMotion)
        {
            return;
        }

        _yawRadians -= Mathf.DegToRad(mouseMotion.Relative.X * MouseSensitivity);
        _pitchRadians -= Mathf.DegToRad(mouseMotion.Relative.Y * MouseSensitivity);
        _pitchRadians = Mathf.Clamp(_pitchRadians, Mathf.DegToRad(MinPitchDegrees), Mathf.DegToRad(MaxPitchDegrees));

        ApplyRotation();
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        var movement = GetMovementVector();

        if (movement == Vector3.Zero)
        {
            return;
        }

        var speed = MoveSpeed;

        if (Input.IsKeyPressed(Key.Shift))
        {
            speed *= SprintMultiplier;
        }

        GlobalPosition += movement.Normalized() * speed * (float)delta;
    }

    private Vector3 GetMovementVector()
    {
        var basis = GlobalTransform.Basis;
        var forward = -basis.Z;
        var right = basis.X;
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

        if (Input.IsPhysicalKeyPressed(Key.E))
        {
            movement += Vector3.Up;
        }

        if (Input.IsPhysicalKeyPressed(Key.Q))
        {
            movement -= Vector3.Up;
        }

        return movement;
    }

    private void ApplyRotation()
    {
        Rotation = new Vector3(_pitchRadians, _yawRadians, 0.0f);
    }
}
