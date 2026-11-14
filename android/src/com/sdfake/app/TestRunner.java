package com.sdfake.app;

import android.app.ActivityManager;
import android.content.Context;
import android.net.Uri;

import com.sdfake.app.engine.FullResult;
import com.sdfake.app.engine.FullTest;

/** Dono do teste em andamento. Vive enquanto o processo viver, independente da tela. */
final class TestRunner {
    static final int IDLE = 0, RUNNING = 1, DONE = 2;

    private static final TestRunner INSTANCE = new TestRunner();

    static TestRunner get() {
        return INSTANCE;
    }

    volatile int state = IDLE;
    volatile String phase = "";
    volatile String detail = "";
    volatile double fraction;
    volatile String target = "";
    volatile FullResult result;

    private final StringBuilder log = new StringBuilder();
    private volatile FullTest test;

    private TestRunner() { }

    synchronized boolean start(Context app, Uri tree, String targetName) {
        if (state == RUNNING) return false;
        state = RUNNING;
        result = null;
        phase = "Preparando...";
        detail = "";
        fraction = 0;
        target = targetName;
        synchronized (log) { log.setLength(0); }

        final FullTest t = new FullTest();
        t.cacheRiskBytes = 2 * totalRam(app);
        t.listener = new FullTest.Listener() {
            @Override
            public void onProgress(String p, double f, String d) {
                phase = p;
                fraction = f;
                detail = d;
            }

            @Override
            public void onLog(String line) {
                synchronized (log) {
                    if (log.length() > 0) log.append('\n');
                    log.append(line);
                }
            }
        };
        test = t;
        final SafStore store = new SafStore(app.getContentResolver(), tree);
        Thread thread = new Thread(new Runnable() {
            @Override
            public void run() {
                FullResult r;
                try {
                    r = t.run(store);
                } catch (Throwable e) {
                    r = new FullResult();
                    r.fatalError = "Erro inesperado: " + e;
                }
                result = r;
                state = DONE;
            }
        }, "sdfake-teste");
        thread.start();
        return true;
    }

    private static long totalRam(Context app) {
        try {
            ActivityManager am = (ActivityManager) app.getSystemService(Context.ACTIVITY_SERVICE);
            ActivityManager.MemoryInfo info = new ActivityManager.MemoryInfo();
            am.getMemoryInfo(info);
            return info.totalMem;
        } catch (RuntimeException e) {
            return 8L << 30;
        }
    }

    void cancel() {
        FullTest t = test;
        if (t != null) t.cancelRequested = true;
    }

    boolean cancelling() {
        FullTest t = test;
        return state == RUNNING && t != null && t.cancelRequested;
    }

    synchronized void reset() {
        if (state == DONE) {
            state = IDLE;
            result = null;
            test = null;
        }
    }

    String logText() {
        synchronized (log) { return log.toString(); }
    }
}
