# Velocity Component for Godot 4

Hey there! This is a movement component I built for 2D platformers in Godot 4. It's been through a bunch of iterations on my own projects, and I figured it might be useful for others too.

## What's This?

It's basically a plug-and-play movement system that handles all the annoying physics stuff you don't want to rewrite every time you start a new platformer. You know - acceleration curves, jump buffering, coyote time, all that good stuff that makes movement feel nice.

## Features

- **Smooth acceleration/deceleration** - Separate values for ground and air movement
- **Jump system** - Multi-jump support with configurable jump height
- **Coyote time** - Because falling off edges shouldn't feel punishing
- **Jump buffering** - Press jump a bit early and it'll still work when you land
- **Gravity manipulation** - Walk on floors or ceilings, your choice
- **Force/impulse system** - For dashes, knockback, wind, whatever
- **Ground slam helper** - One method call and you're slamming down
- **Floating mode support** - Switch between platformer and free movement

## Quick Start

1. Attach the script to a Node in your scene
2. Export your CharacterBody2D reference to the component
3. Call the movement methods from your player script

```csharp
// In your player script
public override void _PhysicsProcess(double delta)
{
    Vector2 input = Input.GetVector("left", "right", "up", "down");
    
    if (input != Vector2.Zero)
        velocityComponent.Accelerate(input, delta);
    else
        velocityComponent.Decelerate(delta);
    
    if (Input.IsActionJustPressed("jump"))
        velocityComponent.BufferJump();
    
    velocityComponent.TryJump();
    
    if (Input.IsActionJustReleased("jump"))
        velocityComponent.CutJump();
}
```

## Why These Defaults?

The component comes with some opinionated defaults that I've found work well:

- **Gravity: 980** - Roughly matches real-world gravity in pixels/second²
- **Coyote Time: 0.15s** - Feels forgiving without being noticeable
- **Jump Buffer: 0.15s** - Same deal
- **Max Speed: 100** - Tune this to your game's scale
- **Air Control: Reduced** - Makes jumps feel more committal

Feel free to tweak these in the inspector. That's what they're there for.

## Signals

The component emits signals for important events:

- `GravityStateChanged(GravityState)` - When gravity flips
- `Landed()` - When hitting the ground
- `Fell()` - When falling off an edge (not jumping)
- `Jumped(int count)` - When jumping (count = which jump in the sequence)

Hook these up for particles, sounds, animations, whatever.

## Some Implementation Notes

**Coyote time vs multi-jump**: The component distinguishes between "just fell off an edge" (grants coyote) and "used a jump" (decrements jump counter). This feels more fair - you don't lose your double jump just because you walked off a ledge.

**Frame rate independence**: Movement uses exponential smoothing, not linear interpolation. This means it feels the same at 60fps or 144fps.

**Gravity states**: The "ceiling" gravity state isn't just flipped movement - it properly inverts the up direction, so all the physics math stays consistent.

**Mass**: There's a mass property that affects forces/impulses. I usually leave it at 1.0 unless I'm making something noticeably heavy or light. It's subtle but helps differentiate character feel.

## Common Use Cases

**Dash:**
```csharp
velocityComponent.AddImpulse(input * dashForce);
```

**Knockback:**
```csharp
velocityComponent.AddImpulse(knockbackDirection * knockbackForce);
```

**Variable jump height (hold to jump higher):**
```csharp
// Already handled by CutJump() if you release the button early
```

**Celeste-style dash refresh:**
```csharp
velocityComponent.ResetJumps(); // Call this when dashing or hitting a crystal or whatever
```

## Compatibility

Built and tested with Godot 4.6 using C#. Should work fine with Godot 4.3+.

## License

MIT - do whatever you want with it. Would be cool if you let me know if you use it in something, but no pressure.

## Contributing

Found a bug? Got an idea? Feel free to open an issue. I'm generally open to improvements as long as they don't break the core design

## Questions?

If something's unclear or you're running into issues, open an issue and I'll try to help out. I'm not always super fast to respond, but I'll get to it eventually.

---

Made this because I got tired of rewriting the same movement code for every jam. Hope it saves you some time too.
