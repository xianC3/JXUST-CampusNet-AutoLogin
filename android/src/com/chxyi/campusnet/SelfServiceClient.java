package com.chxyi.campusnet;

import android.net.Network;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.NetworkInterface;
import java.net.URL;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

final class SelfServiceClient {
    private static final Pattern CHECK_CODE_PATTERN = Pattern.compile(
            "name=\"checkcode\"\\s+value=\"([^\"]+)\"");
    private static final Pattern FORM_ACTION_PATTERN = Pattern.compile(
            "<form\\s+action=\"([^\"]+)\"");
    private static final int MAX_REDIRECTS = 4;

    static final class CleanupResult {
        final boolean loggedIn;
        final boolean matchedDevice;
        final int removedSessions;
        final String message;

        CleanupResult(
                boolean loggedIn,
                boolean matchedDevice,
                int removedSessions,
                String message
        ) {
            this.loggedIn = loggedIn;
            this.matchedDevice = matchedDevice;
            this.removedSessions = removedSessions;
            this.message = message;
        }
    }

    private final Network network;
    private final String baseUrl;
    private final Map<String, String> cookies = new HashMap<>();

    private SelfServiceClient(Network network, String baseUrl) {
        this.network = network;
        this.baseUrl = baseUrl.replaceAll("/+$", "");
    }

    static CleanupResult cleanupOwnSessions(
            String baseUrl,
            Network network,
            String account,
            String password,
            String deviceMac,
            Set<String> trustedMacs,
            boolean personalMode
    ) {
        String normalizedMac = normalizeMac(deviceMac);
        if (!isPlausibleMac(normalizedMac)) {
            return new CleanupResult(
                    false,
                    false,
                    0,
                    "设备数已达上限，但未能取得本机 MAC，已停止注销操作；请打开应用后再试"
            );
        }
        if (account == null || account.trim().isEmpty() ||
                password == null || password.isEmpty()) {
            return new CleanupResult(
                    false,
                    false,
                    0,
                    "设备数已达上限，但账号或密码未配置，无法进入自助服务"
            );
        }

        SelfServiceClient client = new SelfServiceClient(network, baseUrl);
        try {
            LoginAttempt login = client.login(account.trim(), password);
            if (!login.success) {
                return new CleanupResult(
                        false,
                        false,
                        0,
                        login.message
                );
            }

            Response listResponse = client.request(
                    baseUrl + "/dashboard/getOnlineList",
                    "GET",
                    null,
                    true
            );
            if (listResponse.code != 200) {
                return new CleanupResult(
                        true,
                        false,
                        0,
                        "自助服务已登录，但读取在线设备列表失败"
                );
            }

            JSONArray online = new JSONArray(listResponse.text);
            Set<String> matchingSessions = selectCleanupSessions(
                    online,
                    normalizedMac,
                    trustedMacs,
                    personalMode
            );

            if (matchingSessions.isEmpty()) {
                return new CleanupResult(
                        true,
                        false,
                        0,
                        "设备数已达上限，但自助服务中没有找到与本机 MAC 完全一致的在线记录；未注销任何设备"
                );
            }

            int removed = 0;
            for (String sessionId : matchingSessions) {
                String encoded = URLEncoder.encode(sessionId, "UTF-8");
                Response response = client.request(
                        baseUrl + "/dashboard/tooffline?sessionid=" + encoded,
                        "GET",
                        null,
                        true
                );
                if (response.code == 200) {
                    try {
                        JSONObject json = new JSONObject(response.text);
                        if (json.optBoolean("success", false)) {
                            removed++;
                        }
                    } catch (Exception ignored) {
                    }
                }
            }

            if (removed > 0) {
                return new CleanupResult(
                        true,
                        true,
                        removed,
                        "已注销自助服务中与本机 MAC 一致的旧登录记录"
                );
            }
            return new CleanupResult(
                    true,
                    true,
                    0,
                    "已找到本机 MAC 的旧记录，但自助服务拒绝注销"
            );
        } catch (Exception e) {
            return new CleanupResult(
                    false,
                    false,
                    0,
                    "设备数已达上限，进入自助服务失败；未注销任何设备"
            );
        }
    }

