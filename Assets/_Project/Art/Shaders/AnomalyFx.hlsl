// Эффекты аномалий-маркеров («Искажение» и «Глитч») для шейдера блоков.
// Объёмы задаёт AnomalyFxDirector (ближайшие к камере, до VM_MAX_ANOMALIES).
#ifndef VOIDMALL_ANOMALY_FX_INCLUDED
#define VOIDMALL_ANOMALY_FX_INCLUDED

#include "LampRules.hlsl"

#define VM_MAX_ANOMALIES 8

float4 _VM_Anomalies[VM_MAX_ANOMALIES];       // xyz — центр, w — радиус
float4 _VM_AnomalyParams[VM_MAX_ANOMALIES];   // x — вид (1 искажение, 2 глитч), y — сила
float _VM_AnomalyCount;
float _VM_LayerCount;

float VM_AnomalyFalloff(float3 positionWS, int i)
{
    float4 volume = _VM_Anomalies[i];
    float f = saturate(1.0 - distance(positionWS, volume.xyz) / max(volume.w, 0.001));
    return f * f * (3.0 - 2.0 * f);
}

// «Искажение»: стены плывут — плавное смещение вершин в мировых координатах (без швов между чанками).
float3 VM_Distort(float3 positionWS)
{
    float3 offset = 0;
    int count = (int)_VM_AnomalyCount;
    [loop] for (int i = 0; i < count; i++)
    {
        if (_VM_AnomalyParams[i].x != 1.0)
            continue;
        float f = VM_AnomalyFalloff(positionWS, i) * _VM_AnomalyParams[i].y;
        if (f <= 0)
            continue;
        float3 p = positionWS;
        float t = _VM_Time;
        offset += f * float3(
            sin(p.y * 0.9 + p.z * 0.45 + t * 1.3),
            sin(p.x * 0.7 + p.z * 0.3 + t * 0.9) * 0.5,
            cos(p.y * 0.8 + p.x * 0.5 + t * 1.1));
    }
    return positionWS + offset * 0.45;
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
