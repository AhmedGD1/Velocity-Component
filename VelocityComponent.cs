/// <summary>
/// A velocity-based movement component for 2D platformers.
/// Handles acceleration, jumping (with coyote time & buffering), gravity manipulation, and more.
/// Attach to a CharacterBody2D node and assign the controller reference.
/// </summary>
using Godot;
using System;

namespace Utilities.Movement;

[GlobalClass]
public partial class VelocityComponent : Node
{
    public enum GravityState 
    { 
        Floor,
        Ceiling 
    }

    [Signal] public delegate void GravityStateChangedEventHandler(GravityState state);
    [Signal] public delegate void LandedEventHandler();

    /// <summary>
    /// Emitted if controller fell off an adge (not mid air)
    /// </summary>
    [Signal] public delegate void FellEventHandler();
    
    /// <summary>
    /// Count refers to how many jumps did controller perform
    /// </summary>
    /// <param name="count"></param>
    [Signal] public delegate void JumpedEventHandler(int count);

    public const float Gravity = 980f;
    public const float CoyoteTime = 0.15f;
    public const float JumpBufferingTime = 0.15f;

    /// <summary>
    /// The moving object: CharacterBody2D
    /// </summary>
    [Export] private CharacterBody2D controller;

    /// <summary>
    /// The maximum speed controller can reach while moving
    /// </summary>
    [Export(PropertyHint.Range, "10, 1000")] private float maxSpeed = 100f;

    /// <summary>
    /// The mass of the controller. Don't change it unless you make a big chunky enemy
    /// </summary>
    [Export(PropertyHint.Range, "0.1, 100")] private float mass = 1f;

    [ExportGroup("Movement Control")]
    [ExportSubgroup("Ground")]
    /// <summary>
    /// How fast can controller reach his maximum speed
    /// </summary>
    [Export(PropertyHint.Range, "0.5, 150")] private float acceleration = 40f;

    /// <summary>
    /// How fast can controller stop
    /// </summary>
    [Export(PropertyHint.Range, "0, 200")] private float deceleration = 40f;

    [ExportSubgroup("Air")]
    /// <summary>
    /// How fast can controller reach his maximum speed mid air
    /// </summary>
    [Export(PropertyHint.Range, "0.1, 150")] private float airAcceleration = 15f;

    /// <summary>
    /// How fast can controller stop mid air
    /// </summary>
    [Export(PropertyHint.Range, "0, 100")] private float airDeceleration = 10f;


    [ExportGroup("Jump & Gravity")]
    [Export] private GravityState CurrentGravityState
    {
        get => gravityState;
        set => SetGravityState(value);
    }

    /// <summary>
    /// How many pixels can controller jump ?
    /// </summary>
    [Export(PropertyHint.Range, "5, 200")] private float JumpHeight
    {
        get => jumpHeight;
        set => UpdateJumpHeight(value);
    }

    /// <summary>
    /// Default gravity is 980.0. Use gravity scale to modify it
    /// </summary>
    [Export(PropertyHint.Range, "0.1, 5")] private float GravityScale
    {
        get => gravityScale;
        set => SetGravityScale(value);
    }

    [Export(PropertyHint.Range, "1, 10")] private int maxJumps = 2;

    /// <summary>
    /// Can limit the speed of falling. 200 -> fall slowly, 1000 -> fall fast
    /// </summary>
    [Export(PropertyHint.Range, "200, 1000")] private float maxFallSpeed = 500f;

    /// <summary>
    /// Used with platformer games like mario. makes gravity increase while falling, note: this could be limited by max fall speed
    /// </summary>
    [Export(PropertyHint.Range, "1, 2")] private float fallGravityMultiplier = 1f;

    /// <summary>
    /// Enable if you want auto coyote detection. can be modified manually
    /// </summary>
    [Export] private bool grantCoyoteOnFall = true;

    private GravityState gravityState = GravityState.Floor;

