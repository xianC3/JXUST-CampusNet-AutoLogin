package com.chxyi.campusnet;

import android.content.Context;
import android.content.SharedPreferences;
import android.net.ConnectivityManager;
import android.net.LinkAddress;
import android.net.LinkProperties;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.wifi.WifiInfo;
import android.net.wifi.WifiManager;

import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.Inet4Address;
import java.net.URL;
import java.net.URLEncoder;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.Random;
import java.util.Set;

final class CampusLoginClient {
    private final Context context;

    CampusLoginClient(Context context) {
        this.context = context.getApplicationContext();
    }

    static final class Result {
        final boolean success;
        final boolean skipped;
        final String message;
        final String ssid;
        final boolean retry;

        Result(boolean success, boolean skipped, String message, String ssid) {
            this(success, skipped, message, ssid, false);
        }

        Result(boolean success, boolean skipped, String message, String ssid, boolean retry) {
            this.success = success;
            this.skipped = skipped;
            this.message = message;
            this.ssid = ssid;
            this.retry = retry;
        }
    }

    Result checkAndLogin() {
        String account = AccountStore.account(context);
        String loginAccount = AccountStore.loginAccount(context);
        String password = AccountStore.password(context);
        if (account.isEmpty() || password.isEmpty()) {
            return new Result(
                    false,
                    false,
                    "请先在应用里填写校园网账号和密码",
                    null
            );
        }

        Network wifiNetwork = findWifiNetwork();
        if (wifiNetwork == null) {
            return new Result(
                    true,
                    true,
                    "当前不是 Wi-Fi 网络，已跳过",
                    getCurrentSsid()
            );
        }

        String ssid = getCurrentSsid();
        boolean ssidKnown = isUsableSsid(ssid);
        if (ssidKnown && !isCampusSsid(ssid)) {
            return new Result(
                    true,
                    true,
                    "当前网络“" + ssid + "”不在校园网名单，已跳过",
                    ssid
            );
        }

        JSONObject status = null;
        boolean portalRecognized = false;
        try {
            status = requestJson(buildStatusUrl(), wifiNetwork);
            portalRecognized = true;
        } catch (Exception ignored) {
            // A known campus SSID can continue even if status is temporarily unavailable.
        }

        if (status == null && ssidKnown && isCampusSsid(ssid)) {
            if (InternetChecker.isAvailable(context, wifiNetwork)) {
                return new Result(
                        true,
                        false,
                        "外网可用；校园门户状态接口暂时异常",
                        ssid
                );
            }
        }

        if (!ssidKnown && !portalRecognized) {
            return new Result(
                    false,
                    false,
                    "正在识别校园网连接，请保持 Wi-Fi 连接",
                    null,
                    true
            );
        }

        if (portalRecognized && isOnline(status)) {
            String onlineUid = status.optString("uid", loginAccount);
            rememberMac(status.optString("olmac", ""));
            if (!InternetChecker.isAvailable(context, wifiNetwork)) {
                return new Result(
                        false,
                        false,
                        "校园网已认证，但外网暂时不可用，正在等待网络恢复",
                        ssid,
                        true
                );
            }
            String message = ssidKnown
                    ? "已在线且外网可用：" + onlineUid
                    : "已在线且外网可用（通过校园门户识别）";
            return new Result(true, false, message, ssid);
        }

        String ip = getLocalIpv4(wifiNetwork);
        if (ip == null || ip.isEmpty()) {
            return new Result(
                    false,
                    false,
                    "正在获取校园网地址，请稍后重试",
                    ssid,
                    true
            );
        }

        String deviceMac = trustedDeviceMac();
        try {
            JSONObject login = requestJson(
                    buildLoginUrl(ip, loginAccount, password, deviceMac),
                    wifiNetwork
            );
            if (isLoginSuccess(login)) {
                if (!InternetChecker.isAvailable(context, wifiNetwork)) {
                    return new Result(
                            false,
                            false,
                            "校园网认证成功，但外网暂时不可用，正在等待网络恢复",
                            ssid,
                            true
                    );
                }
                String message = ssidKnown
                        ? "登录成功且外网可用：" + ip
                        : "登录成功且外网可用（通过校园门户识别）：" + ip;
                return new Result(true, false, message, ssid);
            }

            String message = login.optString("msg", "门户拒绝了登录请求");
            if (PortalMessageMatcher.isDeviceLimit(message)) {
                SelfServiceClient.CleanupResult cleanup =
                        SelfServiceClient.cleanupOwnSessions(
                                AppConfig.SELF_SERVICE_BASE,
                                wifiNetwork,
                                account,
                                password,
                                deviceMac,
                                TrustedMacStore.load(context),
                                AppConfig.PERSONAL_MODE
                        );
                appendLog(context, "LIMIT " + cleanup.message);
                if (cleanup.removedSessions > 0) {
                    try {
                        Thread.sleep(1200);
                    } catch (InterruptedException interrupted) {
                        Thread.currentThread().interrupt();
                    }
                    JSONObject retry = requestJson(
                            buildLoginUrl(ip, loginAccount, password, deviceMac),
                            wifiNetwork
                    );
                    if (isLoginSuccess(retry)) {
                        if (!InternetChecker.isAvailable(context, wifiNetwork)) {
                            return new Result(
                                    false,
                                    false,
                                    "已清理本机旧登录，但外网暂时不可用",
                                    ssid,
                                    true
                            );
                        }
                        return new Result(
                                true,
                                false,
                                "已清理旧会话，连接及外网均正常：" + ip,
                                ssid
                        );
                    }
                    message = cleanup.message + "；重新登录仍未成功";
                } else {
                    message = cleanup.message;
                }
            }
            if (PortalMessageMatcher.isTransientBusy(message)) {
                if (InternetChecker.isAvailable(context, wifiNetwork)) {
                    return new Result(
                            true,
                            false,
                            "外网可用；校园门户暂时繁忙",
                            ssid
                    );
                }
                return new Result(
                        false,
                        false,
                        "校园门户系统忙，正在自动重试",
                        ssid,
                        true
                );
            }
            return new Result(false, false, message, ssid);
        } catch (Exception e) {
            if (InternetChecker.isAvailable(context, wifiNetwork)) {
                return new Result(
                        true,
                        false,
                        "外网可用；校园门户请求暂时失败",
                        ssid
                );
            }
            return new Result(
                    false,
                    false,
                    "校园门户暂时不可达，正在等待网络稳定",
                    ssid,
                    true
            );
        }
    }

