package com.chxyi.campusnet;

import android.content.Context;
import android.content.SharedPreferences;

final class AccountStore {
    private AccountStore() {
    }

    static String account(Context context) {
        SharedPreferences prefs =
                context.getSharedPreferences(AppConfig.PREFS, Context.MODE_PRIVATE);
        String value = prefs.getString(AppConfig.KEY_ACCOUNT, AppConfig.ACCOUNT);
        String local = ProviderCatalog.localAccount(
                value == null ? AppConfig.ACCOUNT : value);
        return local.isEmpty() ? "" : local;
    }

    static String providerSuffix(Context context) {
        SharedPreferences prefs =
                context.getSharedPreferences(AppConfig.PREFS, Context.MODE_PRIVATE);
        String savedSuffix = prefs.getString(
                AppConfig.KEY_PROVIDER_SUFFIX, null);
        if (savedSuffix != null) {
            return savedSuffix;
        }

        String rawAccount = prefs.getString(
                AppConfig.KEY_ACCOUNT, AppConfig.ACCOUNT);
        String detected = ProviderCatalog.detectSuffix(rawAccount);
        return detected == null ? AppConfig.DEFAULT_PROVIDER_SUFFIX : detected;
    }

    static String loginAccount(Context context) {
        return ProviderCatalog.loginAccount(
                account(context), providerSuffix(context));
    }

    static String password(Context context) {
        SharedPreferences prefs =
                context.getSharedPreferences(AppConfig.PREFS, Context.MODE_PRIVATE);
        String value = prefs.getString(AppConfig.KEY_PASSWORD, AppConfig.PASSWORD);
        return value == null ? AppConfig.PASSWORD : value;
    }

    static void save(
            Context context,
            String account,
            String password,
            String providerSuffix
    ) {
        context.getSharedPreferences(AppConfig.PREFS, Context.MODE_PRIVATE)
                .edit()
                .putString(AppConfig.KEY_ACCOUNT, ProviderCatalog.localAccount(account))
                .putString(AppConfig.KEY_PASSWORD, password)
                .putString(AppConfig.KEY_PROVIDER_SUFFIX, providerSuffix)
                .apply();
    }
}
