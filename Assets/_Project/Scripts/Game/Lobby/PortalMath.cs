using UnityEngine;

namespace VoidMall.Game
{
    /// <summary>
    /// Геометрия портала (этап 10). У каждого проёма «вперёд» — направление прохода: вошёл в проём A по его forward —
    /// вышел из проёма B по его forward. Поза переносится как «относительно A» → «относительно B».
    /// </summary>
    public static class PortalMath
    {
        public static Vector3 MapPoint(Vector3 point, Vector3 fromPos, Quaternion fromRot, Vector3 toPos, Quaternion toRot) =>
            toPos + toRot * (Quaternion.Inverse(fromRot) * (point - fromPos));

        public static Vector3 MapDirection(Vector3 direction, Quaternion fromRot, Quaternion toRot) =>
            toRot * (Quaternion.Inverse(fromRot) * direction);

        public static Quaternion MapRotation(Quaternion rotation, Quaternion fromRot, Quaternion toRot) =>
            toRot * (Quaternion.Inverse(fromRot) * rotation);

        /// <summary>Положение точки относительно проёма: x — вбок, y — вверх от низа, z — вдоль прохода (минус — до плоскости).</summary>
        public static Vector3 Local(Vector3 point, Vector3 portalPos, Quaternion portalRot) =>
            Quaternion.Inverse(portalRot) * (point - portalPos);

        /// <summary>Перешёл ли через плоскость проёма (до — перед ним, после — за ним) в пределах проёма шириной width и высотой height.</summary>
        public static bool Crossed(Vector3 before, Vector3 after, Vector3 portalPos, Quaternion portalRot, float width, float height)
        {
            var a = Local(before, portalPos, portalRot);
            var b = Local(after, portalPos, portalRot);
            if (!(a.z < 0f && b.z >= 0f))
                return false;
            // Где отрезок пересёк плоскость — там и проверяем проём.
            float t = a.z / (a.z - b.z);
            var hit = Vector3.Lerp(a, b, t);
            return Mathf.Abs(hit.x) <= width * 0.5f && hit.y >= -0.5f && hit.y <= height;
        }

        /// <summary>
        /// Плоскость отсечения для камеры портала (в координатах камеры): всё между камерой и проёмом выхода отрезается
        /// наклонной ближней плоскостью, иначе стены за проёмом закрыли бы вид.
        /// </summary>
        public static Vector4 ClipPlane(Matrix4x4 worldToCamera, Vector3 planePos, Vector3 planeNormal, Vector3 cameraPos, float offset = 0.02f)
        {
            float side = Mathf.Sign(Vector3.Dot(planeNormal, planePos - cameraPos));
            Vector3 position = worldToCamera.MultiplyPoint(planePos);
            Vector3 normal = worldToCamera.MultiplyVector(planeNormal).normalized * side;
            float distance = -Vector3.Dot(position, normal) + offset;
            return new Vector4(normal.x, normal.y, normal.z, distance);
        }
    }
}
