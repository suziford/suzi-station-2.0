using System.Linq;
using System.Numerics;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Spawners;
using static Robust.Client.Animations.AnimationTrackProperty;

namespace Content.Client._Suziford.Animations;

// suzi-station start: add

/// <summary>
/// Marker component applied to clientside clone entities used for drop animations.
/// </summary>
[RegisterComponent]
[Access(typeof(EntityDropAnimationSystem))]
public sealed partial class EntityDropAnimationComponent : Component
{
    /// <summary>The real item whose sprite is hidden while this clone animates.</summary>
    public EntityUid RealItem;
    /// <summary>The original color to restore on the real item when the animation ends.</summary>
    public Color OrigColor;
}

/// <summary>
/// System that handles animating a clone of an entity being dropped from a player's hand to the ground.
/// Mirrors <see cref="EntityPickupAnimationSystem"/> in the reverse direction.
/// </summary>
public sealed class EntityDropAnimationSystem : EntitySystem
{
    [Dependency] private readonly AnimationPlayerSystem _animations = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    // easeOutCubic: f(t) = 1-(1-t)^3, sampled at t = 0, 0.25, 0.5, 0.75, 1.0
    private static readonly float[] EaseOutFactors = { 0f, 0.578125f, 0.875f, 0.984375f, 1f };

    private const float AnimDuration = 0.25f;
    private const float KeyStep = 0.0625f; // AnimDuration / 4 keyframe intervals
    private const float TiltDegrees = 25f;

