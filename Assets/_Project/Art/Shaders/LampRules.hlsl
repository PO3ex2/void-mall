// Правила ламп — точная копия VoidMall.World.LampRules и VoidMall.Core.Hash (xxHash32).
// Любое изменение здесь нужно повторить в C#, иначе реальные источники света разойдутся с эмиссией.
#ifndef VOIDMALL_LAMP_RULES_INCLUDED
#define VOIDMALL_LAMP_RULES_INCLUDED

uint _VM_Seed;
float _VM_LampWave;
float _VM_Skylight;
float _VM_EmergencyWave;
float _VM_Time;

// Ячейки поля аномалий вокруг камеры (x, z — номер макроячейки): там лампы мерцают чаще (FieldFlickerLampChance).
float4 _VM_FieldCells[9];
float _VM_FieldCount;

bool VM_InField(int3 cell)
{
    int2 macro = int2(cell.x >> 6, cell.z >> 6);
    for (int i = 0; i < 9; i++)
    {
        if (i >= (int)_VM_FieldCount)
            break;
        if ((int)_VM_FieldCells[i].x == macro.x && (int)_VM_FieldCells[i].y == macro.y)
            return true;
    }
    return false;
}

uint VM_Rotl(uint v, int c) { return (v << c) | (v >> (32 - c)); }
uint VM_Round(uint h, uint v) { h += v * 3266489917u; return VM_Rotl(h, 17) * 668265263u; }
uint VM_Avalanche(uint h)
{
    h ^= h >> 15;
    h *= 2246822519u;
    h ^= h >> 13;
    h *= 3266489917u;
    h ^= h >> 16;
    return h;
}

uint VM_Hash1(uint seed, int x) { return VM_Avalanche(VM_Round(seed + 374761393u + 4u, (uint)x)); }

uint VM_Hash4(uint seed, int x, int y, int z, int w)
{
    return VM_Avalanche(VM_Round(VM_Round(VM_Round(VM_Round(seed + 374761393u + 16u, (uint)x), (uint)y), (uint)z), (uint)w));
}

float VM_ToFloat01(uint h) { return (float)(h >> 8) * (1.0 / 16777216.0); }

int VM_LevelAt(int y)
{
    if (y < 8) return -1;
    if (y < 18) return (y - 8) / 5;
    if (y < 60) return 2 + (y - 18) / 7;
    return 8;
}

float VM_Flicker(uint lamp, float time, float rate)
{
    int step = (int)floor(time * rate + VM_ToFloat01(lamp) * 7.0);
    return VM_ToFloat01(VM_Hash1(lamp, step)) > 0.35 ? 1.0 : 0.08;
}

float VM_LampIntensity(int3 cell)
{
    int3 group = int3(cell.x >> 6, VM_LevelAt(cell.y), cell.z >> 6);
    if (VM_ToFloat01(VM_Hash4(_VM_Seed, group.x, group.y, group.z, 901)) < 0.12)
        return 0;
    uint lamp = VM_Hash4(_VM_Seed, cell.x, cell.y, cell.z, 902);
    float roll = (float)(lamp % 1000u) / 1000.0;
    if (roll < 0.05)
        return 0;

    float phase = VM_ToFloat01(VM_Hash4(_VM_Seed, group.x, group.y, group.z, 900));
    float margin = _VM_LampWave - phase;
    if (margin > 0.03)
        return 0;
    float on = 1;
    if (margin > -0.03)
        on = VM_Flicker(lamp, _VM_Time, 14.0);
    if (roll < 0.05 + (VM_InField(cell) ? 0.35 : 0.08))
        on *= VM_Flicker(lamp ^ 0x9E3779B9u, _VM_Time, 7.0);
    return on;
}

// Аварийная лампа: фаза отключения своя у каждой (полночь), рядом с фазой — мигает.
float VM_EmergencyIntensity(int3 cell)
{
    uint lamp = VM_Hash4(_VM_Seed, cell.x, cell.y, cell.z, 903);
    float phase = 0.06 + VM_ToFloat01(lamp) * (1.0 - 2.0 * 0.06);
    float margin = _VM_EmergencyWave - phase;
    if (margin > 0.06)
        return 0;
    return margin > -0.06 ? VM_Flicker(lamp, _VM_Time, 14.0) : 1.0;
}

// kind: 1 — лампа, 2 — аварийная, 3 — световой фонарь, 4 — экран «неба» оазиса.
float VM_LampKindIntensity(int3 cell, int kind)
{
    if (kind == 1) return VM_LampIntensity(cell);
    if (kind == 2) return VM_EmergencyIntensity(cell);
    if (kind == 3 || kind == 4) return _VM_Skylight;
    return 0;
}

#endif
