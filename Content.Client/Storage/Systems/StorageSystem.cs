// SPDX-FileCopyrightText: 2022 Fishfish458 <47410468+Fishfish458@users.noreply.github.com>
// SPDX-FileCopyrightText: 2022 fishfish458 <fishfish458>
// SPDX-FileCopyrightText: 2022 metalgearsloth <comedian_vs_clown@hotmail.com>
// SPDX-FileCopyrightText: 2022 mirrorcult <lunarautomaton6@gmail.com>
// SPDX-FileCopyrightText: 2023 DrSmugleaf <DrSmugleaf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 Leon Friedrich <60421075+ElectroJr@users.noreply.github.com>
// SPDX-FileCopyrightText: 2023 Nemanja <98561806+EmoGarbage404@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 DrSmugleaf <10968691+DrSmugleaf@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 Plykiya <58439124+Plykiya@users.noreply.github.com>
// SPDX-FileCopyrightText: 2024 eoineoineoin <github@eoinrul.es>
// SPDX-FileCopyrightText: 2024 plykiya <plykiya@protonmail.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Errant <35878406+Errant-4@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 metalgearsloth <31366439+metalgearsloth@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 pathetic meowmeow <uhhadd@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Numerics;
using Content.Client._Suziford.Animations;
using Content.Client.Animations;
using Content.Shared._Suziford.Hands;
using Content.Shared.Hands;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Client.Player;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client.Storage.Systems;

