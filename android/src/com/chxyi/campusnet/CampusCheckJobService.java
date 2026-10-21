package com.chxyi.campusnet;

import android.app.job.JobParameters;
import android.app.job.JobService;

import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class CampusCheckJobService extends JobService {
    private final ExecutorService executor = Executors.newSingleThreadExecutor();

    @Override
    public boolean onStartJob(JobParameters params) {
        if (!getSharedPreferences(AppConfig.PREFS, MODE_PRIVATE)
                .getBoolean(AppConfig.KEY_AUTO_ENABLED, true)) {
            jobFinished(params, false);
            return false;
        }
        executor.execute(() -> {
            try {
                CampusLoginClient.Result result =
                        new CampusLoginClient(getApplicationContext()).checkAndLogin();
                getSharedPreferences(AppConfig.PREFS, MODE_PRIVATE)
                        .edit()
                        .putString(AppConfig.KEY_LAST_STATUS, result.message)
                        .putLong(AppConfig.KEY_LAST_TIME, System.currentTimeMillis())
                        .apply();
                CampusLoginClient.appendLog(
                        getApplicationContext(),
                        (result.success ? "JOB-OK " :
                                (result.retry ? "JOB-WAIT " : "JOB-ERROR ")) + result.message
                );
                android.content.Intent update = new android.content.Intent(AppConfig.ACTION_STATUS);
                update.setPackage(getPackageName());
                sendBroadcast(update);
            } finally {
                jobFinished(params, false);
            }
        });
        return true;
    }

    @Override
    public boolean onStopJob(JobParameters params) {
        return false;
    }

    @Override
    public void onDestroy() {
        executor.shutdownNow();
        super.onDestroy();
    }
}
