package com.chxyi.campusnet;

import java.util.Locale;

final class ProviderCatalog {
    static final class Option {
        final String name;
        final String suffix;

        Option(String name, String suffix) {
            this.name = name;
            this.suffix = suffix;
        }
    }

    static final Option[] OPTIONS = {
            new Option("校园网", ""),
            new Option("联通", "@unicomyt"),
            new Option("移动", "@cmccyt"),
            new Option("电信", "@telecomyt")
    };

    private ProviderCatalog() {
    }

    static String localAccount(String account) {
        String value = account == null ? "" : account.trim();
        for (Option option : OPTIONS) {
            if (!option.suffix.isEmpty() &&
                    value.toLowerCase(Locale.US).endsWith(option.suffix)) {
                return value.substring(0, value.length() - option.suffix.length());
            }
        }
        return value;
    }

    static String detectSuffix(String account) {
        String value = account == null ? "" : account.trim();
        for (Option option : OPTIONS) {
            if (!option.suffix.isEmpty() &&
                    value.toLowerCase(Locale.US).endsWith(option.suffix)) {
                return option.suffix;
            }
        }
        return null;
    }

    static String loginAccount(String localAccount, String suffix) {
        String local = localAccount(localAccount);
        for (Option option : OPTIONS) {
            if (option.suffix.equals(suffix)) {
                return local + option.suffix;
            }
        }
        return local;
    }

    static int indexOfSuffix(String suffix) {
        for (int i = 0; i < OPTIONS.length; i++) {
            if (OPTIONS[i].suffix.equals(suffix)) {
                return i;
            }
        }
        return 0;
    }

    static String suffixAt(int index) {
        if (index < 0 || index >= OPTIONS.length) {
            return OPTIONS[0].suffix;
        }
        return OPTIONS[index].suffix;
    }
}