    private boolean isOnline(JSONObject status) {
        return status != null &&
                ("1".equals(status.optString("result")) ||
                        "ok".equals(status.optString("result")));
    }

    private boolean isLoginSuccess(JSONObject result) {
        return result != null &&
                ("1".equals(result.optString("result")) ||
                        "ok".equals(result.optString("result")) ||
                        "2".equals(result.optString("ret_code")));
    }

    private boolean isCampusSsid(String ssid) {
        SharedPreferences prefs =
                context.getSharedPreferences(AppConfig.PREFS, Context.MODE_PRIVATE);
        String saved = prefs.getString(AppConfig.KEY_SSIDS, null);
        if (saved == null) {
            saved = AppConfig.DEFAULT_SSID;
        }
        if (saved.trim().isEmpty()) {
            return false;
        }
        for (String item : saved.split("\\n")) {
            if (ssid.equals(item.trim())) {
                return true;
            }
        }
        return false;
    }

    String getCurrentSsid() {
        try {
            WifiManager manager =
                    (WifiManager) context.getSystemService(Context.WIFI_SERVICE);
            if (manager == null) {
                return null;
            }
            WifiInfo info = manager.getConnectionInfo();
            if (info == null) {
                return null;
            }
            String ssid = info.getSSID();
            if (ssid == null) {
                return null;
            }
            if (ssid.length() >= 2 &&
                    ssid.startsWith("\"") &&
                    ssid.endsWith("\"")) {
                ssid = ssid.substring(1, ssid.length() - 1);
            }
            return ssid;
        } catch (Exception e) {
            return null;
        }
    }

    private boolean isUsableSsid(String ssid) {
        return ssid != null &&
                !ssid.isEmpty() &&
                !"<unknown ssid>".equals(ssid) &&
                !"0x".equals(ssid);
    }

    private Network findWifiNetwork() {
        try {
            ConnectivityManager manager =
                    (ConnectivityManager) context.getSystemService(
                            Context.CONNECTIVITY_SERVICE);
            if (manager == null) {
                return null;
            }
            for (Network network : manager.getAllNetworks()) {
                NetworkCapabilities capabilities =
                        manager.getNetworkCapabilities(network);
                if (capabilities != null &&
                        capabilities.hasTransport(
                                NetworkCapabilities.TRANSPORT_WIFI)) {
                    return network;
                }
            }
        } catch (Exception ignored) {
        }
        return null;
    }

    private String getLocalIpv4(Network wifiNetwork) {
        try {
            ConnectivityManager manager =
                    (ConnectivityManager) context.getSystemService(
                            Context.CONNECTIVITY_SERVICE);
            if (manager == null) {
                return null;
            }
            return getIpv4(manager.getLinkProperties(wifiNetwork));
        } catch (Exception ignored) {
            return null;
        }
    }

    private String getIpv4(LinkProperties properties) {
        if (properties == null) {
            return null;
        }
        for (LinkAddress address : properties.getLinkAddresses()) {
            if (address.getAddress() instanceof Inet4Address &&
                    !address.getAddress().isLoopbackAddress()) {
                return address.getAddress().getHostAddress();
            }
        }
        return null;
    }

