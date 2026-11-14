package com.sdfake.app.engine;

/**
 * Padrao pseudoaleatorio (identico ao da versao para Windows). Cada bloco de 1 MiB e unico e
 * carrega no inicio a sua "chave" (posicao) e a semente da sessao, o que permite reconhecer
 * dados descartados, corrompidos ou sobrescritos por outro bloco.
 */
public final class Pattern {
    public static final int BLOCK = 1 << 20;
    public static final int PIECE = 4096;
    public static final int LONGS = BLOCK / 8;
    private static final long GOLDEN = 0x9E3779B97F4A7C15L;

    private Pattern() { }

    private static long mix(long z) {
        z = (z ^ (z >>> 30)) * 0xBF58476D1CE4E5B9L;
        z = (z ^ (z >>> 27)) * 0x94D049BB133111EBL;
        return z ^ (z >>> 31);
    }

    public static void fill(long[] dst, long seed, long key) {
        long s = mix(seed ^ mix(key + GOLDEN));
        dst[0] = key;
        dst[1] = seed;
        for (int i = 2; i < dst.length; i++) {
            s += GOLDEN;
            dst[i] = mix(s);
        }
    }

    /** Quantos pedacos de 4 KiB diferem. */
    public static int badPieces(long[] a, long[] b) {
        int per = PIECE / 8;
        int pieces = a.length / per;
        int bad = 0;
        for (int p = 0; p < pieces; p++) {
            int o = p * per;
            for (int i = 0; i < per; i++) {
                if (a[o + i] != b[o + i]) { bad++; break; }
            }
        }
        return bad;
    }

    /** Tudo 0x00 ou tudo 0xFF (dado nunca gravado de verdade). */
    public static boolean isBlank(long[] a) {
        long first = a[0];
        if (first != 0L && first != -1L) return false;
        for (int i = 1; i < a.length; i++) if (a[i] != first) return false;
        return true;
    }

    public static long newSeed() {
        long s = new java.security.SecureRandom().nextLong() ^ System.nanoTime();
        if (s == 0L || s == -1L) s = 0x5DFA4E5DFA4E5DFAL;
        return s;
    }
}