public sealed class StorageSystem : SharedStorageSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly EntityPickupAnimationSystem _entityPickupAnimation = default!;
    [Dependency] private readonly EntityDropAnimationSystem _entityDropAnimation = default!; // suzi-station add

    private Dictionary<EntityUid, ItemStorageLocation> _oldStoredItems = new();

    // suzi-station start: add
    // Track storages whose state we have already seen at least once.
    // On the FIRST ComponentHandleState call _oldStoredItems is always empty,
    // so every item in the storage looks "newly inserted" — causing spurious
    // drop animations for all pre-existing contents (e.g. belt on spawn,
    // picking up a backpack from the floor).
    // Cleared in Shutdown() so stale UIDs never carry over to a new session.
    private readonly HashSet<EntityUid> _seenStorages = new();

    // suzi-station end: add

    private List<(StorageBoundUserInterface Bui, bool Value)> _queuedBuis = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StorageComponent, ComponentHandleState>(OnStorageHandleState);
        SubscribeNetworkEvent<PickupAnimationEvent>(HandlePickupAnimation);
        SubscribeAllEvent<DropAnimationEvent>(HandleDropAnimation); // suzi-station add
        SubscribeAllEvent<AnimateInsertingEntitiesEvent>(HandleAnimatingInsertingEntities);
    }

    private void OnStorageHandleState(EntityUid uid, StorageComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not StorageComponentState state)
            return;

        component.Grid.Clear();
        component.Grid.AddRange(state.Grid);
        component.MaxItemSize = state.MaxItemSize;
        component.Whitelist = state.Whitelist;
        component.Blacklist = state.Blacklist;

        _oldStoredItems.Clear();

        foreach (var item in component.StoredItems)
        {
            _oldStoredItems.Add(item.Key, item.Value);
        }

        component.StoredItems.Clear();

        foreach (var (nent, location) in state.StoredItems)
        {
            var ent = EnsureEntity<StorageComponent>(nent, uid);
            component.StoredItems[ent] = location;
        }

        component.SavedLocations.Clear();

        foreach (var loc in state.SavedLocations)
        {
            component.SavedLocations[loc.Key] = new(loc.Value);
        }

        UpdateOccupied((uid, component));

        // suzi-station start: add
        // Animate items that just appeared in this storage and originated from the local player.
        // This state-based trigger is the reliable fallback: it fires regardless of whether
        // client prediction of the insertion ran, bypassing all IsFirstTimePredicted / PVS issues.
        //
        // Guard: skip animation on the FIRST state reception for this storage.
        // At that point _oldStoredItems is always empty, so every item in the
        // storage would look "newly inserted" and receive a spurious drop animation
        // (e.g. all belt contents on spawn, all backpack contents when picked up).
        // _seenStorages is cleared in Shutdown() so it never carries stale UIDs
        // into a new session (which would make isFirstSeen incorrectly false).
        var isFirstSeen = _seenStorages.Add(uid);

        if (!isFirstSeen)
        {
            var localPlayer = _player.LocalEntity;
            if (localPlayer != null && Exists(localPlayer.Value))
            {
                var storageCoords = Transform(uid).Coordinates;
                var playerCoords = Transform(localPlayer.Value).Coordinates;

                if (Exists(playerCoords.EntityId) && Exists(storageCoords.EntityId) &&
                    !TransformSystem.InRange(storageCoords, playerCoords, 0.1f) &&
                    TransformSystem.InRange(storageCoords, playerCoords, 3.0f))
                {
                    var finalMapPos = TransformSystem.ToMapCoordinates(storageCoords).Position;
                    var finalPos = Vector2.Transform(finalMapPos, TransformSystem.GetInvWorldMatrix(playerCoords.EntityId));

                    foreach (var (ent, _) in component.StoredItems)
                    {
                        if (_oldStoredItems.ContainsKey(ent) || !Exists(ent))
                            continue;

                        // Skip items currently being picked up (pickup animation running).
                        // The server sends one tick of intermediate state before processing
                        // the pickup command, making the item look "newly inserted".
                        if (_entityPickupAnimation.IsPickingUp(ent))
                            continue;

                        _entityDropAnimation.AnimateEntityDrop(ent, playerCoords, finalPos, Transform(ent).LocalRotation);
                    }
                }
            }
        }
        // suzi-station end: add

        var uiDirty = !component.StoredItems.SequenceEqual(_oldStoredItems);

        if (uiDirty && UI.TryGetOpenUi<StorageBoundUserInterface>(uid, StorageComponent.StorageUiKey.Key, out var storageBui))
        {
            storageBui.Refresh();
            // Make sure nesting still updated.
            var player = _player.LocalEntity;

            if (NestedStorage && player != null && ContainerSystem.TryGetContainingContainer((uid, null, null), out var container) &&
                UI.TryGetOpenUi<StorageBoundUserInterface>(container.Owner, StorageComponent.StorageUiKey.Key, out var containerBui))
            {
                _queuedBuis.Add((containerBui, false));
            }
        }
    }

    public override void UpdateUI(Entity<StorageComponent?> entity)
    {
        if (UI.TryGetOpenUi<StorageBoundUserInterface>(entity.Owner, StorageComponent.StorageUiKey.Key, out var sBui))
        {
            sBui.Refresh();
        }
    }

    // suzi-station start: add
    public override void Shutdown()
    {
        base.Shutdown();
        _seenStorages.Clear();
    }
    // suzi-station end: add

    protected override void HideStorageWindow(EntityUid uid, EntityUid actor)
    {
        if (UI.TryGetOpenUi<StorageBoundUserInterface>(uid, StorageComponent.StorageUiKey.Key, out var storageBui))
        {
            _queuedBuis.Add((storageBui, false));
        }
    }

    // suzi-station start: add
    private void HandleDropAnimation(DropAnimationEvent msg)
    {
        // Call AnimateEntityDrop directly (no IsFirstTimePredicted guard) so server events
        // reach non-predicting clients. Mirrors how HandlePickupAnimation works.
        // Double-animation for predicting clients is prevented by the ContainsKey cooldown
        // inside AnimateEntityDrop itself.
        var item = GetEntity(msg.ItemUid);
        var initialCoords = GetCoordinates(msg.InitialPosition);
        var finalCoords = GetCoordinates(msg.FinalPosition);

        if (!Exists(initialCoords.EntityId) || !Exists(finalCoords.EntityId))
            return;

        if (TransformSystem.InRange(finalCoords, initialCoords, 0.1f))
            return;

        var finalMapPos = TransformSystem.ToMapCoordinates(finalCoords).Position;
        var finalPos = Vector2.Transform(finalMapPos, TransformSystem.GetInvWorldMatrix(initialCoords.EntityId));
        _entityDropAnimation.AnimateEntityDrop(item, initialCoords, finalPos, msg.InitialAngle);
    }

    public override void PlayDropAnimation(EntityUid uid, EntityCoordinates initialCoordinates, EntityCoordinates finalCoordinates,
        Angle initialRotation, EntityUid? user = null)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        DropAnimation(uid, initialCoordinates, finalCoordinates, initialRotation);
    }

    public void DropAnimation(EntityUid item, EntityCoordinates initialCoords, EntityCoordinates finalCoords, Angle initialAngle)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (TransformSystem.InRange(finalCoords, initialCoords, 0.1f) ||
            !Exists(initialCoords.EntityId) || !Exists(finalCoords.EntityId))
        {
            return;
        }

        var finalMapPos = TransformSystem.ToMapCoordinates(finalCoords).Position;
        var finalPos = Vector2.Transform(finalMapPos, TransformSystem.GetInvWorldMatrix(initialCoords.EntityId));

        _entityDropAnimation.AnimateEntityDrop(item, initialCoords, finalPos, initialAngle);
    }
    // suzi-station end: add

    protected override void ShowStorageWindow(EntityUid uid, EntityUid actor)
    {
        if (UI.TryGetOpenUi<StorageBoundUserInterface>(uid, StorageComponent.StorageUiKey.Key, out var storageBui))
        {
            _queuedBuis.Add((storageBui, true));
        }
    }

    /// <inheritdoc />
    public override void PlayPickupAnimation(EntityUid uid, EntityCoordinates initialCoordinates, EntityCoordinates finalCoordinates,
        Angle initialRotation, EntityUid? user = null)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        PickupAnimation(uid, initialCoordinates, finalCoordinates, initialRotation);
    }

    private void HandlePickupAnimation(PickupAnimationEvent msg)
    {
        PickupAnimation(GetEntity(msg.ItemUid), GetCoordinates(msg.InitialPosition), GetCoordinates(msg.FinalPosition), msg.InitialAngle);
    }

    public void PickupAnimation(EntityUid item, EntityCoordinates initialCoords, EntityCoordinates finalCoords, Angle initialAngle)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (TransformSystem.InRange(finalCoords, initialCoords, 0.1f) ||
            !Exists(initialCoords.EntityId) || !Exists(finalCoords.EntityId))
        {
            return;
        }

        var finalMapPos = TransformSystem.ToMapCoordinates(finalCoords).Position;
        var finalPos = Vector2.Transform(finalMapPos, TransformSystem.GetInvWorldMatrix(initialCoords.EntityId));

        _entityPickupAnimation.AnimateEntityPickup(item, initialCoords, finalPos, initialAngle);
    }

    /// <summary>
    /// Animate the newly stored entities in <paramref name="msg"/> flying towards this storage's position
    /// </summary>
    /// <param name="msg"></param>
    public void HandleAnimatingInsertingEntities(AnimateInsertingEntitiesEvent msg)
    {
        TryComp(GetEntity(msg.Storage), out TransformComponent? transformComp);

        for (var i = 0; msg.StoredEntities.Count > i; i++)
        {
            var entity = GetEntity(msg.StoredEntities[i]);

            var initialPosition = msg.EntityPositions[i];
            if (Exists(entity) && transformComp != null)
            {
                _entityPickupAnimation.AnimateEntityPickup(entity, GetCoordinates(initialPosition), transformComp.LocalPosition, msg.EntityAngles[i]);
            }
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
        {
            return;
        }

        // This update loop exists just to synchronize with UISystem and avoid 1-tick delays.
        // If deferred opens / closes ever get removed you can dump this.
        foreach (var (bui, open) in _queuedBuis)
        {
            if (open)
            {
                bui.Show();
            }
            else
            {
                bui.Hide();
            }
        }

        _queuedBuis.Clear();
    }
}