    public float MaxSpeed => maxSpeed;
    public float MaxJumps => maxJumps;
    public float JumpsLeft => jumpsLeft;
    public float CurrentSpeed => controller.Velocity.Length();
    public float HorizontalSpeed => Mathf.Abs(controller.Velocity.X);
    public float VerticalSpeed => Mathf.Abs(controller.Velocity.Y);

    public bool IsGrounded { get; private set; }
    public bool IsFalling { get; private set; }

    private bool isFloatingMode;
    private bool useGravity = true;

    private float jumpHeight = 40f;
    private float jumpVelocity;
    private float gravityScale = 1f;

    private float coyoteTimer;
    private float jumpBufferingTimer;

    private int jumpsLeft;

    public override void _Ready()
    {
        if (controller == null)
        {
            GD.PushError("VelocityComponent: CharacterBody2D controller is not assigned!");
            return;
        }

        isFloatingMode = controller.MotionMode == CharacterBody2D.MotionModeEnum.Floating;
        jumpsLeft = maxJumps;

        ResetJumps();
        UpdateJumpVelocity();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool wasGrounded = IsGrounded;

        UpdateJumpTimers(delta);

        if (useGravity)
            ApplyGravity(delta);
        controller.MoveAndSlide();

        IsGrounded = controller.IsOnFloor();
        // dot here refers to the direction of falling (gravity direction can be changed)
        IsFalling = !IsGrounded && controller.Velocity.Dot(controller.UpDirection) < 0f;

        // if controller start falling & was on a floor at the same time
        if (IsFalling && wasGrounded)
        {
            EmitSignalFell();

            if (grantCoyoteOnFall) // auto detect coyote
                GetCoyote();
        }

        // if controller was mid air & became grounded
        if (!wasGrounded && IsGrounded)
        {
            EmitSignalLanded();
            jumpsLeft = maxJumps;
        }
    }

    public Vector2 GetVelocity() => controller.Velocity;

    /// <summary>
    /// Call it to change the motion mode
    /// Floating: moves along the x & y axis
    /// Grounded: moves along the x axis only & can be affected by gravity
    /// </summary>
    /// <param name="mode"></param>
    public void SwitchMode(CharacterBody2D.MotionModeEnum mode)
    {
        if (controller.MotionMode == mode)
        {
            GD.PushWarning("Invalid Motion mode switch, can't switch to the same mode");
            return;
        }

        controller.MotionMode = mode;
        isFloatingMode = mode == CharacterBody2D.MotionModeEnum.Floating;
    }

    #region Axis Based Movement
    /// <summary>
    /// Call it to move the controller on a specific direction
    /// </summary>
    /// <param name="direction"></param>
    /// <param name="delta"></param>
    public void Accelerate(Vector2 direction, double delta) => AccelerateWithSpeed(direction, delta, maxSpeed);

    /// <summary>
    /// Same as Acceleration but with custom speed implementation
    /// </summary>
    /// <param name="direction"></param>
    /// <param name="delta"></param>
    /// <param name="speed"></param>
    public void AccelerateWithSpeed(Vector2 direction, double delta, float speed)
    {
        float currentAccel = GetAcceleration();
        ApplyMovement(direction, delta, speed, currentAccel);
    }

    /// <summary>
    /// Same as Accelerate but with (max speed multiplied with custom multiplier)
    /// </summary>
    /// <param name="direction"></param>
    /// <param name="delta"></param>
    /// <param name="multiplier"></param>
    public void AccelerateScaled(Vector2 direction, double delta, float multiplier) =>
        AccelerateWithSpeed(direction, delta, maxSpeed * multiplier);

    /// <summary>
    /// Call it to stop controller from moving
    /// </summary>
    /// <param name="delta"></param>
    /// <param name="weight"></param>
    public void Decelerate(double delta, float? weight = null)
    {
        float currentDecel = weight ?? GetDeceleration();
        ApplyMovement(Vector2.Zero, delta, maxSpeed, currentDecel);
    }

