package com.sdfake.app;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.PowerManager;

import com.sdfake.app.engine.Fmt;
import com.sdfake.app.engine.FullResult;

/**
 * Mantem o teste vivo com a tela apagada ou com o app em segundo plano (servico em primeiro
 * plano com notificacao de andamento) e avisa quando o resultado sair.
 */
public final class TestService extends Service {
    private static final String CH_PROGRESS = "andamento";
    private static final String CH_RESULT = "resultado";
    private static final int ID_PROGRESS = 1;
    private static final int ID_RESULT = 2;

    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable tick = new Runnable() {
        @Override
        public void run() {
            update();
        }
    };
    private NotificationManager notifications;
    private PowerManager.WakeLock wake;

    static void start(Context context) {
        Intent i = new Intent(context, TestService.class);
        if (Build.VERSION.SDK_INT >= 26) context.startForegroundService(i);
        else context.startService(i);
    }

    @Override
    public void onCreate() {
        super.onCreate();
        notifications = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel progress = new NotificationChannel(CH_PROGRESS, "Teste em andamento", NotificationManager.IMPORTANCE_LOW);
            progress.setShowBadge(false);
            notifications.createNotificationChannel(progress);
            notifications.createNotificationChannel(
                    new NotificationChannel(CH_RESULT, "Resultado do teste", NotificationManager.IMPORTANCE_DEFAULT));
        }
        PowerManager pm = (PowerManager) getSystemService(POWER_SERVICE);
        wake = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "sdfake:teste");
        wake.setReferenceCounted(false);
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        try {
            Notification n = progressNotification();
            if (Build.VERSION.SDK_INT >= 29) startForeground(ID_PROGRESS, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC);
            else startForeground(ID_PROGRESS, n);
        } catch (RuntimeException e) {
            // O sistema recusou o servico em primeiro plano; o teste segue enquanto o app estiver aberto.
            stopSelf();
            return START_NOT_STICKY;
        }
        notifications.cancel(ID_RESULT);
        wake.acquire(24L * 60 * 60 * 1000);
        handler.removeCallbacks(tick);
        handler.post(tick);
        return START_NOT_STICKY;
    }

    private void update() {
        TestRunner r = TestRunner.get();
        if (r.state == TestRunner.RUNNING) {
            notifications.notify(ID_PROGRESS, progressNotification());
            handler.postDelayed(tick, 1500);
            return;
        }
        FullResult result = r.result;
        if (r.state == TestRunner.DONE && result != null) notifications.notify(ID_RESULT, resultNotification(result));
        shutdown();
    }

    private void shutdown() {
        handler.removeCallbacks(tick);
        if (wake.isHeld()) wake.release();
        stopForeground(true);
        stopSelf();
    }

    /** Android 15+: o sistema limita o tempo deste tipo de servico; encerra o servico sem parar o teste. */
    @Override
    public void onTimeout(int startId, int fgsType) {
        shutdown();
    }

    @Override
    public void onDestroy() {
        handler.removeCallbacks(tick);
        if (wake != null && wake.isHeld()) wake.release();
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    private Notification.Builder builder(String channel) {
        Notification.Builder b = Build.VERSION.SDK_INT >= 26 ? new Notification.Builder(this, channel) : new Notification.Builder(this);
        Intent open = new Intent(this, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP);
        PendingIntent pi = PendingIntent.getActivity(this, 0, open, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        return b.setSmallIcon(R.drawable.ic_stat).setContentIntent(pi).setColor(getColor(R.color.blue));
    }

    private Notification progressNotification() {
        TestRunner r = TestRunner.get();
        String title = r.phase.length() > 0 ? r.phase : "Testando";
        return builder(CH_PROGRESS)
                .setContentTitle(title + "  " + Fmt.percent(r.fraction))
                .setContentText(r.detail.length() > 0 ? r.detail : "Não remova o cartão.")
                .setProgress(1000, (int) (r.fraction * 1000), false)
                .setOngoing(true)
                .setOnlyAlertOnce(true)
                .build();
    }

    private Notification resultNotification(FullResult result) {
        return builder(CH_RESULT)
                .setContentTitle("Resultado: " + result.title())
                .setContentText(result.summary())
                .setStyle(new Notification.BigTextStyle().bigText(result.summary()))
                .setAutoCancel(true)
                .build();
    }
}
