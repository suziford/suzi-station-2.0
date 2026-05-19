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

        // Получаем активную камеру
        var currentEye = _eyeManager.CurrentEye;
        if (currentEye == null)
            return;

        var query = EntityQueryEnumerator<SuziCameraRecoilComponent, EyeComponent>();
        while (query.MoveNext(out var uid, out var recoil, out var eye))
        {
            if (eye.Eye != currentEye)
                continue;

            // --- МАТЕМАТИКА ПРУЖИНЫ ---
            
            // Текущий угол (значение отклонения от нуля)
            double theta = recoil.CurrentRotation.Theta;

            // 1. Сила пружины (сила возврата к нулю): Force = -K * x
            double springForce = -recoil.Stiffness * theta;

            // 2. Сила затухания (сила трения): Force = -D * Velocity
            // Вычисляем коэффициент демпфирования на основе DampingRatio
            double dampFactor = 2f * recoil.DampingRatio * Math.Sqrt(recoil.Stiffness);
            double dampingForce = -dampFactor * recoil.Velocity;

            // 3. Ускорение (сила = масса * ускорение. Считаем массу = 1)
            double acceleration = springForce + dampingForce;

            // 4. Обновляем скорость и позицию
            recoil.Velocity += acceleration * frameTime;
            theta += recoil.Velocity * frameTime;

            // --- КОНЕЦ МАТЕМАТИКИ ---

            // Стабилизация: если колебания стали мизерными, принудительно обнуляем
            if (Math.Abs(theta) < 0.0001 && Math.Abs(recoil.Velocity) < 0.01)
            {
                theta = 0;
                recoil.Velocity = 0;
            }

            // Применяем поворот к активному глазу
            recoil.CurrentRotation = new Angle(theta);
            currentEye.Rotation = recoil.CurrentRotation;
        }
    }

    public void KickCamera(EntityUid uid, Angle amount)
    {
        var recoil = EnsureComp<SuziCameraRecoilComponent>(uid);
        // "Удар" при выстреле теперь мгновенно меняет СКОРОСТЬ пружины, а не угол.
        // Это дает резкий старт и плавное затухание, как на графике (в начале кривая крутая).
        recoil.Velocity += amount.Theta * 12.0f; // 12.0f — множитель "пинка"
    }
}