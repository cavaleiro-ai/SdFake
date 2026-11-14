package com.sdfake.app.engine;

import java.io.FileNotFoundException;
import java.io.IOException;

/** Erro de entrada/saida no dispositivo testado, ja classificado. */
public class StoreException extends IOException {
    public boolean diskFull;
    public boolean deviceGone;
    public boolean readOnly;

    public StoreException(String message) {
        super(message);
    }

    public static StoreException wrap(String what, Throwable e) {
        if (e instanceof StoreException) return (StoreException) e;
        String m = String.valueOf(e.getMessage());
        StoreException s = new StoreException(what + ": " + m);
        s.diskFull = m.contains("ENOSPC") || m.contains("No space left") || m.contains("EDQUOT");
        s.readOnly = m.contains("EROFS") || m.contains("Read-only file system");
        s.deviceGone = e instanceof FileNotFoundException || e instanceof SecurityException
                || m.contains("ENODEV") || m.contains("ENOENT") || m.contains("ENXIO")
                || m.contains("ENOTCONN") || m.contains("ESTALE") || m.contains("ENOMEDIUM");
        return s;
    }
}
