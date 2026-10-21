package com.chxyi.campusnet;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.SharedPreferences;
import android.content.pm.ServiceInfo;
import android.graphics.drawable.Icon;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.NetworkRequest;
import android.net.wifi.WifiManager;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;

import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

public class CampusLoginService extends Service {
    public static final String ACTION_START = "com.chxyi.campusnet.START";
    public static final String ACTION_CHECK_NOW = "com.chxyi.campusnet.CHECK_NOW";
    public static final String ACTION_STOP = "com.chxyi.campusnet.STOP";

    private static final String CHANNEL_ID = "campusnet_status_v2";
    private static final int NOTIFICATION_ID = 1201;
    private static final long PERIODIC_CHECK_MS = 5 * 60 * 1000L;

    private final Handler handler = new Handler(Looper.getMainLooper());
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final AtomicBoolean checking = new AtomicBoolean(false);
    private ConnectivityManager connectivityManager;
    private ConnectivityManager.NetworkCallback networkCallback;
    private BroadcastReceiver wifiStateReceiver;
    private String lastStatus = "自动登录已开启";
    private int quickRetries = 0;

    private final Runnable periodicCheck = new Runnable() {
        @Override
        public void run() {
            runCheck();
            handler.postDelayed(this, PERIODIC_CHECK_MS);
        }
    };

    @Override
    public void onCreate() {
        super.onCreate();
        createNotificationChannel();
        startAsForeground(lastStatus);
        registerNetworkCallback();
        registerWifiStateReceiver();
        handler.postDelayed(periodicCheck, PERIODIC_CHECK_MS);
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        String action = intent == null ? ACTION_START : intent.getAction();
        if (ACTION_STOP.equals(action)) {
            stopSelf();
            return START_NOT_STICKY;
        }

        SharedPreferences prefs = getSharedPreferences(AppConfig.PREFS, MODE_PRIVATE);
        if (!prefs.getBoolean(AppConfig.KEY_AUTO_ENABLED, true)) {
            stopSelf();
            return START_NOT_STICKY;
        }

        startAsForeground(lastStatus);
        runCheck();
        return START_STICKY;
    }

