# Void Mall

Песочница-выживание от первого лица в бесконечном лиминальном торговом центре.
Блоки и воксели, разрушаемый мир, физика в духе Source, крафт, кооператив (позже).

- Движок: Unity 6000.6.4f1, URP
- Документы: [Game Design](Assets/GAME_DESIGN.md) · [Architecture](Assets/ARCHITECTURE.md) · [Roadmap](Assets/ROADMAP.md) · [Исходное описание](Assets/GAME_DESCRIPTION.md)

## Клонирование
Нужен [Git LFS](https://git-lfs.com):
```
git lfs install
git clone https://github.com/PO3ex2/void-mall.git
```

## Запуск полигона контроллера
1. Открыть сцену `Assets/_Project/Scenes/Sandbox_Controller.unity` и нажать Play.
2. Если ассеты потерялись или нужно пересобрать полигон — меню **Void Mall → Setup → All**.

| Действие | Клавиатура и мышь | Геймпад |
|---|---|---|
| Движение / взгляд | WASD / мышь | левый / правый стик |
| Прыжок | Space | A |
| Присесть | Ctrl или C | B |
| Спринт / медленный шаг | Shift / Alt | L3 / — |
| Консоль | `` ` `` (Ё) | — |
| HUD движения / панель веса и травм | F3 / F4 | — |

Полезные команды консоли: `help`, `noclip`, `fly`, `tp x y z`, `getpos`, `time_scale 0.2`, `fov 90`, `sensitivity 0.1`.

## Полигон мира: мол
Сцена `Assets/_Project/Scenes/Sandbox_World.unity` (собирается меню **Void Mall → Setup → Build World Sandbox**): бесконечный мол — парковки P2/P1, 5 торговых этажей, технический этаж. Игрок появляется в галерее первого этажа.

| Действие | Клавиши |
|---|---|
| Инструмент: молоток / бур | 1 / 2 |
| Молоток: ломать (удержание) / поставить блок | ЛКМ / ПКМ |
| Бур: срезать / нарастить гладкий массив | ЛКМ / ПКМ (удержание) |
| Выбрать блок или материал / взять под прицелом | Колесо / СКМ |
| HUD (позиция, этаж, раздел, время, освещённость) | F3 |
| Карта этажа | F6 |

Команды мола: `where` (где я), `anomalies` (ближайшие аномалии), `goto_anomaly abyss|pool|cutout|distortion|glitch`, `time 22.5` (время суток, ночью свет гаснет), `time_daylength 60`, `time_frozen 1`, `seed 42` (пересоздать мир), `regen_chunk`, `world_props 0`, `world_prop_radius 80`, `r_lamplights 40`, `r_lampshadows 2`, `fx_anomaly_strength 1`.

Команды мира: `blocks`, `setblock x y z материал`, `fill x1 y1 z1 x2 y2 z2 материал`, `world_info`, `r_viewdistance 8`, `tool_damage 1000`, `drill_radius 2`, `drill_strength 1`, `debug_mining_log 0`.

Консоль: Tab дополняет команды и аргументы (материалы, виды аномалий, 0/1). Настройки (чувствительность, FOV, графика) сохраняются в `config.cfg` в папке данных игры, свои команды при старте — в `autoexec.cfg`; `exec файл`, `host_writeconfig`.

Подрубленная конструкция, которая больше ни на что не опирается, падает обломком. Бездна прорезает этажи до пустоты под миром: упал ниже −40 м — погиб.
