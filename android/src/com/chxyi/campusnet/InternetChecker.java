package com.chxyi.campusnet;

import android.net.Network;

import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.concurrent.CompletionService;
import java.util.concurrent.ExecutorCompletionService;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;

final class InternetChecker {
    private static final String[] ENDPOINTS = {
            "https://www.baidu.com",
            "http://connect.rom.miui.com/generate_204",
            "http://www.msftconnecttest.com/connecttest.txt",
            "http://captive.apple.com/hotspot-detect.html"
    };

    private InternetChecker() {
    }

    static boolean isAvailable(Network network) {
        ExecutorService executor = Executors.newFixedThreadPool(
                ENDPOINTS.length);
        CompletionService<Boolean> completion =
                new ExecutorCompletionService<>(executor);
        try {
            for (String endpoint : ENDPOINTS) {
                completion.submit(() -> checkEndpoint(network, endpoint));
            }
            long deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(6);
            for (int i = 0; i < ENDPOINTS.length; i++) {
                long remaining = deadline - System.nanoTime();
                if (remaining <= 0) {
                    break;
                }
                Future<Boolean> result = completion.poll(
                        remaining,
                        TimeUnit.NANOSECONDS
                );
                if (result != null && Boolean.TRUE.equals(result.get())) {
                    return true;
                }
            }
            return false;
        } catch (Exception ignored) {
            return false;
        } finally {
            executor.shutdownNow();
        }
    }

    private static boolean checkEndpoint(Network network, String urlText) {
        HttpURLConnection connection = null;
        try {
            connection = (HttpURLConnection) network.openConnection(
                    new URL(urlText));
            connection.setConnectTimeout(4000);
            connection.setReadTimeout(4000);
            connection.setInstanceFollowRedirects(false);
            connection.setRequestProperty("Cache-Control", "no-cache");
            int code = connection.getResponseCode();
            if (code < 200 || code >= 300) {
                return false;
            }
            if (urlText.contains("generate_204")) {
                return code == 204;
            }
            if (urlText.contains("msftconnecttest")) {
                return readText(connection).contains("Microsoft Connect Test");
            }
            if (urlText.contains("captive.apple.com")) {
                return readText(connection).contains("Success");
            }
            return true;
        } catch (Exception ignored) {
            return false;
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
        }
    }

    private static String readText(HttpURLConnection connection)
            throws Exception {
        InputStream input = connection.getInputStream();
        try {
            byte[] buffer = new byte[1024];
            StringBuilder result = new StringBuilder();
            int count;
            while ((count = input.read(buffer)) != -1) {
                result.append(new String(buffer, 0, count, "UTF-8"));
            }
            return result.toString();
        } finally {
            input.close();
        }
    }
}
