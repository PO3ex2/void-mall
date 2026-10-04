using System;
using System.Collections.Generic;

namespace VoidMall.Items
{
    /// <summary>Что нужно сетке о предметах (реализуют ItemRegistry и тестовые заглушки).</summary>
    public interface IItemCatalog
    {
        /// <summary>Размер в клетках (ширина, высота) без поворота.</summary>
        (int width, int height) GridSize(ushort def);

        int MaxStack(ushort def);

        float Weight(ushort def);
    }

    /// <summary>
    /// Стак предметов: описание, количество, прочность, у контейнеров — содержимое.
    /// Ссылочный тип: хотбар хранит ссылку на тот же стак, что лежит в сетке.
    /// </summary>
    public sealed class ItemStack
    {
        private static long _nextId = 1;

        public ushort Def;
        public int Count;

        /// <summary>Прочность 0…1 (инструменты, броня).</summary>
        public float Durability = 1f;

        /// <summary>Содержимое контейнера (рюкзак). Null — не контейнер.</summary>
        public GridInventory Contents;

        /// <summary>Испорченная еда (просрочка): отравляет. Со свежей в один стак не складывается.</summary>
        public bool Spoiled;

        public long Id { get; } = _nextId++;

        public ItemStack(ushort def, int count = 1)
        {
            Def = def;
            Count = count;
        }

        public override string ToString() => $"ItemStack(def {Def} ×{Count})";
    }

    /// <summary>Предмет в сетке: левый верхний угол и поворот на 90°.</summary>
    public struct GridPlacement
    {
        public ItemStack Stack;
        public int X, Y;
        public bool Rotated;
    }

    /// <summary>
    /// Клеточный инвентарь (GDD §5.1, как в S.T.A.L.K.E.R.): предмет занимает W×H клеток, поворачивается,
    /// одинаковые предметы складываются в стаки. Чистая логика без Unity (EditMode-тесты).
    /// </summary>
    public sealed class GridInventory
    {
        private readonly IItemCatalog _catalog;
        private readonly List<GridPlacement> _items = new();
        private ItemStack[] _cells;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public IReadOnlyList<GridPlacement> Items => _items;

        /// <summary>Содержимое изменилось.</summary>
        public event Action Changed;

        public GridInventory(IItemCatalog catalog, int width, int height)
        {
            _catalog = catalog;
            Width = width;
            Height = height;
            _cells = new ItemStack[width * height];
        }

        public (int width, int height) SizeOf(ItemStack stack, bool rotated)
        {
            var (w, h) = _catalog.GridSize(stack.Def);
            return rotated ? (h, w) : (w, h);
        }