    private String buildStatusUrl() {
        int random = 500 + new Random().nextInt(10000);
        return "http://" + AppConfig.PORTAL_HOST +
                "/drcom/chkstatus?callback=dr" + random +
                "&jsVersion=4.X&v=" + random + "&lang=zh";
    }

    private String buildLoginUrl(
            String ip,
            String account,
            String password,
            String mac
    ) throws Exception {
        int random = 500 + new Random().nextInt(10000);
        String callback = "dr" + random;
        return "http://" + AppConfig.PORTAL_HOST + ":" +
                AppConfig.PORTAL_PORT +
                "/eportal/portal/login?" +
                "login_method=" + encode("1") +
                "&user_account=" + encode(account) +
                "&user_password=" + encode(password) +
                "&wlan_user_ip=" + encode(ip) +
                "&wlan_user_ipv6=" + encode("") +
                "&wlan_user_mac=" + encode(mac) +
                "&wlan_ac_ip=" + encode(AppConfig.PORTAL_HOST) +
                "&wlan_ac_name=" + encode("") +
                "&terminal_type=" + encode("1") +
                "&lang=" + encode("zh") +
                "&jsVersion=" + encode("4.X") +
                "&callback=" + encode(callback) +
                "&v=" + encode(String.valueOf(random));
    }

    private String encode(String value) throws Exception {
        return URLEncoder.encode(value, "UTF-8");
    }

    private JSONObject requestJson(String urlText, Network network)
            throws Exception {
        HttpURLConnection connection = null;
        try {
            connection = (HttpURLConnection) network.openConnection(
                    new URL(urlText));
            connection.setRequestMethod("GET");
            connection.setConnectTimeout(6000);
            connection.setReadTimeout(10000);
            connection.setRequestProperty(
                    "User-Agent",
                    "Mozilla/5.0 (Linux; Android 16) AppleWebKit/537.36 " +
                            "Chrome/130.0.0.0 Mobile Safari/537.36"
            );
            connection.setRequestProperty("Accept", "*/*");

            int code = connection.getResponseCode();
            InputStream stream = code >= 200 && code < 300
                    ? connection.getInputStream()
                    : connection.getErrorStream();
            if (stream == null) {
                throw new IllegalStateException("empty response");
            }
            String text = readText(stream).trim();
            int start = text.indexOf('(');
            int end = text.lastIndexOf(')');
            if (start >= 0 && end > start) {
                text = text.substring(start + 1, end);
            }
            return new JSONObject(text);
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
        }
    }

    private String readText(InputStream stream) throws Exception {
        BufferedReader reader =
                new BufferedReader(new InputStreamReader(stream, "UTF-8"));
        StringBuilder builder = new StringBuilder();
        String line;
        try {
            while ((line = reader.readLine()) != null) {
                builder.append(line);
            }
        } finally {
            reader.close();
        }
        return builder.toString();
    }

    private String trustedDeviceMac() {
        String wifiMac = "";
        try {
            WifiManager manager =
                    (WifiManager) context.getSystemService(Context.WIFI_SERVICE);
            if (manager != null) {
                WifiInfo info = manager.getConnectionInfo();
                if (info != null) {
                    wifiMac = SelfServiceClient.normalizeMac(info.getMacAddress());
                }
            }
        } catch (Exception ignored) {
        }
        if (SelfServiceClient.isPlausibleMac(wifiMac)) {
            rememberMac(wifiMac);
            return wifiMac;
        }

        String interfaceMac = SelfServiceClient.findInterfaceMac();
        if (SelfServiceClient.isPlausibleMac(interfaceMac)) {
            rememberMac(interfaceMac);
            return interfaceMac;
        }

        SharedPreferences prefs =
                context.getSharedPreferences(AppConfig.PREFS, Context.MODE_PRIVATE);
        String saved = SelfServiceClient.normalizeMac(
                prefs.getString(AppConfig.KEY_DEVICE_MAC, ""));
        if (SelfServiceClient.isPlausibleMac(saved)) {
            return saved;
        }
        return "";
    }

    private void rememberMac(String mac) {
        String normalized = SelfServiceClient.normalizeMac(mac);
        if (!SelfServiceClient.isPlausibleMac(normalized)) {
            return;
        }
        context.getSharedPreferences(AppConfig.PREFS, Context.MODE_PRIVATE)
                .edit()
                .putString(AppConfig.KEY_DEVICE_MAC, normalized)
                .apply();
        TrustedMacStore.remember(context, normalized);
    }

    static void appendLog(Context context, String message) {
        try {
            File file = new File(context.getFilesDir(), "campusnet.log");
            String line = new SimpleDateFormat(
                    "yyyy-MM-dd HH:mm:ss", Locale.CHINA)
                    .format(new Date()) + " " + message + "\n";
            OutputStream output = new FileOutputStream(file, true);
            try {
                output.write(line.getBytes("UTF-8"));
            } finally {
                output.close();
            }
        } catch (Exception ignored) {
        }
    }
}
