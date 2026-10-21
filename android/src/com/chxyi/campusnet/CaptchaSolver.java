package com.chxyi.campusnet;

import android.graphics.Bitmap;
import android.graphics.BitmapFactory;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.List;

final class CaptchaSolver {
    private static final int GRID_WIDTH = 12;
    private static final int GRID_HEIGHT = 14;
    private static final double MIN_SCORE = 0.68;

    private static final String[][] TEMPLATES = {
            {
                    "000011111000", "000011111000", "000111111100",
                    "001110001110", "111100000111", "111100000111",
                    "111100000111", "111100000111", "111100000111",
                    "111100000111", "111100000111", "001110001110",
                    "000111111100", "000011111000"
            },
            {
                    "001111110000", "001111110000", "111111110000",
                    "111011110000", "000011110000", "000011110000",
                    "000011110000", "000011110000", "000011110000",
                    "000011110000", "000011110000", "000011110000",
                    "111111111111", "111111111111"
            },
            {
                    "001111111100", "001111111100", "111111111110",
                    "111000001111", "110000000111", "000000000111",
                    "000000000111", "000000001111", "000000111110",
                    "000011111100", "000111111000", "001111110000",
                    "111111111111", "111111111111"
            },
            {
                    "001111111100", "001111111100", "111111111110",
                    "110000001111", "000000000111", "000000001111",
                    "000111111100", "000111111110", "000000001111",
                    "000000000111", "000000000111", "110000001111",
                    "111111111110", "001111111000"
            },
            {
                    "000000111100", "000000111100", "000001111100",
                    "000011111100", "000011011100", "000110011100",
                    "001110011100", "001100011100", "111000011100",
                    "111111111111", "111111111111", "000000011100",
                    "000000011100", "000000011100"
            },
            {
                    "001111111110", "001111111110", "001111111110",
                    "001110000000", "001110000000", "001111111100",
                    "001111111110", "001000001111", "000000000111",
                    "000000000111", "000000000111", "110000001111",
                    "111111111110", "001111111000"
            },
            {
                    "000111111100", "000111111100", "001111111110",
                    "111100000010", "111000000000", "111111111100",
                    "111111111110", "111100011111", "111000001111",
                    "111000001111", "111000001111", "111100011111",
                    "111111111110", "000111111100"
            },
            {
                    "111111111111", "111111111111", "111111111111",
                    "000000001111", "000000001110", "000000111110",
                    "000000111100", "000000111100", "000001111000",
                    "000001111000", "000011110000", "000011110000",
                    "000111110000", "000111000000"
            },
            {
                    "001111111100", "001111111100", "111111111110",
                    "111000001111", "111000001111", "111000001111",
                    "111111111110", "111111111110", "111100011111",
                    "111000001111", "111000001111", "111100011111",
                    "111111111110", "001111111100"
            },
            {
                    "000111111000", "000111111000", "001111111110",
                    "111110001110", "111100000111", "111100000111",
                    "111100000111", "111110001111", "001111111111",
                    "000111110111", "000000000110", "001000001110",
                    "001111111100", "000111111000"
            }
    };

    private CaptchaSolver() {
    }

    static String solve(byte[] imageBytes) {
        try {
            Bitmap bitmap = BitmapFactory.decodeByteArray(
                    imageBytes, 0, imageBytes.length);
            if (bitmap == null || bitmap.getWidth() < 40 ||
                    bitmap.getHeight() < 16) {
                return null;
            }

            StringBuilder result = new StringBuilder(4);
            int cellWidth = bitmap.getWidth() / 4;
            for (int i = 0; i < 4; i++) {
                Character digit = recognizeCell(
                        bitmap, i * cellWidth, cellWidth);
                if (digit == null) {
                    return null;
                }
                result.append(digit);
            }
            return result.toString();
        } catch (Exception ignored) {
            return null;
        }
    }

