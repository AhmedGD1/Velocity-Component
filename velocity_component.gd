class_name VelocityComponent extends Node

enum GravityState {
	FLOOR, CEILING
}

signal gravity_state_changed(state: GravityState)
signal landed()
signal fell()
signal jumped()

const GRAVITY: float = 980.0
const COYOTE_TIME: float = 0.15
const JUMP_BUFFERING_TIME: float = 0.15

@export var controller: CharacterBody2D

@export_range(10, 1000) var max_speed: float = 100.0
@export_range(0.1, 100) var mass: float = 1.0

@export_group("Movement Control")

@export_subgroup("Ground")
@export_range(0.5, 150) var acceleration: float = 40.0
@export_range(0.5, 150) var deceleration: float = 40.0

@export_subgroup("Air")
@export_range(0.5, 150) var air_acceleration: float = 15.0
@export_range(0.5, 150) var air_deceleration: float = 15.0

@export_group("Jump & Gravity")
@export var gravity_state: GravityState: set = set_gravity_state

@export_range(5, 200) var jump_height: float = 40.0: set = update_jump_height
@export_range(1, 2) var max_jumps: int = 1

@export_range(0.1, 5) var gravity_scale: float = 1.0: set = set_gravity_scale
@export_range(100, 1000) var max_fall_speed: float = 500.0
@export_range(1, 2) var fall_gravity_multiplier: float = 1.0

@export var grant_coyote_on_fall: bool = true

var is_grounded: bool
var is_falling: bool

var _is_floating_mode: bool
var _use_gravity: bool = true

var _jump_velocity: float
var _coyote_timer: float
var _jump_buffering_timer: float

var _jumps_left: int

func _ready() -> void:
	if !is_instance_valid(controller):
		push_error("VelocityComponent: CharacterBody2D controller is not assigned!")
		return
	
	_is_floating_mode = controller.motion_mode == CharacterBody2D.MotionMode.MOTION_MODE_FLOATING
	
	reset_jumps()
	_update_jump_velocity()

func _physics_process(delta: float) -> void:
	var was_grounded: bool = is_grounded
	
	_update_jump_timers(delta)
	
	if _use_gravity:
		_apply_gravity(delta)
	controller.move_and_slide()
	
	is_grounded = controller.is_on_floor()
	is_falling = !is_grounded && controller.velocity.dot(controller.up_direction) < 0.0
	
	if is_falling && was_grounded:
		fell.emit()
		
		if grant_coyote_on_fall:
			get_coyote()
	
	if !was_grounded && is_grounded:
		landed.emit()
		reset_jumps()

func switch_mode(mode: CharacterBody2D.MotionMode) -> void:
	if !is_instance_valid(controller):
		push_warning("Invalid Motion mode switch, can't switch to the same mode")
		return
	
	controller.motion_mode = mode
	_is_floating_mode = mode == CharacterBody2D.MotionMode.MOTION_MODE_FLOATING

#region Axis Based Movement
func accelerate_with_speed(direction: Vector2, delta: float, speed: float) -> void:
	var accel: float = _get_acceleration()
	apply_movement(direction, delta, speed, accel)

func accelerate_scaled(direction: Vector2, delta: float, speed_scale: float) -> void:
	accelerate_with_speed(direction, delta, max_speed * speed_scale)

func accelerate(direction: Vector2, delta: float) -> void:
	accelerate_with_speed(direction, delta, max_speed)

func decelerate(delta: float, weight: float = -1.0) -> void:
	var applied_weight: float = weight if weight > 0.0 else _get_deceleration()
	apply_movement(Vector2.ZERO, delta, max_speed, applied_weight)

func add_impulse(impulse: Vector2) -> void:
	controller.velocity += impulse / mass

func add_force(force: Vector2, delta: float) -> void:
	controller.velocity += force / mass * delta

func ground_slam() -> void:
	if is_grounded:
		return
	controller.velocity = Vector2.UP * max_fall_speed * 1.5 * controller.up_direction

