// Content.Client/Camera/CameraRecoilComponent.cs
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.Client._Suziford.Camera
{
    [RegisterComponent]
    public sealed partial class SuziCameraRecoilComponent : Component
    {
        // Текущий угол наклона
        public Angle CurrentRotation = Angle.Zero;

        // Целевой угол, к которому стремимся при выстреле
        public Angle TargetRotation = Angle.Zero;

        // Скорость возврата камеры в исходное состояние
        public float ReturnSpeed = 5f;
    }
}