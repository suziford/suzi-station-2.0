using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Suziford.Hands;

// suzi-station start: add
/// <summary>
/// Plays a clientside drop animation by copying the specified entity.
/// Mirrors <see cref="Content.Shared.Hands.PickupAnimationEvent"/> for the drop direction.
/// </summary>
[Serializable, NetSerializable]
public sealed class DropAnimationEvent : EntityEventArgs
{
    /// <summary>
    /// Entity to be copied for the clientside animation.
    /// </summary>
    public readonly NetEntity ItemUid;

    /// <summary>Initial position — approximately the user's hand (where the item was held).</summary>
    public readonly NetCoordinates InitialPosition;

    /// <summary>Final position — where the item lands on the ground.</summary>
    public readonly NetCoordinates FinalPosition;

    public readonly Angle InitialAngle;

    public DropAnimationEvent(
        NetEntity itemUid,
        NetCoordinates initialPosition,
        NetCoordinates finalPosition,
        Angle initialAngle)
    {
        ItemUid = itemUid;
        InitialPosition = initialPosition;
        FinalPosition = finalPosition;
        InitialAngle = initialAngle;
    }
}
// suzi-station end: add