    private void startAsForeground(String status) {
        Notification notification = buildNotification(status);
        if (Build.VERSION.SDK_INT >= 34) {
            startForeground(
                    NOTIFICATION_ID,
                    notification,
                    ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE
            );
        } else {
            startForeground(NOTIFICATION_ID, notification);
        }
    }

    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID,
                    "校园网自动登录",
                    NotificationManager.IMPORTANCE_LOW
            );
            channel.setDescription("显示校园网连接状态");
            channel.setShowBadge(false);
            channel.setSound(null, null);
            channel.enableVibration(false);
            NotificationManager manager =
                    (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
            if (manager != null) {
                manager.createNotificationChannel(channel);
            }
        }
    }

    private Notification buildNotification(String status) {
        Intent openIntent = new Intent(this, MainActivity.class);
        openIntent.setFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        PendingIntent openPending = PendingIntent.getActivity(
                this,
                1,
                openIntent,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE
        );

        Intent checkIntent = new Intent(this, CampusLoginService.class);
        checkIntent.setAction(ACTION_CHECK_NOW);
        PendingIntent checkPending = PendingIntent.getForegroundService(
                this,
                2,
                checkIntent,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE
        );

        Notification.Builder builder = new Notification.Builder(this, CHANNEL_ID)
                .setSmallIcon(R.drawable.ic_stat_wifi)
                .setContentTitle("校园网自动登录")
                .setContentText(status)
                .setContentIntent(openPending)
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .setCategory(Notification.CATEGORY_SERVICE)
                .addAction(
                        new Notification.Action.Builder(
                                Icon.createWithResource(this, R.drawable.ic_stat_wifi),
                                "立即检测",
                                checkPending
                        ).build()
                );
        return builder.build();
    }

    private void updateNotification(String status) {
        NotificationManager manager =
                (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
        if (manager != null) {
            manager.notify(NOTIFICATION_ID, buildNotification(status));
        }
    }

    private void registerNetworkCallback() {
        connectivityManager =
                (ConnectivityManager) getSystemService(Context.CONNECTIVITY_SERVICE);
        if (connectivityManager == null) {
            return;
        }
        networkCallback = new ConnectivityManager.NetworkCallback() {
            @Override
            public void onAvailable(Network network) {
                handler.postDelayed(CampusLoginService.this::runCheck, 250);
            }

            @Override
            public void onCapabilitiesChanged(Network network,
                                              android.net.NetworkCapabilities capabilities) {
                if (capabilities.hasTransport(
                        android.net.NetworkCapabilities.TRANSPORT_WIFI)) {
                    handler.postDelayed(CampusLoginService.this::runCheck, 250);
                    handler.postDelayed(
                            CampusLoginService.this::runCheck,
                            1500
                    );
                }
            }
        };
        NetworkRequest request = new NetworkRequest.Builder()
                .addTransportType(
                        android.net.NetworkCapabilities.TRANSPORT_WIFI)
                .build();
        try {
            connectivityManager.registerNetworkCallback(request, networkCallback);
        } catch (Exception ignored) {
        }
    }

    private void registerWifiStateReceiver() {
        wifiStateReceiver = new BroadcastReceiver() {
            @Override
            public void onReceive(Context context, Intent intent) {
                if (WifiManager.NETWORK_STATE_CHANGED_ACTION.equals(
                        intent.getAction())) {
                    handler.postDelayed(
                            CampusLoginService.this::runCheck,
                            500
                    );
                    handler.postDelayed(
                            CampusLoginService.this::runCheck,
                            2000
                    );
                }
            }
        };
        IntentFilter filter = new IntentFilter(
                WifiManager.NETWORK_STATE_CHANGED_ACTION);
        if (Build.VERSION.SDK_INT >= 33) {
            registerReceiver(
                    wifiStateReceiver,
                    filter,
                    Context.RECEIVER_NOT_EXPORTED
            );
        } else {
            registerReceiver(wifiStateReceiver, filter);
        }
    }

    private void runCheck() {
        if (!checking.compareAndSet(false, true)) {
            return;
        }
        executor.execute(() -> {
            try {
                CampusLoginClient.Result result =
                        new CampusLoginClient(getApplicationContext()).checkAndLogin();
                lastStatus = result.message;
                getSharedPreferences(AppConfig.PREFS, MODE_PRIVATE)
                        .edit()
                        .putString(AppConfig.KEY_LAST_STATUS, result.message)
                        .putLong(AppConfig.KEY_LAST_TIME, System.currentTimeMillis())
                        .apply();
                CampusLoginClient.appendLog(getApplicationContext(),
                        (result.success ? "OK " : (result.retry ? "WAIT " : "ERROR ")) +
                                result.message);
                updateNotification(notificationMessage(result));
                Intent update = new Intent(AppConfig.ACTION_STATUS);
                update.setPackage(getPackageName());
                sendBroadcast(update);
                if (result.retry && quickRetries < 6) {
                    quickRetries++;
                    handler.postDelayed(CampusLoginService.this::runCheck, 5000);
                } else {
                    quickRetries = 0;
                }
            } finally {
                checking.set(false);
            }
        });
    }

    private String notificationMessage(CampusLoginClient.Result result) {
        if (result.success && !result.skipped) {
            return "校园网自动登录已开启（已连接）";
        }
        if (result.skipped) {
            return "校园网自动登录已开启（等待校园网）";
        }
        if (result.retry) {
            return "校园网自动登录已开启（正在识别网络）";
        }
        return "校园网自动登录已开启（请打开应用查看状态）";
    }

    @Override
    public void onDestroy() {
        handler.removeCallbacksAndMessages(null);
        if (connectivityManager != null && networkCallback != null) {
            try {
                connectivityManager.unregisterNetworkCallback(networkCallback);
            } catch (Exception ignored) {
            }
        }
        if (wifiStateReceiver != null) {
            try {
                unregisterReceiver(wifiStateReceiver);
            } catch (Exception ignored) {
            }
        }
        executor.shutdownNow();
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }
}