    /// <summary>
    /// Tracks active drop animations. Prevents double-trigger and restores the real item's original
    /// color when the hide window expires. Only populated for floor drops (not container inserts).
    /// Key = dropped item uid, Value = (seconds remaining, original Color to restore).
    /// </summary>
    private readonly Dictionary<EntityUid, (float Remaining, Color OrigColor)> _activeDropAnimations = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EntityDropAnimationComponent, AnimationCompletedEvent>(OnDropAnimationCompleted);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_activeDropAnimations.Count == 0)
            return;

        foreach (var uid in _activeDropAnimations.Keys.ToArray())
        {
            var (remaining, origColor) = _activeDropAnimations[uid];
            var next = remaining - frameTime;
            if (next <= 0)
            {
                _activeDropAnimations.Remove(uid);
                // Safety fallback: if OnDropAnimationCompleted somehow never fired,
                // restore the item's color here so it doesn't stay transparent forever.
                if (TryComp(uid, out SpriteComponent? spr) && spr.Color == Color.Transparent)
                    spr.Color = origColor;
            }
            else
            {
                _activeDropAnimations[uid] = (next, origColor);
                // Enforce transparency each frame while the clone is alive.
                if (TryComp(uid, out SpriteComponent? spr))
                    spr.Color = Color.Transparent;
            }
        }
    }

    private void OnDropAnimationCompleted(EntityUid cloneUid, EntityDropAnimationComponent component, AnimationCompletedEvent args)
    {
        // Restore the real item's color the instant the animation ends — eliminates the
        // visible gap (blink) that would occur if we waited for the FrameUpdate timer.
        _activeDropAnimations.Remove(component.RealItem);
        if (TryComp(component.RealItem, out SpriteComponent? spr))
            spr.Color = component.OrigColor;
        Del(cloneUid);
    }

    /// <summary>
    ///     Animates a clone of an entity moving from the hand position to the drop location,
    ///     fading in from 30% to 100% opacity with an easeInCubic movement curve.
    ///     The real item is hidden for the duration so only the clone is visible.
    /// </summary>
    public void AnimateEntityDrop(EntityUid uid, EntityCoordinates initial, Vector2 final, Angle initialAngle)
    {
        if (Deleted(uid) || !initial.IsValid(EntityManager))
            return;

        var metadata = MetaData(uid);

        if (IsPaused(uid, metadata))
            return;

        if (!TryComp(uid, out SpriteComponent? sprite0))
        {
            Log.Error("Entity ({0}) couldn't be animated for drop since it doesn't have a {1}!", metadata.EntityName, nameof(SpriteComponent));
            return;
        }

        // Prevent double-triggering (e.g. local prediction fires, then server event arrives later).
        if (_activeDropAnimations.ContainsKey(uid))
            return;

        // Capture original color before any modifications.
        var origColor = sprite0.Color;

        // An opaque container (ShowContents = false) already hides the sprite — don't double-hide.
        // A transparent surface like a table (ShowContents = true) leaves the sprite visible,
        // so we must hide it ourselves just like a floor/Q-drop.
        var hiddenByOpaqueContainer = _containers.TryGetContainingContainer(uid, out var itemContainer)
            && !itemContainer.ShowContents;

        // Keep entry in the dict slightly past AnimDuration so a late server event is still
        // suppressed by the ContainsKey check above.
        _activeDropAnimations[uid] = (AnimDuration + 0.1f, origColor);

        // Determine tilt direction based on movement direction.
        var moveDir = final - initial.Position;
        float tiltRad;
        if (MathF.Abs(moveDir.X) >= MathF.Abs(moveDir.Y))
            tiltRad = moveDir.X >= 0 ? -(TiltDegrees * MathF.PI / 180f) : (TiltDegrees * MathF.PI / 180f);
        else
            tiltRad = moveDir.Y >= 0 ? (TiltDegrees * MathF.PI / 180f) : -(TiltDegrees * MathF.PI / 180f);
        var tiltDelta = new Angle(tiltRad);

        // Clone starts tilted and returns to the natural angle at the drop point.
        var rotStart = initialAngle + tiltDelta;
        var rotEnd = initialAngle;

        var animatableClone = Spawn("clientsideclone", initial);
        var animComp = EnsureComp<EntityDropAnimationComponent>(animatableClone);
        animComp.RealItem = uid;
        animComp.OrigColor = origColor;
        _metaData.SetEntityName(animatableClone, metadata.EntityName);

        var sprite = Comp<SpriteComponent>(animatableClone);
        // CopySprite must run BEFORE we hide the real item so the clone gets the visible color.
        _sprite.CopySprite((uid, sprite0), (animatableClone, sprite));
        _sprite.SetVisible((animatableClone, sprite), true);

        // Hide the real item UNLESS an opaque container already did it.
        // Floor drop or table (ShowContents=true): hide it so only the clone is visible.
        // Opaque container (backpack, crate): container already hid it, leave it alone.
        if (!hiddenByOpaqueContainer)
            sprite0.Color = Color.Transparent;

        var despawn = EnsureComp<TimedDespawnComponent>(animatableClone);
        despawn.Lifetime = AnimDuration + 0.05f;
        _transform.SetLocalRotationNoLerp(animatableClone, rotStart);

        // Pre-compute easeOutCubic position keyframes.
        var startPos = initial.Position;
        var posKeyFrames = new KeyFrame[EaseOutFactors.Length];
        for (var i = 0; i < EaseOutFactors.Length; i++)
            posKeyFrames[i] = new KeyFrame(Vector2.Lerp(startPos, final, EaseOutFactors[i]), i == 0 ? 0f : KeyStep);

        // Pre-compute easeOutCubic rotation keyframes (clone untwists as it lands).
        var rotKeyFrames = new KeyFrame[EaseOutFactors.Length];
        for (var i = 0; i < EaseOutFactors.Length; i++)
        {
            var angle = new Angle(rotStart.Theta + (rotEnd.Theta - rotStart.Theta) * EaseOutFactors[i]);
            rotKeyFrames[i] = new KeyFrame(angle, i == 0 ? 0f : KeyStep);
        }

        var animations = Comp<AnimationPlayerComponent>(animatableClone);

        _animations.Play(new Entity<AnimationPlayerComponent>(animatableClone, animations), new Animation
        {
            Length = TimeSpan.FromSeconds(AnimDuration),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(TransformComponent),
                    Property = nameof(TransformComponent.LocalPosition),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames = new System.Collections.Generic.List<KeyFrame>(posKeyFrames),
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new KeyFrame(new Color(1f, 1f, 1f, 0.3f), 0f),
                        new KeyFrame(Color.White, AnimDuration),
                    },
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(TransformComponent),
                    Property = nameof(TransformComponent.LocalRotation),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames = new System.Collections.Generic.List<KeyFrame>(rotKeyFrames),
                },
            },
        }, "fancy_drop_anim");
    }
}
// suzi-station end: add