    private LoginAttempt login(String account, String password) throws Exception {
        String loginPageUrl = baseUrl + "/login/?302=LI";
        String lastError = "";

        for (int attempt = 0; attempt < 3; attempt++) {
            Response page = request(loginPageUrl, "GET", null, true);
            if (page.code != 200) {
                return new LoginAttempt(false, "自助服务登录页暂时不可达");
            }
            Matcher checkCode = CHECK_CODE_PATTERN.matcher(page.text);
            Matcher action = FORM_ACTION_PATTERN.matcher(page.text);
            if (!checkCode.find() || !action.find()) {
                return new LoginAttempt(false, "自助服务页面格式发生变化，已停止注销操作");
            }

            long nonce = System.currentTimeMillis();
            Response captcha = request(
                    baseUrl + "/login/randomCode?t=" + nonce,
                    "GET",
                    null,
                    false
            );
            if (captcha.code != 200 || captcha.bytes == null) {
                lastError = "自助服务验证码暂时不可用";
                continue;
            }
            String code = CaptchaSolver.solve(captcha.bytes);
            if (code == null) {
                lastError = "验证码识别置信度不足";
                continue;
            }

            Map<String, String> form = new HashMap<>();
            form.put("account", account);
            form.put("password", password);
            form.put("code", code);
            form.put("checkcode", checkCode.group(1));
            form.put("foo", "");
            form.put("bar", "");
            form.put("submit", "登 录");

            String actionUrl = resolveUrl(loginPageUrl, action.group(1));
            Response result = request(
                    actionUrl,
                    "POST",
                    encodeForm(form),
                    true
            );
            if (isDashboard(result.text)) {
                return new LoginAttempt(true, "");
            }
            if (result.text != null &&
                    (result.text.contains("验证码错误") ||
                            result.text.contains("验证码不正确"))) {
                lastError = "验证码识别错误";
                continue;
            }
            if (result.text != null &&
                    (result.text.contains("账号或密码") ||
                            result.text.contains("密码出现错误"))) {
                return new LoginAttempt(
                        false,
                        "自助服务拒绝了账号或密码；未注销任何设备"
                );
            }
            lastError = "自助服务登录未成功";
        }
        return new LoginAttempt(false, lastError + "；未注销任何设备");
    }

    private Response request(
            String urlText,
            String method,
            byte[] body,
            boolean followRedirects
    ) throws Exception {
        String current = urlText;
        for (int redirect = 0; redirect <= MAX_REDIRECTS; redirect++) {
            HttpURLConnection connection =
                    (HttpURLConnection) network.openConnection(new URL(current));
            connection.setRequestMethod(method);
            connection.setInstanceFollowRedirects(false);
            connection.setConnectTimeout(7000);
            connection.setReadTimeout(10000);
            connection.setRequestProperty(
                    "User-Agent",
                    "Mozilla/5.0 (Linux; Android 16) AppleWebKit/537.36 " +
                            "Chrome/130.0.0.0 Mobile Safari/537.36"
            );
            connection.setRequestProperty("Accept", "*/*");
            if (!cookies.isEmpty()) {
                StringBuilder cookieHeader = new StringBuilder();
                for (Map.Entry<String, String> cookie : cookies.entrySet()) {
                    if (cookieHeader.length() > 0) {
                        cookieHeader.append("; ");
                    }
                    cookieHeader.append(cookie.getKey())
                            .append('=')
                            .append(cookie.getValue());
                }
                connection.setRequestProperty("Cookie", cookieHeader.toString());
            }
            if (body != null) {
                connection.setDoOutput(true);
                connection.setRequestProperty(
                        "Content-Type",
                        "application/x-www-form-urlencoded; charset=UTF-8"
                );
                OutputStream output = connection.getOutputStream();
                try {
                    output.write(body);
                } finally {
                    output.close();
                }
            }

            int code = connection.getResponseCode();
            collectCookies(connection);
            if (followRedirects && code >= 300 && code < 400) {
                String location = connection.getHeaderField("Location");
                connection.disconnect();
                if (location == null || location.isEmpty()) {
                    return new Response(code, null, null);
                }
                current = resolveUrl(current, location);
                method = "GET";
                body = null;
                continue;
            }

            InputStream stream = code >= 200 && code < 300
                    ? connection.getInputStream()
                    : connection.getErrorStream();
            byte[] bytes = stream == null ? new byte[0] : readAll(stream);
            connection.disconnect();
            return new Response(
                    code,
                    new String(bytes, StandardCharsets.UTF_8),
                    bytes
            );
        }
        throw new IllegalStateException("too many redirects");
    }