        public ItemStack GetAt(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height ? null : _cells[x + y * Width];

        public bool TryGetPlacement(ItemStack stack, out GridPlacement placement)
        {
            foreach (var item in _items)
            {
                if (item.Stack == stack)
                {
                    placement = item;
                    return true;
                }
            }
            placement = default;
            return false;
        }

        public bool Contains(ItemStack stack) => TryGetPlacement(stack, out _);

        /// <summary>Поместится ли стак в точке (клетки, занятые самим стаком, считаются свободными).</summary>
        public bool CanPlace(ItemStack stack, int x, int y, bool rotated)
        {
            var (w, h) = SizeOf(stack, rotated);
            if (x < 0 || y < 0 || x + w > Width || y + h > Height)
                return false;
            for (int cy = y; cy < y + h; cy++)
            for (int cx = x; cx < x + w; cx++)
            {
                var other = _cells[cx + cy * Width];
                if (other != null && other != stack)
                    return false;
            }
            return true;
        }

        public bool TryPlace(ItemStack stack, int x, int y, bool rotated)
        {
            if (stack == null || stack.Count <= 0 || Contains(stack) || !CanPlace(stack, x, y, rotated))
                return false;
            Occupy(stack, x, y, rotated);
            _items.Add(new GridPlacement { Stack = stack, X = x, Y = y, Rotated = rotated });
            Changed?.Invoke();
            return true;
        }

        /// <summary>Переложить стак внутри сетки (с поворотом).</summary>
        public bool TryMove(ItemStack stack, int x, int y, bool rotated)
        {
            if (!TryGetPlacement(stack, out var old) || !CanPlace(stack, x, y, rotated))
                return false;
            Free(old);
            _items.Remove(old);
            Occupy(stack, x, y, rotated);
            _items.Add(new GridPlacement { Stack = stack, X = x, Y = y, Rotated = rotated });
            Changed?.Invoke();
            return true;
        }

        /// <summary>Первое свободное место (сначала без поворота, потом с поворотом), сверху вниз и слева направо.</summary>
        public bool FindSpace(ItemStack stack, out int x, out int y, out bool rotated)
        {
            for (int r = 0; r < 2; r++)
            {
                rotated = r == 1;
                var (w, h) = SizeOf(stack, rotated);
                if (rotated && w == h)
                    break;
                for (y = 0; y + h <= Height; y++)
                for (x = 0; x + w <= Width; x++)
                    if (CanPlace(stack, x, y, rotated))
                        return true;
            }
            x = y = 0;
            rotated = false;
            return false;
        }

        public bool Remove(ItemStack stack)
        {
            if (!TryGetPlacement(stack, out var placement))
                return false;
            Free(placement);
            _items.Remove(placement);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Добавить предметы: сначала доложить в неполные стаки того же вида, затем положить сам стак на свободное место
        /// (если он не больше максимального), остаток — новыми стаками. Возвращает, сколько не поместилось;
        /// у переданного стака остаётся именно этот остаток (0 — всё разложено или стак лёг целиком).
        /// </summary>
        public int TryAdd(ItemStack stack)
        {
            int max = _catalog.MaxStack(stack.Def);
            if (max > 1 && stack.Contents == null)
            {
                foreach (var item in _items)
                {
                    if (stack.Count == 0)
                        break;
                    if (item.Stack == stack || !CanStack(item.Stack, stack) || item.Stack.Count >= max)
                        continue;
                    int moved = Math.Min(max - item.Stack.Count, stack.Count);
                    item.Stack.Count += moved;
                    stack.Count -= moved;
                }
            }
            if (stack.Count > 0 && stack.Count <= max && !Contains(stack) && FindSpace(stack, out int sx, out int sy, out bool sr))
            {
                Occupy(stack, sx, sy, sr);
                _items.Add(new GridPlacement { Stack = stack, X = sx, Y = sy, Rotated = sr });
                Changed?.Invoke();
                return 0;
            }
            while (stack.Count > 0)
            {
                var part = new ItemStack(stack.Def, Math.Min(stack.Count, max)) { Durability = stack.Durability, Spoiled = stack.Spoiled };
                if (!FindSpace(part, out int x, out int y, out bool rotated))
                    break;
                Occupy(part, x, y, rotated);
                _items.Add(new GridPlacement { Stack = part, X = x, Y = y, Rotated = rotated });
                stack.Count -= part.Count;
            }
            Changed?.Invoke();
            return stack.Count;
        }

        /// <summary>Сколько всего предметов этого вида.</summary>
        public int CountOf(ushort def)
        {
            int count = 0;
            foreach (var item in _items)
                if (item.Stack.Def == def)
                    count += item.Stack.Count;
            return count;
        }

        /// <summary>Списать count предметов вида (из любых стаков). Не хватает — ничего не списывает.</summary>
        public bool TryConsume(ushort def, int count)
        {
            if (CountOf(def) < count)
                return false;
            for (int i = _items.Count - 1; i >= 0 && count > 0; i--)
            {
                var item = _items[i];
                if (item.Stack.Def != def)
                    continue;
                int take = Math.Min(item.Stack.Count, count);
                item.Stack.Count -= take;
                count -= take;
                if (item.Stack.Count == 0)
                {
                    Free(item);
                    _items.RemoveAt(i);
                }
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>Отделить часть стака в новый стак (кладётся на свободное место). Null — не вышло.</summary>
        public ItemStack Split(ItemStack stack, int count)
        {
            if (!Contains(stack) || count <= 0 || count >= stack.Count)
                return null;
            var part = new ItemStack(stack.Def, count) { Durability = stack.Durability, Spoiled = stack.Spoiled };
            if (!FindSpace(part, out int x, out int y, out bool rotated))
                return null;
            stack.Count -= count;
            Occupy(part, x, y, rotated);
            _items.Add(new GridPlacement { Stack = part, X = x, Y = y, Rotated = rotated });
            Changed?.Invoke();
            return part;
        }

        /// <summary>Слить source в target (один вид): сколько влезло. Опустевший source убирается из сетки.</summary>
        public int Merge(ItemStack source, ItemStack target)
        {
            if (source == target || !CanStack(source, target))
                return 0;
            int moved = Math.Min(_catalog.MaxStack(target.Def) - target.Count, source.Count);
            if (moved <= 0)
                return 0;
            target.Count += moved;
            source.Count -= moved;
            if (source.Count == 0)
                Remove(source);
            Changed?.Invoke();
            return moved;
        }

        /// <summary>Можно ли сложить два стака в один: один вид, не контейнеры, одинаково свежие.</summary>
        public static bool CanStack(ItemStack a, ItemStack b) =>
            a.Def == b.Def && a.Contents == null && b.Contents == null && a.Spoiled == b.Spoiled;

        /// <summary>Вес содержимого (с вложенными контейнерами).</summary>
        public float TotalWeight()
        {
            float weight = 0f;
            foreach (var item in _items)
            {
                weight += _catalog.Weight(item.Stack.Def) * item.Stack.Count;
                if (item.Stack.Contents != null)
                    weight += item.Stack.Contents.TotalWeight();
            }
            return weight;
        }

        /// <summary>Уведомить подписчиков после изменения стака снаружи (например, Count).</summary>
        public void NotifyChanged() => Changed?.Invoke();

        private void Occupy(ItemStack stack, int x, int y, bool rotated)
        {
            var (w, h) = SizeOf(stack, rotated);
            for (int cy = y; cy < y + h; cy++)
            for (int cx = x; cx < x + w; cx++)
                _cells[cx + cy * Width] = stack;
        }

        private void Free(GridPlacement placement)
        {
            var (w, h) = SizeOf(placement.Stack, placement.Rotated);
            for (int cy = placement.Y; cy < placement.Y + h; cy++)
            for (int cx = placement.X; cx < placement.X + w; cx++)
                if (_cells[cx + cy * Width] == placement.Stack)
                    _cells[cx + cy * Width] = null;
        }
    }
}
