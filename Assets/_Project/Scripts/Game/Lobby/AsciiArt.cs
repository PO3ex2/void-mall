using System.Text;
using UnityEngine;

namespace VoidMall.Game
{
    /// <summary>
    /// Картинка → ASCII (загрузка ПК, этап 10): изображение режется на клетки символа, яркость под альфой выбирает символ
    /// из рампы от пустого к плотному. Клетка символа вдвое выше ширины — строк вдвое меньше, чем колонок на квадрат.
    /// </summary>
    public static class AsciiArt
    {
        public const string Ramp = " .:-=+*#%@";

        public static string[] FromPixels(Color32[] pixels, int width, int height, int columns)
        {
            columns = Mathf.Max(1, columns);
            int rows = Mathf.Max(1, Mathf.RoundToInt(columns * (float)height / width * 0.5f));
            var lines = new string[rows];
            var line = new StringBuilder(columns);
            for (int row = 0; row < rows; row++)
            {
                line.Clear();
                // Строки картинки снизу вверх (так хранит Unity), строки текста — сверху вниз.
                int y0 = height - 1 - (row + 1) * height / rows + 1;
                int y1 = height - 1 - row * height / rows;
                for (int column = 0; column < columns; column++)
                {
                    int x0 = column * width / columns, x1 = Mathf.Max(x0 + 1, (column + 1) * width / columns);
                    float sum = 0f;
                    int count = 0;
                    for (int y = Mathf.Max(0, y0); y <= y1 && y < height; y++)
                    for (int x = x0; x < x1 && x < width; x++)
                    {
                        var c = pixels[x + y * width];
                        sum += (c.r + c.g + c.b) / (3f * 255f) * (c.a / 255f);
                        count++;
                    }
                    float coverage = count > 0 ? sum / count : 0f;
                    line.Append(Ramp[Mathf.Clamp(Mathf.RoundToInt(coverage * (Ramp.Length - 1)), 0, Ramp.Length - 1)]);
                }
                lines[row] = line.ToString().TrimEnd();
            }
            return lines;
        }

        /// <summary>Из текстуры (должна быть Read/Write).</summary>
        public static string[] FromTexture(Texture2D texture, int columns) =>
            texture != null && texture.isReadable ? FromPixels(texture.GetPixels32(), texture.width, texture.height, columns) : new[] { "HEXARYTE" };
    }
}
