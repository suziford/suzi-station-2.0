using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using System;

namespace Content.Client._Suziford.Camera;

public sealed class CameraRecoilSystem : EntitySystem
{
    [Dependency] private readonly IEyeManager _eyeManager = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // Получаем текущую активную камеру игрока
        var currentEye = _eyeManager.CurrentEye;
        if (currentEye == null)
            return;

        var query = EntityQueryEnumerator<SuziCameraRecoilComponent, EyeComponent>();
        while (query.MoveNext(out var uid, out var recoil, out var eye))
        {
            // Проверяем, что этот компонент принадлежит именно той камере, в которую мы смотрим
            if (eye.Eye != currentEye)
                continue;

            // Используем .Theta (радианы) для расчетов
            var current = (float) recoil.CurrentRotation.Theta;
            var target = (float) recoil.TargetRotation.Theta;

            // Плавный переход (Lerp)
            var nextRotation = MathHelper.Lerp(current, target, frameTime * recoil.ReturnSpeed);
            recoil.CurrentRotation = new Angle((double) nextRotation);

            // Затухание цели
            var nextTarget = MathHelper.Lerp(target, 0f, frameTime * recoil.ReturnSpeed);
            recoil.TargetRotation = new Angle((double) nextTarget);

            // Применяем поворот напрямую к активному глазу через менеджер
            currentEye.Rotation = recoil.CurrentRotation;
        }
    }

    public void KickCamera(EntityUid uid, Angle amount)
    {
        var recoil = EnsureComp<SuziCameraRecoilComponent>(uid);
        recoil.TargetRotation += amount;
    }
}