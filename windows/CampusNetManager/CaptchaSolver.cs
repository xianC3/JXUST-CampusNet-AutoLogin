using System.Drawing;

namespace CampusNetManager;

public static class CaptchaSolver
{
    private const int GridWidth = 12;
    private const int GridHeight = 14;
    private const double MinimumScore = 0.68;

    private static readonly string[][] Templates =
    {
        new[]
        {
            "000011111000", "000011111000", "000111111100",
            "001110001110", "111100000111", "111100000111",
            "111100000111", "111100000111", "111100000111",
            "111100000111", "111100000111", "001110001110",
            "000111111100", "000011111000"
        },
        new[]
        {
            "001111110000", "001111110000", "111111110000",
            "111011110000", "000011110000", "000011110000",
            "000011110000", "000011110000", "000011110000",
            "000011110000", "000011110000", "000011110000",
            "111111111111", "111111111111"
        },
        new[]
        {
            "001111111100", "001111111100", "111111111110",
            "111000001111", "110000000111", "000000000111",
            "000000000111", "000000001111", "000000111110",
            "000011111100", "000111111000", "001111110000",
            "111111111111", "111111111111"
        },
        new[]
        {
            "001111111100", "001111111100", "111111111110",
            "110000001111", "000000000111", "000000001111",
            "000111111100", "000111111110", "000000001111",
            "000000000111", "000000000111", "110000001111",
            "111111111110", "001111111000"
        },
        new[]
        {
            "000000111100", "000000111100", "000001111100",
            "000011111100", "000011011100", "000110011100",
            "001110011100", "001100011100", "111000011100",
            "111111111111", "111111111111", "000000011100",
            "000000011100", "000000011100"
        },
        new[]
        {
            "001111111110", "001111111110", "001111111110",
            "001110000000", "001110000000", "001111111100",
            "001111111110", "001000001111", "000000000111",
            "000000000111", "000000000111", "110000001111",
            "111111111110", "001111111000"
        },
        new[]
        {
            "000111111100", "000111111100", "001111111110",
            "111100000010", "111000000000", "111111111100",
            "111111111110", "111100011111", "111000001111",
            "111000001111", "111000001111", "111100011111",
            "111111111110", "000111111100"
        },
        new[]
        {
            "111111111111", "111111111111", "111111111111",
            "000000001111", "000000001110", "000000111110",
            "000000111100", "000000111100", "000001111000",
            "000001111000", "000011110000", "000011110000",
            "000111110000", "000111000000"
        },
        new[]
        {
            "001111111100", "001111111100", "111111111110",
            "111000001111", "111000001111", "111000001111",
            "111111111110", "111111111110", "111100011111",
            "111000001111", "111000001111", "111100011111",
            "111111111110", "001111111100"
        },
        new[]
        {
            "000111111000", "000111111000", "001111111110",
            "111110001110", "111100000111", "111100000111",
            "111100000111", "111110001111", "001111111111",
            "000111110111", "000000000110", "001000001110",
            "001111111100", "000111111000"
        }
    };

    public static string? Solve(byte[] imageBytes)
    {
        try
        {
            using var stream = new MemoryStream(imageBytes, writable: false);
            using var bitmap = new Bitmap(stream);
            if (bitmap.Width < 40 || bitmap.Height < 16)
            {
                return null;
            }

            var result = new char[4];
            var cellWidth = bitmap.Width / 4;
            for (var index = 0; index < result.Length; index++)
            {
                var digit = RecognizeCell(bitmap, index * cellWidth, cellWidth);
                if (digit == null)
                {
                    return null;
                }
                result[index] = digit.Value;
            }
            return new string(result);
        }
        catch
        {
            return null;
        }
    }

