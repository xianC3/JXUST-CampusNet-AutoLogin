package com.chxyi.campusnet;

import android.content.Context;
import android.content.SharedPreferences;

import java.util.LinkedHashSet;
import java.util.Set;

final class TrustedMacStore {
    private TrustedMacStore() {
    }

    static Set<String> load(Context context) {
        Set<String> result = new LinkedHashSet<>();
        if (!AppConfig.PERSONAL_MODE) {
            return result;
        }

        SharedPreferences prefs =
                context.getSharedPreferences(AppConfig.PREFS, Context.MODE_PRIVATE);
        String saved = prefs.getString(AppConfig.KEY_TRUSTED_MACS, null);
        if (saved == null) {
            saved = AppConfig.DEFAULT_TRUSTED_MACS;
        }
        for (String item : saved.split("[,\\n;\\s]+")) {
            String normalized = SelfServiceClient.normalizeMac(item);
            if (SelfServiceClient.isPlausibleMac(normalized)) {
                result.add(normalized);
            }
        }
        return result;
    }

    static void remember(Context context, String mac) {
        if (!AppConfig.PERSONAL_MODE) {
            return;
        }
        String normalized = SelfServiceClient.normalizeMac(mac);
        if (!SelfServiceClient.isPlausibleMac(normalized)) {
            return;
        }
        Set<String> trusted = load(context);
        if (!trusted.add(normalized)) {
            return;
        }
        StringBuilder builder = new StringBuilder();
        for (String item : trusted) {
            if (builder.length() > 0) {
                builder.append('\n');
            }
            builder.append(item);
        }
        context.getSharedPreferences(AppConfig.PREFS, Context.MODE_PRIVATE)
                .edit()
                .putString(AppConfig.KEY_TRUSTED_MACS, builder.toString())
                .apply();
    }
}
