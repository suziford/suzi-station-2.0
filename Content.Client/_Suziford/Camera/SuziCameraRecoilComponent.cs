using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Client._Suziford.Camera;

[RegisterComponent]
public sealed partial class SuziCameraRecoilComponent : Component
{
    // Текущий поворот (значение x на графике)
    public Angle CurrentRotation = Angle.Zero;

    // --- ПОЛЯ ДЛЯ ФИЗИКИ ПРУЖИНЫ ---

    // Текущая угловая скорость пружины (радиан/сек)
    public double Velocity = 0f;

    // "Жёсткость" пружины. Чем выше, тем резче и быстрее тряска.
    // Рекомендуемые значения: 150 - 400.
    public float Stiffness = 300f;

    // "Затухание" (трение). Чем ниже, тем больше камера будет раскачиваться перед остановкой.
    // 1.0 - критическое затухание (без осцилляции). Ставь 0.3 - 0.6 для эффекта "как на картинке".
    public float DampingRatio = 0.30f;
}