    /// <summary>
    /// Adds a vector2 value to velocity affected by mass
    /// Recommended for instant force implementation (dash, etc.)
    /// </summary>
    /// <param name="impulse"></param>
    public void AddImpulse(Vector2 impulse)
    {
        controller.Velocity += impulse / mass;
    }

    /// <summary>
    /// Adds a vector2 value to velocity affected by mass & delta time
    /// Recommended for run time force addition
    /// </summary>
    /// <param name="force"></param>
    /// <param name="delta"></param>
    public void AddForce(Vector2 force, double delta)
    {
        controller.Velocity += force / mass * (float)delta;
    }

    /// <summary>
    /// Stops the controller immediately from moving.
    /// </summary>
    /// <param name="freezeY"></param>
    public void Stop(bool freezeY = true)
    {
        controller.Velocity = freezeY ? Vector2.Zero : controller.Velocity with { X = 0f };
    }

    private void ApplyMovement(Vector2 direction, double delta, float speed, float weight)
    {
        float currentAccel = weight;
        // for frame‑rate‑independency
        float smoothing = 1f - Mathf.Exp(-currentAccel * (float)delta);
        
        Vector2 desired = direction.Normalized() * speed;
        Vector2 value = controller.Velocity.Lerp(desired, smoothing);

        // always updates the x axis & updates the y axis if the motion mode is floating
        controller.Velocity = new Vector2(value.X, isFloatingMode ? value.Y : controller.Velocity.Y);
    }

    // return the current acceleration (uses acceleration if is on floor or using motion mode floating if not -> uses air acceleration)
    private float GetAcceleration() => (IsGrounded || isFloatingMode) ? acceleration : airAcceleration;

    // return the current deceleration (uses deceleration if is on floor or using motion mode floating if not -> uses air deceleration)
    private float GetDeceleration() => (IsGrounded || isFloatingMode) ? deceleration : airDeceleration;
    #endregion

