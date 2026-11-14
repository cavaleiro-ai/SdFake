package com.sdfake.app;

/** Ponte para a biblioteca nativa (android/native/sdfake.c). */
final class NativeIo {
    private static final boolean LOADED;

    static {
        boolean ok;
        try {
            System.loadLibrary("sdfake");
            ok = true;
        } catch (Throwable e) {
            ok = false;
        }
        LOADED = ok;
    }

    private NativeIo() { }

    private static native int dropCache(int fd);

    /**
     * Pede ao sistema para descartar a copia em memoria do arquivo.
     * Retorna false se nao foi possivel (biblioteca ausente ou sistema de arquivos sem suporte).
     */
    static boolean drop(int fd) {
        if (!LOADED) return false;
        try {
            return dropCache(fd) == 0;
        } catch (Throwable e) {
            return false;
        }
    }
}