    private static Character recognizeCell(
            Bitmap bitmap,
            int startX,
            int width
    ) {
        boolean[][] mask = new boolean[bitmap.getHeight()][width];
        boolean[][] visited = new boolean[bitmap.getHeight()][width];
        for (int y = 0; y < bitmap.getHeight(); y++) {
            for (int x = 0; x < width; x++) {
                mask[y][x] = isGlyphPixel(bitmap.getPixel(startX + x, y));
            }
        }

        List<Point> largest = new ArrayList<>();
        for (int y = 0; y < bitmap.getHeight(); y++) {
            for (int x = 0; x < width; x++) {
                if (!mask[y][x] || visited[y][x]) {
                    continue;
                }
                List<Point> component = collectComponent(mask, visited, x, y);
                if (component.size() > largest.size()) {
                    largest = component;
                }
            }
        }
        if (largest.size() < 35) {
            return null;
        }

        String normalized = normalize(largest);
        if (normalized == null) {
            return null;
        }

        double bestScore = -1.0;
        int bestDigit = -1;
        for (int digit = 0; digit < TEMPLATES.length; digit++) {
            double score = score(normalized, TEMPLATES[digit]);
            if (score > bestScore) {
                bestScore = score;
                bestDigit = digit;
            }
        }
        if (bestScore < MIN_SCORE) {
            return null;
        }
        return (char) ('0' + bestDigit);
    }

    private static List<Point> collectComponent(
            boolean[][] mask,
            boolean[][] visited,
            int startX,
            int startY
    ) {
        ArrayDeque<Point> queue = new ArrayDeque<>();
        List<Point> component = new ArrayList<>();
        queue.add(new Point(startX, startY));
        visited[startY][startX] = true;
        while (!queue.isEmpty()) {
            Point point = queue.removeFirst();
            component.add(point);
            addPoint(mask, visited, queue, point.x + 1, point.y);
            addPoint(mask, visited, queue, point.x - 1, point.y);
            addPoint(mask, visited, queue, point.x, point.y + 1);
            addPoint(mask, visited, queue, point.x, point.y - 1);
        }
        return component;
    }

    private static void addPoint(
            boolean[][] mask,
            boolean[][] visited,
            ArrayDeque<Point> queue,
            int x,
            int y
    ) {
        if (y < 0 || y >= mask.length || x < 0 || x >= mask[y].length) {
            return;
        }
        if (mask[y][x] && !visited[y][x]) {
            visited[y][x] = true;
            queue.addLast(new Point(x, y));
        }
    }

    private static boolean isGlyphPixel(int color) {
        int red = (color >> 16) & 0xff;
        int green = (color >> 8) & 0xff;
        int blue = color & 0xff;
        int max = Math.max(red, Math.max(green, blue));
        int min = Math.min(red, Math.min(green, blue));
        int luminance = (red * 299 + green * 587 + blue * 114) / 1000;
        return luminance < 155 || (max - min > 70 && min < 150);
    }

    private static String normalize(List<Point> points) {
        int minX = Integer.MAX_VALUE;
        int maxX = Integer.MIN_VALUE;
        int minY = Integer.MAX_VALUE;
        int maxY = Integer.MIN_VALUE;
        for (Point point : points) {
            minX = Math.min(minX, point.x);
            maxX = Math.max(maxX, point.x);
            minY = Math.min(minY, point.y);
            maxY = Math.max(maxY, point.y);
        }
        int sourceWidth = maxX - minX + 1;
        int sourceHeight = maxY - minY + 1;
        if (sourceWidth < 7 || sourceHeight < 10 ||
                sourceHeight > 18 || sourceWidth > 18) {
            return null;
        }

        boolean[][] source = new boolean[sourceHeight][sourceWidth];
        for (Point point : points) {
            source[point.y - minY][point.x - minX] = true;
        }
        StringBuilder result = new StringBuilder(GRID_WIDTH * GRID_HEIGHT);
        for (int y = 0; y < GRID_HEIGHT; y++) {
            int sourceY = Math.min(
                    sourceHeight - 1,
                    (int) ((long) y * sourceHeight / GRID_HEIGHT)
            );
            for (int x = 0; x < GRID_WIDTH; x++) {
                int sourceX = Math.min(
                        sourceWidth - 1,
                        (int) ((long) x * sourceWidth / GRID_WIDTH)
                );
                result.append(source[sourceY][sourceX] ? '1' : '0');
            }
        }
        return result.toString();
    }

    private static double score(String sample, String[] templateRows) {
        int intersection = 0;
        int union = 0;
        int offset = 0;
        for (String row : templateRows) {
            for (int i = 0; i < row.length(); i++) {
                boolean actual = sample.charAt(offset + i) == '1';
                boolean expected = row.charAt(i) == '1';
                if (actual && expected) {
                    intersection++;
                }
                if (actual || expected) {
                    union++;
                }
            }
            offset += row.length();
        }
        return union == 0 ? 0.0 : (double) intersection / union;
    }

    private static final class Point {
        final int x;
        final int y;

        Point(int x, int y) {
            this.x = x;
            this.y = y;
        }
    }
}
