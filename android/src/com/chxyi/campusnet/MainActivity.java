package com.chxyi.campusnet;

import android.Manifest;
import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.content.ComponentName;
import android.location.LocationManager;
import android.net.Uri;
import android.net.wifi.WifiInfo;
import android.net.wifi.WifiManager;
import android.os.Build;
import android.os.Bundle;
import android.os.PowerManager;
import android.provider.Settings;
import android.view.View;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ArrayAdapter;
import android.widget.Spinner;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

public class MainActivity extends Activity {
    private static final int REQUEST_PERMISSIONS = 1001;

    private SharedPreferences prefs;
    private TextView statusText;
    private TextView currentWifiText;
    private TextView diagnosticsText;
    private TextView ssidListText;
    private TextView accountText;
    private EditText accountInput;
    private EditText passwordInput;
    private Spinner providerSpinner;
    private Switch autoSwitch;
    private boolean receiverRegistered;
    private boolean providerSelectorReady;

    private final BroadcastReceiver statusReceiver = new BroadcastReceiver() {
        @Override
        public void onReceive(Context context, Intent intent) {
            refreshUi();
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        prefs = getSharedPreferences(AppConfig.PREFS, MODE_PRIVATE);
        ensureDefaults();

        statusText = findViewById(R.id.statusText);
        currentWifiText = findViewById(R.id.currentWifiText);
        diagnosticsText = findViewById(R.id.diagnosticsText);
        ssidListText = findViewById(R.id.ssidListText);
        autoSwitch = findViewById(R.id.autoSwitch);
        accountText = findViewById(R.id.accountText);
        TextView hintText = findViewById(R.id.hintText);
        accountInput = findViewById(R.id.accountInput);
        passwordInput = findViewById(R.id.passwordInput);
        providerSpinner = findViewById(R.id.providerSpinner);
        Button loginButton = findViewById(R.id.loginButton);
        Button addWifiButton = findViewById(R.id.addWifiButton);
        Button manageWifiButton = findViewById(R.id.manageWifiButton);
        Button saveAccountButton = findViewById(R.id.saveAccountButton);
        Button batteryButton = findViewById(R.id.batteryButton);
        Button settingsButton = findViewById(R.id.settingsButton);

        if (AccountStore.account(this).isEmpty()) {
            hintText.setText("通用版：首次使用请在下方填写自己的校园网账号和密码。应用只会在名单中的校园 Wi-Fi 下访问门户，其他网络会直接跳过。");
        } else {
            hintText.setText("个人版已预置账号和校园网配置。应用只会在名单中的校园 Wi-Fi 下访问门户，家庭网络、热点和其他 Wi-Fi 会直接跳过。");
        }
        String[] providerNames = new String[ProviderCatalog.OPTIONS.length];
        for (int i = 0; i < ProviderCatalog.OPTIONS.length; i++) {
            providerNames[i] = ProviderCatalog.OPTIONS[i].name;
        }
        ArrayAdapter<String> providerAdapter = new ArrayAdapter<>(
                this,
                android.R.layout.simple_spinner_item,
                providerNames
        );
        providerAdapter.setDropDownViewResource(
                android.R.layout.simple_spinner_dropdown_item);
        providerSpinner.setAdapter(providerAdapter);
        providerSpinner.setSelection(
                ProviderCatalog.indexOfSuffix(AccountStore.providerSuffix(this)));
        providerSelectorReady = true;
        providerSpinner.setOnItemSelectedListener(
                new android.widget.AdapterView.OnItemSelectedListener() {
                    @Override
                    public void onItemSelected(
                            android.widget.AdapterView<?> parent,
                            View view,
                            int position,
                            long id
                    ) {
                        if (!providerSelectorReady) {
                            return;
                        }
                        prefs.edit()
                                .putString(
                                        AppConfig.KEY_PROVIDER_SUFFIX,
                                        ProviderCatalog.suffixAt(position)
                                )
                                .apply();
                        refreshUi();
                    }

                    @Override
                    public void onNothingSelected(
                            android.widget.AdapterView<?> parent
                    ) {
                    }
                }
        );
        accountInput.setText(AccountStore.account(this));
        passwordInput.setText(AccountStore.password(this));

        autoSwitch.setChecked(prefs.getBoolean(AppConfig.KEY_AUTO_ENABLED, true));
        autoSwitch.setOnCheckedChangeListener((buttonView, isChecked) -> {
            prefs.edit().putBoolean(AppConfig.KEY_AUTO_ENABLED, isChecked).apply();
            if (isChecked) {
                startLoginServiceIfAllowed(null);
            } else {
                JobSchedule.cancel(this);
                stopService(new Intent(this, CampusLoginService.class));
                statusText.setText("自动登录已关闭");
            }
        });

        loginButton.setOnClickListener(v -> {
            if (!hasLocationPermission()) {
                requestRequiredPermissions();
            }
            startLoginServiceIfAllowed(CampusLoginService.ACTION_CHECK_NOW);
            Toast.makeText(this, "正在检查校园网", Toast.LENGTH_SHORT).show();
        });

        addWifiButton.setOnClickListener(v -> addCurrentWifi());
        manageWifiButton.setOnClickListener(v -> showManageWifiDialog());

        saveAccountButton.setOnClickListener(v -> saveAccountAndLogin());

        batteryButton.setOnClickListener(v -> requestBatteryOptimizationExemption());

        settingsButton.setOnClickListener(v -> openAutostartSettings());

        if (!hasLocationPermission()) {
            requestRequiredPermissions();
        }
        startLoginServiceIfAllowed(null);
        maybePromptBatteryOptimization();
        refreshUi();
    }

    private void ensureDefaults() {
        String rawAccount = prefs.getString(
                AppConfig.KEY_ACCOUNT, AppConfig.ACCOUNT);
        String detectedSuffix = ProviderCatalog.detectSuffix(rawAccount);
        String providerSuffix = prefs.getString(
                AppConfig.KEY_PROVIDER_SUFFIX,
                detectedSuffix == null
                        ? AppConfig.DEFAULT_PROVIDER_SUFFIX
                        : detectedSuffix
        );
        SharedPreferences.Editor editor = prefs.edit()
                .putString(
                        AppConfig.KEY_ACCOUNT,
                        ProviderCatalog.localAccount(rawAccount)
                )
                .putString(AppConfig.KEY_PROVIDER_SUFFIX, providerSuffix);

        if (prefs.getString(AppConfig.KEY_SSIDS, null) == null) {
            editor.putString(AppConfig.KEY_SSIDS, AppConfig.DEFAULT_SSID)
                    .putBoolean(AppConfig.KEY_AUTO_ENABLED, true);
        }
        editor.apply();
    }

    private String maskAccount(String account) {
        if (account == null || account.isEmpty()) {
            return "未配置";
        }
        int at = account.indexOf('@');
        if (at <= 4) {
            return account;
        }
        return account.substring(0, 3) + "****" + account.substring(at - 2);
    }

    private boolean hasLocationPermission() {
        // Approximate location is enough to start the service; the portal fallback
        // can still identify a campus network when the ROM hides the SSID.
        return checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION)
                == PackageManager.PERMISSION_GRANTED ||
                checkSelfPermission(Manifest.permission.ACCESS_COARSE_LOCATION)
                        == PackageManager.PERMISSION_GRANTED;
    }

