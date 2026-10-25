package com.chxyi.campusnet;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

final class CleanupPlanner {
    static final class Session {
        final String sessionId;
        final String mac;
        final long loginTime;
        final int index;

        Session(String sessionId, String mac, long loginTime, int index) {
            this.sessionId = sessionId;
            this.mac = mac;
            this.loginTime = loginTime;
            this.index = index;
        }
    }

    private CleanupPlanner() {
    }

    static Set<String> select(
            List<Session> sessions,
            String currentMac,
            Set<String> trustedMacs,
            boolean personalMode
    ) {
        String normalizedCurrent = normalizeMac(currentMac);
        boolean currentPresent = false;
        Session oldestSession = null;
        for (Session session : sessions) {
            if (session.sessionId == null || session.sessionId.isEmpty() ||
                    session.mac == null || session.mac.isEmpty()) {
                continue;
            }
            if (isPlausibleMac(normalizedCurrent) &&
                    normalizeMac(session.mac).equals(normalizedCurrent)) {
                currentPresent = true;
            }
            if (oldestSession == null ||
                    session.loginTime < oldestSession.loginTime ||
                    (session.loginTime == oldestSession.loginTime &&
                            session.index < oldestSession.index)) {
                oldestSession = session;
            }
        }
        if (!currentPresent) {
            Set<String> fallback = new LinkedHashSet<>();
            if (oldestSession != null &&
                    oldestSession.sessionId != null &&
                    !oldestSession.sessionId.isEmpty()) {
                fallback.add(oldestSession.sessionId);
            }
            return fallback;
        }

        Map<String, List<Session>> byMac = new LinkedHashMap<>();
        for (Session session : sessions) {
            if (session.sessionId == null || session.sessionId.isEmpty() ||
                    session.mac == null || session.mac.isEmpty()) {
                continue;
            }
            byMac.computeIfAbsent(
                    session.mac,
                    ignored -> new ArrayList<>()
            ).add(session);
        }

        Set<String> removals = new LinkedHashSet<>();
        for (List<Session> group : byMac.values()) {
            group.sort(
                    Comparator.comparingLong(
                            (Session item) -> item.loginTime
                    ).reversed().thenComparingInt(item -> item.index)
            );
            for (int i = 1; i < group.size(); i++) {
                removals.add(group.get(i).sessionId);
            }
        }

        Set<String> known = new LinkedHashSet<>();
        if (trustedMacs != null) {
            for (String item : trustedMacs) {
                String normalized = normalizeMac(item);
                if (isPlausibleMac(normalized)) {
                    known.add(normalized);
                }
            }
        }
        known.add(normalizedCurrent);

        if (personalMode) {
            for (Session session : sessions) {
                if (session.sessionId != null &&
                        !session.sessionId.isEmpty() &&
                        !known.contains(session.mac)) {
                    removals.add(session.sessionId);
                }
            }
        } else if (removals.isEmpty() && sessions.size() > 1) {
            List<Session> ordered = new ArrayList<>(sessions);
            Collections.sort(
                    ordered,
                    Comparator.comparingLong(
                            (Session item) -> item.loginTime
                    ).thenComparingInt(item -> item.index)
            );
            for (Session session : ordered) {
                if (session.sessionId != null &&
                        !session.sessionId.isEmpty()) {
                    removals.add(session.sessionId);
                    break;
                }
            }
        }
        return removals;
    }

    static String normalizeMac(String value) {
        return value == null
                ? ""
                : value.replaceAll("[^0-9A-Fa-f]", "")
                        .toUpperCase(java.util.Locale.US);
    }

    static boolean isPlausibleMac(String value) {
        return value != null &&
                value.matches("[0-9A-F]{12}") &&
                !"000000000000".equals(value) &&
                !"FFFFFFFFFFFF".equals(value);
    }
}