    private void collectCookies(HttpURLConnection connection) {
        for (Map.Entry<String, List<String>> header :
                connection.getHeaderFields().entrySet()) {
            if (header.getKey() == null ||
                    !"Set-Cookie".equalsIgnoreCase(header.getKey())) {
                continue;
            }
            for (String value : header.getValue()) {
                String pair = value.split(";", 2)[0];
                int equals = pair.indexOf('=');
                if (equals > 0) {
                    cookies.put(
                            pair.substring(0, equals).trim(),
                            pair.substring(equals + 1).trim()
                    );
                }
            }
        }
    }

    private static byte[] encodeForm(Map<String, String> form) throws Exception {
        StringBuilder body = new StringBuilder();
        for (Map.Entry<String, String> item : form.entrySet()) {
            if (body.length() > 0) {
                body.append('&');
            }
            body.append(URLEncoder.encode(item.getKey(), "UTF-8"))
                    .append('=')
                    .append(URLEncoder.encode(item.getValue(), "UTF-8"));
        }
        return body.toString().getBytes(StandardCharsets.UTF_8);
    }

    private static byte[] readAll(InputStream input) throws Exception {
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        byte[] buffer = new byte[4096];
        int count;
        try {
            while ((count = input.read(buffer)) != -1) {
                output.write(buffer, 0, count);
            }
        } finally {
            input.close();
        }
        return output.toByteArray();
    }

    private static String resolveUrl(String base, String location)
            throws Exception {
        return new URL(new URL(base), location).toString();
    }

    private static boolean isDashboard(String text) {
        return text != null &&
                (text.contains("dashboard/getOnlineList") ||
                        text.contains("id=\"OnlineLisBig\""));
    }

    private static Set<String> selectCleanupSessions(
            JSONArray online,
            String currentMac,
            Set<String> trustedMacs,
            boolean personalMode
    ) {
        List<CleanupPlanner.Session> sessions = new ArrayList<>();
        SimpleDateFormat format = new SimpleDateFormat(
                "yyyy-MM-dd HH:mm:ss",
                Locale.CHINA
        );
        format.setLenient(false);
        for (int i = 0; i < online.length(); i++) {
            JSONObject item = online.optJSONObject(i);
            if (item == null) {
                continue;
            }
            String mac = normalizeMac(item.optString("mac", ""));
            String sessionId = item.optString("sessionId", "").trim();
            long loginTime = 0;
            String loginText = item.optString("loginTime", "");
            try {
                Date parsed = format.parse(loginText);
                if (parsed != null) {
                    loginTime = parsed.getTime();
                }
            } catch (Exception ignored) {
            }
            sessions.add(new CleanupPlanner.Session(
                    sessionId,
                    mac,
                    loginTime,
                    i
            ));
        }
        return CleanupPlanner.select(
                sessions,
                currentMac,
                trustedMacs,
                personalMode
        );
    }

    static String normalizeMac(String value) {
        if (value == null) {
            return "";
        }
        return value.replaceAll("[^0-9A-Fa-f]", "")
                .toUpperCase(Locale.US);
    }

    static boolean isPlausibleMac(String value) {
        return value != null &&
                value.matches("[0-9A-F]{12}") &&
                !"000000000000".equals(value) &&
                !"FFFFFFFFFFFF".equals(value);
    }

    static String findInterfaceMac() {
        try {
            for (NetworkInterface item :
                    java.util.Collections.list(NetworkInterface.getNetworkInterfaces())) {
                String name = item.getName();
                if (name == null ||
                        !(name.equals("wlan0") || name.startsWith("ap"))) {
                    continue;
                }
                byte[] hardware = item.getHardwareAddress();
                if (hardware == null || hardware.length != 6) {
                    continue;
                }
                StringBuilder mac = new StringBuilder(12);
                for (byte part : hardware) {
                    mac.append(String.format(Locale.US, "%02X", part & 0xff));
                }
                String normalized = normalizeMac(mac.toString());
                if (isPlausibleMac(normalized)) {
                    return normalized;
                }
            }
        } catch (Exception ignored) {
        }
        return "";
    }

    private static final class LoginAttempt {
        final boolean success;
        final String message;

        LoginAttempt(boolean success, String message) {
            this.success = success;
            this.message = message;
        }
    }

    private static final class Response {
        final int code;
        final String text;
        final byte[] bytes;

        Response(int code, String text, byte[] bytes) {
            this.code = code;
            this.text = text;
            this.bytes = bytes;
        }
    }
}
