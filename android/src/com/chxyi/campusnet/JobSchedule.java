package com.chxyi.campusnet;

import android.app.job.JobInfo;
import android.app.job.JobScheduler;
import android.content.ComponentName;
import android.content.Context;

final class JobSchedule {
    private static final int JOB_ID = 2201;
    private static final long PERIOD_MS = 15 * 60 * 1000L;

    private JobSchedule() {
    }

    static void schedule(Context context) {
        try {
            JobScheduler scheduler =
                    (JobScheduler) context.getSystemService(Context.JOB_SCHEDULER_SERVICE);
            if (scheduler == null) {
                return;
            }
            JobInfo job = new JobInfo.Builder(
                    JOB_ID,
                    new ComponentName(context, CampusCheckJobService.class)
            )
                    .setPersisted(true)
                    .setPeriodic(PERIOD_MS)
                    .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY)
                    .build();
            scheduler.schedule(job);
        } catch (Exception ignored) {
        }
    }

    static void cancel(Context context) {
        try {
            JobScheduler scheduler =
                    (JobScheduler) context.getSystemService(Context.JOB_SCHEDULER_SERVICE);
            if (scheduler != null) {
                scheduler.cancel(JOB_ID);
            }
        } catch (Exception ignored) {
        }
    }
}