    #region Jump Methods
    /// <summary>
    /// Makes controller jumps if (controller is on floor, has coyote or has more jumps to perform) & acquired buffered jump
    /// </summary>
    /// <returns></returns>
    public bool TryJump()
    {
        if (CanJump())
        {
            Jump();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when (controller is on floor, has coyote or has more jumps to perform) & acquired buffered jump
    /// </summary>
    /// <param name="requiresBuffered"></param>
    /// <returns></returns>
    public bool CanJump(bool requiresBuffered = true)
    {
        bool isOnSurface = HasCoyote() || IsGrounded || jumpsLeft > 0;
        return isOnSurface && (HasBufferedJump() || !requiresBuffered);
    }

    /// <summary>
    /// Resets jumps (jumps left = max jumps)
    /// Can be used as a power up (celeste like ability)
    /// </summary>
    public void ResetJumps() => jumpsLeft = maxJumps;

    /// <summary>
    /// Gets the coyote manually (can be acquired automatically if (grant coyote on fall) is true)
    /// </summary>
    /// <param name="duration"></param>
    public void GetCoyote(float duration = CoyoteTime) => coyoteTimer = duration;
    public bool HasCoyote() => coyoteTimer > 0f;
    public void ConsumeCoyote() => coyoteTimer = 0f;

    /// <summary>
    /// Buffers the jump input. 
    /// If controller pressed the jump button before he reaches floor, buffer it & make him jump automatically when he reaches floor
    /// </summary>
    /// <param name="duration"></param>
    public void BufferJump(float duration = JumpBufferingTime) => jumpBufferingTimer = duration;
    public bool HasBufferedJump() => jumpBufferingTimer > 0f;
    public void ConsumeBufferedJump() => jumpBufferingTimer = 0f;

    /// <summary>
    /// Call it to jump manually
    /// </summary>
    public void Jump()
    {
        float y = jumpVelocity * controller.UpDirection.Y;
        controller.Velocity = new Vector2(controller.Velocity.X, y);
        jumpsLeft--;

        EmitSignalJumped(maxJumps - jumpsLeft);
        ConsumeBufferedJump();
        ConsumeCoyote();
    }

    /// <summary>
    /// Cuts the jump mid air (polish effect)
    /// </summary>
    public void CutJump()
    {
        if (controller.Velocity.Dot(controller.UpDirection) < 0f)
            controller.Velocity = new Vector2(controller.Velocity.X, controller.Velocity.Y * 0.5f);
    }

    /// <summary>
    /// Changes the max jumps quantity
    /// </summary>
    /// <param name="value"></param>
    /// <param name="resetJumps"></param>
    public void SetMaxJumps(int value, bool resetJumps = false)
    {
        maxJumps = value;

        if (resetJumps)
            jumpsLeft = maxJumps;
    }

    /// <summary>
    /// Updates jump velocity based on gravity & jump height(in pixels)
    /// </summary>
    private void UpdateJumpVelocity()
    {
        float gravity = Gravity * gravityScale;
        jumpVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
    }
    
    private void UpdateJumpTimers(double delta)
    {
        float dt = (float)delta;

        if (coyoteTimer > 0f) coyoteTimer -= dt;
        if (jumpBufferingTimer > 0f) jumpBufferingTimer -= dt;
    }

    /// <summary>
    /// Updates how tall can controller jump
    /// </summary>
    /// <param name="value"></param>
    public void UpdateJumpHeight(float value)
    {
        jumpHeight = value;
        UpdateJumpVelocity();
    }
    #endregion

    #region Gravity Manipulation
    public void SetGravityState(GravityState state)
    {
        if (!IsInstanceValid(controller))
            return;
        
        float upDirection = state switch
        {   
            GravityState.Floor => -1f,
            GravityState.Ceiling => 1f,
            _ => throw new ArgumentException("Invalid state")
        };

        gravityState = state;
        controller.UpDirection = new Vector2(0, -upDirection);
        EmitSignalGravityStateChanged(gravityState);
    }

    /// <summary>
    /// Enables & Disables gravity
    /// Can be used on a specific action (mid air attack, etc.)
    /// </summary>
    /// <param name="active"></param>
    public void SetGravityActive(bool active) => useGravity = active;

    /// <summary>
    /// Returns the current state of gravity (Does controller walk on floors or ceilings ?)
    /// </summary>
    /// <returns></returns>
    public GravityState GetGravityState() => gravityState;

    /// <summary>
    /// Changes the gravity scale value & update jump velocity based on it
    /// </summary>
    /// <param name="scale"></param>
    public void SetGravityScale(float scale)
    {
        gravityScale = scale;
        UpdateJumpVelocity();
    }

    /// <summary>
    /// Move the controller to the direction of gravity & limits its movement based on (max fall speed)
    /// </summary>
    /// <param name="delta"></param>
    private void ApplyGravity(double delta)
    {   
        // can't accelerate if controller was on a floor or using floating mode
        if (IsGrounded || isFloatingMode)
            return;

        float dt = (float)delta;
        float gravity = GetGravity();

        // accelerates towards the down direction
        Vector2 down = -controller.UpDirection;
        controller.Velocity += down * gravity * dt;

        // limits fall speed
        float downSpeed = controller.Velocity.Dot(down);
        if (downSpeed > maxFallSpeed)
            controller.Velocity -= down * (downSpeed - maxFallSpeed);
    }

    /// <summary>
    /// returns the gravity multiplied by gravity scale if is jumping
    /// returns the gravity multiplied by gravity scale multiplied by fall gravity multiplier if is falling
    /// </summary>
    /// <returns></returns>
    private float GetGravity() => Gravity * gravityScale * (IsFalling ? fallGravityMultiplier : 1f);
    #endregion
}
