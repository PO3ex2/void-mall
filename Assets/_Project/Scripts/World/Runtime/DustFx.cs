using UnityEngine;

namespace VoidMall.World
{
    /// <summary>Облако пыли: разовый выброс частиц, оседающих на мир (обрушения, поломка пропов).</summary>
    public static class DustFx
    {
        public static void Burst(Material material, Bounds bounds, int count, Color from, Color to)
        {
            if (material == null)
                return;
            var go = new GameObject("Dust");
            go.transform.position = bounds.center;
            var particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = particles.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.35f);
            main.startColor = new ParticleSystem.MinMaxGradient(from, to);
            main.gravityModifier = 0.6f;
            main.maxParticles = 600;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(count, 10, 500)) });

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = bounds.size;

            var collision = particles.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.dampen = 0.6f;
            collision.bounce = 0.1f;

            var sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            particles.Play();
        }
    }
}