    private static char? RecognizeCell(Bitmap bitmap, int startX, int width)
    {
        var mask = new bool[bitmap.Height, width];
        var visited = new bool[bitmap.Height, width];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                mask[y, x] = IsGlyphPixel(bitmap.GetPixel(startX + x, y));
            }
        }

        List<Point>? largest = null;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!mask[y, x] || visited[y, x])
                {
                    continue;
                }
                var component = CollectComponent(mask, visited, x, y);
                if (largest == null || component.Count > largest.Count)
                {
                    largest = component;
                }
            }
        }

        if (largest == null || largest.Count < 35)
        {
            return null;
        }

        var normalized = Normalize(largest);
        if (normalized == null)
        {
            return null;
        }

        var bestScore = -1.0;
        var bestDigit = -1;
        for (var digit = 0; digit < Templates.Length; digit++)
        {
            var current = Score(normalized, Templates[digit]);
            if (current > bestScore)
            {
                bestScore = current;
                bestDigit = digit;
            }
        }

        return bestScore < MinimumScore
            ? null
            : (char)('0' + bestDigit);
    }

    private static List<Point> CollectComponent(
        bool[,] mask,
        bool[,] visited,
        int startX,
        int startY)
    {
        var queue = new Queue<Point>();
        var component = new List<Point>();
        queue.Enqueue(new Point(startX, startY));
        visited[startY, startX] = true;
        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            component.Add(point);
            AddPoint(mask, visited, queue, point.X + 1, point.Y);
            AddPoint(mask, visited, queue, point.X - 1, point.Y);
            AddPoint(mask, visited, queue, point.X, point.Y + 1);
            AddPoint(mask, visited, queue, point.X, point.Y - 1);
        }
        return component;
    }

    private static void AddPoint(
        bool[,] mask,
        bool[,] visited,
        Queue<Point> queue,
        int x,
        int y)
    {
        if (y < 0 || y >= mask.GetLength(0) ||
            x < 0 || x >= mask.GetLength(1))
        {
            return;
        }
        if (mask[y, x] && !visited[y, x])
        {
            visited[y, x] = true;
            queue.Enqueue(new Point(x, y));
        }
    }

    private static bool IsGlyphPixel(System.Drawing.Color color)
    {
        var maximum = Math.Max(color.R, Math.Max(color.G, color.B));
        var minimum = Math.Min(color.R, Math.Min(color.G, color.B));
        var luminance = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
        return luminance < 155 || (maximum - minimum > 70 && minimum < 150);
    }

    private static string? Normalize(List<Point> points)
    {
        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        var sourceWidth = maxX - minX + 1;
        var sourceHeight = maxY - minY + 1;
        if (sourceWidth < 7 || sourceHeight < 10 ||
            sourceHeight > 18 || sourceWidth > 18)
        {
            return null;
        }

        var source = new bool[sourceHeight, sourceWidth];
        foreach (var point in points)
        {
            source[point.Y - minY, point.X - minX] = true;
        }

        var result = new char[GridWidth * GridHeight];
        var offset = 0;
        for (var y = 0; y < GridHeight; y++)
        {
            var sourceY = Math.Min(
                sourceHeight - 1,
                (int)((long)y * sourceHeight / GridHeight)
            );
            for (var x = 0; x < GridWidth; x++)
            {
                var sourceX = Math.Min(
                    sourceWidth - 1,
                    (int)((long)x * sourceWidth / GridWidth)
                );
                result[offset++] = source[sourceY, sourceX] ? '1' : '0';
            }
        }
        return new string(result);
    }

    private static double Score(string sample, IReadOnlyList<string> templateRows)
    {
        var intersection = 0;
        var union = 0;
        var offset = 0;
        foreach (var row in templateRows)
        {
            for (var index = 0; index < row.Length; index++)
            {
                var actual = sample[offset + index] == '1';
                var expected = row[index] == '1';
                if (actual && expected)
                {
                    intersection++;
                }
                if (actual || expected)
                {
                    union++;
                }
            }
            offset += row.Length;
        }
        return union == 0 ? 0 : (double)intersection / union;
    }
}
