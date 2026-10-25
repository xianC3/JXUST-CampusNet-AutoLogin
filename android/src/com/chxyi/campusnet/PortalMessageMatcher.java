package com.chxyi.campusnet;

import java.util.Locale;

final class PortalMessageMatcher {
    private PortalMessageMatcher() {
    }

    static boolean isDeviceLimit(String message) {
        String value = message == null ? "" : message;
        return value.contains("设备数") ||
                value.contains("终端数") ||
                value.contains("在线数") ||
                value.contains("登陆数") ||
                value.contains("登录数") ||
                value.contains("达到上限") ||
                value.contains("已达上限") ||
                value.contains("已上限") ||
                value.contains("超过限制") ||
                value.contains("不允许重复登录") ||
                value.contains("终端在线数量") ||
                value.toLowerCase(Locale.US).contains("limit users");
    }

    static boolean isTransientBusy(String message) {
        String value = message == null ? "" : message;
        return value.contains("系统忙") ||
                value.contains("服务器忙") ||
                value.contains("请稍后再试") ||
                value.contains("操作频繁");
    }
}
