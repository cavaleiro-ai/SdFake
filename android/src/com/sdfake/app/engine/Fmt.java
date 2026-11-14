package com.sdfake.app.engine;

import java.text.DecimalFormat;
import java.text.DecimalFormatSymbols;
import java.util.Locale;

public final class Fmt {
    private static final DecimalFormatSymbols BR = new DecimalFormatSymbols(Locale.forLanguageTag("pt-BR"));

    private Fmt() { }

    private static String num(double v, String pattern) {
        return new DecimalFormat(pattern, BR).format(v);
    }

    /** Tamanho em unidades decimais, como vem na embalagem (1 GB = 1.000.000.000 bytes). */
    public static String bytes(long b) {
        if (b < 0) return "?";
        double v = b;
        if (v >= 1e12) return num(v / 1e12, "0.##") + " TB";
        if (v >= 1e9) return num(v / 1e9, "0.#") + " GB";
        if (v >= 1e6) return num(v / 1e6, "0") + " MB";
        if (v >= 1e3) return num(v / 1e3, "0") + " KB";
        return b + " bytes";
    }

    public static String speed(double mbs) {
        if (mbs <= 0) return "-";
        return num(mbs, mbs >= 10 ? "0" : "0.0") + " MB/s";
    }

    public static String duration(long ms) {
        long s = ms / 1000;
        if (s < 1) return "menos de 1 s";
        if (s < 60) return s + " s";
        if (s < 3600) return (s / 60) + " min " + two(s % 60) + " s";
        return (s / 3600) + " h " + two((s % 3600) / 60) + " min";
    }

    private static String two(long v) {
        return v < 10 ? "0" + v : String.valueOf(v);
    }

    public static String eta(long remainingBytes, double mbs) {
        if (mbs <= 0.01) return "";
        double sec = remainingBytes / (mbs * 1e6);
        if (sec > 360000 || sec < 2) return "";
        return "faltam ~" + duration((long) (sec * 1000));
    }

    public static String percent(double fraction) {
        return num(fraction * 100, "0.#") + "%";
    }
}