func stop(freeze_y: bool = true) -> void:
	if freeze_y:
		controller.velocity.y = 0.0
	controller.velocity.x = 0.0

func apply_movement(dir: Vector2, delta: float, speed: float, weight: float) -> void:
	var smoothing: float = 1.0 - exp(-weight * delta)
	var desired: Vector2 = dir.normalized() * speed
	
	if _is_floating_mode:
		controller.velocity.y = lerp(controller.velocity.y, desired.y, smoothing)
	controller.velocity.x = lerp(controller.velocity.x, desired.x, smoothing)

func _get_acceleration() -> float:
	return acceleration if (is_grounded || _is_floating_mode) else air_acceleration

func _get_deceleration() -> float:
	return deceleration if (is_grounded || _is_floating_mode) else air_deceleration
#endregion

#region Jump Methods
func get_coyote(duration: float = COYOTE_TIME) -> void: _coyote_timer = duration
func has_coyote() -> bool: return _coyote_timer > 0.0
func consume_coyote() -> void: _coyote_timer = 0.0

func buffer_jump(duration: float = JUMP_BUFFERING_TIME) -> void: _jump_buffering_timer
func has_buffered_jump() -> bool: return _jump_buffering_timer > 0.0
func consume_buffered_jump() -> void: _jump_buffering_timer = 0.0

func can_jump(required_buffered_jump: bool = true) -> bool:
	var is_on_surface: bool = has_coyote() || is_grounded || _jumps_left > 0
	return is_on_surface && (has_buffered_jump() || !required_buffered_jump)

func try_jump() -> bool:
	if can_jump():
		jump()
		return true
	return false

func jump() -> void:
	controller.velocity.y = _jump_velocity * controller.up_direction.y
	_jumps_left -= 1
	
	jumped.emit(max_jumps - _jumps_left)
	consume_coyote()
	consume_buffered_jump()

func reset_jumps() -> void:
	_jumps_left = max_jumps

func cut_jump() -> void:
	if controller.velocity.dot(controller.up_direction) < 0.0:
		controller.velocity.y *= 0.5

func set_max_jumps(value: float, reset_jumps: bool = false) -> void:
	max_jumps = maxi(0, value)
	
	if reset_jumps:
		reset_jumps()

func update_jump_height(height: float) -> void:
	jump_height = height
	_update_jump_velocity()

func _update_jump_velocity() -> void:
	var grav: float = GRAVITY * gravity_scale
	_jump_velocity = sqrt(2.0 * grav * jump_height)

func _update_jump_timers(delta: float) -> void:
	if _coyote_timer > 0.0:
		_coyote_timer -= delta
	
	if _jump_buffering_timer > 0.0:
		_jump_buffering_timer -= delta
#endregion

#region Gravity Manipulation
func set_gravity_state(state: GravityState) -> void:
	if !is_instance_valid(controller):
		return
	
	var up_direction: float
	
	match state:
		GravityState.FLOOR: up_direction = -1.0
		GravityState.CEILING: up_direction = 1.0
	
	gravity_state = state
	controller.up_direction = Vector2(0, -up_direction)
	gravity_state_changed.emit(state)

func set_gravity_active(active: bool) -> void: 
	_use_gravity = active

func set_gravity_scale(value: float) -> void:
	gravity_scale = value
	_update_jump_velocity()

func _apply_gravity(delta: float) -> void:
	if is_grounded || _is_floating_mode:
		return
	
	var gravity: float = _get_gravity()
	var down: Vector2 = -controller.up_direction
	
	controller.velocity += down * gravity * delta
	
	var down_speed: float = controller.velocity.dot(down)
	if down_speed > max_fall_speed:
		controller.velocity -= down * (down_speed - max_fall_speed)

func _get_gravity() -> float:
	return GRAVITY * gravity_scale * (fall_gravity_multiplier if is_falling else 1.0)
#endregion












