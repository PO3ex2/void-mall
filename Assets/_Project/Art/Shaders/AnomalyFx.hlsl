// Эффекты аномалий-маркеров для шейдеров блоков и пропов: «Искажение» и пульсация (смещение вершин),
// «Смотри на меня» (лёгкая рябь вокруг вспышки), «Глитч» (подмена текстур, полосы).
// Объёмы задаёт AnomalyFxDirector (ближайшие к камере, до VM_MAX_ANOMALIES).
#ifndef VOIDMALL_ANOMALY_FX_INCLUDED
#define VOIDMALL_ANOMALY_FX_INCLUDED

#include "LampRules.hlsl"

#define VM_MAX_ANOMALIES 8

// Период пульсирующего искажения — как AnomalyPhysics.PulsePeriod в C#.
#define VM_PULSE_PERIOD 3.0

float4 _VM_Anomalies[VM_MAX_ANOMALIES];       // xyz — центр, w — радиус
float4 _VM_AnomalyParams[VM_MAX_ANOMALIES];   // x — вид (AnomalyMarkerKind), y — сила
float _VM_AnomalyCount;
float _VM_LayerCount;

float VM_AnomalyFalloff(float3 positionWS, int i)
{
    float4 volume = _VM_Anomalies[i];
    float f = saturate(1.0 - distance(positionWS, volume.xyz) / max(volume.w, 0.001));
    return f * f * (3.0 - 2.0 * f);
}

// Фаза пульсации: > 0 — тянет к центру (стены сжимаются внутрь), < 0 — отталкивает.
float VM_PulsePhase()
{
    return sin(_VM_Time * 6.2831853 / VM_PULSE_PERIOD);
}

// «Искажение»: стены плывут — плавное смещение вершин в мировых координатах (без швов между чанками).
// Пульсация: вершины «дышат» к центру и от него. «Смотри на меня»: мелкая рябь.
float3 VM_Distort(float3 positionWS)
{
    float3 offset = 0;
    int count = (int)_VM_AnomalyCount;
    [loop] for (int i = 0; i < count; i++)
    {
        float kind = _VM_AnomalyParams[i].x;
        if (kind == 2.0 || kind == 5.0)
            continue;
        float f = VM_AnomalyFalloff(positionWS, i) * _VM_AnomalyParams[i].y;
        if (f <= 0)
            continue;
        float3 p = positionWS;
        float t = _VM_Time;
        if (kind == 1.0)
        {
            offset += 0.45 * f * float3(
                sin(p.y * 0.9 + p.z * 0.45 + t * 1.3),
                sin(p.x * 0.7 + p.z * 0.3 + t * 0.9) * 0.5,
                cos(p.y * 0.8 + p.x * 0.5 + t * 1.1));
        }
        else if (kind == 3.0)
        {
            // Мягко: у центра почти не сдвигаем (иначе крупные грани мебели стягиваются в точку и рвутся в осколки).
            float3 toCenter = _VM_Anomalies[i].xyz - p;
            float len = max(length(toCenter), 0.001);
            offset += toCenter / len * f * VM_PulsePhase() * 0.22 * saturate(len / 2.0);
        }
        else if (kind == 4.0)
        {
            offset += 0.08 * f * float3(
                sin(p.y * 3.1 + t * 4.0),
                sin(p.x * 2.7 + p.z * 2.3 + t * 3.3),
                cos(p.y * 2.9 + t * 3.7));
        }
    }
    return positionWS + offset;
}

// «Глитч»: сила (0…1) в точке.
float VM_Glitch(float3 positionWS)
{
    float g = 0;
    int count = (int)_VM_AnomalyCount;
    [loop] for (int i = 0; i < count; i++)
    {
        if (_VM_AnomalyParams[i].x != 2.0)
            continue;
        g += VM_AnomalyFalloff(positionWS, i) * _VM_AnomalyParams[i].y;
    }
    return saturate(g);
}

#endif
