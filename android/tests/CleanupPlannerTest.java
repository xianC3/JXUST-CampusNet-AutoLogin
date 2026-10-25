package com.chxyi.campusnet;

import java.util.Arrays;
import java.util.Collections;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;

public final class CleanupPlannerTest {
    public static void main(String[] args) {
        genericKeepsNewest();
        personalRemovesUnknownAndDuplicate();
        personalKeepsOtherOwnedDevice();
        duplicateRemovesOnlyOlderLogin();
        missingCurrentRemovesOldest();
        emptyCurrentRemovesOldest();
        System.out.println("RESULT all cleanup planner tests passed");
    }

    private static void genericKeepsNewest() {
        List<CleanupPlanner.Session> sessions = Arrays.asList(
                session("55", "AABBCCDDEEFF", 200),
                session("99", "112233445566", 100)
        );
        Set<String> removals = CleanupPlanner.select(
                sessions,
                "AABBCCDDEEFF",
                Collections.emptySet(),
                false
        );
        check(removals.equals(set("99")), "generic oldest");
    }

    private static void personalRemovesUnknownAndDuplicate() {
        List<CleanupPlanner.Session> sessions = Arrays.asList(
                session("55", "AABBCCDDEEFF", 300),
                session("56", "AABBCCDDEEFF", 100),
                session("77", "998877665544", 200)
        );
        Set<String> removals = CleanupPlanner.select(
                sessions,
                "AABBCCDDEEFF",
                set("AABBCCDDEEFF", "A1B2C3D4E5F6"),
                true
        );
        check(removals.equals(set("56", "77")), "personal unknown and duplicate");
    }

    private static void personalKeepsOtherOwnedDevice() {
        List<CleanupPlanner.Session> sessions = Arrays.asList(
                session("55", "AABBCCDDEEFF", 200),
                session("99", "112233445566", 100)
        );
        Set<String> removals = CleanupPlanner.select(
                sessions,
                "AABBCCDDEEFF",
                set("AABBCCDDEEFF", "112233445566"),
                true
        );
        check(removals.isEmpty(), "personal keeps owned device");
    }

    private static void duplicateRemovesOnlyOlderLogin() {
        List<CleanupPlanner.Session> sessions = Arrays.asList(
                session("55", "AABBCCDDEEFF", 300),
                session("56", "AABBCCDDEEFF", 100),
                session("77", "998877665544", 200)
        );
        Set<String> removals = CleanupPlanner.select(
                sessions,
                "AABBCCDDEEFF",
                Collections.emptySet(),
                false
        );
        check(removals.equals(set("56")), "duplicate old account");
    }

    private static void missingCurrentRemovesOldest() {
        List<CleanupPlanner.Session> sessions = Arrays.asList(
                session("55", "AABBCCDDEEFF", 300),
                session("99", "112233445566", 100)
        );
        Set<String> removals = CleanupPlanner.select(
                sessions,
                "DDEEFF001122",
                set("AABBCCDDEEFF", "112233445566"),
                false
        );
        check(removals.equals(set("99")), "missing current removes oldest");
    }

    private static void emptyCurrentRemovesOldest() {
        List<CleanupPlanner.Session> sessions = Arrays.asList(
                session("55", "AABBCCDDEEFF", 300),
                session("99", "112233445566", 100)
        );
        Set<String> removals = CleanupPlanner.select(
                sessions,
                "",
                set("AABBCCDDEEFF", "112233445566"),
                false
        );
        check(removals.equals(set("99")), "empty current removes oldest");
    }

    private static CleanupPlanner.Session session(
            String id,
            String mac,
            long loginTime
    ) {
        return new CleanupPlanner.Session(id, mac, loginTime, 0);
    }

    private static Set<String> set(String... values) {
        return new LinkedHashSet<>(Arrays.asList(values));
    }

    private static void check(boolean condition, String name) {
        if (!condition) {
            throw new AssertionError(name);
        }
        System.out.println("PASS | " + name);
    }
}
