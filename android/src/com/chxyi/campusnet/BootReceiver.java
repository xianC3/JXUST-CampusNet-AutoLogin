package com.chxyi.campusnet;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.os.Build;

public class BootReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        if (!Intent.ACTION_BOOT_COMPLETED.equals(intent.getAction())) {
            return;
        }
        SharedPreferences prefs =
                context.getSharedPreferences(AppConfig.PREFS, Context.MODE_PRIVATE);
        if (!prefs.getBoolean(AppConfig.KEY_AUTO_ENABLED, true)) {
            return;
        }

        JobSchedule.schedule(context);
        Intent serviceIntent = new Intent(context, CampusLoginService.class);
        serviceIntent.setAction(CampusLoginService.ACTION_START);
        try {
            if (Build.VERSION.SDK_INT >= 26) {
                context.startForegroundService(serviceIntent);
            } else {
                context.startService(serviceIntent);
            }
        } catch (Exception ignored) {
        }
    }
}
