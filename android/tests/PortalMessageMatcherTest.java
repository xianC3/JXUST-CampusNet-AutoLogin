package com.chxyi.campusnet;

public final class PortalMessageMatcherTest {
    public static void main(String[] args) {
        check(
                PortalMessageMatcher.isDeviceLimit(
                        "您的登陆数已上限，请登陆自助服务下线其他终端后重新登陆Limit Users Err"
                ),
                "实际门户设备上限文本"
        );
        check(
                PortalMessageMatcher.isDeviceLimit("Limit Users Err"),
                "英文设备上限文本"
        );
        check(
                PortalMessageMatcher.isTransientBusy("系统忙！"),
                "系统忙"
        );
        check(
                !PortalMessageMatcher.isDeviceLimit("账号或密码错误"),
                "密码错误不应当作设备上限"
        );
        check(
                !PortalMessageMatcher.isTransientBusy("账号或密码错误"),
                "密码错误不应自动重试"
        );
        System.out.println("RESULT all portal message matcher tests passed");
    }

    private static void check(boolean condition, String name) {
        if (!condition) {
            throw new AssertionError(name);
        }
        System.out.println("PASS | " + name);
    }
}