    private void requestRequiredPermissions() {
        List<String> permissions = new ArrayList<>();
        if (checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION)
                != PackageManager.PERMISSION_GRANTED) {
            permissions.add(Manifest.permission.ACCESS_FINE_LOCATION);
            permissions.add(Manifest.permission.ACCESS_COARSE_LOCATION);
        }
        if (Build.VERSION.SDK_INT >= 33) {
            if (checkSelfPermission(Manifest.permission.NEARBY_WIFI_DEVICES)
                    != PackageManager.PERMISSION_GRANTED) {
                permissions.add(Manifest.permission.NEARBY_WIFI_DEVICES);
            }
            if (checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)
                    != PackageManager.PERMISSION_GRANTED) {
                permissions.add(Manifest.permission.POST_NOTIFICATIONS);
            }
        }
        if (!permissions.isEmpty()) {
            requestPermissions(permissions.toArray(new String[0]), REQUEST_PERMISSIONS);
        }
    }

    @Override
    public void onRequestPermissionsResult(
            int requestCode,
            String[] permissions,
            int[] grantResults
    ) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == REQUEST_PERMISSIONS) {
            if (!hasLocationPermission()) {
                Toast.makeText(
                        this,
                        "未授予位置权限；应用仍会尝试通过校园门户识别网络",
                        Toast.LENGTH_LONG
                ).show();
            }
            startLoginServiceIfAllowed(CampusLoginService.ACTION_CHECK_NOW);
            maybePromptBatteryOptimization();
            refreshUi();
        }
    }

    private void startLoginServiceIfAllowed(String action) {
        if (AccountStore.account(this).isEmpty() || AccountStore.password(this).isEmpty()) {
            if (statusText != null) {
                statusText.setText("请先填写校园网账号和密码，然后点“保存账号并登录”");
            }
            return;
        }
        if (!prefs.getBoolean(AppConfig.KEY_AUTO_ENABLED, true)) {
            return;
        }
        JobSchedule.schedule(this);
        Intent intent = new Intent(this, CampusLoginService.class);
        if (action != null) {
            intent.setAction(action);
        } else {
            intent.setAction(CampusLoginService.ACTION_START);
        }
        try {
            if (Build.VERSION.SDK_INT >= 26) {
                startForegroundService(intent);
            } else {
                startService(intent);
            }
        } catch (Exception e) {
            statusText.setText("无法启动后台服务，请打开应用设置检查权限");
        }
    }

    private void addCurrentWifi() {
        String ssid = currentSsid();
        if (ssid == null || ssid.isEmpty() || "<unknown ssid>".equals(ssid)) {
            if (!isLocationServiceEnabled()) {
                Toast.makeText(this, "请先打开系统定位开关", Toast.LENGTH_LONG).show();
            } else if (checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION)
                    != PackageManager.PERMISSION_GRANTED) {
                Toast.makeText(this, "请授予“精确位置”权限", Toast.LENGTH_LONG).show();
                requestRequiredPermissions();
            } else {
                Toast.makeText(
                        this,
                        "系统暂时隐藏了 Wi-Fi 名称，请保持连接后重试",
                        Toast.LENGTH_LONG
                ).show();
            }
            return;
        }

        String saved = prefs.getString(AppConfig.KEY_SSIDS, AppConfig.DEFAULT_SSID);
        if (saved == null) {
            saved = AppConfig.DEFAULT_SSID;
        }
        String base = saved.trim();
        for (String item : saved.split("\\n")) {
            if (ssid.equals(item.trim())) {
                Toast.makeText(this, "这个 Wi-Fi 已经在校园网名单中", Toast.LENGTH_SHORT).show();
                return;
            }
        }

        String updated = base.isEmpty() ? ssid : base + "\n" + ssid;
        prefs.edit().putString(AppConfig.KEY_SSIDS, updated).apply();
        Toast.makeText(this, "已添加：" + ssid, Toast.LENGTH_SHORT).show();
        startLoginServiceIfAllowed(CampusLoginService.ACTION_CHECK_NOW);
        refreshUi();
    }

    private void showManageWifiDialog() {
        String saved = prefs.getString(AppConfig.KEY_SSIDS, AppConfig.DEFAULT_SSID);
        if (saved == null) {
            saved = AppConfig.DEFAULT_SSID;
        }

        Set<String> configured = new LinkedHashSet<>();
        for (String item : saved.split("\\n")) {
            String trimmed = item.trim();
            if (!trimmed.isEmpty()) {
                configured.add(trimmed);
            }
        }
        if (configured.isEmpty()) {
            Toast.makeText(this, "校园网名单为空，请先添加当前 Wi-Fi", Toast.LENGTH_SHORT).show();
            return;
        }

        String[] items = configured.toArray(new String[0]);
        new AlertDialog.Builder(this)
                .setTitle("管理校园网名单")
                .setItems(items, (dialog, which) -> confirmRemoveWifi(items[which]))
                .setNegativeButton("取消", null)
                .show();
    }

    private void confirmRemoveWifi(String ssid) {
        new AlertDialog.Builder(this)
                .setTitle("删除校园网网络？")
                .setMessage("确定不再自动登录「" + ssid + "」吗？")
                .setPositiveButton("删除", (dialog, which) -> removeWifi(ssid))
                .setNegativeButton("取消", null)
                .show();
    }

    private void removeWifi(String ssid) {
        String saved = prefs.getString(AppConfig.KEY_SSIDS, AppConfig.DEFAULT_SSID);
        if (saved == null) {
            saved = AppConfig.DEFAULT_SSID;
        }
        Set<String> remaining = new LinkedHashSet<>();
        for (String item : saved.split("\\n")) {
            String trimmed = item.trim();
            if (!trimmed.isEmpty() && !trimmed.equals(ssid)) {
                remaining.add(trimmed);
            }
        }

        StringBuilder builder = new StringBuilder();
        for (String item : remaining) {
            if (builder.length() > 0) {
                builder.append('\n');
            }
            builder.append(item);
        }
        prefs.edit().putString(AppConfig.KEY_SSIDS, builder.toString()).apply();
        Toast.makeText(this, "已从校园网名单删除：" + ssid, Toast.LENGTH_SHORT).show();
        startLoginServiceIfAllowed(CampusLoginService.ACTION_CHECK_NOW);
        refreshUi();
    }

    private void saveAccountAndLogin() {
        String account = accountInput.getText().toString().trim();
        String password = passwordInput.getText().toString();
        if (account.isEmpty() || password.isEmpty()) {
            Toast.makeText(this, "账号和密码不能为空", Toast.LENGTH_SHORT).show();
            return;
        }
        String providerSuffix = ProviderCatalog.suffixAt(
                providerSpinner.getSelectedItemPosition());
        AccountStore.save(this, account, password, providerSuffix);
        prefs.edit().putBoolean(AppConfig.KEY_AUTO_ENABLED, true).apply();
        autoSwitch.setChecked(true);
        Toast.makeText(this, "账号已保存，正在检查连接", Toast.LENGTH_SHORT).show();
        startLoginServiceIfAllowed(CampusLoginService.ACTION_CHECK_NOW);
        maybePromptBatteryOptimization();
        refreshUi();
    }

    private String currentSsid() {
        try {
            WifiManager manager = (WifiManager) getApplicationContext()
                    .getSystemService(Context.WIFI_SERVICE);
            if (manager == null) {
                return null;
            }
            WifiInfo info = manager.getConnectionInfo();
            if (info == null || info.getSSID() == null) {
                return null;
            }
            String ssid = info.getSSID();
            if (ssid.length() >= 2 && ssid.startsWith("\"") && ssid.endsWith("\"")) {
                ssid = ssid.substring(1, ssid.length() - 1);
            }
            return ssid;
        } catch (Exception e) {
            return null;
        }
    }

    private void requestBatteryOptimizationExemption() {
        try {
            PowerManager powerManager =
                    (PowerManager) getSystemService(Context.POWER_SERVICE);
            if (powerManager != null &&
                    powerManager.isIgnoringBatteryOptimizations(getPackageName())) {
                Toast.makeText(this, "后台运行已经允许", Toast.LENGTH_SHORT).show();
                return;
            }
            Intent intent = new Intent(
                    Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS,
                    Uri.parse("package:" + getPackageName())
            );
            startActivity(intent);
        } catch (Exception e) {
            new AlertDialog.Builder(this)
                    .setTitle("请手动设置后台运行")
                    .setMessage("请在应用信息中把省电策略设为“无限制”，并开启自启动。")
                    .setPositiveButton("打开应用设置", (dialog, which) -> {
                        Intent settingsIntent = new Intent(
                                Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                                Uri.parse("package:" + getPackageName())
                        );
                        startActivity(settingsIntent);
                    })
                    .setNegativeButton("知道了", null)
                    .show();
        }
    }

    private void maybePromptBatteryOptimization() {
        if (AccountStore.account(this).isEmpty() || AccountStore.password(this).isEmpty()) {
            return;
        }
        if (prefs.getBoolean(AppConfig.KEY_BATTERY_PROMPTED, false)) {
            return;
        }
        PowerManager powerManager = (PowerManager) getSystemService(Context.POWER_SERVICE);
        if (powerManager != null &&
                powerManager.isIgnoringBatteryOptimizations(getPackageName())) {
            prefs.edit().putBoolean(AppConfig.KEY_BATTERY_PROMPTED, true).apply();
            return;
        }
        prefs.edit().putBoolean(AppConfig.KEY_BATTERY_PROMPTED, true).apply();
        new AlertDialog.Builder(this)
                .setTitle("允许后台自动登录")
                .setMessage("接下来请允许应用在后台运行。HyperOS、MagicOS 等系统还需要在应用信息中开启“自启动”，把省电策略设为“无限制”，并允许后台活动。")
                .setPositiveButton("去允许", (dialog, which) -> requestBatteryOptimizationExemption())
                .setNegativeButton("稍后", null)
                .show();
    }

    private void openAutostartSettings() {
        String[][] candidates = {
                {
                        "com.hihonor.systemmanager",
                        "com.hihonor.systemmanager.startupmgr.ui.StartupNormalAppListActivity"
                },
                {
                        "com.huawei.systemmanager",
                        "com.huawei.systemmanager.startupmgr.ui.StartupNormalAppListActivity"
                },
                {
                        "com.miui.securitycenter",
                        "com.miui.permcenter.autostart.AutoStartManagementActivity"
                }
        };
        for (String[] candidate : candidates) {
            try {
                Intent intent = new Intent();
                intent.setComponent(new ComponentName(
                        candidate[0], candidate[1]));
                startActivity(intent);
                return;
            } catch (Exception ignored) {
            }
        }
        Intent settingsIntent = new Intent(
                Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                Uri.parse("package:" + getPackageName())
        );
        startActivity(settingsIntent);
    }

    private boolean isLocationServiceEnabled() {
        try {
            LocationManager manager =
                    (LocationManager) getSystemService(Context.LOCATION_SERVICE);
            if (manager == null) {
                return false;
            }
            if (Build.VERSION.SDK_INT >= 28) {
                return manager.isLocationEnabled();
            }
            return manager.isProviderEnabled(LocationManager.GPS_PROVIDER) ||
                    manager.isProviderEnabled(LocationManager.NETWORK_PROVIDER);
        } catch (Exception e) {
            return false;
        }
    }

    private void refreshUi() {
        String currentAccount = AccountStore.account(this);
        String providerName = ProviderCatalog.OPTIONS[
                ProviderCatalog.indexOfSuffix(AccountStore.providerSuffix(this))]
                .name;
        accountText.setText(
                "账号：" + maskAccount(currentAccount) + "（" + providerName + "）");
        if (accountInput != null && !accountInput.hasFocus()) {
            accountInput.setText(currentAccount);
        }
        String status = prefs.getString(
                AppConfig.KEY_LAST_STATUS,
                "等待检查，请确认已连接校园 Wi-Fi"
        );
        long lastTime = prefs.getLong(AppConfig.KEY_LAST_TIME, 0);
        if (lastTime > 0) {
            status += "\n\n最后检查："
                    + new SimpleDateFormat("MM-dd HH:mm:ss", Locale.CHINA)
                    .format(new Date(lastTime));
        }
        if (AccountStore.account(this).isEmpty() || AccountStore.password(this).isEmpty()) {
            status = "请先在上方填写校园网账号和密码，然后点“保存账号并登录”";
        }
        statusText.setText(status);
        autoSwitch.setChecked(prefs.getBoolean(AppConfig.KEY_AUTO_ENABLED, true));

        String ssid = currentSsid();
        if (ssid == null || ssid.isEmpty() || "<unknown ssid>".equals(ssid)) {
            if (!isLocationServiceEnabled()) {
                currentWifiText.setText("当前 Wi-Fi：系统定位未开启");
            } else if (checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION)
                    != PackageManager.PERMISSION_GRANTED) {
                currentWifiText.setText("当前 Wi-Fi：未授予“精确位置”权限");
            } else {
                currentWifiText.setText("当前 Wi-Fi：系统暂未提供名称（将尝试通过校园门户识别）");
            }
        } else {
            currentWifiText.setText("当前 Wi-Fi：" + ssid);
        }

        String fineLocation = checkSelfPermission(Manifest.permission.ACCESS_FINE_LOCATION)
                == PackageManager.PERMISSION_GRANTED ? "已允许" : "未允许";
        String nearby = "不适用";
        String notifications = "不适用";
        if (Build.VERSION.SDK_INT >= 33) {
            nearby = checkSelfPermission(Manifest.permission.NEARBY_WIFI_DEVICES)
                    == PackageManager.PERMISSION_GRANTED ? "已允许" : "未允许";
            notifications = checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)
                    == PackageManager.PERMISSION_GRANTED ? "已允许" : "未允许";
        }
        PowerManager powerManager = (PowerManager) getSystemService(Context.POWER_SERVICE);
        String battery = powerManager != null &&
                powerManager.isIgnoringBatteryOptimizations(getPackageName())
                ? "已允许" : "未允许";
        diagnosticsText.setText(
                "兼容性检查：精确位置 " + fineLocation +
                        "｜附近设备 " + nearby +
                        "｜系统定位 " + (isLocationServiceEnabled() ? "已开启" : "未开启") +
                        "｜通知 " + notifications +
                        "｜后台运行 " + battery
        );

        String saved = prefs.getString(AppConfig.KEY_SSIDS, AppConfig.DEFAULT_SSID);
        if (saved == null) {
            saved = AppConfig.DEFAULT_SSID;
        }
        ssidListText.setText(
                "校园网名单：" + (saved.trim().isEmpty() ? "（空）" : saved.replace("\n", "、"))
        );
    }

    @SuppressLint("UnspecifiedRegisterReceiverFlag")
    @Override
    protected void onStart() {
        super.onStart();
        IntentFilter filter = new IntentFilter(AppConfig.ACTION_STATUS);
        if (Build.VERSION.SDK_INT >= 33) {
            registerReceiver(statusReceiver, filter, Context.RECEIVER_NOT_EXPORTED);
        } else {
            registerReceiver(statusReceiver, filter);
        }
        receiverRegistered = true;
    }

    @Override
    protected void onStop() {
        if (receiverRegistered) {
            unregisterReceiver(statusReceiver);
            receiverRegistered = false;
        }
        super.onStop();
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (prefs.getBoolean(AppConfig.KEY_AUTO_ENABLED, true)) {
            startLoginServiceIfAllowed(null);
        }
        refreshUi();
    }
}
